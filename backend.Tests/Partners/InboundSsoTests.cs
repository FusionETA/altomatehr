using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Partners;

namespace AltomateHR.Api.Tests.Partners;

// SSO coming IN — an external platform dropping one of its provisioned admins
// into AltomateHR already signed in.
//
// This endpoint turns an external assertion into a real session, so what is
// pinned here is the ways it must REFUSE. Everything it lets through, someone
// outside this system asked for.
public class InboundSsoTests
{
    private const string Org = "org-1";
    private const string Other = "org-2";

    private static (InboundSsoService Sso, StubStore Store) Make(params OrganizationMembership[] memberships)
    {
        var users = new List<User>
        {
            new() { Id = "usr-owner", Email = "owner@acme.com" },
            new() { Id = "usr-admin", Email = "admin@acme.com" },
            new() { Id = "usr-staff", Email = "staff@acme.com" },
        };
        var store = new StubStore();
        return (new InboundSsoService(store, new StubDirectory(users, memberships), new StubAuth()), store);
    }

    private static OrganizationMembership Member(string userId, string role, string org = Org) =>
        new() { UserId = userId, Role = role, OrganizationId = org };

    // ─── Minting ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("owner@acme.com", "usr-owner", "Owner")]
    [InlineData("admin@acme.com", "usr-admin", "Admin")]
    public async Task MintsATicketForAnAdministrativeSeat(string email, string userId, string role)
    {
        var (sso, store) = Make(Member(userId, role));

        var ticket = await sso.MintAsync(email, Org);

        Assert.NotNull(ticket);
        Assert.Contains(ticket!.Ticket, ticket.RedirectPath);
        // The partner validates this and refuses a response without it.
        Assert.Equal(120, ticket.ExpiresIn);
        // The user is pinned at mint time, not re-resolved from the email later.
        Assert.Equal(userId, store.Last!.UserId);
        Assert.Equal(Org, store.Last.OrganizationId);
    }

    // The seat the external platform provisions is an administrative one. A
    // wider door would let an integration mint sessions for ordinary staff who
    // never agreed to it.
    [Fact]
    public async Task RefusesAnEmployee()
    {
        var (sso, _) = Make(Member("usr-staff", "Employee"));

        Assert.Null(await sso.MintAsync("staff@acme.com", Org));
    }

    // THE containment rule. The email says who; the calling key's org says
    // where. Being an admin of another company is not a way into this one.
    [Fact]
    public async Task RefusesAnAdminOfADifferentOrganization()
    {
        var (sso, _) = Make(Member("usr-admin", "Admin", Other));

        Assert.Null(await sso.MintAsync("admin@acme.com", Org));
    }

