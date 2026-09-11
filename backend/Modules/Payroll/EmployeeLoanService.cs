using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class EmployeeLoanService : IEmployeeLoanService
{
    private readonly IEmployeeLoanRepository _loans;
    private readonly IPayrollRunRepository _runs;
    private readonly IDirectoryService _directory;
    private readonly IAuditService _audit;

    public EmployeeLoanService(
        IEmployeeLoanRepository loans,
        IPayrollRunRepository runs,
        IDirectoryService directory,
        IAuditService audit)
    {
        _loans = loans;
        _runs = runs;
        _directory = directory;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeeLoanDto>> GetAllAsync(string? employeeProfileId = null)
    {
        var loans = await _loans.GetAllAsync(employeeProfileId);
        if (loans.Count == 0) return [];

        var submitted = await SubmittedPeriodsAsync();
        var names = await NamesAsync();

        return [.. loans.Select(l => ToDto(l, submitted, names))];
    }

    public async Task<EmployeeLoanDto?> GetAsync(string id)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        return ToDto(loan, await SubmittedPeriodsAsync(), await NamesAsync());
    }

    public async Task<EmployeeLoanDto> CreateAsync(SaveEmployeeLoanDto dto)
    {
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

        return ToDto(loan, await SubmittedPeriodsAsync(), await NamesAsync());
    }

    public async Task<EmployeeLoanDto?> UpdateAsync(string id, SaveEmployeeLoanDto dto)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        var submitted = await SubmittedPeriodsAsync();

        // Once a loan has started repaying, its earlier installments are
        // inside filed payslips. Re-terming it here would leave those months
        // deducting an amount this schedule no longer contains — the numbers
        // would stop reconciling and nothing would say why. Cancel it and
        // record a new loan for the balance instead.
        if (PayrollLoans.Summarise(loan, submitted).HasStarted)
        {
            throw new PayrollLoanException(
                "This loan has already started repaying, so its terms are fixed. Cancel it and "
                + "record a new loan for the outstanding balance.");
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

        return ToDto(loan, submitted, await NamesAsync());
    }

    public async Task<EmployeeLoanDto?> SetStatusAsync(string id, LoanStatus status)
    {
        var loan = await _loans.GetByIdAsync(id);
        if (loan is null) return null;

        loan.Status = status;
        loan.UpdatedAt = DateTime.UtcNow;
        await _loans.UpdateAsync(loan);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollLoanUpdate,
            $"Set a staff loan to {status}",
            TargetType: "EmployeeLoan",
            TargetId: loan.Id,
            Metadata: new { Status = status.ToString() }));

        return ToDto(loan, await SubmittedPeriodsAsync(), await NamesAsync());
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
            loan.InstallmentAmount = Money.Round2(dto.Schedule[0]);
            loan.ScheduleJson = PayrollLoans.SerialiseSchedule(dto.Schedule);
            return;
        }

        var (terms, schedule) = PayrollLoans.BuildFromTerms(
            dto.Mode, loan.PrincipalAmount, dto.InstallmentCount, dto.InstallmentAmount);

        loan.InstallmentAmount = terms.InstallmentAmount;
        loan.InstallmentCount = terms.InstallmentCount;
        loan.ScheduleJson = PayrollLoans.SerialiseSchedule(schedule);
    }

    // Which periods have a SUBMITTED run. That is the only durable evidence a
    // deduction was actually taken, and it is why reverting a month un-pays
    // its installment without the loan row changing at all.
    private async Task<IReadOnlyList<PayrollLoans.Period>> SubmittedPeriodsAsync() =>
        [.. (await _runs.GetAllAsync())
            .Where(r => r.Status == PayrollRunStatus.SUBMITTED)
            .Select(r => new PayrollLoans.Period(r.PeriodYear, r.PeriodMonth))];

    // The name lives on the User, not the profile, so the two are joined the
    // same way generation does it.
    private async Task<IReadOnlyDictionary<string, string>> NamesAsync()
    {
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u.Name ?? u.Email, StringComparer.Ordinal);

        return (await _directory.GetProfilesForCurrentOrgAsync())
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => users.GetValueOrDefault(g.First().UserId) ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static EmployeeLoanDto ToDto(
        EmployeeLoan loan,
        IReadOnlyList<PayrollLoans.Period> submitted,
        IReadOnlyDictionary<string, string> names)
    {
        var summary = PayrollLoans.Summarise(loan, submitted);

        return new EmployeeLoanDto
        {
            Id = loan.Id,
            EmployeeProfileId = loan.EmployeeProfileId,
            EmployeeName = names.TryGetValue(loan.EmployeeProfileId, out var name)
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
            Schedule = [.. PayrollLoans.Breakdown(loan, submitted).Select(i => new LoanInstallmentDto
            {
                Index = i.Index,
                Year = i.Year,
                Month = i.Month,
                PeriodLabel = PayrollLoans.PeriodLabel(i.Year, i.Month),
                Amount = i.Amount,
                Paid = i.Paid,
            })],
            PaidInstallments = summary.PaidInstallments,
            PaidAmount = summary.PaidAmount,
            RemainingAmount = summary.RemainingAmount,
            EndYear = summary.EndYear,
            EndMonth = summary.EndMonth,
            FullyRepaid = summary.FullyRepaid,
            HasStarted = summary.HasStarted,
        };
    }
}
