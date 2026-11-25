using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Entities;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace AltomateHR.Api.Tests.Payroll;

// A person's MAIN project (EmployeeProfile.MainProjectId): the project their
// pay posts under in the journal when they are on teams in more than one.
//
//   · one project → that project, nothing to choose
//   · several, main chosen (and still theirs) → the main project, and the AI
//     split's PROJECT rules see only it
//   · several, none chosen (or one they have left) → "(No project)", flagged
//
// And with the journal set to "Sum by project", the AI split keeps one line
// per project inside each people rule, tagged like the standard journal.
public class PayrollJournalMainProjectTests : IDisposable
{
    private const string SiteA = "24X - SITE A";
    private const string SiteB = "25K - SITE B";
    private const string Hq = "HQ - SALES & EXPENSES";

    private readonly AppDbContext _db;
    private readonly PayrollXeroSyncService _service;

    public PayrollJournalMainProjectTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-main-project-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, new StubCurrentUser());

        var audit = new FakeAuditService();
        _service = new PayrollXeroSyncService(
            new PayrollRunRepository(_db),
            new PayslipRepository(_db),
            new PayrollSettingsService(new PayrollSettingsRepository(_db), audit),
            new ChartOfAccountRepository(_db),
            new FakeClaimsService(),
            new NoTrackingXero(),
            audit,
            new EmployeeProfileRepository(_db),
            NullLogger<PayrollXeroSyncService>.Instance,
            new TeamRepository(_db),
            new TeamMembershipRepository(_db),
            new ProjectRepository(_db));
    }

    public void Dispose() => _db.Dispose();

    // ─── The service: who posts under which project ─────────────────────

    private async Task SeedAsync()
    {
        foreach (var slot in PayrollXeroAccounts.All)
        {
            _db.ChartOfAccounts.Add(new ChartOfAccount
            {
                Id = $"local-{slot}", OrganizationId = "org-1", Code = $"CODE-{slot}", Name = slot,
                XeroAccountId = $"xero-{slot}",
            });
        }

        var mapping = new PayrollXeroMapping
        {
            AggregationMode = XeroAggregationMode.SUM_BY_PROJECT,
            Accounts = PayrollXeroAccounts.All.ToDictionary(
                slot => slot, slot => (string?)$"xero-{slot}", StringComparer.Ordinal),
        };
        _db.PayrollSettings.Add(new PayrollSettings
        {
            OrganizationId = "org-1",
            XeroMappingJson = JsonSerializer.Serialize(mapping, PayrollSnapshotJson.Options),
        });

        _db.Projects.AddRange(
            new Project { Id = "p-a", OrganizationId = "org-1", Name = SiteA },
            new Project { Id = "p-b", OrganizationId = "org-1", Name = SiteB },
            new Project { Id = "p-old", OrganizationId = "org-1", Name = "OLD SITE", IsArchived = true });
        _db.Teams.AddRange(
            new Team { Id = "t-a", OrganizationId = "org-1", ProjectId = "p-a", Name = "PROJECT", LayerCount = 2 },
            new Team { Id = "t-b", OrganizationId = "org-1", ProjectId = "p-b", Name = "PROJECT", LayerCount = 2 });

        // 1: Site A only. 2: both, main = B. 3: both, no main.
        // 4: both, "main" is a project they are not on → ignored.
        // 5: approver on A (layer 1) and member of B, main = A — seats count.
        Person(1, 1_000m, null, ("t-a", 0));
        Person(2, 2_000m, "p-b", ("t-a", 0), ("t-b", 0));
        Person(3, 3_000m, null, ("t-a", 0), ("t-b", 0));
        Person(4, 4_000m, "p-old", ("t-a", 0), ("t-b", 0));
        Person(5, 5_000m, "p-a", ("t-a", 1), ("t-b", 0));

        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = "run-1", OrganizationId = "org-1", PeriodYear = 2026, PeriodMonth = 11,
            Status = PayrollRunStatus.DRAFT,
        });

        await _db.SaveChangesAsync();
    }

    private void Person(int n, decimal pay, string? mainProjectId, params (string Team, int Layer)[] seats)
    {
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = $"emp-{n}", OrganizationId = "org-1", UserId = $"usr-{n}", MainProjectId = mainProjectId,
        });
        foreach (var (team, layer) in seats)
        {
            _db.TeamMemberships.Add(new TeamMembership
            {
                OrganizationId = "org-1", TeamId = team, EmployeeId = $"usr-{n}", Layer = layer,
            });
        }

        // No statutory deductions, so net = pay and the journal balances.
        _db.Payslips.Add(new Payslip
        {
            Id = $"slip-{n}", OrganizationId = "org-1", PayrollRunId = "run-1",
            EmployeeProfileId = $"emp-{n}", UserId = $"usr-{n}", SnapshotName = $"Person {n}",
            ProratedPay = pay, GrossPay = pay, NetPay = pay,
        });
    }

    private static decimal Salary(PayrollXeroPreview preview, string project) =>
        preview.Lines.Where(l => l.Description == $"SALARY - {project}").Sum(l => l.Amount);

    [Fact]
    public async Task Each_persons_pay_posts_under_their_only_or_main_project()
    {
        await SeedAsync();

        var preview = await _service.PreviewAsync("run-1");

        Assert.NotNull(preview);
        Assert.True(preview.Ok, preview.Error);
        Assert.Equal(1_000m + 5_000m, Salary(preview, SiteA));   // only project; main = A
        Assert.Equal(2_000m, Salary(preview, SiteB));             // main = B
        Assert.Equal(3_000m + 4_000m, Salary(preview, PayrollJournal.NoProject));
        Assert.Equal(0m, preview.Balance);
    }

    [Fact]
    public async Task People_on_several_projects_without_a_usable_main_project_are_named()
    {
        await SeedAsync();

        var preview = await _service.PreviewAsync("run-1");

        var warning = Assert.Single(preview!.Checks!, c => c.Warning);
        Assert.Contains("2 on more than one project", warning.Label);
        Assert.Contains("Person 3", warning.Detail);
        Assert.Contains("Person 4", warning.Detail);   // a main project they aren't on is ignored
        Assert.DoesNotContain("Person 2", warning.Detail);
        Assert.DoesNotContain("Person 5", warning.Detail);
    }

    [Fact]
    public async Task Settings_list_names_current_staff_still_needing_a_main_project()
    {
        await SeedAsync();
        foreach (var n in new[] { 1, 2, 3, 4, 5 })
            _db.Users.Add(new User { Id = $"usr-{n}", Name = $"Person {n}", Email = $"p{n}@example.test" });
        // Archived: on both projects with no main, but no longer paid — not listed.
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-6", OrganizationId = "org-1", UserId = "usr-6", IsArchived = true,
        });
        _db.TeamMemberships.AddRange(
            new TeamMembership { OrganizationId = "org-1", TeamId = "t-a", EmployeeId = "usr-6" },
            new TeamMembership { OrganizationId = "org-1", TeamId = "t-b", EmployeeId = "usr-6" });
        _db.Users.Add(new User { Id = "usr-6", Name = "Person 6", Email = "p6@example.test" });
        await _db.SaveChangesAsync();

        var audit = new FakeAuditService();
        var service = new PayrollXeroSyncService(
            new PayrollRunRepository(_db), new PayslipRepository(_db),
            new PayrollSettingsService(new PayrollSettingsRepository(_db), audit),
            new ChartOfAccountRepository(_db), new FakeClaimsService(), new NoTrackingXero(), audit,
            new EmployeeProfileRepository(_db), NullLogger<PayrollXeroSyncService>.Instance,
            new TeamRepository(_db), new TeamMembershipRepository(_db), new ProjectRepository(_db),
            new DirectoryService(
                new OrganizationMembershipRepository(_db), new EmployeeProfileRepository(_db), new UserRepository(_db)));

        Assert.Equal(["Person 3", "Person 4"], await service.GetPeopleNeedingMainProjectAsync());
    }

    // ─── The AI split ───────────────────────────────────────────────────

    private static PayrollJournal.Input Input(XeroAggregationMode mode, params PayrollJournal.EmployeeRow[] rows) => new()
    {
        PeriodYear = 2026,
        PeriodMonth = 11,
        Mapping = new PayrollXeroMapping
        {
            AggregationMode = mode,
            Accounts = PayrollXeroAccounts.All.ToDictionary(
                slot => slot, slot => (string?)$"acct-{slot}", StringComparer.Ordinal),
        },
        AccountCodeById = PayrollXeroAccounts.All.ToDictionary(
            slot => $"acct-{slot}", slot => slot == PayrollXeroAccounts.Salary ? "6100" : $"C-{slot}",
            StringComparer.Ordinal),
        Rows = rows,
    };

    private static PayrollJournal.EmployeeRow Row(
        string id, decimal pay, IReadOnlyList<string> projects, string? main = null) => new()
    {
        EmployeeName = $"Person {id}",
        EmployeeId = id,
        Projects = projects,
        MainProject = main,
        ProjectName = projects.Count == 1 ? projects[0] : main ?? PayrollJournal.NoProject,
        ProratedPay = pay,
        NetPay = pay,
    };

    private static PayrollJournalSplit.Context Context() => new()
    {
        ActiveAccounts = new[] { "300/214", "520/021" }
            .ToDictionary(c => c, c => $"{c} · account", StringComparer.OrdinalIgnoreCase),
        Projects = new HashSet<string>([SiteA, SiteB, Hq], StringComparer.OrdinalIgnoreCase),
        TrackingCategories = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = new HashSet<string>([SiteA, Hq], StringComparer.OrdinalIgnoreCase),
        },
    };

    private static PayrollJournalSplitRule Rule(
        SplitBasis basis, string code, IReadOnlyList<string>? match = null,
        IReadOnlyList<PayrollJournalSplitTag>? tracking = null) => new()
    {
        Source = PayrollXeroAccounts.Salary,
        Basis = basis,
        AccountCode = code,
        Match = match ?? [],
        Tracking = tracking ?? [],
    };

    private static readonly PayrollJournalSplit.ByProject ByProject =
        new("Project", new HashSet<string>([SiteA, Hq], StringComparer.OrdinalIgnoreCase));

    private static PayrollJournalSplit.Result Split(
        PayrollJournal.Input input, PayrollJournalSplit.ByProject? byProject,
        params PayrollJournalSplitRule[] rules) =>
        PayrollJournalSplit.Apply(PayrollJournal.Build(input), input.Rows, rules, Context(), byProject);

    [Fact]
    public void A_project_rule_sees_only_the_main_project()
    {
        // On HQ and Site A, main = Site A. HQ's rule comes first, but must not
        // take them.
        var result = Split(
            Input(XeroAggregationMode.PER_EMPLOYEE, Row("1", 4_000m, [Hq, SiteA], main: SiteA)),
            null,
            Rule(SplitBasis.PROJECT, "520/021", [Hq]),
            Rule(SplitBasis.PROJECT, "300/214", [SiteA]));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(4_000m, result.Lines.Where(l => l.AccountCode == "300/214").Sum(l => l.Amount));
        Assert.Equal(0m, result.Lines.Where(l => l.AccountCode == "520/021").Sum(l => l.Amount));
        Assert.DoesNotContain(result.Checks, c => c.Label == "Staff in more than one project");
    }

    [Fact]
    public void Without_a_main_project_the_first_matching_rule_takes_them_and_says_so()
    {
        var result = Split(
            Input(XeroAggregationMode.PER_EMPLOYEE, Row("1", 4_000m, [Hq, SiteA])),
            null,
            Rule(SplitBasis.PROJECT, "520/021", [Hq]),
            Rule(SplitBasis.PROJECT, "300/214", [SiteA]));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(4_000m, result.Lines.Where(l => l.AccountCode == "520/021").Sum(l => l.Amount));
        var warning = Assert.Single(result.Checks, c => c.Label == "Staff in more than one project");
        Assert.Contains("main project", warning.Detail);
    }

    [Fact]
    public void By_project_each_people_rule_posts_one_tagged_line_per_project()
    {
        var input = Input(
            XeroAggregationMode.SUM_BY_PROJECT,
            Row("1", 1_000m, [SiteA]),
            Row("2", 2_000m, [SiteA, SiteB], main: SiteB),
            Row("3", 3_000m, [SiteB]),
            Row("4", 500m, [Hq]));

        var result = Split(input, ByProject,
            Rule(SplitBasis.PROJECT, "520/021", [Hq]),
            Rule(SplitBasis.REMAINDER, "300/214"));

        Assert.True(result.Ok, result.Error);
        var site = result.Lines.Where(l => l.AccountCode == "300/214").ToList();
        Assert.Equal(2, site.Count);

        var a = Assert.Single(site, l => l.Description.EndsWith(SiteA));
        Assert.Equal(1_000m, a.Amount);
        Assert.Equal(SiteA, Assert.Single(a.AllTags).Option);

        // Site B isn't an option in the tracking category: its own line, untagged.
        var b = Assert.Single(site, l => l.Description.EndsWith(SiteB));
        Assert.Equal(5_000m, b.Amount);
        Assert.Empty(b.AllTags);

        var hq = Assert.Single(result.Lines, l => l.AccountCode == "520/021");
        Assert.Equal(500m, hq.Amount);
        Assert.Equal(Hq, Assert.Single(hq.AllTags).Option);
    }

    [Fact]
    public void A_rule_with_its_own_tracking_keeps_one_line()
    {
        var input = Input(
            XeroAggregationMode.SUM_BY_PROJECT,
            Row("1", 1_000m, [SiteA]),
            Row("2", 3_000m, [SiteB]));

        var result = Split(input, ByProject,
            Rule(SplitBasis.EVERYONE, "300/214",
                tracking: [new PayrollJournalSplitTag("Project", SiteA)]));

        Assert.True(result.Ok, result.Error);
        var line = Assert.Single(result.Lines, l => l.AccountCode == "300/214");
        Assert.Equal(4_000m, line.Amount);
        Assert.Equal(SiteA, Assert.Single(line.AllTags).Option);
    }

    [Fact]
    public void Not_by_project_a_people_rule_is_still_one_line()
    {
        var input = Input(
            XeroAggregationMode.PER_EMPLOYEE,
            Row("1", 1_000m, [SiteA]),
            Row("2", 3_000m, [SiteB]));

        var result = Split(input, null, Rule(SplitBasis.EVERYONE, "300/214"));

        Assert.True(result.Ok, result.Error);
        var line = Assert.Single(result.Lines, l => l.AccountCode == "300/214");
        Assert.Equal(4_000m, line.Amount);
        Assert.Empty(line.AllTags);
    }

    // ─── Doubles ────────────────────────────────────────────────────────

    private sealed class NoTrackingXero : IXeroService
    {
        public Task<IReadOnlyList<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync() =>
            Task.FromResult<IReadOnlyList<XeroTrackingCategoryResponse>>([]);
        public Task<bool> IsConnectedAsync() => Task.FromResult(true);
        public Task<XeroManualJournalResponse> CreateManualJournalAsync(XeroManualJournalRequest journal) =>
            throw new NotSupportedException();
        public Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? returnUrl) => throw new NotSupportedException();
        public Task<string> CompleteCallbackAsync(string code, string state) => throw new NotSupportedException();
        public Task<XeroStatusDto> GetStatusAsync() => throw new NotSupportedException();
        public Task DisconnectAsync() => throw new NotSupportedException();
        public Task<XeroSyncAccountsResultDto> SyncAccountsAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<XeroCurrencyResponse>> GetCurrenciesAsync() => throw new NotSupportedException();
        public Task<XeroSyncProjectsResultDto> SyncProjectsAsync() => throw new NotSupportedException();
        public Task<XeroFileContent?> GetFileContentAsync(string fileId) => throw new NotSupportedException();
        public Task<XeroBillResponse> CreateBillAsync(XeroBillRequest bill) => throw new NotSupportedException();
        public Task<XeroSpendResponse> CreateSpendAsync(XeroSpendRequest spend) => throw new NotSupportedException();
        public Task<XeroProjectTrackingDto> GetProjectTrackingAsync() => throw new NotSupportedException();
        public Task<XeroSyncProjectsResultDto?> SetProjectTrackingCategoryAsync(string? categoryId) =>
            throw new NotSupportedException();
        public Task<bool> TryAttachFileAsync(string fileId, string objectId, XeroAttachTarget target) =>
            Task.FromResult(false);
        public Task<XeroUploadedFile?> TryUploadFileAsync(
            string folderName, byte[] content, string fileName, string contentType) =>
            Task.FromResult<XeroUploadedFile?>(null);
    }
}
