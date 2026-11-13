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

    private static readonly int[] Thirty = [30];
    private static readonly int[] SixtyAndThirty = [60, 30];

    private static (DocumentReminderStage, int) Before(int days) => (DocumentReminderStage.BEFORE, days);

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, DocumentReminderStage.BEFORE)]
    [InlineData(10, DocumentReminderStage.BEFORE)]   // a date entered late
    [InlineData(0, DocumentReminderStage.ON_THE_DAY)]
    [InlineData(-40, DocumentReminderStage.EXPIRED)] // already lapsed when switched on
    public void StageFor_PicksTheDueReminder(int daysLeft, DocumentReminderStage? expected) =>
        Assert.Equal(expected, DocumentExpiryReminderService.StageFor(daysLeft, [], Thirty));

    [Fact]
    public void StageFor_SendsEachReminderOnce()
    {
        Assert.Null(DocumentExpiryReminderService.StageFor(29, [Before(30)], Thirty));
        Assert.Null(DocumentExpiryReminderService.StageFor(0, [(DocumentReminderStage.ON_THE_DAY, 0)], Thirty));
        // The day after: the on-the-day reminder already said it expired.
        Assert.Null(DocumentExpiryReminderService.StageFor(-1, [(DocumentReminderStage.ON_THE_DAY, 0)], Thirty));
        Assert.Null(DocumentExpiryReminderService.StageFor(-5, [(DocumentReminderStage.EXPIRED, -1)], Thirty));
        // 30 days before doesn't stand in for the day itself.
        Assert.Equal(DocumentReminderStage.ON_THE_DAY,
            DocumentExpiryReminderService.StageFor(0, [Before(30)], Thirty));
    }

    // 60 and 30: one at 60, quiet until 30, one at 30.
    [Fact]
    public void StageFor_TwoReminders_OneInEachWindow()
    {
        Assert.Null(DocumentExpiryReminderService.StageFor(61, [], SixtyAndThirty));
        Assert.Equal(DocumentReminderStage.BEFORE, DocumentExpiryReminderService.StageFor(60, [], SixtyAndThirty));
        Assert.Null(DocumentExpiryReminderService.StageFor(45, [Before(60)], SixtyAndThirty));
        Assert.Equal(DocumentReminderStage.BEFORE, DocumentExpiryReminderService.StageFor(30, [Before(60)], SixtyAndThirty));
        Assert.Null(DocumentExpiryReminderService.StageFor(29, [Before(60), Before(30)], SixtyAndThirty));
        // Entered late, at 20 days: one reminder, not two.
        Assert.Equal(DocumentReminderStage.BEFORE, DocumentExpiryReminderService.StageFor(20, [], SixtyAndThirty));
        Assert.Null(DocumentExpiryReminderService.StageFor(19, [Before(20)], SixtyAndThirty));
    }

    // Changed from 60 to 30 after the 60-day one went: the 30-day one still goes.
    [Fact]
    public void StageFor_SettingChangedMidway_StillRemindsAtTheNewPoint()
    {
        Assert.Null(DocumentExpiryReminderService.StageFor(40, [Before(60)], Thirty));
        Assert.Equal(DocumentReminderStage.BEFORE, DocumentExpiryReminderService.StageFor(30, [Before(60)], Thirty));
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
        var mail = Assert.Single(world.Mail.Sent, m => m.To == "chong@x.test");
        Assert.Contains("Arjun", mail.Html);
        Assert.DoesNotContain("Rina", mail.Html);
    }

    // Only a limited admin ticked: the staff they don't cover still reach an
    // admin — the owner — rather than nobody.
    [Fact]
    public async Task StaffNoTickedAdminCovers_GoToTheOwner()
    {
        var world = new World();
        world.Staff("rina", "Indonesian", permitDays: 5);
        world.Staff("arjun", "Nepali", permitDays: 5);
        world.LimitedAdmin("chong", covers: ["arjun"]);
        await world.SaveSettingAsync(enabled: true, emailStaff: false, "chong");

        await world.RunAsync();

        Assert.Equal(["Rina's work permit expires in 5 days"],
            world.Notes.Sent.Where(n => n.UserId == "owner").Select(n => n.Title));
        Assert.Equal(["chong@x.test", "owner@x.test"], world.Mail.Sent.Select(m => m.To).Order());
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

    // The company's chosen timing drives the run: 60 and 30 days before.
    [Fact]
    public async Task ChosenDaysBefore_RemindAtSixtyThenThirty()
    {
        var world = new World();
        world.Staff("rina", "Indonesian", permitDays: 60);
        await world.SaveSettingAsync(enabled: true, emailStaff: false, first: 60, second: 30);

        Assert.Equal(1, (await world.RunAsync()).Reminders);
        Assert.Equal(0, (await world.RunAsync(Today.AddDays(15))).Reminders);
        Assert.Equal(1, (await world.RunAsync(Today.AddDays(30))).Reminders);
        Assert.Contains(world.Notes.Sent, n => n.Title == "Your work permit expires in 60 days");
        Assert.Contains(world.Notes.Sent, n => n.Title == "Your work permit expires in 30 days");
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

        public async Task SaveSettingAsync(bool enabled, bool emailStaff, params string[] admins) =>
            await SaveSettingAsync(enabled, emailStaff, 30, null, admins);

        public async Task SaveSettingAsync(bool enabled, bool emailStaff, int first, int? second, params string[] admins)
        {
            await using var db = Context(Org);
            var service = new NotificationSettingService(
                new NotificationSettingRepository(db), _directory,
                new StubCurrentUser { OrganizationId = Org, UserId = "owner" }, new FakeAuditService());
            await service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
                new UpdateNotificationSettingDto
                {
                    Enabled = enabled, EmailStaff = emailStaff, AdminUserIds = [.. admins],
                    FirstReminderDays = first, SecondReminderDays = second,
                });
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
        Assert.Equal("Work permit & passport expiry · to Ben · 30 days before · staff emailed", Assert.Single(audit.Written).Summary);
    }

    [Theory]
    [InlineData(45, null)]   // not one of the choices
    [InlineData(30, 30)]     // the second must be closer than the first
    [InlineData(30, 60)]
    public async Task ReminderDaysMustBeFromTheList_SecondCloserThanFirst(int first, int? second)
    {
        var (service, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
            new UpdateNotificationSettingDto { Enabled = true, FirstReminderDays = first, SecondReminderDays = second }));
    }

    [Fact]
    public async Task ReminderDays_DefaultThirty_SecondOptional()
    {
        var (service, _) = Create();
        Assert.Equal([30], (await service.ForOrgAsync("org-a", NotificationKind.DOCUMENT_EXPIRY)).ReminderDays);

        await service.UpdateAsync(NotificationKind.DOCUMENT_EXPIRY,
            new UpdateNotificationSettingDto { Enabled = true, FirstReminderDays = 60, SecondReminderDays = 14 });

        Assert.Equal([60, 14], (await service.ForOrgAsync("org-a", NotificationKind.DOCUMENT_EXPIRY)).ReminderDays);
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
