using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType, EmployeePolicy

namespace AltomateHR.Api.Modules.Payroll;

// Orchestrates a month of payroll: create the run, build every payslip through
// PayslipCalculator, snapshot the result.
//
// The one rule that shapes everything here: GENERATION IS DESTRUCTIVE. Pressing
// Generate discards the run's payslips and line items and rebuilds them from the
// employees' current profiles. That is what makes a draft run safe to re-run
// after fixing a profile — and why anything that must survive a regeneration
// cannot live on a payslip alone.
public class PayrollRunService : IPayrollRunService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollSettingsService _settings;
    private readonly IDirectoryService _directory;
    private readonly IPayrollRunAdjustmentRepository _adjustments;
    private readonly IPayrollRunClaimRepository _runClaims;
    private readonly IPolicyService _policies;
    private readonly IStatutoryFileService _statutory;
    private readonly IHoursSummaryService _hours;
    private readonly ILeaveService _leave;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IPayrollXeroSyncService _xeroSync;
    private readonly IEmployeeLoanService _loans;

    public PayrollRunService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollSettingsService settings,
        IDirectoryService directory,
        IPayrollRunAdjustmentRepository adjustments,
        IPayrollRunClaimRepository runClaims,
        IPolicyService policies,
        IStatutoryFileService statutory,
        IHoursSummaryService hours,
        ILeaveService leave,
        ICurrentUser currentUser,
        IAuditService audit,
        IPayrollXeroSyncService xeroSync,
        IEmployeeLoanService loans)
    {
        _statutory = statutory;
        _hours = hours;
        _leave = leave;
        _currentUser = currentUser;
        _runs = runs;
        _payslips = payslips;
        _settings = settings;
        _directory = directory;
        _adjustments = adjustments;
        _runClaims = runClaims;
        _policies = policies;
        _audit = audit;
        _xeroSync = xeroSync;
        _loans = loans;
    }

    // The JSON on EmployeeProfile was written by the reference app, so reads are
    // case-insensitive and enums arrive as names rather than ordinals.
    private static readonly JsonSerializerOptions ProfileJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Shared with every reader of these columns — see PayrollSnapshotJson.
    private static JsonSerializerOptions SnapshotJson => PayrollSnapshotJson.Options;

    public async Task<List<PayrollRunDto>> GetAllAsync()
    {
        var runs = await _runs.GetAllAsync();
        return runs.Select(ToDto).ToList();
    }

    public async Task<PayrollRunDetailDto?> GetAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return null;

        var payslips = await _payslips.GetForRunAsync(run.Id);
        var lineItems = await _payslips.GetLineItemsForRunAsync(run.Id);

        return BuildDetail(run, payslips, lineItems);
    }

    public async Task<PayrollRunSaveResult> CreateAsync(CreatePayrollRunDto dto)
    {
        var existing = await _runs.GetByPeriodAsync(dto.PeriodYear, dto.PeriodMonth);
        if (existing is not null)
        {
            return new PayrollRunSaveResult(
                false, null,
                $"A payroll run already exists for {PeriodLabel(dto.PeriodYear, dto.PeriodMonth)}.");
        }

        var run = await _runs.AddAsync(new PayrollRun
        {
            PeriodYear = dto.PeriodYear,
            PeriodMonth = dto.PeriodMonth,
            Status = PayrollRunStatus.DRAFT,
            Source = PayrollRunSource.COMPUTED,
        });

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunCreate,
            $"Started the {PeriodLabel(run.PeriodYear, run.PeriodMonth)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    public async Task<PayrollRunGenerateResult> GenerateAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return new PayrollRunGenerateResult(false, null, null);

        // A submitted run is a filed figure. Rebuilding its payslips would
        // rewrite history that KWSP, PERKESO and LHDN have already been told.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunGenerateResult(
                false, null, "Only a draft run can be generated.");
        }

        var settings = await _settings.GetEffectiveAsync();

        var profiles = await _directory.GetProfilesForCurrentOrgAsync();
        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, StringComparer.Ordinal);
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, StringComparer.Ordinal);

        // This year's locked-in figures, excluding this run — a run must never
        // feed its own YTD baseline, or every regeneration would compound.
        var ytdByEmployee = await _payslips.GetYtdByEmployeeAsync(run.PeriodYear, run.Id);

        // The two tables that exist because THIS method is destructive. Both
        // are read up front, keyed by employee: what the admin typed, and the
        // claims they attached to be paid back through this month's pay.
        var adjustments = (await _adjustments.GetForRunAsync(run.Id))
            .ToDictionary(a => a.EmployeeProfileId, StringComparer.Ordinal);

        var claimsByEmployee = (await _runClaims.GetForRunAsync(run.Id))
            .GroupBy(c => c.EmployeeProfileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // OT multipliers come from each employee's policy. Resolved in one
        // batch — the per-employee call is two queries a head, which on a
        // 200-person run bought 400 round trips for a handful of policies.
        var policies = await _policies.GetEffectivePoliciesForEmployeesAsync(
            profiles.Select(p => p.UserId));

        // Attendance and leave for the period, both org-wide in one read.
        // `to` is the last day of the month — inclusive, which is what both
        // seams expect.
        var periodStart = new DateTime(run.PeriodYear, run.PeriodMonth, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var hoursByUser = await _hours.GetHoursForEmployeesAsync(
            profiles.Select(p => p.UserId), periodStart, periodEnd);

        // Keyed by user id, like the hours — the Leave module works in users.
        var unpaidDaysByUser = await _leave.GetApprovedUnpaidDaysForOrgAsync(periodStart, periodEnd);

        // What each employee's active loans take this period. One query for
        // the whole run, like the hours and the leave above it.
        var loanRepayments = await _loans.GetRepaymentsForPeriodAsync(
            run.PeriodYear, run.PeriodMonth);

        var workingDaysBasis = PayPeriod.WorkingDaysForPeriod(
            run.PeriodYear, run.PeriodMonth, settings.WorkingDaysRule);

        var payslips = new List<Payslip>();
        var lineItems = new List<PayslipLineItem>();
        var skipped = new List<SkippedEmployeeDto>();
        var now = DateTime.UtcNow;

        foreach (var profile in profiles)
        {
            var name = users.TryGetValue(profile.UserId, out var user)
                ? user.Name
                : string.Empty;

            var skipReason = SkipReasonFor(profile, run);
            if (skipReason is not null)
            {
                skipped.Add(new SkippedEmployeeDto
                {
                    EmployeeProfileId = profile.Id,
                    Name = name,
                    Reason = skipReason,
                });
                continue;
            }

            var ytd = ytdByEmployee.TryGetValue(profile.Id, out var found)
                ? found
                : new PayrollYtdTotals();

            adjustments.TryGetValue(profile.Id, out var adjustment);
            policies.TryGetValue(profile.UserId, out var policy);
            var attached = claimsByEmployee.GetValueOrDefault(profile.Id) ?? [];
            var buckets = hoursByUser.GetValueOrDefault(profile.UserId);
            var unpaidDays = (decimal)unpaidDaysByUser.GetValueOrDefault(profile.UserId);

            var loanRepayment = loanRepayments.GetValueOrDefault(profile.Id);

            var result = PayslipCalculator.Calculate(
                BuildInput(
                    profile, settings, run, ytd, adjustment, attached, policy,
                    buckets, unpaidDays, workingDaysBasis, loanRepayment));

            memberships.TryGetValue(profile.UserId, out var membership);

            var payslip = ToPayslip(run, profile, name, membership, result, now);
            payslips.Add(payslip);
            lineItems.AddRange(result.LineItems.Select(li => ToLineItem(payslip.Id, li, now)));
        }

        await _payslips.ReplaceForRunAsync(run.Id, payslips, lineItems);

        ApplyTotals(run, payslips);
        run.GeneratedAt = now;
        // The payslips now match their inputs, so the staleness warning clears.
        run.LastMutatedAt = null;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunGenerate,
            $"Generated {payslips.Count} payslip(s) for the "
            + $"{PeriodLabel(run.PeriodYear, run.PeriodMonth)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear,
                run.PeriodMonth,
                PayslipCount = payslips.Count,
                SkippedCount = skipped.Count,
                run.TotalGross,
                run.TotalNet,
                run.TotalCostToEmployer,
            }));

        return new PayrollRunGenerateResult(true, new GeneratePayrollRunResultDto
        {
            Detail = BuildDetail(run, payslips, lineItems),
            PayslipCount = payslips.Count,
            SkippedEmployees = skipped,
        }, null);
    }

    // ─── The status machine ─────────────────────────────────────────────

    public async Task<PayrollRunSaveResult> SubmitForApprovalAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return Refused(run.Status == PayrollRunStatus.PENDING_APPROVAL
                ? "This run is already awaiting approval."
                : "This run has already been submitted.");
        }

        var payslips = await _payslips.GetForRunAsync(run.Id);

        // Guard 1 — an empty run cannot be finalised. Usually it just means
        // Generate was never pressed.
        if (payslips.Count == 0)
        {
            return Refused(
                "Generate this run's payslips before submitting — an empty run cannot be finalised.");
        }

        // Guard 2 — staleness. The inputs moved after the payslips were built,
        // so the figures about to be filed are not the ones the inputs now
        // imply. LastMutatedAt is set by every adjustment and claim mutation
        // and cleared by generation.
        var latestGeneration = payslips.Max(p => p.CreatedAt);
        if (run.LastMutatedAt is not null && run.LastMutatedAt > latestGeneration)
        {
            return Refused(
                "Payroll inputs changed after this draft was generated. "
                + "Re-run payroll before submitting.");
        }

        // Guard 3 — nobody may take home nothing. Deductions larger than
        // someone's pay is a data-entry mistake far more often than it is a
        // real month, and once submitted the payslip is immutable.
        //
        // Note this is a SUBMIT-time guard, not a floor inside the calculator:
        // the calculator lets net go negative (as the reference's does), and
        // whether EA 1955 s.24 requires a floor is still an open question — see
        // MIGRATION.md. Refusing here is the protection that exists today.
        var nonPositive = payslips.Where(p => p.NetPay <= 0m).ToList();
        if (nonPositive.Count > 0)
        {
            var names = string.Join(", ", nonPositive.Take(5).Select(p => p.SnapshotName));
            var more = nonPositive.Count > 5 ? $" and {nonPositive.Count - 5} more" : string.Empty;

            return Refused(
                $"Cannot submit — net pay is zero or negative for: {names}{more}. "
                + "Reduce their deductions, then re-run payroll.");
        }

        // Guard 4 — chronological order. YTD is read off SUBMITTED runs only,
        // and a payslip snapshot is immutable once filed. Submitting February
        // before January freezes February's PCB, EPF and SOCSO against a zero
        // YTD, and submitting January afterwards cannot go back and fix it.
        var refusal = await ChronologyRefusalAsync(run);
        if (refusal is not null) return Refused(refusal);

        // Guard 5 — statutory readiness. The submission files this month will
        // need are defined by the generators, so this asks THEM what is
        // missing. Failing here, while the run is still a draft and freely
        // editable, is far cheaper than failing at generation time: by then
        // the run is filed, and fixing it means a revert that cascades through
        // the rest of the year.
        var readiness = await _statutory.GetReadinessAsync(run.Id);
        if (readiness is not null && !readiness.Ok)
        {
            return Refused($"Cannot submit — fix these first: {readiness.Describe()}");
        }

        var now = DateTime.UtcNow;
        run.Status = PayrollRunStatus.PENDING_APPROVAL;
        run.SubmittedForApprovalAt = now;
        run.SubmittedForApprovalById = _currentUser.UserId;
        // A fresh submission clears the last rejection — the reason described
        // the version that was sent back, not this one.
        run.ApprovalRejectionReason = null;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunSubmitForApproval,
            $"Submitted the {PeriodLabel(run.PeriodYear, run.PeriodMonth)} payroll run for approval",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear,
                run.PeriodMonth,
                PayslipCount = payslips.Count,
                run.TotalNet,
                run.TotalCostToEmployer,
            }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    // Null means the order is fine. Two ways it is not, both fixed the same
    // way — submit the earlier month first.
    private async Task<string?> ChronologyRefusalAsync(PayrollRun run)
    {
        var (prevYear, prevMonth) = run.PeriodMonth > 1
            ? (run.PeriodYear, run.PeriodMonth - 1)
            : (run.PeriodYear - 1, 12);

        var previous = await _runs.GetByPeriodAsync(prevYear, prevMonth);

        if (previous is not null)
        {
            return previous.Status == PayrollRunStatus.SUBMITTED
                ? null
                : $"Submit {PeriodLabel(prevYear, prevMonth)} first — payroll runs are "
                  + "submitted in order, and that month's run is still a draft.";
        }

        // No run for the previous month at all. That is fine for an org's very
        // first run, and a gap otherwise.
        var hasEarlier = await _runs.HasEarlierSubmittedRunAsync(run.PeriodYear, run.PeriodMonth);

        return hasEarlier
            ? $"Create and submit {PeriodLabel(prevYear, prevMonth)} first — there are earlier "
              + "submitted runs but no run for that month. Skipping it would freeze this "
              + "month's PCB, EPF and SOCSO against a zero year-to-date, and a submitted "
              + "payslip cannot be edited afterwards."
            : null;
    }

    public async Task<PayrollRunSaveResult> ApproveAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.PENDING_APPROVAL)
        {
            return Refused("Only a run awaiting approval can be approved.");
        }

        var now = DateTime.UtcNow;
        run.Status = PayrollRunStatus.SUBMITTED;
        run.SubmittedAt = now;
        // The APPROVER, not the proposer. Who put this month's pay live is the
        // question an audit asks, and SubmittedForApprovalById still records
        // who proposed it.
        run.SubmittedById = _currentUser.UserId;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunApprove,
            $"Approved the {PeriodLabel(run.PeriodYear, run.PeriodMonth)} payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new
            {
                run.PeriodYear,
                run.PeriodMonth,
                run.SubmittedForApprovalById,
                run.TotalNet,
                run.TotalCostToEmployer,
            }));

        // Post to Xero, if the org asked for that. Deliberately AFTER the
        // approval is committed and deliberately best-effort: an approval
        // already given must not be undone because an accounting integration
        // was unreachable. The failure lands on the run's own error column.
        await _xeroSync.SyncOnApprovalAsync(run.Id);

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    public async Task<PayrollRunSaveResult> RejectAsync(string id, string? reason)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        if (run.Status != PayrollRunStatus.PENDING_APPROVAL)
        {
            return Refused("Only a run awaiting approval can be sent back.");
        }

        run.Status = PayrollRunStatus.DRAFT;
        run.ApprovalRejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // The proposal is withdrawn, so who made it is no longer current.
        run.SubmittedForApprovalAt = null;
        run.SubmittedForApprovalById = null;
        await _runs.UpdateAsync(run);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunRejectApproval,
            $"Sent the {PeriodLabel(run.PeriodYear, run.PeriodMonth)} payroll run back to draft",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, Reason = run.ApprovalRejectionReason }));

        return new PayrollRunSaveResult(true, ToDto(run), null);
    }

    public async Task<PayrollRunRevertResult> RevertToDraftAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return new PayrollRunRevertResult(false, null, [], null);

        if (run.Status != PayrollRunStatus.SUBMITTED)
        {
            return new PayrollRunRevertResult(
                false, null, [], "Only a submitted run can be reverted to draft.");
        }

        // THE cascade. Every later submitted month in the same year carries
        // YTD-cumulative figures — PCB's annualisation, the SOCSO/EIS relief —
        // computed off this month. Reverting this one alone would leave them
        // filed against a year-to-date that no longer exists.
        var later = await _runs.GetSubmittedLaterInYearAsync(run.PeriodYear, run.PeriodMonth);

        var reverted = later.Append(run).ToList();
        foreach (var target in reverted) RevertOne(target);

        await _runs.UpdateManyAsync(reverted);

        var alsoReverted = later
            .Select(r => PeriodLabel(r.PeriodYear, r.PeriodMonth))
            .ToList();

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunRevertToDraft,
            $"Reverted the {PeriodLabel(run.PeriodYear, run.PeriodMonth)} payroll run to draft"
            + (alsoReverted.Count == 0
                ? string.Empty
                : $", and with it {string.Join(", ", alsoReverted)}"),
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth, AlsoReverted = alsoReverted }));

        return new PayrollRunRevertResult(true, ToDto(run), alsoReverted, null);
    }

    // Back to DRAFT, with the approval trail cleared so a re-submission records
    // who did it THIS time. LastMutatedAt is deliberately untouched: a status
    // change does not make payslips any more or less current than they were.
    private static void RevertOne(PayrollRun run)
    {
        run.Status = PayrollRunStatus.DRAFT;
        run.SubmittedAt = null;
        run.SubmittedById = null;
        run.SubmittedForApprovalAt = null;
        run.SubmittedForApprovalById = null;
        run.ApprovalRejectionReason = null;
    }

    public async Task<IReadOnlyList<string>> GetRevertImpactAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null || run.Status != PayrollRunStatus.SUBMITTED) return [];

        var later = await _runs.GetSubmittedLaterInYearAsync(run.PeriodYear, run.PeriodMonth);
        return later.Select(r => PeriodLabel(r.PeriodYear, r.PeriodMonth)).ToList();
    }

    public async Task<PayrollRunSaveResult> DeleteDraftAsync(string id)
    {
        var run = await _runs.GetByIdAsync(id);
        if (run is null) return NotFound();

        // Only a draft. A submitted run has been filed, and a pending one is
        // somebody else's decision to make.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return Refused("Only a draft run can be deleted. Revert it to draft first.");
        }

        // Clear what hangs off the run before the run itself. Passing empty
        // lists to ReplaceForRunAsync is the delete half of the same
        // transactional replace generation uses.
        await _payslips.ReplaceForRunAsync(run.Id, [], []);
        await _adjustments.DeleteForRunAsync(run.Id);
        // Detaching leaves the claims themselves untouched — they simply become
        // free to attach to another run.
        await _runClaims.DeleteForRunAsync(run.Id);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunDelete,
            $"Deleted the {PeriodLabel(run.PeriodYear, run.PeriodMonth)} draft payroll run",
            TargetType: "PayrollRun",
            TargetId: run.Id,
            Metadata: new { run.PeriodYear, run.PeriodMonth }));

        await _runs.DeleteAsync(run);

        return new PayrollRunSaveResult(true, null, null);
    }

    private static PayrollRunSaveResult NotFound() => new(false, null, null);

    private static PayrollRunSaveResult Refused(string error) => new(false, null, error);

    // ─── Who belongs on the run ─────────────────────────────────────────

    // Null means "include them". Everything else is a legitimate absence, not an
    // error — which is why the caller reports these rather than failing.
    private static string? SkipReasonFor(EmployeeProfile profile, PayrollRun run)
    {
        if (profile.IsArchived) return "Archived";

        // Their final payroll has already been reported to LHDN, so paying them
        // again would contradict a filed Form E.
        if (profile.ReportedToLhdn) return "Final payroll already reported to LHDN";

        var calendarDays = PayPeriod.CalendarDaysInMonth(run.PeriodYear, run.PeriodMonth);
        var workedDays = PayPeriod.EffectiveWorkedDays(
            run.PeriodYear, run.PeriodMonth, profile.JoinDate, profile.LeaveDate, calendarDays);

        // Joined after the period ended, or left before it started.
        return workedDays is null ? "Not employed during this period" : null;
    }

    // ─── Profile → calculator input ─────────────────────────────────────

    private static PayslipCalculator.Input BuildInput(
        EmployeeProfile profile,
        PayrollSettings settings,
        PayrollRun run,
        PayrollYtdTotals ytd,
        PayrollRunAdjustment? adjustment,
        IReadOnlyList<PayrollRunClaim> attachedClaims,
        EmployeePolicy? policy,
        HoursBucketsDto? hours,
        decimal unpaidLeaveDays,
        int workingDaysBasis,
        decimal loanRepayment)
    {
        // The employee's prior-employer carryover (their TP3) folds into the YTD
        // figures, so a mid-year joiner is not over-withheld until December.
        //
        // `PrevIncludesPriorThisOrgPeriod` marks a rehire whose declared prev*
        // figures ALREADY include the months they worked here earlier this year.
        // Adding this org's own YTD on top would then double-count it.
        var carryOwnOrgYtd = !profile.PrevIncludesPriorThisOrgPeriod;

        // Cash overtime needs hours on the run's adjustment row, and a policy
        // that does not say otherwise.
        //
        // A policy can switch cash OT off two ways, and both are honoured: OT
        // disabled outright, or TIME_BANK, which credits time off for the same
        // hours — paying cash as well would pay them twice.
        //
        // The ABSENCE of a policy is not one of those ways, and this is a
        // deliberate divergence from the reference, which pays nothing without
        // one. Two reasons. The hours only exist because an admin typed them,
        // so paying zero for them is an underpayment with nothing on the
        // payslip to explain it. And EA 1955 s.60A sets a floor an employer
        // does not opt into by configuring a policy — so with no policy the
        // calculator's statutory defaults apply, which is what its OtRate*
        // defaults already are.
        var cashOt = adjustment is not null
                     && (policy is null || (policy.OtEnabled && policy.OtMethod == OtMethod.CASH));

        // Attendance figures only mean anything when the employee's policy puts
        // them on attendance at all. Otherwise the column reads "—" rather than
        // a confident zero, which for HOURLY staff would be a zero PAYSLIP.
        var attendanceApplies = policy?.CanAccessAttendance == true && hours is not null;

        // NORMAL minutes, not total worked: anything past the shift length sits
        // in BeyondShiftMin and only becomes money through an approved overtime
        // submission. Counting it here would pay it twice, once flat and once
        // at the OT multiplier.
        var autoWorkedHours = attendanceApplies ? hours!.NormalMin / 60m : (decimal?)null;
        var autoExpectedHours = attendanceApplies ? hours!.ExpectedMin / 60m : (decimal?)null;

        // The admin's per-run override always wins over what attendance derived.
        var workedHours = adjustment?.WorkedHours ?? autoWorkedHours;

        // Expected hours are a MONTHLY concept — an hourly employee is paid for
        // what they worked, with nothing to compare it against.
        var expectedHours = profile.SalaryType == SalaryType.MONTHLY
            ? adjustment?.ExpectedHours ?? autoExpectedHours
            : null;

        return new PayslipCalculator.Input
        {
            PeriodYear = run.PeriodYear,
            PeriodMonth = run.PeriodMonth,

            SalaryType = profile.SalaryType,
            MonthlySalary = profile.MonthlySalary,
            HourlyRate = profile.HourlyRate,
            // The profile's recurring rows with this run's overrides applied,
            // followed by this run's one-off rows. Merged into ONE list on
            // purpose: the calculator has a single category-aware routing loop,
            // and a one-off deduction has to obey `ReducesBase`, the exemption
            // ceilings and the six `SubjectTo*` flags exactly as a recurring one
            // does.
            FixedAllowances = WithLoanRepayment(
                WithUnpaidLeave(
                    PayrollRunAdjustments.Merge(
                        ParseFixedAllowances(profile.FixedAllowancesJson), adjustment),
                    profile, unpaidLeaveDays, workingDaysBasis),
                loanRepayment),
            JoinDate = profile.JoinDate,
            LeaveDate = profile.LeaveDate,

            Nationality = profile.Nationality,
            HasPr = profile.HasPr,
            IsResident = profile.IsResident,
            IsOku = profile.IsOku,
            DateOfBirth = profile.DateOfBirth,

            ContributeToEpf = profile.ContributeToEpf,
            EpfMemberBefore1998 = profile.EpfMemberBefore1998,
            EpfEmployeeRate = profile.EpfEmployeeRate,
            EpfEmployeeVoluntary = profile.EpfEmployeeVoluntary,
            EpfEmployerVoluntary = profile.EpfEmployerVoluntary,

            SocsoScheme = profile.SocsoScheme,
            ContributeToEis = profile.ContributeToEis,
            ContributeToSkbbk = profile.ContributeToSkbbk,

            SpouseWorking = profile.SpouseWorking,
            SpouseDisabled = profile.SpouseDisabled,
            Children = ParseChildRelief(profile.ChildReliefJson),

            IncomeTaxNumber = profile.IncomeTaxNumber,
            EpfNumber = profile.EpfNumber,
            SocsoNumber = profile.SocsoNumber,

            WorkingDaysRule = settings.WorkingDaysRule,
            DefaultEpfEmployeeRate = settings.DefaultEpfEmployeeRate,
            HrdfEnabled = settings.HrdfEnabled,
            HrdfRate = settings.HrdfRate,
            AutoApplySocsoEisRelief = settings.AutoApplySocsoEisRelief,

            // ---- Overtime ----
            // Hours are the admin's, from the run's adjustment row; the
            // multipliers are the policy's. Both or neither: `cashOt` is false
            // when the policy banks overtime as time off or disables it
            // outright, and paying cash for hours already banked would pay them
            // twice.
            OtNormalHours = cashOt ? adjustment!.OtNormalHours : 0m,
            OtRestHours = cashOt ? adjustment!.OtRestHours : 0m,
            OtPublicHours = cashOt ? adjustment!.OtPublicHours : 0m,

            // Left at the calculator's statutory defaults (EA 1955 s.60A) when
            // the employee has no policy — the floor the Act sets, not zero.
            OtRateNormal = policy?.OtRateNormalDay ?? 1.5m,
            OtRateRest = policy?.OtRateRestDay ?? 2.0m,
            OtRatePublicHoliday = policy?.OtRatePublicHoliday ?? 3.0m,

            // ---- Attendance ----
            // Derived from the period's attendance, overridable per run.
            // Display-only for MONTHLY staff, whose pay is day-based — but for
            // HOURLY staff WorkedHours IS the paid quantity.
            WorkedHours = workedHours,
            ExpectedHours = expectedHours,
            UnpaidLeaveDays = unpaidLeaveDays > 0m ? unpaidLeaveDays : null,

            // ---- Attached claims ----
            // Each becomes a REIMBURSEMENT line carrying its claim id, so the
            // rebuilt line item still points back at what it is paying.
            // Snapshotted amounts, not the claims' current ones.
            Reimbursements = attachedClaims
                .Select(c => new PayslipCalculator.Reimbursement(c.ClaimId, c.Label, c.Amount))
                .ToList(),

            YtdTaxable = ytd.Taxable + Carry(profile.PrevRemuneration, carryOwnOrgYtd, ytd.Taxable),
            YtdEpf = ytd.Epf + Carry(profile.PrevEpf, carryOwnOrgYtd, ytd.Epf),
            YtdPcb = ytd.Pcb + Carry(profile.PrevPcb, carryOwnOrgYtd, ytd.Pcb),
            YtdZakat = ytd.Zakat + Carry(profile.PrevZakat, carryOwnOrgYtd, ytd.Zakat),
            YtdSocsoEis = ytd.SocsoEis,
            YtdAllowableDeductions = ytd.AllowableDeductions
                + Carry(profile.PrevAllowableDeductions, carryOwnOrgYtd, ytd.AllowableDeductions),
            YtdAllowanceByCategory = ytd.AllowanceByCategory,
        };
    }

    // Approved unpaid leave, docked as its own line rather than by shrinking
    // the salary — so the payslip says why someone was paid less.
    //
    // Appended as an ordinary category row so it goes through the same routing
    // as everything else: `deduct_unpaid_leave` reduces the statutory bases as
    // well as gross, because the wage was never earned. The category is marked
    // SkipProration — the amount is already stated at the full daily rate, so
    // prorating a joiner's deduction as well would dock them twice.
    private static IReadOnlyList<FixedAllowance> WithUnpaidLeave(
        IReadOnlyList<FixedAllowance> rows,
        EmployeeProfile profile,
        decimal unpaidLeaveDays,
        int workingDaysBasis)
    {
        if (profile.SalaryType != SalaryType.MONTHLY) return rows;

        var amount = PayPeriod.UnpaidLeaveDeduction(
            profile.MonthlySalary, unpaidLeaveDays, workingDaysBasis);

        if (amount <= 0m) return rows;

        return [.. rows, new FixedAllowance
        {
            Category = PayrollAdjustmentCategories.DeductUnpaidLeave,
            Name = "Unpaid Leave",
            Amount = amount,
        }];
    }

    // A staff loan's installment for this period, as a deduction row.
    //
    // It goes through the same merged list as everything else so it obeys its
    // category's flags — `deduct_loan_repayment` does NOT reduce the statutory
    // bases, because repaying a loan is the employee spending money they
    // earned, not earning less. EPF, SOCSO, EIS and PCB are all computed on
    // the wage before it.
    private static IReadOnlyList<FixedAllowance> WithLoanRepayment(
        IReadOnlyList<FixedAllowance> rows, decimal repayment)
    {
        if (repayment <= 0m) return rows;

        return [.. rows, new FixedAllowance
        {
            Category = PayrollAdjustmentCategories.DeductLoanRepayment,
            Name = "Loan Repayment",
            Amount = repayment,
        }];
    }

    // How much of a declared prev* figure to add on top of this org's own YTD.
    //
    // Normally all of it — the figure is strictly the previous employer's. On a
    // rehire flagged `PrevIncludesPriorThisOrgPeriod` the declared total already
    // covers the months worked here, so this org's YTD is subtracted back out
    // before adding, and the result is floored at zero rather than credited.
    private static decimal Carry(decimal? declared, bool isPriorEmployerOnly, decimal ownOrgYtd)
    {
        if (declared is null or <= 0m) return 0m;

        return isPriorEmployerOnly
            ? declared.Value
            : Math.Max(0m, declared.Value - ownOrgYtd);
    }

    private static IReadOnlyList<FixedAllowance> ParseFixedAllowances(string? json) =>
        Parse<FixedAllowance>(json);

    private static IReadOnlyList<ChildRelief> ParseChildRelief(string? json) =>
        Parse<ChildRelief>(json);

    // Malformed JSON reads as an empty list. These columns are free-form and
    // written by another system: one bad row must not take a month's payroll
    // down, and an empty list under-claims rather than over-claims.
    private static IReadOnlyList<T> Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, ProfileJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ─── Result → rows ──────────────────────────────────────────────────

    private static Payslip ToPayslip(
        PayrollRun run,
        EmployeeProfile profile,
        string name,
        OrganizationMembership? membership,
        PayslipCalculator.Result r,
        DateTime now) => new()
        {
            PayrollRunId = run.Id,
            EmployeeProfileId = profile.Id,
            UserId = profile.UserId,

            SnapshotName = name,
            SnapshotEmployeeNumber = membership?.EmployeeNumber,
            SnapshotPosition = membership?.JobTitle,
            SnapshotNationality = profile.Nationality,
            SnapshotIsResident = profile.IsResident,

            SnapshotSalaryType = profile.SalaryType,
            SnapshotMonthlySalary = profile.MonthlySalary,
            SnapshotHourlyRate = profile.HourlyRate,
            SnapshotEpfRatesJson = JsonSerializer.Serialize(r.EpfRates, SnapshotJson),

            TotalWorkingDays = r.TotalWorkingDays,
            ProratedDays = r.ProratedDays,
            ProrationDaysInPeriod = r.ProrationDaysInPeriod,
            ProratedFactor = r.ProratedFactor,
            WorkedHours = r.WorkedHours,
            ExpectedHours = r.ExpectedHours,
            UnpaidLeaveDays = r.UnpaidLeaveDays,

            BasicPay = r.BasicPay,
            ProratedPay = r.ProratedPay,
            OtNormalHours = r.OtNormalHours,
            OtRestHours = r.OtRestHours,
            OtPublicHours = r.OtPublicHours,
            OtPay = r.OtPay,
            TotalAllowances = r.TotalAllowances,
            TotalReimbursements = r.TotalReimbursements,
            TotalDeductions = r.TotalDeductions,
            TotalBenefitsInKind = r.TotalBenefitsInKind,

            EpfEmployee = r.EpfEmployee,
            EpfEmployer = r.EpfEmployer,
            SocsoEmployee = r.SocsoEmployee,
            SocsoEmployer = r.SocsoEmployer,
            EisEmployee = r.EisEmployee,
            EisEmployer = r.EisEmployer,
            SkbbkEmployee = r.SkbbkEmployee,
            SkbbkWage = r.SkbbkWage,
            Pcb = r.Pcb,
            PcbNormal = r.PcbNormal,
            PcbAdditional = r.PcbAdditional,
            PcbCalculationJson = JsonSerializer.Serialize(r.PcbCalculation, SnapshotJson),
            Cp38 = r.Cp38,
            Zakat = r.Zakat,
            Hrdf = r.Hrdf,
            HrdfWage = r.HrdfWage,

            GrossPay = r.GrossPay,
            NetPay = r.NetPay,
            TotalCostToEmployer = r.TotalCostToEmployer,

            StatutoryWarnings = r.StatutoryWarnings.Count == 0
                ? null
                : string.Join(',', r.StatutoryWarnings),

            CreatedAt = now,
            UpdatedAt = now,
        };

    private static PayslipLineItem ToLineItem(
        string payslipId, PayslipCalculator.LineItem li, DateTime now) => new()
        {
            PayslipId = payslipId,
            Kind = li.Kind,
            Label = li.Label,
            Amount = li.Amount,
            Category = li.Category,
            PcbTaxableAmount = li.PcbTaxableAmount,
            ClaimId = li.ClaimId,
            SubjectToEpf = li.SubjectToEpf,
            SubjectToSocso = li.SubjectToSocso,
            SubjectToEis = li.SubjectToEis,
            SubjectToPcb = li.SubjectToPcb,
            CreatedAt = now,
        };

    // Denormalised sums so the runs list renders without loading every payslip.
    // Recomputed wholesale on each generation rather than adjusted, because a
    // regeneration replaces the payslips it is summing.
    private static void ApplyTotals(PayrollRun run, List<Payslip> payslips)
    {
        run.EmployeeCount = payslips.Count;

        run.TotalGross = payslips.Sum(p => p.GrossPay);
        run.TotalNet = payslips.Sum(p => p.NetPay);
        run.TotalEmployeeEpf = payslips.Sum(p => p.EpfEmployee);
        run.TotalEmployerEpf = payslips.Sum(p => p.EpfEmployer);
        run.TotalEmployeeSocso = payslips.Sum(p => p.SocsoEmployee);
        run.TotalEmployerSocso = payslips.Sum(p => p.SocsoEmployer);
        run.TotalEmployeeEis = payslips.Sum(p => p.EisEmployee);
        run.TotalEmployerEis = payslips.Sum(p => p.EisEmployer);
        run.TotalEmployeeSkbbk = payslips.Sum(p => p.SkbbkEmployee);
        run.TotalPcb = payslips.Sum(p => p.Pcb);
        run.TotalCp38 = payslips.Sum(p => p.Cp38);
        run.TotalZakat = payslips.Sum(p => p.Zakat);

        run.TotalHrdf = payslips.Sum(p => p.Hrdf);
        // The levy return asks for the headcount and the wage base it was
        // charged on, so both are counted off the wage rather than the levy —
        // a zero-wage employee is not "subject to" it.
        run.EmployeesSubjectToHrdf = payslips.Count(p => p.HrdfWage > 0m);
        run.TotalWagesSubjectToHrdf = payslips.Sum(p => p.HrdfWage);

        run.TotalCostToEmployer = payslips.Sum(p => p.TotalCostToEmployer);
    }

    // ─── Mapping out ────────────────────────────────────────────────────

    private static PayrollRunDetailDto BuildDetail(
        PayrollRun run,
        IReadOnlyList<Payslip> payslips,
        IReadOnlyList<PayslipLineItem> lineItems)
    {
        var byPayslip = lineItems
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        return new PayrollRunDetailDto
        {
            Run = ToDto(run),
            Payslips = payslips
                .Select(p => PayslipMapper.ToDto(
                    p,
                    byPayslip.TryGetValue(p.Id, out var items) ? items : []))
                .ToList(),
        };
    }

    private static PayrollRunDto ToDto(PayrollRun r) => new()
    {
        Id = r.Id,
        PeriodYear = r.PeriodYear,
        PeriodMonth = r.PeriodMonth,
        PeriodLabel = PeriodLabel(r.PeriodYear, r.PeriodMonth),
        Status = r.Status,
        Source = r.Source,
        EmployeeCount = r.EmployeeCount,
        TotalGross = r.TotalGross,
        TotalNet = r.TotalNet,
        TotalEmployeeEpf = r.TotalEmployeeEpf,
        TotalEmployerEpf = r.TotalEmployerEpf,
        TotalEmployeeSocso = r.TotalEmployeeSocso,
        TotalEmployerSocso = r.TotalEmployerSocso,
        TotalEmployeeEis = r.TotalEmployeeEis,
        TotalEmployerEis = r.TotalEmployerEis,
        TotalEmployeeSkbbk = r.TotalEmployeeSkbbk,
        TotalPcb = r.TotalPcb,
        TotalCp38 = r.TotalCp38,
        TotalZakat = r.TotalZakat,
        TotalHrdf = r.TotalHrdf,
        EmployeesSubjectToHrdf = r.EmployeesSubjectToHrdf,
        TotalWagesSubjectToHrdf = r.TotalWagesSubjectToHrdf,
        TotalCostToEmployer = r.TotalCostToEmployer,
        GeneratedAt = r.GeneratedAt,
        SubmittedForApprovalAt = r.SubmittedForApprovalAt,
        SubmittedForApprovalById = r.SubmittedForApprovalById,
        SubmittedAt = r.SubmittedAt,
        SubmittedById = r.SubmittedById,
        ApprovalRejectionReason = r.ApprovalRejectionReason,
        // Stale = the inputs moved after the payslips were built.
        IsStale = r.LastMutatedAt is not null
                  && (r.GeneratedAt is null || r.LastMutatedAt > r.GeneratedAt),
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };


    private static string PeriodLabel(int year, int month) =>
        PayrollPeriodLabel.For(year, month);
}
