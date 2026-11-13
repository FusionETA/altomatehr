using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.DocumentExpiry;
using AltomateHR.Api.Modules.DocumentExpiry.Entities;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Notifications.Entities;
using AltomateHR.Api.Modules.NotificationSettings;
using AltomateHR.Api.Modules.NotificationSettings.Dtos;
using AltomateHR.Api.Modules.NotificationSettings.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Auth;
using AltomateHR.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.DocumentExpiry;

// The daily work permit / passport reminders: when each one goes, to whom,
// and that each goes only once per expiry date.
public class DocumentExpiryReminderTests
{
    private static readonly DateTime Today = new(2026, 11, 13);
    private const string Org = "org-a";

    // ---- When ----

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, DocumentReminderStage.BEFORE)]
    [InlineData(10, DocumentReminderStage.BEFORE)]   // a date entered late
    [InlineData(0, DocumentReminderStage.ON_THE_DAY)]
    [InlineData(-40, DocumentReminderStage.EXPIRED)] // already lapsed when switched on
    public void StageFor_PicksTheDueReminder(int daysLeft, DocumentReminderStage? expected) =>
        Assert.Equal(expected, DocumentExpiryReminderService.StageFor(daysLeft, []));

    [Fact]
    public void StageFor_SendsEachReminderOnce()
    {
        Assert.Null(DocumentExpiryReminderService.StageFor(29, [DocumentReminderStage.BEFORE]));
        Assert.Null(DocumentExpiryReminderService.StageFor(0, [DocumentReminderStage.ON_THE_DAY]));
        // The day after: the on-the-day reminder already said it expired.
        Assert.Null(DocumentExpiryReminderService.StageFor(-1, [DocumentReminderStage.ON_THE_DAY]));
        Assert.Null(DocumentExpiryReminderService.StageFor(-5, [DocumentReminderStage.EXPIRED]));
        // 30 days before doesn't stand in for the day itself.
        Assert.Equal(DocumentReminderStage.ON_THE_DAY,
            DocumentExpiryReminderService.StageFor(0, [DocumentReminderStage.BEFORE]));
    }

    // ---- Who ----

    [Fact]
    public async Task NobodyTicked_TheOwnerAndTheStaffMemberAreTold_StaffNotEmailedByDefault()
    {
        var world = new World();
        world.Staff("rina", "Indonesian", permitDays: 30);

        var result = await world.RunAsync();

        Assert.Equal(1, result.Reminders);
        Assert.Equal(
            [("owner", "Rina's work permit expires in 30 days"), ("rina", "Your work permit expires in 30 days")],
            world.Notes.Sent.Select(n => (n.UserId, n.Title)).OrderBy(x => x.UserId));
        Assert.Equal(["owner@x.test"], world.Mail.Sent.Select(m => m.To));
        Assert.All(world.Notes.Sent, n => Assert.Equal(NotificationType.DOCUMENT_EXPIRY, n.Type));
    }

    [Fact]
    public async Task TickedAdminsGetIt_AndStaffAreEmailedWhenSwitchedOn()
    {
        var world = new World();
        world.Staff("rina", "Indonesian", passportDays: 0);
        await world.SaveSettingAsync(enabled: true, emailStaff: true, "ben");

        await world.RunAsync();

        Assert.Equal(["ben@x.test", "rina@x.test"], world.Mail.Sent.Select(m => m.To).Order());
        Assert.Contains(world.Notes.Sent, n => n.UserId == "ben" && n.Title == "Rina's passport expires today");
        Assert.DoesNotContain(world.Notes.Sent, n => n.UserId == "owner");
        var staffMail = Assert.Single(world.Mail.Sent, m => m.To == "rina@x.test");
        Assert.Equal("Your passport expires today", staffMail.Subject);
    }

    [Fact]
    public async Task ALimitedAdmin_OnlyHearsAboutTheStaffTheirAccessCovers()
    {
        var world = new World();
        world.Staff("rina", "Indonesian", permitDays: 5);
        world.Staff("arjun", "Nepali", permitDays: 5);
        world.LimitedAdmin("chong", covers: ["arjun"]);
        await world.SaveSettingAsync(enabled: true, emailStaff: false, "chong");

        await world.RunAsync();

        Assert.Equal(["Arjun's work permit expires in 5 days"],
            world.Notes.Sent.Where(n => n.UserId == "chong").Select(n => n.Title));
        var mail = Assert.Single(world.Mail.Sent);
        Assert.Contains("Arjun", mail.Html);
        Assert.DoesNotContain("Rina", mail.Html);
    }

    [Fact]
    public async Task SwitchedOff_NothingIsSent()
    {
        var world = new World();
        world.Staff("rina", "Indonesian", permitDays: 0);
        await world.SaveSettingAsync(enabled: false, emailStaff: true);

        var result = await world.RunAsync();

        Assert.Equal(0, result.Reminders);
        Assert.Empty(world.Notes.Sent);
        Assert.Empty(world.Mail.Sent);
    }

    [Fact]
    public async Task CitizensPrsAndLeaversAreLeftOut()
    {
        var world = new World();
        world.Staff("ali", "Malaysian", permitDays: 0);
        world.Staff("pr", "Indonesian", permitDays: 0, pr: true);
        world.Staff("gone", "Indonesian", permitDays: 0, archived: true);

        var result = await world.RunAsync();

        Assert.Equal(0, result.Reminders);
    }

    // ---- Once ----

    [Fact]
    public async Task ARunTwiceTheSameDay_SendsNothingTheSecondTime_ANewDateStartsOver()
    {
        var world = new World();
        var rina = world.Staff("rina", "Indonesian", permitDays: 30);

        await world.RunAsync();
        var again = await world.RunAsync();
        Assert.Equal(0, again.Reminders);

        // A week later: still inside 30 days, already reminded.
        Assert.Equal(0, (await world.RunAsync(Today.AddDays(7))).Reminders);

        // Renewed to a date 30 days out from then: reminded again.
        await world.UpdateAsync(rina, p => p.WorkPermitExpiry = Today.AddDays(37));
        Assert.Equal(1, (await world.RunAsync(Today.AddDays(7))).Reminders);
    }

    [Fact]
    public async Task ManyDocumentsForOneAdmin_OneBellNote_EveryDocumentInTheEmail()
    {
        var world = new World();
        foreach (var name in new[] { "a1", "a2", "a3", "a4" })
            world.Staff(name, "Indonesian", permitDays: -3);

        await world.RunAsync();

        var ownerNote = Assert.Single(world.Notes.Sent, n => n.UserId == "owner");
        Assert.Equal("4 documents need renewing", ownerNote.Title);
        var mail = Assert.Single(world.Mail.Sent);
        Assert.Equal("4 documents need renewing at Alpha", mail.Subject);
        Assert.Equal(4, CountOf(mail.Html, "ALREADY EXPIRED"));
    }

    private static int CountOf(string s, string part) => (s.Length - s.Replace(part, "").Length) / part.Length;

    // ---- A company, its people, and the real settings + repository ----

    private sealed class World
    {
        private readonly string _db = $"doc-expiry-{Guid.NewGuid()}";
        private readonly Directory _directory = new();
        private readonly Scopes _scopes = new();
        public FakeNotificationService Notes { get; } = new();
        public CapturingEmailSender Mail { get; } = new();

        public World()
        {
            using var db = Context(null);
            db.Organizations.Add(new Organization { Id = Org, Name = "Alpha" });
            db.SaveChanges();
            Member("owner", "Owner");
            Member("ben", "Admin");
        }

        private AppDbContext Context(string? org) =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_db).Options,
                new StubCurrentUser { OrganizationId = org, UserId = "owner", Role = "Owner" });

        private OrganizationMembership Member(string id, string role, string? policyScope = null)
        {
            var name = char.ToUpperInvariant(id[0]) + id[1..];
            _directory.Users[id] = new User { Id = id, Name = name, Email = $"{id}@x.test" };
            var m = new OrganizationMembership { OrganizationId = Org, UserId = id, Role = role, PolicyScope = policyScope };
            _directory.Members.Add(m);
            return m;
        }

        public string Staff(string id, string nationality, int? permitDays = null, int? passportDays = null,
            bool pr = false, bool archived = false)
        {
            Member(id, "Employee");
            using var db = Context(null);
            var profile = new EmployeeProfile
            {
                OrganizationId = Org,
                UserId = id,
                Nationality = nationality,
                HasPr = pr,
                IsArchived = archived,
                WorkPermitNumber = permitDays is null ? null : $"PLKS-{id}",
                WorkPermitExpiry = permitDays is { } p ? Today.AddDays(p) : null,
                IdType = IdType.PASSPORT,
                IdNumber = $"P-{id}",
                PassportExpiry = passportDays is { } d ? Today.AddDays(d) : null,
            };
            db.EmployeeProfiles.Add(profile);
            db.SaveChanges();
            return profile.Id;
        }

        public void LimitedAdmin(string id, string[] covers)
        {
            Member(id, "Admin", policyScope: "pol-x");
            _scopes.Covers[id] = covers;
        }

        public async Task UpdateAsync(string profileId, Action<EmployeeProfile> change)
        {
            await using var db = Context(null);
            var p = await db.EmployeeProfiles.SingleAsync(x => x.Id == profileId);
            change(p);
            await db.SaveChangesAsync();
        }

        public async Task SaveSettingAsync(bool enabled, bool emailStaff, params string[] admins)
        {
            await using var db = Context(Org);
            var service = new NotificationSettingService(
                new NotificationSettingRepository(db), _directory,
                new StubCurrentUser { OrganizationId = Org, UserId = "owner" }, new FakeAuditService());
            await service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
                new UpdateNotificationSettingDto { Enabled = enabled, EmailStaff = emailStaff, AdminUserIds = [.. admins] });
        }

        public async Task<DocumentExpiryRunResult> RunAsync(DateTime? today = null)
        {
            // Like the background job: no signed-in company.
            await using var db = Context(null);
            var settings = new NotificationSettingService(
                new NotificationSettingRepository(db), _directory,
                new StubCurrentUser { OrganizationId = null, UserId = null }, new FakeAuditService());
            var service = new DocumentExpiryReminderService(
                new DocumentExpiryRepository(db), settings, _directory, _scopes, Notes, Mail,
                Options.Create(new PortalOptions { BaseUrl = "https://app.test" }),
                NullLogger<DocumentExpiryReminderService>.Instance);
            return await service.RunAsync(today ?? Today);
        }
    }

    private sealed class Scopes : IEmployeeScopeResolver
    {
        public Dictionary<string, string[]> Covers { get; } = new();

        public Task<ScopedEmployees> ResolveAsync(string organizationId, string adminUserId, IReadOnlyList<string> policyIds) =>
            Task.FromResult(new ScopedEmployees(
                [.. Covers.GetValueOrDefault(adminUserId, []).Append(adminUserId).Select(u => new ScopedEmployee(u, null))]));
    }

    private sealed class Directory : IDirectoryService
    {
        public Dictionary<string, User> Users { get; } = new();
        public List<OrganizationMembership> Members { get; } = [];

        public Task<User?> GetUserAsync(string id) => Task.FromResult(Users.GetValueOrDefault(id));
        public Task<List<User>> GetUsersAsync() => Task.FromResult(Users.Values.ToList());
        public Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync() => Task.FromResult(Members.ToList());
        public Task<List<OrganizationMembership>> GetMembershipsForOrgAsync(string organizationId) =>
            Task.FromResult(Members.Where(m => m.OrganizationId == organizationId).ToList());
        public Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId) =>
            Task.FromResult(Members.FirstOrDefault(m => m.OrganizationId == organizationId && m.UserId == userId));
        public Task<OrganizationMembership?> GetMembershipForUserAsync(string userId) =>
            Task.FromResult(Members.FirstOrDefault(m => m.UserId == userId));
        public Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId) =>
            Task.FromResult(Members.Where(m => m.UserId == userId).ToList());
        public Task<int> CountMembershipsByShiftAsync(string shiftId) => Task.FromResult(0);
        public Task<List<EmployeeProfile>> GetProfilesForCurrentOrgAsync() => Task.FromResult(new List<EmployeeProfile>());
    }
}

