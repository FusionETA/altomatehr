using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class EmployeeLoanService : IEmployeeLoanService
{
    private readonly IEmployeeLoanRepository _loans;
    private readonly IPayrollRunRepository _runs;
    private readonly IDirectoryService _directory;
    private readonly IAuditService _audit;
    // Optional so hand-built instances in tests need not supply it; the app
    // always does. See PayrollDraftStaleness for why saves here mark drafts.
    private readonly IPayrollDraftStaleness? _drafts;

    public EmployeeLoanService(
        IEmployeeLoanRepository loans,
        IPayrollRunRepository runs,
        IDirectoryService directory,
        IAuditService audit,
        IPayrollDraftStaleness? drafts = null)
    {
        _drafts = drafts;
        _loans = loans;
        _runs = runs;
        _directory = directory;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeeLoanDto>> GetAllAsync(string? employeeProfileId = null)
    {
        var loans = await _loans.GetAllAsync(employeeProfileId);
        if (loans.Count == 0) return [];

        var context = await LoadContextAsync();
        return [.. loans.Select(l => ToDto(l, context))];
    }

    public async Task<EmployeeLoanDto?> GetAsync(string id)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        return ToDto(loan, await LoadContextAsync());
    }

    public async Task<EmployeeLoanDto> CreateAsync(SaveEmployeeLoanDto dto)
    {
        await RequireProfileAsync(dto.EmployeeProfileId);

        var now = DateTime.UtcNow;
        var loan = new EmployeeLoan
        {
            EmployeeProfileId = dto.EmployeeProfileId,
            StartYear = dto.StartYear,
            StartMonth = dto.StartMonth,
            Notes = dto.Notes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ApplyTerms(loan, dto);
        await _loans.AddAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollLoanCreate,
            $"Recorded a {loan.PrincipalAmount:0.00} staff loan over {loan.InstallmentCount} month(s)",
            TargetType: "EmployeeLoan",
            TargetId: loan.Id,
            Metadata: new
            {
                loan.EmployeeProfileId,
                loan.PrincipalAmount,
                loan.InstallmentCount,
                loan.StartYear,
                loan.StartMonth,
            }));

        // A draft built before this deducts the old instalment (or none).
        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();
        return ToDto(loan, await LoadContextAsync());
    }

    public async Task<EmployeeLoanDto?> UpdateAsync(string id, SaveEmployeeLoanDto dto)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        if (loan.Status == LoanStatus.PAUSED)
        {
            throw new PayrollLoanException("This loan is paused. Resume it before changing its terms.");
        }

        // Once any month of the loan is filed or awaiting approval, its
        // earlier installments are inside payslips. Re-terming the whole loan
        // would leave those months deducting an amount this schedule no longer
        // contains. Only what is still owed can change, through Re-plan.
        if (PayrollLoans.FirstEditableIndex(loan, await LockedPeriodsAsync()) > 0)
        {
            throw new PayrollLoanException(
                "This loan has already started repaying, so the amount lent and the months already "
                + "deducted are fixed. Use Re-plan to change what is still owed.");
        }

        loan.EmployeeProfileId = dto.EmployeeProfileId;
        loan.StartYear = dto.StartYear;
        loan.StartMonth = dto.StartMonth;
        loan.Notes = dto.Notes;
        loan.UpdatedAt = DateTime.UtcNow;

        ApplyTerms(loan, dto);
        await _loans.UpdateAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollLoanUpdate,
            "Updated a staff loan",
            TargetType: "EmployeeLoan",
            TargetId: loan.Id,
            Metadata: new { loan.PrincipalAmount, loan.InstallmentCount }));

        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();
        return ToDto(loan, await LoadContextAsync());
    }

    // ─── Changing a loan that has started ───────────────────────────────

    public async Task<EmployeeLoanDto?> ReplanAsync(string id, ReplanLoanDto dto)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;
        RequireActive(loan, "re-planned");

        var firstEditable = PayrollLoans.FirstEditableIndex(loan, await LockedPeriodsAsync());
        var before = PayrollLoans.ResolveSchedule(loan);

        var after = PayrollLoans.Replan(
            before, loan.PrincipalAmount, firstEditable,
            dto.Mode, dto.InstallmentCount, dto.InstallmentAmount, dto.Remainder);

        // "Over N months" or "RM X a month" described the loan as recorded. A
        // started loan re-planned is neither any more, so it becomes CUSTOM —
        // which also says at a glance that it was changed. Not started, the
        // re-plan IS the loan's terms, so the admin's mode stands.
        var mode = firstEditable > 0 || dto.Remainder is { Count: > 0 }
            ? LoanRepaymentMode.CUSTOM
            : dto.Mode;

        return await SaveScheduleAsync(
            loan, after, mode, firstEditable,
            AuditActions.PayrollLoanReplan,
            $"Re-planned a staff loan: RM {PayrollLoans.RemainingToPlan(before, loan.PrincipalAmount, firstEditable):N2} "
            + $"still owed, now over {after.Count - firstEditable} month(s)",
            before);
    }

    public async Task<EmployeeLoanDto?> SkipMonthsAsync(string id, SkipLoanMonthsDto dto)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;
        RequireActive(loan, "paused");

        var firstEditable = PayrollLoans.FirstEditableIndex(loan, await LockedPeriodsAsync());
        var at = RequireChangeableMonth(loan, dto.FromYear, dto.FromMonth, firstEditable);
        var before = PayrollLoans.ResolveSchedule(loan);
        var after = PayrollLoans.InsertSkipped(before, at, dto.Months);

        return await SaveScheduleAsync(
            loan, after, LoanRepaymentMode.CUSTOM, firstEditable,
            AuditActions.PayrollLoanSkip,
            $"Skipped {dto.Months} month(s) of a staff loan from "
            + $"{PayrollLoans.PeriodLabel(dto.FromYear, dto.FromMonth)}",
            before);
    }

    public async Task<EmployeeLoanDto?> PauseAsync(string id, PauseLoanDto dto)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;
        RequireActive(loan, "paused");

        var firstEditable = PayrollLoans.FirstEditableIndex(loan, await LockedPeriodsAsync());
        RequireChangeableMonth(loan, dto.FromYear, dto.FromMonth, firstEditable);

        loan.Status = LoanStatus.PAUSED;
        loan.PausedFromYear = dto.FromYear;
        loan.PausedFromMonth = dto.FromMonth;
        loan.UpdatedAt = DateTime.UtcNow;
        await _loans.UpdateAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollLoanPause,
            $"Paused a staff loan from {PayrollLoans.PeriodLabel(dto.FromYear, dto.FromMonth)}",
            TargetType: "EmployeeLoan",
            TargetId: loan.Id,
            Metadata: new { dto.FromYear, dto.FromMonth }));

        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();
        return ToDto(loan, await LoadContextAsync());
    }

    public async Task<EmployeeLoanDto?> ResumeAsync(string id, ResumeLoanDto dto)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        if (loan.Status != LoanStatus.PAUSED || PayrollLoans.PausedIndex(loan) is not { } pausedAt)
        {
            throw new PayrollLoanException("Only a paused loan can be resumed.");
        }

        var locked = await LockedPeriodsAsync();
        var resumeAt = PayrollLoans.PeriodIndex(loan, dto.Year, dto.Month);

        if (resumeAt < pausedAt)
        {
            throw new PayrollLoanException(
                $"The loan is paused from {PausedLabel(loan)}, so it can resume from that month at the earliest.");
        }

        // The months the pause covered that are already filed took nothing,
        // so deductions can only restart after them.
        var firstEditable = PayrollLoans.FirstEditableIndex(loan, locked);
        if (resumeAt < firstEditable)
        {
            var earliest = PayrollLoans.PeriodAtIndex(loan.StartYear, loan.StartMonth, firstEditable);
            throw new PayrollLoanException(
                $"{PayrollLoans.PeriodLabel(dto.Year, dto.Month)} is already submitted or awaiting approval. "
                + $"Resume from {PayrollLoans.PeriodLabel(earliest.Year, earliest.Month)} or later.");
        }

        var before = PayrollLoans.ResolveSchedule(loan);
        var after = PayrollLoans.InsertSkipped(before, pausedAt, resumeAt - pausedAt);
        var pausedLabel = PausedLabel(loan);

        ClearPause(loan);
        return await SaveScheduleAsync(
            loan, after,
            resumeAt > pausedAt ? LoanRepaymentMode.CUSTOM : loan.Mode,
            firstEditable,
            AuditActions.PayrollLoanResume,
            $"Resumed a staff loan paused from {pausedLabel}, deducting again from "
            + $"{PayrollLoans.PeriodLabel(dto.Year, dto.Month)}",
            before);
    }

    public async Task<EmployeeLoanDto?> SetStatusAsync(string id, LoanStatus status)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        if (loan.Status == LoanStatus.PAUSED && status == LoanStatus.ACTIVE)
        {
            throw new PayrollLoanException("This loan is paused. Resume it, choosing the month deductions restart.");
        }

        // Cancelling a paused loan: the paused months already filed took
        // nothing, so they are written in as RM 0 first. Otherwise, once the
        // pause is gone, those months would read as repaid.
        if (loan.Status == LoanStatus.PAUSED && PayrollLoans.PausedIndex(loan) is { } pausedAt)
        {
            var firstEditable = PayrollLoans.FirstEditableIndex(loan, await LockedPeriodsAsync());
            var filedWhilePaused = Math.Max(0, firstEditable - pausedAt);
            if (filedWhilePaused > 0)
            {
                var schedule = PayrollLoans.InsertSkipped(
                    PayrollLoans.ResolveSchedule(loan), pausedAt, filedWhilePaused);
                loan.InstallmentCount = schedule.Count;
                loan.ScheduleJson = PayrollLoans.SerialiseSchedule(schedule);
            }

            ClearPause(loan);
        }

        loan.Status = status;
        loan.UpdatedAt = DateTime.UtcNow;
        await _loans.UpdateAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollLoanUpdate,
            $"Set a staff loan to {status}",
            TargetType: "EmployeeLoan",
            TargetId: loan.Id,
            Metadata: new { Status = status.ToString() }));

        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();
        return ToDto(loan, await LoadContextAsync());
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return false;

        // Deleting a loan that has been repaying erases the explanation for
        // deductions already on filed payslips.
        if (PayrollLoans.Summarise(loan, await SubmittedPeriodsAsync()).HasStarted)
        {
            throw new PayrollLoanException(
                "This loan has already been deducted from at least one submitted run, so it cannot "
                + "be deleted. Cancel it instead — the repayments stay explained.");
        }

        await _loans.DeleteAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollLoanDelete,
            "Deleted a staff loan that had not started repaying",
            TargetType: "EmployeeLoan",
            TargetId: id));

        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();
        return true;
    }

    // What generation asks for: one figure per employee for this period.
    // Someone with two loans repays both, so the amounts are summed.
    public async Task<IReadOnlyDictionary<string, decimal>> GetRepaymentsForPeriodAsync(
        int year, int month)
    {
        var repayments = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var loan in await _loans.GetActiveAsync())
        {
            var due = PayrollLoans.InstallmentForPeriod(loan, year, month);
            if (due <= 0m) continue;

            repayments.TryGetValue(loan.EmployeeProfileId, out var running);
            repayments[loan.EmployeeProfileId] = running + due;
        }

        return repayments;
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static void ApplyTerms(EmployeeLoan loan, SaveEmployeeLoanDto dto)
    {
        loan.PrincipalAmount = Money.Round2(dto.PrincipalAmount);
        loan.Mode = dto.Mode;

        if (dto.Schedule is { Count: > 0 })
        {
            // A hand-varied schedule is authoritative — it says both how much
            // each month takes and how many months there are.
            PayrollLoans.ValidateSchedule(dto.Schedule, loan.PrincipalAmount);

            loan.InstallmentCount = dto.Schedule.Count;
            loan.InstallmentAmount = Money.Round2(PayrollLoans.HeadlineInstallment(dto.Schedule, 0));
            loan.ScheduleJson = PayrollLoans.SerialiseSchedule(dto.Schedule);
            return;
        }

        var (terms, schedule) = PayrollLoans.BuildFromTerms(
            dto.Mode, loan.PrincipalAmount, dto.InstallmentCount, dto.InstallmentAmount);

        loan.InstallmentAmount = terms.InstallmentAmount;
        loan.InstallmentCount = terms.InstallmentCount;
        loan.ScheduleJson = PayrollLoans.SerialiseSchedule(schedule);
    }

    // One save path for re-plan, skip and resume, so each writes the same
    // fields and the same before/after audit.
    private async Task<EmployeeLoanDto> SaveScheduleAsync(
        EmployeeLoan loan,
        IReadOnlyList<decimal> schedule,
        LoanRepaymentMode mode,
        int firstEditable,
        string auditAction,
        string auditSummary,
        IReadOnlyList<decimal> before)
    {
        PayrollLoans.ValidateSchedule(schedule, loan.PrincipalAmount);

        loan.Mode = mode;
        loan.InstallmentCount = schedule.Count;
        loan.InstallmentAmount = Money.Round2(PayrollLoans.HeadlineInstallment(schedule, firstEditable));
        loan.ScheduleJson = PayrollLoans.SerialiseSchedule(schedule);
        loan.UpdatedAt = DateTime.UtcNow;
        await _loans.UpdateAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            auditAction,
            auditSummary,
            TargetType: "EmployeeLoan",
            TargetId: loan.Id,
            Metadata: new
            {
                Mode = mode.ToString(),
                ScheduleBefore = before,
                ScheduleAfter = schedule,
            }));

        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();
        return ToDto(loan, await LoadContextAsync());
    }

    // The payroll roster lists members who have no saved profile yet, under a
    // stand-in id that exists nowhere. A loan recorded against one would never
    // deduct — no payslip carries that id — and would show no name.
    private async Task RequireProfileAsync(string employeeProfileId)
    {
        var profiles = await _directory.GetProfilesForCurrentOrgAsync();
        if (!profiles.Any(p => string.Equals(p.Id, employeeProfileId, StringComparison.Ordinal)))
        {
            throw new PayrollLoanException(
                "This employee has no payroll profile yet. Open them under Payroll → Employees, "
                + "save their details, then record the loan.");
        }
    }

    private static void RequireActive(EmployeeLoan loan, string verb)
    {
        switch (loan.Status)
        {
            case LoanStatus.ACTIVE:
                return;
            case LoanStatus.PAUSED:
                throw new PayrollLoanException($"This loan is paused. Resume it before it can be {verb}.");
            default:
                throw new PayrollLoanException($"Only an active loan can be {verb}.");
        }
    }

    // A skip or pause has to start inside the schedule, and on a month whose
    // payroll is not already submitted or awaiting approval.
    private static int RequireChangeableMonth(EmployeeLoan loan, int year, int month, int firstEditable)
    {
        var index = PayrollLoans.PeriodIndex(loan, year, month);
        var label = PayrollLoans.PeriodLabel(year, month);

        if (index < 0)
        {
            throw new PayrollLoanException(
                $"The loan starts in {PayrollLoans.PeriodLabel(loan.StartYear, loan.StartMonth)}, after "
                + $"{label}. Pick a month from the start onwards.");
        }

        if (index >= loan.InstallmentCount)
        {
            var end = PayrollLoans.EndPeriod(loan);
            throw new PayrollLoanException(
                $"The last installment is in {PayrollLoans.PeriodLabel(end.Year, end.Month)}, so there is "
                + $"nothing to pause in {label}.");
        }

        if (index < firstEditable)
        {
            var earliest = PayrollLoans.PeriodAtIndex(loan.StartYear, loan.StartMonth, firstEditable);
            throw new PayrollLoanException(
                $"{label} is already submitted or awaiting approval. The earliest month that can change is "
                + $"{PayrollLoans.PeriodLabel(earliest.Year, earliest.Month)}.");
        }

        return index;
    }

    private static string PausedLabel(EmployeeLoan loan) =>
        loan.PausedFromYear is { } y && loan.PausedFromMonth is { } m
            ? PayrollLoans.PeriodLabel(y, m)
            : string.Empty;

    private static void ClearPause(EmployeeLoan loan)
    {
        loan.Status = LoanStatus.ACTIVE;
        loan.PausedFromYear = null;
        loan.PausedFromMonth = null;
    }

    // Which periods have a SUBMITTED run. That is the only durable evidence a
    // deduction was actually taken, and it is why reverting a month un-pays
    // its installment without the loan row changing at all.
    private async Task<IReadOnlyList<PayrollLoans.Period>> SubmittedPeriodsAsync() =>
        [.. (await _runs.GetAllAsync())
            .Where(r => r.Status == PayrollRunStatus.SUBMITTED)
            .Select(r => new PayrollLoans.Period(r.PeriodYear, r.PeriodMonth))];

    // Submitted OR awaiting approval: an approver is looking at that month's
    // payslips, deductions included, so its installment must not move under
    // them. Drafts are free to change — they are marked for a re-run.
    private async Task<IReadOnlyList<PayrollLoans.Period>> LockedPeriodsAsync() =>
        [.. (await _runs.GetAllAsync())
            .Where(r => r.Status is PayrollRunStatus.SUBMITTED or PayrollRunStatus.PENDING_APPROVAL)
            .Select(r => new PayrollLoans.Period(r.PeriodYear, r.PeriodMonth))];

    // Everything ToDto needs, loaded once per request rather than per loan.
    private sealed record LoanContext(
        IReadOnlyList<PayrollLoans.Period> Submitted,
        IReadOnlyList<PayrollLoans.Period> Locked,
        IReadOnlyDictionary<string, string> Names,
        IReadOnlyDictionary<string, EmployeeProfile> Profiles,
        ILookup<string, EmployeeLoan> LoansByEmployee);

    private async Task<LoanContext> LoadContextAsync()
    {
        var runs = await _runs.GetAllAsync();
        var submitted = runs
            .Where(r => r.Status == PayrollRunStatus.SUBMITTED)
            .Select(r => new PayrollLoans.Period(r.PeriodYear, r.PeriodMonth))
            .ToList();
        var locked = runs
            .Where(r => r.Status is PayrollRunStatus.SUBMITTED or PayrollRunStatus.PENDING_APPROVAL)
            .Select(r => new PayrollLoans.Period(r.PeriodYear, r.PeriodMonth))
            .ToList();

        // The name lives on the User, not the profile, so the two are joined
        // the same way generation does it.
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => PersonName.Display(u.Name, u.Email), StringComparer.Ordinal);
        var profiles = (await _directory.GetProfilesForCurrentOrgAsync())
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var names = profiles.ToDictionary(
            kv => kv.Key,
            kv => users.GetValueOrDefault(kv.Value.UserId) ?? string.Empty,
            StringComparer.Ordinal);

        // For the 50%-of-salary check, which counts all of a person's loans.
        var loansByEmployee = (await _loans.GetActiveAsync()).ToLookup(l => l.EmployeeProfileId);

        return new LoanContext(submitted, locked, names, profiles, loansByEmployee);
    }

    private static EmployeeLoanDto ToDto(EmployeeLoan loan, LoanContext context)
    {
        var summary = PayrollLoans.Summarise(loan, context.Submitted);
        var schedule = PayrollLoans.ResolveSchedule(loan);
        var firstEditable = PayrollLoans.FirstEditableIndex(loan, context.Locked);
        var firstEditablePeriod = PayrollLoans.PeriodAtIndex(loan.StartYear, loan.StartMonth, firstEditable);
        var locked = context.Locked.ToHashSet();
        var profile = context.Profiles.GetValueOrDefault(loan.EmployeeProfileId);

        return new EmployeeLoanDto
        {
            Id = loan.Id,
            EmployeeProfileId = loan.EmployeeProfileId,
            EmployeeName = context.Names.TryGetValue(loan.EmployeeProfileId, out var name)
                ? name
                : string.Empty,
            PrincipalAmount = loan.PrincipalAmount,
            Mode = loan.Mode,
            InstallmentAmount = loan.InstallmentAmount,
            StartYear = loan.StartYear,
            StartMonth = loan.StartMonth,
            InstallmentCount = loan.InstallmentCount,
            Status = loan.Status,
            Notes = loan.Notes,
            Schedule = [.. PayrollLoans.Breakdown(loan, context.Submitted).Select(i => new LoanInstallmentDto
            {
                Index = i.Index,
                Year = i.Year,
                Month = i.Month,
                PeriodLabel = PayrollLoans.PeriodLabel(i.Year, i.Month),
                Amount = i.Amount,
                Paid = i.Paid,
                Paused = i.Paused,
                Locked = locked.Contains(new PayrollLoans.Period(i.Year, i.Month)),
            })],
            PaidInstallments = summary.PaidInstallments,
            PaidAmount = summary.PaidAmount,
            RemainingAmount = summary.RemainingAmount,
            EndYear = summary.EndYear,
            EndMonth = summary.EndMonth,
            FullyRepaid = summary.FullyRepaid,
            HasStarted = summary.HasStarted,
            PausedFromYear = loan.PausedFromYear,
            PausedFromMonth = loan.PausedFromMonth,
            FirstEditableYear = firstEditablePeriod.Year,
            FirstEditableMonth = firstEditablePeriod.Month,
            RemainingToPlan = PayrollLoans.RemainingToPlan(schedule, loan.PrincipalAmount, firstEditable),
            Warnings = PayrollLoans.Warnings(
                loan,
                context.LoansByEmployee[loan.EmployeeProfileId],
                context.Submitted,
                profile?.SalaryType == SalaryType.MONTHLY ? profile.MonthlySalary : null,
                profile?.LeaveDate),
        };
    }
}
