using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
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

// DRAFT → PENDING_APPROVAL → SUBMITTED, and the two ways back.
//
// SUBMITTED is the state every later run's year-to-date reads from, and a
// submitted payslip is immutable. So nearly all of the value here is in what
// the transitions REFUSE, and in the cascade that stops a revert leaving
// later months filed against a year-to-date that no longer exists.
public class PayrollRunStateMachineTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly StubPayrollXeroSync _xeroSync = new();
    private EmployeeLoanService _loanService = null!;
    private readonly StubPayrollHours _hours = new();
    private readonly StubPayrollLeave _leave = new();
    private readonly PayrollRunService _service;
    private readonly StatutoryFileService _statutory;
    private readonly PayrollRunAdjustmentRepository _adjustments;
    private readonly PayrollRunClaimRepository _runClaims;
    private readonly PayslipRepository _payslips;
    private readonly PayrollRunRepository _runs;

    public PayrollRunStateMachineTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-state-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _payslips = new PayslipRepository(_db);
        _adjustments = new PayrollRunAdjustmentRepository(_db);
        _runClaims = new PayrollRunClaimRepository(_db);
        _runs = new PayrollRunRepository(_db);

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
            _runs,
            _payslips,
            new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit),
            directory,
            _adjustments,
            _runClaims,
            new PolicyService(
                new EmployeePolicyRepository(_db),
                new PolicyLeaveEntitlementRepository(_db),
                directory),
            // The real statutory service over the same context, so the
            // readiness guard on submit is exercised rather than stubbed away.
            _statutory = new StatutoryFileService(
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

    // These tests are about the TRANSITIONS, so the fixtures are statutory-
    // ready by default — a run refused for a missing SSM number would not be
    // testing what the test name claims. The readiness guard has its own
    // tests below, which take the fields away deliberately.
    private void SeedCompanyInfo()
    {
        if (_db.PayrollCompanyInfos.Any()) return;

        _db.PayrollCompanyInfos.Add(new PayrollCompanyInfo
        {
            OrganizationId = "org-1",
            EmployerName = "Globe Engineering Sdn Bhd",
            EmployerTin = "E1234567890",
            RegistrationNo = "202001012345",
            PerkesoEmployerCode = "A1234567890",
        });
        _db.SaveChanges();
    }

    private EmployeeProfile AddEmployee(string userId, string name, decimal? monthlySalary = 5000m)
    {
        SeedCompanyInfo();
        _db.Users.Add(new User { Id = userId, Email = $"{userId}@x.com", Name = name, PasswordHash = "x" });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = "org-1",
            UserId = userId,
            Role = "Employee",
            EmployeeNumber = "E-001",
        });

        var profile = new EmployeeProfile
        {
            OrganizationId = "org-1",
            UserId = userId,
            Nationality = "Malaysian",
            DateOfBirth = new DateTime(1990, 6, 15),
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = monthlySalary,
            EpfEmployeeRate = 11m,
            SocsoScheme = SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            ContributeToEis = true,
            IdNumber = "900101-14-5567",
            IncomeTaxNumber = "SG12345678900",
        };
        _db.EmployeeProfiles.Add(profile);
        _db.SaveChanges();
        return profile;
    }

    // A run that has been generated and is ready to submit.
    private async Task<PayrollRunDto> GeneratedRunAsync(int year = 2026, int month = 1)
    {
        var created = await _service.CreateAsync(
            new CreatePayrollRunDto { PeriodYear = year, PeriodMonth = month });
        Assert.True(created.Ok);

        var generated = await _service.GenerateAsync(created.Run!.Id);
        Assert.True(generated.Ok);

        return created.Run;
    }

    // Straight to SUBMITTED, for tests about what happens afterwards.
    private async Task<PayrollRunDto> SubmittedRunAsync(int year = 2026, int month = 1)
    {
        var run = await GeneratedRunAsync(year, month);
        Assert.True((await _service.SubmitForApprovalAsync(run.Id)).Ok);
        Assert.True((await _service.ApproveAsync(run.Id)).Ok);
        return run;
    }


    // ─── Files come only from an approved run ───────────────────────────

    // Every one of these is filed with a regulator or used to move money,
    // and a DRAFT's figures can still change — regenerating rebuilds every
    // payslip. A file cut from one is a submission the org cannot stand
    // behind, so the SERVICE refuses rather than trusting the UI to hide a
    // button.
    [Fact]
    public async Task StatutoryFiles_AreRefusedBeforeApproval()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        foreach (var result in new[]
        {
            await _statutory.RenderEpfCsvAsync(run.Id),
            await _statutory.RenderPerkesoTxtAsync(run.Id),
            await _statutory.RenderPcbTxtAsync(run.Id),
        })
        {
            Assert.False(result.Ok);
            Assert.Contains("approved", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Documents_AreRefusedBeforeApproval()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        foreach (var result in new[]
        {
            await _statutory.RenderSummaryPdfAsync(run.Id),
            await _statutory.RenderPaymentSchedulePdfAsync(run.Id),
            await _statutory.RenderAllPayslipsZipAsync(run.Id),
            await _statutory.RenderBankFileAsync(run.Id, null),
        })
        {
            Assert.False(result.Ok);
            Assert.Contains("approved", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // A run sitting with the approver is still not final.
    [Fact]
    public async Task Files_AreRefusedWhileAwaitingApproval()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        Assert.True((await _service.SubmitForApprovalAsync(run.Id)).Ok);

        Assert.False((await _statutory.RenderEpfCsvAsync(run.Id)).Ok);
    }

    [Fact]
    public async Task Files_AreProducedOnceApproved()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await SubmittedRunAsync();

        Assert.True((await _statutory.RenderSummaryPdfAsync(run.Id)).Ok);
    }

    // Reverting takes the month back to draft, so the files it could produce
    // go away with it — otherwise a reverted run keeps handing out figures
    // that are no longer filed.
    [Fact]
    public async Task Files_StopAgainAfterARevert()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await SubmittedRunAsync();
        Assert.True((await _service.RevertToDraftAsync(run.Id)).Ok);

        Assert.False((await _statutory.RenderSummaryPdfAsync(run.Id)).Ok);
    }

    // ─── Submitting for approval ────────────────────────────────────────

    [Fact]
    public async Task SubmitForApprovalAsync_MovesADraftToPendingAndRecordsWho()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        _currentUser.UserId = "usr-proposer";

        var result = await _service.SubmitForApprovalAsync(run.Id);

        Assert.True(result.Ok);
        Assert.Equal(PayrollRunStatus.PENDING_APPROVAL, result.Run!.Status);
        Assert.Equal("usr-proposer", result.Run.SubmittedForApprovalById);
        Assert.NotNull(result.Run.SubmittedForApprovalAt);
        // Not live yet — that is the approver's step.
        Assert.Null(result.Run.SubmittedAt);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunSubmitForApproval));
    }

    // Usually means Generate was never pressed. Filing an empty month is not a
    // thing anyone means to do.
    [Fact]
    public async Task SubmitForApprovalAsync_RefusesARunWithNoPayslips()
    {
        var created = await _service.CreateAsync(
            new CreatePayrollRunDto { PeriodYear = 2026, PeriodMonth = 1 });

        var result = await _service.SubmitForApprovalAsync(created.Run!.Id);

        Assert.False(result.Ok);
        Assert.Contains("Generate", result.Error);
    }

    // The inputs moved after the payslips were built, so the figures about to
    // be filed are not the ones the inputs now imply.
    [Fact]
    public async Task SubmitForApprovalAsync_RefusesAStaleDraft()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        // Exactly what an adjustment save does.
        await _runs.MarkMutatedAsync(run.Id);

        var result = await _service.SubmitForApprovalAsync(run.Id);

        Assert.False(result.Ok);
        Assert.Contains("Re-run payroll", result.Error);
        Assert.Equal(profile.Id, Assert.Single(await _payslips.GetForRunAsync(run.Id)).EmployeeProfileId);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_AcceptsARunRegeneratedAfterTheChange()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        await _runs.MarkMutatedAsync(run.Id);

        // Generation is what reconciles the payslips with their inputs.
        await _service.GenerateAsync(run.Id);

        Assert.True((await _service.SubmitForApprovalAsync(run.Id)).Ok);
    }

    // Deductions larger than someone's pay is a data-entry mistake far more
    // often than a real month — and once submitted the payslip is immutable.
    [Fact]
    public async Task SubmitForApprovalAsync_RefusesWhenAnyoneTakesHomeNothing()
    {
        var profile = AddEmployee("usr-1", "Aisyah", monthlySalary: 2000m);
        AddEmployee("usr-2", "Bala");
        var run = await GeneratedRunAsync();

        // A deduction that swallows the whole salary.
        _db.PayrollRunAdjustments.Add(new PayrollRunAdjustment
        {
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            EmployeeProfileId = profile.Id,
            ManualLineItemsJson =
                """[{"category":"deduct_advance","label":"Advance","amount":9000}]""",
        });
        await _db.SaveChangesAsync();
        await _service.GenerateAsync(run.Id);

        var result = await _service.SubmitForApprovalAsync(run.Id);

        Assert.False(result.Ok);
        // Names the person, so the admin knows whose row to fix.
        Assert.Contains("Aisyah", result.Error);
    }

    // ---- Chronology ----
    //
    // YTD is read off SUBMITTED runs only, and a payslip snapshot cannot be
    // edited after the fact. Submitting out of order freezes the later month
    // against a year-to-date that was never true.

    [Fact]
    public async Task SubmitForApprovalAsync_RefusesWhileTheMonthBeforeIsStillADraft()
    {
        AddEmployee("usr-1", "Aisyah");
        await GeneratedRunAsync(2026, 1);
        var february = await GeneratedRunAsync(2026, 2);

        var result = await _service.SubmitForApprovalAsync(february.Id);

        Assert.False(result.Ok);
        Assert.Contains("January 2026", result.Error);
    }

    // The gap case: no January run at all, but the org has submitted earlier
    // months. Skipping January would freeze February against a zero YTD.
    [Fact]
    public async Task SubmitForApprovalAsync_RefusesAGapEvenWithNoRunForTheMonthBefore()
    {
        AddEmployee("usr-1", "Aisyah");
        await SubmittedRunAsync(2025, 12);
        var february = await GeneratedRunAsync(2026, 2);

        var result = await _service.SubmitForApprovalAsync(february.Id);

        Assert.False(result.Ok);
        Assert.Contains("January 2026", result.Error);
    }

    // An org onboarding mid-year has no earlier runs and nothing to skip.
    [Fact]
    public async Task SubmitForApprovalAsync_AllowsAnOrgsVeryFirstRunInAnyMonth()
    {
        AddEmployee("usr-1", "Aisyah");
        var june = await GeneratedRunAsync(2026, 6);

        Assert.True((await _service.SubmitForApprovalAsync(june.Id)).Ok);
    }

    // December → January crosses a year. Comparing months alone would call
    // December "later" than the January that follows it.
    [Fact]
    public async Task SubmitForApprovalAsync_TreatsTheDecemberBeforeAsThePriorMonth()
    {
        AddEmployee("usr-1", "Aisyah");
        await GeneratedRunAsync(2025, 12);
        var january = await GeneratedRunAsync(2026, 1);

        var result = await _service.SubmitForApprovalAsync(january.Id);

        Assert.False(result.Ok);
        Assert.Contains("December 2025", result.Error);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_RefusesARunThatIsNotADraft()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        await _service.SubmitForApprovalAsync(run.Id);

        var again = await _service.SubmitForApprovalAsync(run.Id);

        Assert.False(again.Ok);
        Assert.Contains("already awaiting approval", again.Error);
    }

    // ─── Approving ──────────────────────────────────────────────────────

    // The approver is recorded separately from the proposer: "who put this
    // month's pay live" is a different question from "who prepared it", and an
    // audit asks the first one.
    [Fact]
    public async Task ApproveAsync_RecordsTheApproverApartFromTheProposer()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        _currentUser.UserId = "usr-proposer";
        await _service.SubmitForApprovalAsync(run.Id);

        _currentUser.UserId = "usr-approver";
        var result = await _service.ApproveAsync(run.Id);

        Assert.True(result.Ok);
        Assert.Equal(PayrollRunStatus.SUBMITTED, result.Run!.Status);
        Assert.Equal("usr-approver", result.Run.SubmittedById);
        Assert.Equal("usr-proposer", result.Run.SubmittedForApprovalById);
        Assert.NotNull(result.Run.SubmittedAt);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunApprove));
    }

    [Theory]
    [InlineData(PayrollRunStatus.DRAFT)]
    [InlineData(PayrollRunStatus.SUBMITTED)]
    public async Task ApproveAsync_RefusesAnythingNotAwaitingApproval(PayrollRunStatus status)
    {
        AddEmployee("usr-1", "Aisyah");
        var run = status == PayrollRunStatus.SUBMITTED
            ? await SubmittedRunAsync()
            : await GeneratedRunAsync();

        var result = await _service.ApproveAsync(run.Id);

        Assert.False(result.Ok);
        Assert.Contains("awaiting approval", result.Error);
    }

    // Only SUBMITTED runs feed YTD, so an approval is what makes a month
    // visible to the next one.
    [Fact]
    public async Task ApproveAsync_IsWhatMakesTheMonthCountTowardsYtd()
    {
        AddEmployee("usr-1", "Aisyah");
        var january = await GeneratedRunAsync(2026, 1);

        var beforeApproval = await _payslips.GetYtdByEmployeeAsync(2026, null);
        Assert.Empty(beforeApproval);

        await _service.SubmitForApprovalAsync(january.Id);
        await _service.ApproveAsync(january.Id);

        Assert.NotEmpty(await _payslips.GetYtdByEmployeeAsync(2026, null));
    }

    // ─── Rejecting ──────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_SendsThePendingRunBackWithItsReason()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        await _service.SubmitForApprovalAsync(run.Id);

        var result = await _service.RejectAsync(run.Id, "  Bala's OT looks wrong  ");

        Assert.True(result.Ok);
        Assert.Equal(PayrollRunStatus.DRAFT, result.Run!.Status);
        Assert.Equal("Bala's OT looks wrong", result.Run.ApprovalRejectionReason);
        // The proposal is withdrawn, so who made it is no longer current.
        Assert.Null(result.Run.SubmittedForApprovalById);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunRejectApproval));
    }

    // The reason describes the version that was sent back, not the next one.
    [Fact]
    public async Task SubmitForApprovalAsync_ClearsAPreviousRejectionReason()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        await _service.SubmitForApprovalAsync(run.Id);
        await _service.RejectAsync(run.Id, "wrong");

        var again = await _service.SubmitForApprovalAsync(run.Id);

        Assert.True(again.Ok);
        Assert.Null(again.Run!.ApprovalRejectionReason);
    }

    [Fact]
    public async Task RejectAsync_RefusesARunThatIsNotPending()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        var result = await _service.RejectAsync(run.Id, "nope");

        Assert.False(result.Ok);
    }

    // ─── Reverting ──────────────────────────────────────────────────────

    [Fact]
    public async Task RevertToDraftAsync_PutsASubmittedRunBackAndClearsItsTrail()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await SubmittedRunAsync();

        var result = await _service.RevertToDraftAsync(run.Id);

        Assert.True(result.Ok);
        Assert.Equal(PayrollRunStatus.DRAFT, result.Run!.Status);
        Assert.Null(result.Run.SubmittedAt);
        Assert.Null(result.Run.SubmittedById);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunRevertToDraft));
    }

    // THE cascade. February and March carry YTD-cumulative figures computed off
    // January. Reverting January alone would leave them filed against a
    // year-to-date that no longer exists.
    [Fact]
    public async Task RevertToDraftAsync_CascadesToEveryLaterSubmittedMonthThatYear()
    {
        AddEmployee("usr-1", "Aisyah");
        var january = await SubmittedRunAsync(2026, 1);
        await SubmittedRunAsync(2026, 2);
        await SubmittedRunAsync(2026, 3);

        var result = await _service.RevertToDraftAsync(january.Id);

        Assert.True(result.Ok);
        Assert.Equal(["February 2026", "March 2026"], result.AlsoReverted);

        var all = await _service.GetAllAsync();
        Assert.All(all, r => Assert.Equal(PayrollRunStatus.DRAFT, r.Status));
    }

    // A different year's runs do not depend on this one's year-to-date.
    [Fact]
    public async Task RevertToDraftAsync_LeavesTheFollowingYearAlone()
    {
        AddEmployee("usr-1", "Aisyah");
        var december = await SubmittedRunAsync(2025, 12);
        var january = await SubmittedRunAsync(2026, 1);

        var result = await _service.RevertToDraftAsync(december.Id);

        Assert.True(result.Ok);
        Assert.Empty(result.AlsoReverted);

        var stillSubmitted = (await _service.GetAllAsync()).Single(r => r.Id == january.Id);
        Assert.Equal(PayrollRunStatus.SUBMITTED, stillSubmitted.Status);
    }

    // The admin has to be told before confirming, not after.
    [Fact]
    public async Task GetRevertImpactAsync_NamesTheMonthsARevertWouldDragBack()
    {
        AddEmployee("usr-1", "Aisyah");
        var january = await SubmittedRunAsync(2026, 1);
        await SubmittedRunAsync(2026, 2);

        Assert.Equal(["February 2026"], await _service.GetRevertImpactAsync(january.Id));
    }

    [Fact]
    public async Task GetRevertImpactAsync_IsEmptyForADraft()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        Assert.Empty(await _service.GetRevertImpactAsync(run.Id));
    }

    // Reverting exists to let an admin FIX a filed month, so it must stay
    // reachable — including for the payslips and attachments it keeps.
    [Fact]
    public async Task RevertToDraftAsync_KeepsThePayslipsSoTheDraftCanBeEdited()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await SubmittedRunAsync();

        await _service.RevertToDraftAsync(run.Id);

        Assert.Single(await _payslips.GetForRunAsync(run.Id));
    }

    // A status change does not make payslips any more or less current than
    // they were, so it must not touch the staleness flag either way.
    [Fact]
    public async Task StatusChanges_DoNotTouchTheStalenessFlag()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        await _service.SubmitForApprovalAsync(run.Id);
        await _service.ApproveAsync(run.Id);
        await _service.RevertToDraftAsync(run.Id);

        Assert.Null((await _runs.GetByIdAsync(run.Id))!.LastMutatedAt);
    }

    [Fact]
    public async Task RevertToDraftAsync_RefusesARunThatIsNotSubmitted()
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        var result = await _service.RevertToDraftAsync(run.Id);

        Assert.False(result.Ok);
        Assert.Contains("Only a submitted run", result.Error);
    }

    // ─── Deleting a draft ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteDraftAsync_RemovesTheRunAndEverythingHangingOffIt()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();

        _db.PayrollRunAdjustments.Add(new PayrollRunAdjustment
        {
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            EmployeeProfileId = profile.Id,
            OtNormalHours = 4m,
        });
        _db.PayrollRunClaims.Add(new PayrollRunClaim
        {
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            ClaimId = "clm-1",
            EmployeeProfileId = profile.Id,
            Label = "Taxi",
            Amount = 50m,
        });
        await _db.SaveChangesAsync();

        var result = await _service.DeleteDraftAsync(run.Id);

        Assert.True(result.Ok);
        Assert.Empty(await _service.GetAllAsync());
        Assert.Empty(await _payslips.GetForRunAsync(run.Id));
        Assert.Empty(await _payslips.GetLineItemsForRunAsync(run.Id));
        Assert.Empty(await _adjustments.GetForRunAsync(run.Id));
        Assert.Empty(await _runClaims.GetForRunAsync(run.Id));
        Assert.True(_audit.Recorded(AuditActions.PayrollRunDelete));
    }

    // Deleting the run frees its claims rather than consuming them — the claim
    // itself was never the payroll's to destroy.
    [Fact]
    public async Task DeleteDraftAsync_LeavesTheClaimFreeToAttachElsewhere()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        _db.PayrollRunClaims.Add(new PayrollRunClaim
        {
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            ClaimId = "clm-1",
            EmployeeProfileId = profile.Id,
            Label = "Taxi",
            Amount = 50m,
        });
        await _db.SaveChangesAsync();

        await _service.DeleteDraftAsync(run.Id);

        Assert.Null(await _runClaims.GetByClaimIdAsync("clm-1"));
    }

    [Theory]
    [InlineData(PayrollRunStatus.PENDING_APPROVAL)]
    [InlineData(PayrollRunStatus.SUBMITTED)]
    public async Task DeleteDraftAsync_RefusesAnythingButADraft(PayrollRunStatus status)
    {
        AddEmployee("usr-1", "Aisyah");
        var run = await GeneratedRunAsync();
        await _service.SubmitForApprovalAsync(run.Id);
        if (status == PayrollRunStatus.SUBMITTED) await _service.ApproveAsync(run.Id);

        var result = await _service.DeleteDraftAsync(run.Id);

        Assert.False(result.Ok);
        Assert.Single(await _service.GetAllAsync());
    }

    [Fact]
    public async Task DeleteDraftAsync_ReportsAMissingRunAsNotFound()
    {
        var result = await _service.DeleteDraftAsync("nope");

        Assert.False(result.Ok);
        Assert.Null(result.Error);
    }

    // ─── Phase 6b: the statutory readiness guard ────────────────────────
    //
    // What the submission files need is defined by the generators, so the
    // guard asks them. Failing here — while the run is still a freely
    // editable draft — is far cheaper than failing weeks later at generation
    // time, when fixing it means a revert that cascades through the year.

    [Fact]
    public async Task SubmitForApprovalAsync_RefusesWhenCompanyInfoIsIncomplete()
    {
        AddEmployee("usr-1", "Aisyah");
        var info = await _db.PayrollCompanyInfos.FirstAsync();
        info.PerkesoEmployerCode = null;
        await _db.SaveChangesAsync();

        var run = await GeneratedRunAsync();
        var result = await _service.SubmitForApprovalAsync(run.Id);

        Assert.False(result.Ok);
        Assert.Contains("PERKESO employer code", result.Error);
        Assert.Equal(PayrollRunStatus.DRAFT,
            (await _runs.GetByIdAsync(run.Id))!.Status);
    }

    [Fact]
    public async Task SubmitForApprovalAsync_RefusesAndNamesAnEmployeeMissingAnIc()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        profile.IdNumber = null;
        await _db.SaveChangesAsync();

        var run = await GeneratedRunAsync();
        var result = await _service.SubmitForApprovalAsync(run.Id);

        Assert.False(result.Ok);
        Assert.Contains("Aisyah", result.Error);
    }

    // PCB computes without a TIN, and a new joiner waiting on one must not
    // hold up everyone else's pay. The CP39 file checks it separately, and
    // only for employees who actually had tax withheld.
    [Fact]
    public async Task SubmitForApprovalAsync_AllowsAMissingIncomeTaxNumber()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        profile.IncomeTaxNumber = null;
        await _db.SaveChangesAsync();

        var run = await GeneratedRunAsync();

        Assert.True((await _service.SubmitForApprovalAsync(run.Id)).Ok);
    }
}