// Settings → Notifications on its own.
public class NotificationSettingServiceTests
{
    [Fact]
    public async Task Defaults_On_ToTheOwner_StaffNotEmailed()
    {
        var (service, _) = Create();

        var dto = await service.GetAsync();

        var setting = Assert.Single(dto.Notifications);
        Assert.Equal(NotificationKind.DOCUMENT_EXPIRY, setting.Kind);
        Assert.True(setting.Enabled);
        Assert.False(setting.EmailStaff);
        Assert.Empty(setting.AdminUserIds);
        Assert.Null(setting.UpdatedAt);
        // The owner first; the limited admin is marked.
        Assert.Equal(["owner", "ben", "chong"], dto.Admins.Select(a => a.UserId));
        Assert.True(dto.Admins.Single(a => a.UserId == "chong").IsLimited);
    }

    [Fact]
    public async Task OnlyAnAdminOrTheOwnerCanBePicked()
    {
        var (service, audit) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
            new UpdateNotificationSettingDto { Enabled = true, AdminUserIds = ["staff"] }));

        var saved = await service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
            new UpdateNotificationSettingDto { Enabled = true, AdminUserIds = ["ben", "ben"], EmailStaff = true });
        Assert.Equal(["ben"], saved.AdminUserIds);
        Assert.Equal("Work permit & passport expiry · to Ben · staff emailed", Assert.Single(audit.Written).Summary);
    }

    [Fact]
    public async Task ATickedAdminWhoLeft_FallsBackToTheOwner()
    {
        var (service, _, directory) = CreateWithDirectory();
        await service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
            new UpdateNotificationSettingDto { Enabled = true, AdminUserIds = ["ben"] });
        directory.RemoveAll(m => m.UserId == "ben");

        var effective = await service.ForOrgAsync("org-a", NotificationKind.DOCUMENT_EXPIRY);

        Assert.Equal(["owner"], effective.Admins.Select(a => a.UserId));
    }

    private static (NotificationSettingService, FakeAuditService) Create()
    {
        var (s, a, _) = CreateWithDirectory();
        return (s, a);
    }

    private static (NotificationSettingService, FakeAuditService, List<OrganizationMembership>) CreateWithDirectory()
    {
        var members = new List<OrganizationMembership>
        {
            new() { OrganizationId = "org-a", UserId = "chong", Role = "Admin", PolicyScope = "pol-1" },
            new() { OrganizationId = "org-a", UserId = "ben", Role = "Admin" },
            new() { OrganizationId = "org-a", UserId = "owner", Role = "Owner" },
            new() { OrganizationId = "org-a", UserId = "staff", Role = "Employee" },
        };
        var users = members.ToDictionary(m => m.UserId,
            m => new User { Id = m.UserId, Name = char.ToUpperInvariant(m.UserId[0]) + m.UserId[1..], Email = $"{m.UserId}@x.test" });
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ns-{Guid.NewGuid()}").Options,
            new StubCurrentUser { OrganizationId = "org-a" });
        var audit = new FakeAuditService();
        var service = new NotificationSettingService(
            new NotificationSettingRepository(db), new MembersDirectory(members, users),
            new StubCurrentUser { OrganizationId = "org-a", UserId = "owner" }, audit);
        return (service, audit, members);
    }

    private sealed class MembersDirectory(List<OrganizationMembership> members, Dictionary<string, User> users) : IDirectoryService
    {
        public Task<User?> GetUserAsync(string id) => Task.FromResult(users.GetValueOrDefault(id));
        public Task<List<User>> GetUsersAsync() => Task.FromResult(users.Values.ToList());
        public Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync() => Task.FromResult(members.ToList());
        public Task<List<OrganizationMembership>> GetMembershipsForOrgAsync(string organizationId) => Task.FromResult(members.ToList());
        public Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId) =>
            Task.FromResult(members.FirstOrDefault(m => m.UserId == userId));
        public Task<OrganizationMembership?> GetMembershipForUserAsync(string userId) =>
            Task.FromResult(members.FirstOrDefault(m => m.UserId == userId));
        public Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId) => Task.FromResult(new List<OrganizationMembership>());
        public Task<int> CountMembershipsByShiftAsync(string shiftId) => Task.FromResult(0);
        public Task<List<EmployeeProfile>> GetProfilesForCurrentOrgAsync() => Task.FromResult(new List<EmployeeProfile>());
    }
}
