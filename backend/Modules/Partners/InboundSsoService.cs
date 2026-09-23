using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Partners;

// SSO coming IN — an external platform (Altomate Accounting) dropping one of
// its provisioned admins into AltomateHR already signed in.
//
// The opposite direction from SsoController, which launches a signed-in HR user
// OUT to a partner app. Both use the same Redis ticket store; only the trust
// runs the other way. Here the external platform asserts WHO, and its own
// wp_live_ key decides WHERE — so a key can only ever reach its own tenant.
//
// Admins and owners only, deliberately. This is the seat the external platform
// provisions and pays for, and a wider door would let an integration mint
// sessions for ordinary staff who never agreed to it.
public interface IInboundSsoService
{
    // Server-to-server. Returns null when the email is not an admin or owner of
    // `organizationId` — the caller learns nothing more than that.
    Task<InboundTicket?> MintAsync(string email, string organizationId);

    // Browser hand-off. Null when the ticket is unknown, already used, expired,
    // or its subject is no longer an administrative member of that org.
    Task<AuthResult?> RedeemAsync(string ticket);
}

public record InboundTicket(string Ticket, string RedirectPath);

// Just enough of the resolved membership for the mint to proceed.
internal record AdministrativeMember(string UserId, string Role);

public class InboundSsoService : IInboundSsoService
{
    // Long enough to survive a redirect, short enough that a ticket left in a
    // browser history or a referrer header is already dead.
    private static readonly TimeSpan TicketTtl = TimeSpan.FromSeconds(120);

    // Not a partner client, but the ticket store keys on one. A reserved value
    // rather than an empty string, so a partner ticket and an inbound ticket can
    // never be mistaken for each other if the store is ever inspected.
    private const string InboundClientId = "__inbound_sso__";

    private readonly IPartnerAuthStore _store;
    private readonly IDirectoryService _directory;
    private readonly IAuthService _auth;

    public InboundSsoService(
        IPartnerAuthStore store, IDirectoryService directory, IAuthService auth)
    {
        _store = store;
        _directory = directory;
        _auth = auth;
    }

    public async Task<InboundTicket?> MintAsync(string email, string organizationId)
    {
        var membership = await FindAdministrativeMemberAsync(email, organizationId);
        if (membership is null) return null;

        // The user id is resolved NOW and stored, rather than the email being
        // re-resolved at redemption: it removes a second lookup from the
        // callback, and pins the ticket to the account that existed when the
        // external platform asked.
        var ticket = await _store.MintTicketAsync(
            new PartnerTicketData(InboundClientId, membership.UserId, organizationId),
            TicketTtl);

        return new InboundTicket(ticket, $"/sso/callback?t={Uri.EscapeDataString(ticket)}");
    }

    public async Task<AuthResult?> RedeemAsync(string ticket)
    {
        // Single-use: the store deletes on read, so a replay finds nothing.
        var data = await _store.RedeemTicketAsync(ticket);
        if (data is null || data.ClientId != InboundClientId) return null;

        // Re-checked at redemption, not just at minting. Two minutes is short,
        // but a seat revoked inside that window must not still open a session.
        var membership = await _directory.GetMembershipAsync(data.OrganizationId, data.UserId);
        if (membership is null || !OrgRoles.IsAdministrative(membership.Role)) return null;

        // Mints the same access + refresh pair a login does, for that org.
        return await _auth.SwitchOrgAsync(data.UserId, data.OrganizationId);
    }

    // The account must be an ADMIN OR OWNER of this org. Matched on the
    // membership, never on the user alone: being an admin somewhere else is not
    // a way into this tenant.
    private async Task<AdministrativeMember?> FindAdministrativeMemberAsync(
        string email, string organizationId)
    {
        var wanted = email.Trim();
        if (wanted.Length == 0) return null;

        var users = await _directory.GetUsersAsync();
        var user = users.FirstOrDefault(u =>
            string.Equals(u.Email, wanted, StringComparison.OrdinalIgnoreCase));
        if (user is null) return null;

        var membership = await _directory.GetMembershipAsync(organizationId, user.Id);
        if (membership is null || !OrgRoles.IsAdministrative(membership.Role)) return null;

        return new AdministrativeMember(user.Id, membership.Role);
    }
}