    [Theory]
    [InlineData("nobody@acme.com")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefusesAnEmailItCannotPlace(string email)
    {
        var (sso, _) = Make(Member("usr-admin", "Admin"));

        Assert.Null(await sso.MintAsync(email, Org));
    }

    [Fact]
    public async Task MatchesTheEmailWithoutRegardToCase()
    {
        var (sso, _) = Make(Member("usr-admin", "Admin"));

        Assert.NotNull(await sso.MintAsync("ADMIN@Acme.com", Org));
    }

    // ─── Redeeming ──────────────────────────────────────────────────────

    [Fact]
    public async Task RedeemsOnceAndOnlyOnce()
    {
        var (sso, _) = Make(Member("usr-admin", "Admin"));
        var ticket = (await sso.MintAsync("admin@acme.com", Org))!.Ticket;

        Assert.NotNull(await sso.RedeemAsync(ticket));
        // A ticket left in a browser history or a referrer header is dead.
        Assert.Null(await sso.RedeemAsync(ticket));
    }

    [Fact]
    public async Task RefusesATicketItNeverMinted()
    {
        var (sso, _) = Make(Member("usr-admin", "Admin"));

        Assert.Null(await sso.RedeemAsync("not-a-real-ticket"));
    }

    // Both directions share one ticket store. A partner launch ticket must not
    // be redeemable as an inbound one, or an app we hand tickets TO could walk
    // one back in and land a session.
    [Fact]
    public async Task RefusesAPartnerLaunchTicket()
    {
        var (sso, store) = Make(Member("usr-admin", "Admin"));
        var foreign = await store.MintTicketAsync(
            new PartnerTicketData("appraisify", "usr-admin", Org), TimeSpan.FromMinutes(2));

        Assert.Null(await sso.RedeemAsync(foreign));
    }

    // Two minutes is short, but a seat revoked inside that window must not
    // still open a session — so the role is checked again at redemption.
    [Fact]
    public async Task RefusesWhenTheSeatWasDowngradedAfterMinting()
    {
        var memberships = new List<OrganizationMembership> { Member("usr-admin", "Admin") };
        var store = new StubStore();
        var directory = new StubDirectory(
            [new User { Id = "usr-admin", Email = "admin@acme.com" }], memberships);
        var sso = new InboundSsoService(store, directory, new StubAuth());

        var ticket = (await sso.MintAsync("admin@acme.com", Org))!.Ticket;
        memberships[0].Role = "Employee";           // demoted in the meantime

        Assert.Null(await sso.RedeemAsync(ticket));
    }

    // ─── Stubs ──────────────────────────────────────────────────────────

    private sealed class StubStore : IPartnerAuthStore
    {
        private readonly Dictionary<string, PartnerTicketData> _tickets = [];
        private int _next;

        public PartnerTicketData? Last { get; private set; }

        public Task<string> MintTicketAsync(PartnerTicketData data, TimeSpan ttl)
        {
            var ticket = $"tkt-{++_next}";
            _tickets[ticket] = data;
            Last = data;
            return Task.FromResult(ticket);
        }

        // Delete-on-read, like the real store.
        public Task<PartnerTicketData?> RedeemTicketAsync(string ticket)
        {
            if (!_tickets.Remove(ticket, out var data)) return Task.FromResult<PartnerTicketData?>(null);
            return Task.FromResult<PartnerTicketData?>(data);
        }

        public Task StoreAccessTokenAsync(string token, PartnerTokenData data, TimeSpan ttl) =>
            Task.CompletedTask;
        public Task<PartnerTokenData?> GetAccessTokenAsync(string token) =>
            Task.FromResult<PartnerTokenData?>(null);
        public Task StoreRefreshTokenAsync(string token, PartnerTokenData data, TimeSpan ttl) =>
            Task.CompletedTask;
        public Task<PartnerTokenData?> RedeemRefreshTokenAsync(string token) =>
            Task.FromResult<PartnerTokenData?>(null);
    }

    private sealed class StubAuth : IAuthService
    {
        public Task<AuthResult?> SwitchOrgAsync(string userId, string organizationId) =>
            Task.FromResult<AuthResult?>(new AuthResult(
                "access", "admin@acme.com", "Admin", organizationId,
                "refresh", DateTime.UtcNow.AddDays(7)));

        public Task<AuthResult?> LoginAsync(string email, string password) =>
            throw new NotSupportedException();
        public Task<AuthResult?> RefreshAsync(string refreshToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserOrgDto>> GetOrgsAsync(string userId) => throw new NotSupportedException();
        public Task LogoutAsync(string refreshToken) => throw new NotSupportedException();
        public Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string?> ResetPasswordAsync(string email, string otp, string newPassword) =>
            throw new NotSupportedException();
        public Task<string?> ChangePasswordAsync(string userId, string current, string next) =>
            throw new NotSupportedException();
    }

    private sealed class StubDirectory(List<User> users, IEnumerable<OrganizationMembership> memberships)
        : IDirectoryService
    {
        private readonly List<OrganizationMembership> _memberships = memberships.ToList();

        public Task<List<User>> GetUsersAsync() => Task.FromResult(users);

        public Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId) =>
            Task.FromResult(_memberships.FirstOrDefault(
                m => m.OrganizationId == organizationId && m.UserId == userId));

        public Task<User?> GetUserAsync(string id) =>
            Task.FromResult(users.FirstOrDefault(u => u.Id == id));
        public Task<OrganizationMembership?> GetMembershipForUserAsync(string userId) =>
            throw new NotSupportedException();
        public Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync() =>
            throw new NotSupportedException();
        public Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId) =>
            throw new NotSupportedException();
        public Task<int> CountMembershipsByShiftAsync(string shiftId) => throw new NotSupportedException();
        public Task<List<EmployeeProfile>> GetProfilesForCurrentOrgAsync() => throw new NotSupportedException();
    }
}
