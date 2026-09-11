using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// The run service against a real EF context, so the tenant filter, the unique
// (org, period) constraint and the destructive regeneration all run for real.
public class PayrollRunServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly StubPayrollXeroSync _xeroSync = new();
    private EmployeeLoanService _loanService = null!;
    private readonly StubPayrollHours _hours = new();
    private readonly StubPayrollLeave _leave = new();
    private readonly PayrollRunService _service;
    private readonly PayslipRepository _payslips;
    private readonly PayrollRunAdjustmentRepository _adjustments;
    private readonly PayrollRunClaimRepository _runClaims;

    public PayrollRunServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-runs-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _payslips = new PayslipRepository(_db);
        _adjustments = new PayrollRunAdjustmentRepository(_db);
        _runClaims = new PayrollRunClaimRepository(_db);

        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            new EmployeeProfileRepository(_db));

        // The real loan service over the same context, so a loan's
        // repayment reaches generation for real rather than through a stub
        // that always says nothing is owed.
        _loanService = new EmployeeLoanService(
            new EmployeeLoanRepository(_db),
            new PayrollRunRepository(_db),
            directory,
            _audit);

        _service = new PayrollRunService(
            new PayrollRunRepository(_db),
            _payslips,
            new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit),
            directory,
            _adjustments,
            _runClaims,
            // The real policy service over the same in-memory context, so the
            // OT gate (OtEnabled / CASH vs TIME_BANK) is exercised for real
            // rather than through a stand-in that always says yes.
            new PolicyService(
                new EmployeePolicyRepository(_db),
                new PolicyLeaveEntitlementRepository(_db),
                directory),
            // The real statutory service over the same context, so the
            // readiness guard on submit is exercised rather than stubbed away.
            new StatutoryFileService(
                new PayrollRunRepository(_db),
                new PayslipRepository(_db),
                new PayrollCompanyInfoRepository(_db),
                directory,
                new StubPayrollOrganizations(),
                _currentUser,
                new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit)),
            _hours,
            _leave,
            _currentUser,
            _audit,
            _xeroSync,
            _loanService);
    }

    public void Dispose() => _db.Dispose();

    // ─── Fixtures ───────────────────────────────────────────────────────

    private EmployeeProfile AddEmployee(
        string userId,
        string name,
        decimal? monthlySalary = 5000m,
        DateTime? joinDate = null,
        DateTime? leaveDate = null,
        bool isArchived = false,
        bool reportedToLhdn = false,
        string? employeeNumber = "E-001",
        string? jobTitle = "Technician",
        string? fixedAllowancesJson = null,
        string organizationId = "org-1")
    {
        _db.Users.Add(new User
        {
            Id = userId,
            Email = $"{userId}@altomate.com",
            Name = name,
            PasswordHash = "x",
        });

        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = "Employee",
            EmployeeNumber = employeeNumber,
            JobTitle = jobTitle,
        });

        var profile = new EmployeeProfile
        {
            OrganizationId = organizationId,
            UserId = userId,
            Nationality = "Malaysian",
            DateOfBirth = new DateTime(1990, 6, 15),
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = monthlySalary,
            EpfEmployeeRate = 11m,
            SocsoScheme = SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            ContributeToEis = true,
            IncomeTaxNumber = "SG1",
            EpfNumber = "EPF1",
            SocsoNumber = "SOC1",
            JoinDate = joinDate,
            LeaveDate = leaveDate,
            IsArchived = isArchived,
            ReportedToLhdn = reportedToLhdn,
            FixedAllowancesJson = fixedAllowancesJson,
        };

        _db.EmployeeProfiles.Add(profile);
        _db.SaveChanges();

        return profile;
    }

    private static CreatePayrollRunDto Period(int year = 2026, int month = 1) =>
        new() { PeriodYear = year, PeriodMonth = month };

    private async Task<PayrollRunDto> CreateRunAsync(int year = 2026, int month = 1)
    {
        var created = await _service.CreateAsync(Period(year, month));
        Assert.True(created.Ok);
        return created.Run!;
    }

    // ─── Creating a run ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_StartsADraftRunForThePeriod()
    {
        var run = await CreateRunAsync(2026, 3);

        Assert.Equal(PayrollRunStatus.DRAFT, run.Status);
        Assert.Equal(PayrollRunSource.COMPUTED, run.Source);
        Assert.Equal("March 2026", run.PeriodLabel);
        Assert.Null(run.GeneratedAt);
        Assert.Equal(0, run.EmployeeCount);
    }

    // One run per period is what makes "the January run" unambiguous for every
    // statutory filing, so a second attempt is a conflict rather than a new run.
    [Fact]
    public async Task CreateAsync_RefusesASecondRunForTheSamePeriod()
    {
        await CreateRunAsync();

        var again = await _service.CreateAsync(Period());

        Assert.False(again.Ok);
        Assert.NotNull(again.Error);
        Assert.Single(await _service.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_IsAudited()
    {
        await CreateRunAsync();

        Assert.True(_audit.Recorded(AuditActions.PayrollRunCreate));
    }

    // ─── Generating payslips ────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_ProducesOnePayslipPerEmployee()
    {
        AddEmployee("usr-1", "Aisyah");
        AddEmployee("usr-2", "Bala");
        var run = await CreateRunAsync();

        var result = await _service.GenerateAsync(run.Id);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Result!.PayslipCount);
        Assert.Empty(result.Result.SkippedEmployees);
    }

    // The whole reason a payslip exists: raising someone's salary in March must
    // not rewrite what January paid them.
    [Fact]
    public async Task GenerateAsync_SnapshotsIdentityAndSalary()
    {
        var profile = AddEmployee("usr-1", "Aisyah", employeeNumber: "E-42", jobTitle: "Foreman");
        var run = await CreateRunAsync();

        var result = await _service.GenerateAsync(run.Id);
        var payslip = Assert.Single(result.Result!.Detail.Payslips);

        Assert.Equal(profile.Id, payslip.EmployeeProfileId);
        Assert.Equal("usr-1", payslip.UserId);
        Assert.Equal("Aisyah", payslip.SnapshotName);
        Assert.Equal("E-42", payslip.SnapshotEmployeeNumber);
        Assert.Equal("Foreman", payslip.SnapshotPosition);
        Assert.Equal("Malaysian", payslip.SnapshotNationality);
        Assert.Equal(5000m, payslip.SnapshotMonthlySalary);
        Assert.Equal(SalaryType.MONTHLY, payslip.SnapshotSalaryType);

        // The EPF rates that actually applied, not the profile's declared rate.
        Assert.NotNull(payslip.SnapshotEpfRatesJson);
        Assert.Contains("MALAYSIAN_UNDER_60", payslip.SnapshotEpfRatesJson);
    }

    [Fact]
    public async Task GenerateAsync_RollsThePayslipsUpIntoTheRunTotals()
    {
        AddEmployee("usr-1", "Aisyah");
        AddEmployee("usr-2", "Bala", monthlySalary: 3000m);
        var run = await CreateRunAsync();

        var detail = (await _service.GenerateAsync(run.Id)).Result!.Detail;

        Assert.Equal(2, detail.Run.EmployeeCount);
        Assert.Equal(8000m, detail.Run.TotalGross);
        Assert.Equal(detail.Payslips.Sum(p => p.NetPay), detail.Run.TotalNet);
        Assert.Equal(detail.Payslips.Sum(p => p.EpfEmployer), detail.Run.TotalEmployerEpf);
        Assert.Equal(
            detail.Payslips.Sum(p => p.TotalCostToEmployer),
            detail.Run.TotalCostToEmployer);
        Assert.NotNull(detail.Run.GeneratedAt);
    }

    // Generation is destructive on purpose: it discards the previous payslips
    // rather than merging into them, which is what makes a draft safe to re-run
    // after fixing a profile.
    [Fact]
    public async Task GenerateAsync_RebuildsFromScratchRatherThanAccumulating()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();

        await _service.GenerateAsync(run.Id);
        var firstIds = (await _payslips.GetForRunAsync(run.Id)).Select(p => p.Id).ToList();

        // Fix the salary and re-run.
        profile.MonthlySalary = 6000m;
        await _db.SaveChangesAsync();

        var detail = (await _service.GenerateAsync(run.Id)).Result!.Detail;

        var payslip = Assert.Single(detail.Payslips);
        Assert.Equal(6000m, payslip.SnapshotMonthlySalary);
        Assert.Equal(6000m, payslip.GrossPay);

        // The old row is gone, not shadowed by a second one.
        var stored = await _payslips.GetForRunAsync(run.Id);
        Assert.Single(stored);
        Assert.DoesNotContain(stored[0].Id, firstIds);
    }

    [Fact]
    public async Task GenerateAsync_ReplacesLineItemsToo()
    {
        var profile = AddEmployee(
            "usr-1", "Aisyah",
            fixedAllowancesJson:
            """[{"category":"allowance_standard","name":"Site","amount":300}]""");
        var run = await CreateRunAsync();

        await _service.GenerateAsync(run.Id);
        Assert.Single(await _payslips.GetLineItemsForRunAsync(run.Id));

        profile.FixedAllowancesJson = null;
        await _db.SaveChangesAsync();

        await _service.GenerateAsync(run.Id);
        Assert.Empty(await _payslips.GetLineItemsForRunAsync(run.Id));
    }

    // A submitted run is a figure KWSP, PERKESO and LHDN have already been told.
    [Fact]
    public async Task GenerateAsync_RefusesToRebuildARunThatIsNoLongerADraft()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();
        await _service.GenerateAsync(run.Id);

        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == run.Id);
        stored.Status = PayrollRunStatus.SUBMITTED;
        await _db.SaveChangesAsync();

        var result = await _service.GenerateAsync(run.Id);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    // Null Result with a null Error is the service's "not found".
    [Fact]
    public async Task GenerateAsync_ReportsAnUnknownRunAsNotFound()
    {
        var result = await _service.GenerateAsync("run-that-never-was");

        Assert.False(result.Ok);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GenerateAsync_IsAudited()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();

        await _service.GenerateAsync(run.Id);

        Assert.True(_audit.Recorded(AuditActions.PayrollRunGenerate));
    }

    // ─── Who lands on the run ───────────────────────────────────────────

    // Not an error — these people legitimately do not belong on this run, and
    // saying so beats a silently short payslip count.
    [Fact]
    public async Task GenerateAsync_SkipsEmployeesWhoDoNotBelongOnThePeriod()
    {
        AddEmployee("usr-1", "Aisyah");
        AddEmployee("usr-2", "Archived", isArchived: true);
        AddEmployee("usr-3", "Filed", reportedToLhdn: true);
        AddEmployee("usr-4", "Future", joinDate: new DateTime(2026, 2, 1));
        AddEmployee("usr-5", "Departed", leaveDate: new DateTime(2025, 12, 31));

        var run = await CreateRunAsync();
        var result = (await _service.GenerateAsync(run.Id)).Result!;

        Assert.Equal(1, result.PayslipCount);
        Assert.Equal(4, result.SkippedEmployees.Count);
        Assert.Contains(result.SkippedEmployees, s => s.Name == "Archived");
        Assert.Contains(result.SkippedEmployees, s => s.Reason.Contains("LHDN"));
    }

    // Someone who joined mid-period still belongs on the run — prorated.
    [Fact]
    public async Task GenerateAsync_IncludesAMidPeriodJoinerAtTheProratedAmount()
    {
        AddEmployee("usr-1", "Aisyah", joinDate: new DateTime(2026, 1, 16));
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(16, payslip.ProratedDays);       // 16–31 Jan inclusive
        Assert.Equal(31, payslip.ProrationDaysInPeriod);
        Assert.Equal(5000m, payslip.BasicPay);        // the salary is unchanged
        Assert.Equal(2580.65m, payslip.ProratedPay);  // 5000 × 16/31
    }

    // ─── Profile JSON ───────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_TurnsFixedAllowanceJsonIntoLineItems()
    {
        AddEmployee(
            "usr-1", "Aisyah",
            fixedAllowancesJson:
            """
            [{"category":"allowance_parking","name":"Parking","amount":150},
             {"category":"deduct_loan_repayment","name":"Staff loan","amount":200}]
            """);

        var run = await CreateRunAsync();
        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(2, payslip.LineItems.Count);
        Assert.Equal(150m, payslip.TotalAllowances);
        Assert.Equal(200m, payslip.TotalDeductions);
        Assert.Equal(5150m, payslip.GrossPay);
    }

    // These columns are free-form JSON written by another system. One bad row
    // must not take a month's payroll down with it.
    [Fact]
    public async Task GenerateAsync_TreatsMalformedProfileJsonAsEmpty()
    {
        AddEmployee("usr-1", "Aisyah", fixedAllowancesJson: "{not json at all");
        var run = await CreateRunAsync();

        var result = await _service.GenerateAsync(run.Id);

        Assert.True(result.Ok);
        var payslip = Assert.Single(result.Result!.Detail.Payslips);
        Assert.Empty(payslip.LineItems);
        Assert.Equal(5000m, payslip.GrossPay);
    }

    [Fact]
    public async Task GenerateAsync_ReadsChildReliefFromTheProfile()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 9000m);
        profile.ChildReliefJson =
            """
            [{"abilityStatus":"NORMAL","currentlyStudying":"UNDER_18","pcbDeduction":"FULL"},
             {"abilityStatus":"NORMAL","currentlyStudying":"DEGREE_ABROAD","pcbDeduction":"FULL"}]
            """;
        await _db.SaveChangesAsync();

        var run = await CreateRunAsync();
        var withChildren = Assert.Single(
            (await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        profile.ChildReliefJson = null;
        await _db.SaveChangesAsync();

        var withoutChildren = Assert.Single(
            (await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        // Child relief lowers chargeable income, hence the withholding.
        Assert.True(withChildren.Pcb < withoutChildren.Pcb);
    }

    // ─── Statutory warnings ─────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_SurfacesMissingStatutoryNumbers()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        profile.IncomeTaxNumber = null;
        await _db.SaveChangesAsync();

        var run = await CreateRunAsync();
        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Contains(
            PayslipCalculator.Warnings.MissingIncomeTaxNumber, payslip.StatutoryWarnings);
    }

    // ─── Reading back ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsTheRunWithItsPayslipsAndLineItems()
    {
        AddEmployee(
            "usr-1", "Aisyah",
            fixedAllowancesJson:
            """[{"category":"allowance_meal","name":"Meals","amount":120}]""");
        var run = await CreateRunAsync();
        await _service.GenerateAsync(run.Id);

        var detail = await _service.GetAsync(run.Id);

        Assert.NotNull(detail);
        var payslip = Assert.Single(detail.Payslips);
        Assert.Equal("Meals", Assert.Single(payslip.LineItems).Label);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForAnUnknownRun() =>
        Assert.Null(await _service.GetAsync("nope"));

    // Newest period first — the runs list is read top-down.
    [Fact]
    public async Task GetAllAsync_OrdersNewestPeriodFirst()
    {
        await CreateRunAsync(2025, 12);
        await CreateRunAsync(2026, 2);
        await CreateRunAsync(2026, 1);

        var runs = await _service.GetAllAsync();

        Assert.Equal(["February 2026", "January 2026", "December 2025"],
            runs.Select(r => r.PeriodLabel));
    }

    // ─── Tenancy ────────────────────────────────────────────────────────

    // Payslips carry their own OrganizationId rather than inheriting the run's,
    // so the global filter reaches them. Without that, another tenant's payroll
    // would be readable through a guessed run id.
    [Fact]
    public async Task AnotherOrgsRunAndPayslipsAreInvisible()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();
        await _service.GenerateAsync(run.Id);

        _currentUser.OrganizationId = "org-2";

        Assert.Empty(await _service.GetAllAsync());
        Assert.Null(await _service.GetAsync(run.Id));
        Assert.Empty(await _payslips.GetForRunAsync(run.Id));
        Assert.Empty(await _payslips.GetLineItemsForRunAsync(run.Id));
    }

    // The roster comes through the tenant-filtered profile read, so a second
    // org's employees cannot land on this org's run.
    [Fact]
    public async Task GenerateAsync_OnlyPaysThisOrgsEmployees()
    {
        AddEmployee("usr-1", "Ours");
        AddEmployee("usr-2", "Theirs", organizationId: "org-2");

        var run = await CreateRunAsync();
        var detail = (await _service.GenerateAsync(run.Id)).Result!.Detail;

        Assert.Equal("Ours", Assert.Single(detail.Payslips).SnapshotName);
    }

    // ─── Year to date ───────────────────────────────────────────────────

    // A draft is not tax withheld, and the run being generated must never feed
    // its own baseline — otherwise every regeneration would compound.
    [Fact]
    public async Task YearToDate_CountsOnlySubmittedRuns()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 9000m);

        var january = await CreateRunAsync(2026, 1);
        await _service.GenerateAsync(january.Id);

        // Still a draft → contributes nothing.
        Assert.Empty(await _payslips.GetYtdByEmployeeAsync(2026, null));

        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == january.Id);
        stored.Status = PayrollRunStatus.SUBMITTED;
        await _db.SaveChangesAsync();

        var ytd = await _payslips.GetYtdByEmployeeAsync(2026, null);
        var totals = ytd[profile.Id];

        Assert.Equal(9000m, totals.Taxable);
        Assert.True(totals.Epf > 0m);
        Assert.True(totals.Pcb > 0m);

        // Excluding that run — as the generate path does for the run in hand —
        // takes it back out.
        Assert.Empty(await _payslips.GetYtdByEmployeeAsync(2026, january.Id));
    }

    // Regression: an allowance that sat under its annual exemption ceiling was
    // never taxed, so adding its full amount back into next month's Y inflates
    // the withholding and compounds all year. The taxable PORTION is what
    // carries forward.
    [Fact]
    public async Task YearToDate_CarriesTheTaxablePortionNotTheFullAmount()
    {
        var profile = AddEmployee(
            "usr-1", "Aisyah",
            monthlySalary: 9000m,
            fixedAllowancesJson:
            """[{"category":"allowance_travel_official","name":"Travel","amount":1500}]""");

        var january = await CreateRunAsync(2026, 1);
        await _service.GenerateAsync(january.Id);

        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == january.Id);
        stored.Status = PayrollRunStatus.SUBMITTED;
        await _db.SaveChangesAsync();

        var totals = (await _payslips.GetYtdByEmployeeAsync(2026, null))[profile.Id];

        // The RM 1,500 was entirely inside the RM 6,000/year ceiling, so Y is
        // the salary alone — not 10,500.
        Assert.Equal(9000m, totals.Taxable);

        // The full amount still counts toward the ceiling for next month.
        Assert.Equal(
            1500m,
            totals.AllowanceByCategory[PayrollAdjustmentCategories.AllowanceTravelOfficial]);
    }

    // ΣLP accumulates the TP1 declarations already relieved this year, so next
    // month's ceiling check starts from the right place.
    [Fact]
    public async Task YearToDate_AccumulatesTp1Declarations()
    {
        var profile = AddEmployee(
            "usr-1", "Aisyah",
            monthlySalary: 9000m,
            fixedAllowancesJson:
            """[{"category":"deduct_tp1_lifestyle","name":"Books","amount":400}]""");

        var january = await CreateRunAsync(2026, 1);
        await _service.GenerateAsync(january.Id);

        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == january.Id);
        stored.Status = PayrollRunStatus.SUBMITTED;
        await _db.SaveChangesAsync();

        var totals = (await _payslips.GetYtdByEmployeeAsync(2026, null))[profile.Id];

        Assert.Equal(400m, totals.AllowableDeductions);
    }

    // A prior year's runs are a different tax year and must not leak in.
    [Fact]
    public async Task YearToDate_IsScopedToTheCalendarYear()
    {
        AddEmployee("usr-1", "Aisyah", monthlySalary: 9000m);

        var lastYear = await CreateRunAsync(2025, 12);
        await _service.GenerateAsync(lastYear.Id);
        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == lastYear.Id);
        stored.Status = PayrollRunStatus.SUBMITTED;
        await _db.SaveChangesAsync();

        Assert.Empty(await _payslips.GetYtdByEmployeeAsync(2026, null));
        Assert.NotEmpty(await _payslips.GetYtdByEmployeeAsync(2025, null));
    }

    // The employee's declared prior-employer figures fold into the YTD so a
    // mid-year joiner is not under-withheld until December.
    [Fact]
    public async Task PriorEmployerCarryover_RaisesThisMonthsWithholding()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 9000m);

        var july = await CreateRunAsync(2026, 7);
        var without = Assert.Single((await _service.GenerateAsync(july.Id)).Result!.Detail.Payslips);

        profile.PrevRemuneration = 54_000m;
        profile.PrevEpf = 5_940m;
        await _db.SaveChangesAsync();

        var with = Assert.Single((await _service.GenerateAsync(july.Id)).Result!.Detail.Payslips);

        Assert.True(with.Pcb > without.Pcb);
    }

    // A rehire whose declared figures ALREADY include the months worked here
    // must not have this org's own YTD added on top.
    [Fact]
    public async Task RehireCarryover_DoesNotDoubleCountThisOrgsOwnYearToDate()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 9000m);

        var january = await CreateRunAsync(2026, 1);
        await _service.GenerateAsync(january.Id);
        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == january.Id);
        stored.Status = PayrollRunStatus.SUBMITTED;
        await _db.SaveChangesAsync();

        // Declared total for the year so far is 9,000 — which is exactly the
        // January this org already paid.
        profile.PrevRemuneration = 9_000m;
        profile.PrevIncludesPriorThisOrgPeriod = true;
        await _db.SaveChangesAsync();

        var february = await CreateRunAsync(2026, 2);
        var flagged = Assert.Single(
            (await _service.GenerateAsync(february.Id)).Result!.Detail.Payslips);

        // Without the flag the same declaration would be counted twice.
        profile.PrevIncludesPriorThisOrgPeriod = false;
        await _db.SaveChangesAsync();

        var doubleCounted = Assert.Single(
            (await _service.GenerateAsync(february.Id)).Result!.Detail.Payslips);

        Assert.True(flagged.Pcb < doubleCounted.Pcb);
    }

    // ─── Settings ───────────────────────────────────────────────────────

    // An org that has never configured payroll still has to be payable — the
    // service falls back to the statutory defaults rather than refusing.
    [Fact]
    public async Task GenerateAsync_WorksForAnOrgWithNoSettingsRow()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();

        var result = await _service.GenerateAsync(run.Id);

        Assert.True(result.Ok);
        // The default rule is the 26-day basis.
        Assert.Equal(26, Assert.Single(result.Result!.Detail.Payslips).TotalWorkingDays);
    }

    [Fact]
    public async Task GenerateAsync_HonoursTheOrgsWorkingDaysRule()
    {
        AddEmployee("usr-1", "Aisyah");
        _db.PayrollSettings.Add(new PayrollSettings
        {
            OrganizationId = "org-1",
            WorkingDaysRule = WorkingDaysRule.CALENDAR,
        });
        await _db.SaveChangesAsync();

        var run = await CreateRunAsync();
        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(31, payslip.TotalWorkingDays);
    }

    [Fact]
    public async Task GenerateAsync_AppliesTheOrgsHrdfLevy()
    {
        AddEmployee("usr-1", "Aisyah");
        _db.PayrollSettings.Add(new PayrollSettings
        {
            OrganizationId = "org-1",
            HrdfEnabled = true,
            HrdfRate = 1m,
        });
        await _db.SaveChangesAsync();

        var run = await CreateRunAsync();
        var detail = (await _service.GenerateAsync(run.Id)).Result!.Detail;

        Assert.Equal(50m, Assert.Single(detail.Payslips).Hrdf);
        Assert.Equal(50m, detail.Run.TotalHrdf);
        Assert.Equal(1, detail.Run.EmployeesSubjectToHrdf);
        Assert.Equal(5000m, detail.Run.TotalWagesSubjectToHrdf);
    }

    // ─── Phase 4: adjustments, claims and overtime ──────────────────────
    //
    // The two tables that exist ONLY because generation is destructive.
    // Everything below is really one question: does what an admin typed
    // survive the next press of Generate, and does it reach the right wage
    // base when it does.

    private EmployeePolicy AddPolicy(
        bool otEnabled = true,
        OtMethod otMethod = OtMethod.CASH,
        decimal normal = 1.5m,
        decimal rest = 2.0m,
        decimal publicHoliday = 3.0m,
        string organizationId = "org-1")
    {
        var policy = new EmployeePolicy
        {
            OrganizationId = organizationId,
            Name = "Standard",
            IsDefault = true,
            OtEnabled = otEnabled,
            OtMethod = otMethod,
            OtRateNormalDay = normal,
            OtRateRestDay = rest,
            OtRatePublicHoliday = publicHoliday,
        };
        _db.EmployeePolicies.Add(policy);
        _db.SaveChanges();
        return policy;
    }

    private async Task AddAdjustmentAsync(
        string runId,
        string employeeProfileId,
        decimal otNormal = 0m,
        decimal otRest = 0m,
        decimal otPublic = 0m,
        string? manualJson = null,
        string? overridesJson = null,
        decimal? workedHours = null)
    {
        _db.PayrollRunAdjustments.Add(new PayrollRunAdjustment
        {
            OrganizationId = "org-1",
            PayrollRunId = runId,
            EmployeeProfileId = employeeProfileId,
            OtNormalHours = otNormal,
            OtRestHours = otRest,
            OtPublicHours = otPublic,
            ManualLineItemsJson = manualJson ?? "[]",
            FixedAllowanceOverridesJson = overridesJson ?? "{}",
            WorkedHours = workedHours,
        });
        await _db.SaveChangesAsync();
    }

    private async Task AttachClaimAsync(
        string runId, string employeeProfileId, string claimId, decimal amount, string label)
    {
        _db.PayrollRunClaims.Add(new PayrollRunClaim
        {
            OrganizationId = "org-1",
            PayrollRunId = runId,
            ClaimId = claimId,
            EmployeeProfileId = employeeProfileId,
            Label = label,
            Amount = amount,
        });
        await _db.SaveChangesAsync();
    }

    // ---- Overtime ----

    [Fact]
    public async Task GenerateAsync_PaysOvertimeFromTheAdjustmentAtThePolicysRates()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5200m);
        AddPolicy(normal: 1.5m);
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        // 5200 / 26 days / 8 h = RM 25.00/h; 10 h x 1.5 = RM 375.00.
        Assert.Equal(10m, payslip.OtNormalHours);
        Assert.Equal(375m, payslip.OtPay);
    }

    // A TIME_BANK policy credits time off for the same hours. Paying cash here
    // as well would pay them twice — the reference gates on this, and so must
    // this port.
    [Fact]
    public async Task GenerateAsync_PaysNoCashOvertimeUnderATimeBankPolicy()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        AddPolicy(otMethod: OtMethod.TIME_BANK);
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(0m, payslip.OtPay);
        Assert.Equal(0m, payslip.OtNormalHours);
    }

    [Fact]
    public async Task GenerateAsync_PaysNoOvertimeWhenThePolicyDisablesIt()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        AddPolicy(otEnabled: false);
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(0m, payslip.OtPay);
    }

    [Fact]
    public async Task GenerateAsync_UsesEachDayTypesOwnMultiplier()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5200m);
        AddPolicy(normal: 1.5m, rest: 2.0m, publicHoliday: 3.0m);
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 2m, otRest: 2m, otPublic: 2m);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        // RM 25/h x 2 h x (1.5 + 2 + 3) = RM 325.00.
        Assert.Equal(325m, payslip.OtPay);
    }

    // EPF Act 1991 s.2 excludes overtime from wages; SOCSO and EIS include it.
    // Getting this backwards is a contribution filing that does not reconcile.
    [Fact]
    public async Task GenerateAsync_KeepsOvertimeOutOfEpfButInsideSocsoAndEis()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5200m);
        AddPolicy();
        var run = await CreateRunAsync();

        var withoutOt = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m);
        var withOt = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(withoutOt.EpfEmployee, withOt.EpfEmployee);
        Assert.Equal(withoutOt.EpfEmployer, withOt.EpfEmployer);
        Assert.True(withOt.SocsoEmployer > withoutOt.SocsoEmployer);
        Assert.True(withOt.EisEmployer > withoutOt.EisEmployer);
    }

    // No policy at all falls back to the EA 1955 s.60A statutory floor rather
    // than to zero — an employee without a policy is still owed overtime.
    [Fact]
    public async Task GenerateAsync_FallsBackToTheStatutoryMultipliersWithNoPolicy()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5200m);
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(375m, payslip.OtPay);
    }

    // ---- Manual line items ----

    // The reason manual rows are merged into the fixed-allowance list rather
    // than passed as free-form amounts: the category decides the statutory
    // treatment, and a one-off row has to obey it exactly as a recurring one.
    [Fact]
    public async Task GenerateAsync_RoutesAManualRowThroughItsCategorysFlags()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, manualJson: """
            [{"kind":"ALLOWANCE","category":"allowance_travel_official","label":"Site travel","amount":300}]
            """);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);
        var line = Assert.Single(payslip.LineItems);

        Assert.Equal("Site travel", line.Label);
        Assert.Equal(300m, line.Amount);
        // Official-duty travel is not wages for EPF/SOCSO/EIS (PR 5/2019 §7.2.1).
        Assert.False(line.SubjectToEpf);
        Assert.False(line.SubjectToSocso);
        Assert.False(line.SubjectToEis);
    }

    [Fact]
    public async Task GenerateAsync_TakesAManualDeductionOffNetPay()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();

        var before = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        await AddAdjustmentAsync(run.Id, profile.Id, manualJson: """
            [{"category":"deduct_advance","label":"Salary advance","amount":200}]
            """);
        var after = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(200m, after.TotalDeductions);
        Assert.True(after.NetPay < before.NetPay);
    }

    // ---- Fixed allowance overrides ----

    // The trap, end to end: overriding the amount must move the money without
    // moving the row's statutory treatment.
    [Fact]
    public async Task GenerateAsync_OverridesTheAmountWhileKeepingTheCategorysTreatment()
    {
        var profile = AddEmployee("usr-1", "Aisyah", fixedAllowancesJson:
            """[{"category":"allowance_travel_official","name":"Travel","amount":500}]""");
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, overridesJson: """{"0":{"amount":250}}""");

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);
        var line = Assert.Single(payslip.LineItems);

        Assert.Equal(250m, line.Amount);
        Assert.Equal("allowance_travel_official", line.Category);
        Assert.False(line.SubjectToEpf);
    }

    [Fact]
    public async Task GenerateAsync_SkipsAnOverriddenRowForThisRunOnly()
    {
        var profile = AddEmployee("usr-1", "Aisyah", fixedAllowancesJson:
            """[{"category":"allowance_standard","name":"Phone","amount":100}]""");
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, overridesJson: """{"0":{"skip":true}}""");

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Empty(payslip.LineItems);
        Assert.Equal(0m, payslip.TotalAllowances);
    }

    // ---- Attached claims ----

    [Fact]
    public async Task GenerateAsync_TurnsAnAttachedClaimIntoAReimbursementLine()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();
        await AttachClaimAsync(run.Id, profile.Id, "clm-1", 120m, "Taxi to site");

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);
        var line = Assert.Single(payslip.LineItems);

        Assert.Equal(PayslipLineKind.REIMBURSEMENT, line.Kind);
        Assert.Equal("Taxi to site", line.Label);
        Assert.Equal(120m, line.Amount);
        // The rebuilt line still points back at what it is paying.
        Assert.Equal("clm-1", line.ClaimId);
        Assert.Equal(120m, payslip.TotalReimbursements);
    }

    // Paying back what someone already spent is not wages. A reimbursement
    // that reached the contribution bases would over-collect on every agency
    // at once.
    [Fact]
    public async Task GenerateAsync_KeepsAReimbursementOutOfEveryStatutoryBase()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();

        var before = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        await AttachClaimAsync(run.Id, profile.Id, "clm-1", 500m, "Hotel");
        var after = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(before.EpfEmployee, after.EpfEmployee);
        Assert.Equal(before.SocsoEmployee, after.SocsoEmployee);
        Assert.Equal(before.EisEmployee, after.EisEmployee);
        Assert.Equal(before.Pcb, after.Pcb);
        // It still reaches the employee: gross and net both rise by the full amount.
        Assert.Equal(before.NetPay + 500m, after.NetPay);
    }

    [Fact]
    public async Task GenerateAsync_GivesEachEmployeeOnlyTheirOwnClaims()
    {
        var aisyah = AddEmployee("usr-1", "Aisyah");
        var bala = AddEmployee("usr-2", "Bala");
        var run = await CreateRunAsync();
        await AttachClaimAsync(run.Id, aisyah.Id, "clm-1", 120m, "Taxi");
        await AttachClaimAsync(run.Id, bala.Id, "clm-2", 80m, "Parking");

        var payslips = (await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips;

        Assert.Equal(120m, payslips.Single(p => p.EmployeeProfileId == aisyah.Id).TotalReimbursements);
        Assert.Equal(80m, payslips.Single(p => p.EmployeeProfileId == bala.Id).TotalReimbursements);
    }

    // ---- Surviving a regeneration ----

    // THE reason both tables exist. Payslips and their line items are wiped and
    // rebuilt on every Generate press; the adjustment and the attachment are
    // not, so the same figures come back.
    [Fact]
    public async Task GenerateAsync_ReappliesAdjustmentsAndClaimsOnEveryRegeneration()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5200m);
        AddPolicy();
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m, manualJson: """
            [{"category":"deduct_advance","label":"Advance","amount":200}]
            """);
        await AttachClaimAsync(run.Id, profile.Id, "clm-1", 120m, "Taxi");

        var first = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);
        var second = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(first.OtPay, second.OtPay);
        Assert.Equal(first.TotalDeductions, second.TotalDeductions);
        Assert.Equal(first.TotalReimbursements, second.TotalReimbursements);
        Assert.Equal(first.NetPay, second.NetPay);
        Assert.Equal(first.LineItems.Count, second.LineItems.Count);
    }

    // Generation is what reconciles the payslips with their inputs, so it is
    // what clears the warning. Before phase 4 nothing ever set it.
    [Fact]
    public async Task GenerateAsync_ClearsTheStalenessFlagItsInputsRaised()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 4m);

        var stored = await _db.PayrollRuns.FirstAsync(r => r.Id == run.Id);
        stored.LastMutatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var detail = (await _service.GenerateAsync(run.Id)).Result!.Detail;

        Assert.False(detail.Run.IsStale);
        Assert.Null((await _db.PayrollRuns.FirstAsync(r => r.Id == run.Id)).LastMutatedAt);
    }

    // An adjustment for someone who is not on this run (archived, or left
    // before the period) must not resurrect them into it.
    [Fact]
    public async Task GenerateAsync_IgnoresAnAdjustmentForASkippedEmployee()
    {
        var profile = AddEmployee("usr-1", "Aisyah", isArchived: true);
        AddPolicy();
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, otNormal: 10m);

        var result = await _service.GenerateAsync(run.Id);

        Assert.Equal(0, result.Result!.PayslipCount);
        Assert.Single(result.Result.SkippedEmployees);
    }

    // ─── Phase 5: attendance hours and unpaid leave ─────────────────────

    private EmployeePolicy AddAttendancePolicy(bool canAccessAttendance = true)
    {
        var policy = new EmployeePolicy
        {
            OrganizationId = "org-1",
            Name = "Attendance",
            IsDefault = true,
            CanAccessAttendance = canAccessAttendance,
        };
        _db.EmployeePolicies.Add(policy);
        _db.SaveChanges();
        return policy;
    }

    // ---- Attendance-derived hours ----

    [Fact]
    public async Task GenerateAsync_SnapshotsHoursDerivedFromAttendance()
    {
        AddEmployee("usr-1", "Aisyah");
        AddAttendancePolicy();
        _hours.Set("usr-1", normalMin: 9_000, expectedMin: 10_560);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(150m, payslip.WorkedHours);     // 9,000 / 60
        Assert.Equal(176m, payslip.ExpectedHours);   // 10,560 / 60
    }

    // Minutes past the shift length only become money through an approved
    // overtime submission. Counting them as normal hours as well would pay
    // them twice — once flat, once at the OT multiplier.
    [Fact]
    public async Task GenerateAsync_LeavesBeyondShiftMinutesOutOfWorkedHours()
    {
        AddEmployee("usr-1", "Aisyah");
        AddAttendancePolicy();
        _hours.Set("usr-1", normalMin: 9_000, expectedMin: 10_560, beyondShiftMin: 600);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(150m, payslip.WorkedHours);
    }

    // Without attendance access the honest answer is "unknown", not zero. For
    // an HOURLY employee a confident zero would be a zero payslip.
    [Fact]
    public async Task GenerateAsync_LeavesHoursNullWhenThePolicyDoesNotGrantAttendance()
    {
        AddEmployee("usr-1", "Aisyah");
        AddAttendancePolicy(canAccessAttendance: false);
        _hours.Set("usr-1", normalMin: 9_000, expectedMin: 10_560);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Null(payslip.WorkedHours);
        Assert.Null(payslip.ExpectedHours);
    }

    [Fact]
    public async Task GenerateAsync_PrefersTheAdminsHoursOverrideOverAttendance()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        AddAttendancePolicy();
        _hours.Set("usr-1", normalMin: 9_000, expectedMin: 10_560);
        var run = await CreateRunAsync();
        await AddAdjustmentAsync(run.Id, profile.Id, workedHours: 120m);

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(120m, payslip.WorkedHours);
        // Not overridden, so attendance still supplies the comparison.
        Assert.Equal(176m, payslip.ExpectedHours);
    }

    // For HOURLY staff the attendance figure IS the paid quantity, not a
    // display column.
    [Fact]
    public async Task GenerateAsync_PaysHourlyStaffForTheHoursAttendanceRecorded()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: null);
        profile.SalaryType = SalaryType.HOURLY;
        profile.HourlyRate = 20m;
        await _db.SaveChangesAsync();
        AddAttendancePolicy();
        _hours.Set("usr-1", normalMin: 6_000, expectedMin: 10_560);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(100m, payslip.WorkedHours);      // 6,000 / 60
        Assert.Equal(2000m, payslip.BasicPay);        // 100 h × RM 20
        // Expected hours are a monthly comparison; an hourly employee has none.
        Assert.Null(payslip.ExpectedHours);
    }

    // ---- Unpaid leave ----

    // The salary stays whole and the absence is docked as its own line, so the
    // payslip says WHY someone was paid less.
    [Fact]
    public async Task GenerateAsync_DocksApprovedUnpaidLeaveAsItsOwnLine()
    {
        AddEmployee("usr-1", "Aisyah", monthlySalary: 2600m);
        _leave.SetUnpaidDays("usr-1", 2);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);
        var line = Assert.Single(payslip.LineItems);

        // Default TWENTY_SIX basis: 2600 / 26 = RM 100/day × 2 days.
        Assert.Equal("Unpaid Leave", line.Label);
        Assert.Equal(200m, line.Amount);
        Assert.Equal(2600m, payslip.BasicPay);   // the salary itself is untouched
        Assert.Equal(2m, payslip.UnpaidLeaveDays);
    }

    // The wage was never earned, so it comes off the statutory bases too —
    // deducting it from take-home alone would over-contribute on all four.
    [Fact]
    public async Task GenerateAsync_TakesUnpaidLeaveOutOfTheStatutoryBasesToo()
    {
        AddEmployee("usr-1", "Aisyah", monthlySalary: 2600m);
        var run = await CreateRunAsync();
        var withoutLeave = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        _leave.SetUnpaidDays("usr-1", 2);
        var withLeave = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.True(withLeave.EpfEmployee < withoutLeave.EpfEmployee);
        Assert.True(withLeave.SocsoEmployee <= withoutLeave.SocsoEmployee);
        Assert.Equal(withoutLeave.GrossPay - 200m, withLeave.GrossPay);
    }

    // Hourly staff are paid for the hours they worked, so an absence is
    // already absent from their pay — docking it again would charge twice.
    [Fact]
    public async Task GenerateAsync_DoesNotDockUnpaidLeaveFromHourlyStaff()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: null);
        profile.SalaryType = SalaryType.HOURLY;
        profile.HourlyRate = 20m;
        await _db.SaveChangesAsync();
        AddAttendancePolicy();
        _hours.Set("usr-1", normalMin: 6_000, expectedMin: 10_560);
        _leave.SetUnpaidDays("usr-1", 3);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Empty(payslip.LineItems);
        Assert.Equal(2000m, payslip.GrossPay);
    }

    // The org's s.60I basis decides what one day of pay is worth, so it
    // legitimately reaches the deduction — unlike s.18A proration, which it
    // must never reach.
    [Fact]
    public async Task GenerateAsync_UsesTheOrgsWorkingDaysBasisForTheDailyRate()
    {
        AddEmployee("usr-1", "Aisyah", monthlySalary: 3100m);
        _db.PayrollSettings.Add(new PayrollSettings
        {
            OrganizationId = "org-1",
            WorkingDaysRule = WorkingDaysRule.CALENDAR,
        });
        await _db.SaveChangesAsync();
        _leave.SetUnpaidDays("usr-1", 1);
        var run = await CreateRunAsync(2026, 1);   // January has 31 days

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Equal(100m, Assert.Single(payslip.LineItems).Amount);   // 3100 / 31
    }

    [Fact]
    public async Task GenerateAsync_AddsNoUnpaidLeaveLineWhenThereIsNone()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.Empty(payslip.LineItems);
        Assert.Null(payslip.UnpaidLeaveDays);
    }

    // ─── Phase 6a: the PCB breakdown snapshot ───────────────────────────

    // The payslip stores the formula that produced ITS deduction, so a
    // Detailed Calculations PDF rendered years later shows the arithmetic
    // that actually applied rather than today's rates.
    [Fact]
    public async Task GenerateAsync_SnapshotsThePcbBreakdownOnEveryPayslip()
    {
        AddEmployee("usr-1", "Aisyah", monthlySalary: 8000m);
        var run = await CreateRunAsync();

        var payslip = Assert.Single((await _service.GenerateAsync(run.Id)).Result!.Detail.Payslips);

        Assert.NotNull(payslip.PcbCalculationJson);

        var breakdown = System.Text.Json.JsonSerializer.Deserialize<PcbBreakdown>(
            payslip.PcbCalculationJson!,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });

        Assert.NotNull(breakdown);
        Assert.Equal(PcbFormula.Resident, breakdown!.Formula);

        // The snapshot explains the figure that was actually withheld.
        Assert.Equal(payslip.PcbNormal, breakdown.PcbNormal);
        Assert.Equal(payslip.PcbAdditional, breakdown.PcbAdditional);
        Assert.Equal(breakdown.YearlyTax, Math.Max(0m, (breakdown.P - breakdown.M) * breakdown.R + breakdown.B));
    }

    // ─── Loan repayments (phase 8a) ─────────────────────────────────────

    // The point of the whole slice: a recorded loan turns into a deduction on
    // the payslip, without anyone typing it into the run.
    [Fact]
    public async Task GenerateAsync_DeductsAnActiveLoanInstallment()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5000m);
        await _loanService.CreateAsync(new SaveEmployeeLoanDto
        {
            EmployeeProfileId = profile.Id,
            PrincipalAmount = 1200m,
            Mode = LoanRepaymentMode.FIXED,
            InstallmentCount = 12,
            StartYear = 2026,
            StartMonth = 1,
        });

        var run = await CreateRunAsync(2026, 1);
        await _service.GenerateAsync(run.Id);

        var payslip = (await _service.GetAsync(run.Id))!.Payslips.Single();

        Assert.Contains(payslip.LineItems, li =>
            li.Category == PayrollAdjustmentCategories.DeductLoanRepayment && li.Amount == 100m);
    }

    // Repaying a loan is the employee spending money they earned, not earning
    // less. Every statutory base has to be computed BEFORE it comes off, or
    // the employer under-contributes and LHDN under-collects.
    [Fact]
    public async Task GenerateAsync_ALoanDoesNotShrinkTheStatutoryBases()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5000m);

        // The SAME run generated twice, so the comparison isolates the loan.
        // Comparing two different months would fold in that month's own PCB
        // annualisation and prove nothing.
        var run = await CreateRunAsync(2026, 1);
        await _service.GenerateAsync(run.Id);
        var withoutLoan = (await _service.GetAsync(run.Id))!.Payslips.Single();

        await _loanService.CreateAsync(new SaveEmployeeLoanDto
        {
            EmployeeProfileId = profile.Id,
            PrincipalAmount = 1200m,
            Mode = LoanRepaymentMode.FIXED,
            InstallmentCount = 12,
            StartYear = 2026,
            StartMonth = 1,
        });

        await _service.GenerateAsync(run.Id);
        var withLoan = (await _service.GetAsync(run.Id))!.Payslips.Single();

        Assert.Equal(withoutLoan.EpfEmployee, withLoan.EpfEmployee);
        Assert.Equal(withoutLoan.SocsoEmployee, withLoan.SocsoEmployee);
        Assert.Equal(withoutLoan.EisEmployee, withLoan.EisEmployee);
        Assert.Equal(withoutLoan.GrossPay, withLoan.GrossPay);
        Assert.Equal(withoutLoan.Pcb, withLoan.Pcb);

        // Only the take-home moves, and by exactly the installment.
        Assert.Equal(withoutLoan.NetPay - 100m, withLoan.NetPay);
    }

    [Fact]
    public async Task GenerateAsync_ACancelledLoanDeductsNothing()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5000m);
        var loan = await _loanService.CreateAsync(new SaveEmployeeLoanDto
        {
            EmployeeProfileId = profile.Id,
            PrincipalAmount = 1200m,
            Mode = LoanRepaymentMode.FIXED,
            InstallmentCount = 12,
            StartYear = 2026,
            StartMonth = 1,
        });
        await _loanService.SetStatusAsync(loan.Id, LoanStatus.CANCELLED);

        var payslip = await GeneratedPayslipAsync(2026, 1);

        Assert.DoesNotContain(payslip.LineItems, li =>
            li.Category == PayrollAdjustmentCategories.DeductLoanRepayment);
    }

    // A loan that has run its course stops on its own, without anyone
    // remembering to close it.
    [Fact]
    public async Task GenerateAsync_ALoanPastItsLastInstallmentDeductsNothing()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 5000m);
        await _loanService.CreateAsync(new SaveEmployeeLoanDto
        {
            EmployeeProfileId = profile.Id,
            PrincipalAmount = 200m,
            Mode = LoanRepaymentMode.FIXED,
            InstallmentCount = 2,
            StartYear = 2026,
            StartMonth = 1,
        });

        var third = await GeneratedPayslipAsync(2026, 3);

        Assert.DoesNotContain(third.LineItems, li =>
            li.Category == PayrollAdjustmentCategories.DeductLoanRepayment);
    }

    private async Task<PayslipDto> GeneratedPayslipAsync(int year, int month)
    {
        var run = await CreateRunAsync(year, month);
        await _service.GenerateAsync(run.Id);

        return (await _service.GetAsync(run.Id))!.Payslips.Single();
    }
}
