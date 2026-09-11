using System.Text.Json;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Loan repayment arithmetic. Pure — no EF, no clock.
//
// The one rule everything here serves: **the installments must add up to the
// principal**. An employee repaying a sen more than they borrowed is a real
// complaint, and a sen less is a write-off nobody approved. The last
// installment absorbs the rounding, and `ValidateSchedule` refuses anything
// that does not reconcile.
public static class PayrollLoans
{
    public readonly record struct Period(int Year, int Month);

    public sealed record Installment(int Index, int Year, int Month, decimal Amount, bool Paid);

    public sealed record Terms(decimal InstallmentAmount, int InstallmentCount);

    public sealed record Summary(
        int TotalInstallments,
        int PaidInstallments,
        decimal PaidAmount,
        decimal RemainingAmount,
        int EndYear,
        int EndMonth,
        bool FullyRepaid,
        // True once at least one installment period has a SUBMITTED run —
        // the point after which editing the terms stops being harmless.
        bool HasStarted);

    // ─── Periods ────────────────────────────────────────────────────────

    public static Period PeriodAtIndex(int startYear, int startMonth, int index)
    {
        // Month is 1-based, so shift to 0-based for the division and back
        // after. Integer division floors, which is what carrying years needs.
        var raw = startMonth - 1 + index;

        return new Period(startYear + raw / 12, raw % 12 + 1);
    }

    public static Period EndPeriod(EmployeeLoan loan) =>
        PeriodAtIndex(loan.StartYear, loan.StartMonth, Math.Max(0, loan.InstallmentCount - 1));

    // How far into the schedule a period falls. Negative, or past the count,
    // means the period is outside the repayment window.
    public static int PeriodIndex(EmployeeLoan loan, int year, int month) =>
        (year - loan.StartYear) * 12 + (month - loan.StartMonth);

    // ─── Schedules ──────────────────────────────────────────────────────

    // An equal split where the LAST installment absorbs the remainder, so the
    // total is exactly the principal rather than 12 × a rounded figure.
    public static IReadOnlyList<decimal> BuildEqualSchedule(
        decimal principal, decimal installmentAmount, int installmentCount)
    {
        if (installmentCount <= 0) return [];

        var schedule = new List<decimal>(installmentCount);

        for (var i = 0; i < installmentCount - 1; i++)
        {
            schedule.Add(Money.Round2(installmentAmount));
        }

        schedule.Add(Math.Max(0m,
            Money.Round2(principal - Money.Round2(installmentAmount) * (installmentCount - 1))));

        return schedule;
    }

    // The stored schedule when there is a usable one, an equal split
    // otherwise. A stored schedule of the wrong LENGTH is ignored rather than
    // half-used — it means the terms were edited without the schedule, and
    // reading past its end would silently deduct zero.
    public static IReadOnlyList<decimal> ResolveSchedule(EmployeeLoan loan)
    {
        var stored = ParseSchedule(loan.ScheduleJson);

        if (stored is not null && stored.Count == loan.InstallmentCount)
        {
            return [.. stored.Select(Money.Round2)];
        }

        return BuildEqualSchedule(loan.PrincipalAmount, loan.InstallmentAmount, loan.InstallmentCount);
    }

    // What this loan deducts in this period. Zero when the loan is not active
    // or the period falls outside the window — a cancelled loan must stop
    // deducting immediately, not at the end of its schedule.
    public static decimal InstallmentForPeriod(EmployeeLoan loan, int year, int month)
    {
        if (loan.Status != LoanStatus.ACTIVE) return 0m;
        if (loan.InstallmentCount <= 0 || loan.PrincipalAmount <= 0m) return 0m;

        var index = PeriodIndex(loan, year, month);
        if (index < 0 || index >= loan.InstallmentCount) return 0m;

        var schedule = ResolveSchedule(loan);

        return index < schedule.Count ? Money.Round2(schedule[index]) : 0m;
    }

    // ─── Progress ───────────────────────────────────────────────────────

    // An installment counts as PAID when its period has a submitted run —
    // that is the only durable evidence the deduction actually happened. The
    // loan row itself records no payments, on purpose: reverting a month has
    // to un-pay its installment, and it does so for free this way.
    public static IReadOnlyList<Installment> Breakdown(
        EmployeeLoan loan, IEnumerable<Period> submittedPeriods)
    {
        var submitted = submittedPeriods.ToHashSet();

        return [.. ResolveSchedule(loan).Select((amount, index) =>
        {
            var period = PeriodAtIndex(loan.StartYear, loan.StartMonth, index);

            return new Installment(
                index, period.Year, period.Month, Money.Round2(amount), submitted.Contains(period));
        })];
    }

    public static Summary Summarise(EmployeeLoan loan, IEnumerable<Period> submittedPeriods)
    {
        var breakdown = Breakdown(loan, submittedPeriods);
        var paid = breakdown.Where(i => i.Paid).ToList();
        var paidAmount = Money.Round2(paid.Sum(i => i.Amount));
        var end = EndPeriod(loan);

        return new Summary(
            loan.InstallmentCount,
            paid.Count,
            paidAmount,
            Math.Max(0m, Money.Round2(loan.PrincipalAmount - paidAmount)),
            end.Year,
            end.Month,
            paid.Count >= loan.InstallmentCount,
            paid.Count > 0);
    }

    // ─── Creating a loan ────────────────────────────────────────────────

    // Turn whichever question the admin answered into both figures.
    public static Terms ComputeTerms(
        LoanRepaymentMode mode, decimal principal, int? installmentCount, decimal? installmentAmount)
    {
        principal = Money.Round2(principal);
        if (principal <= 0m) throw new PayrollLoanException("Loan amount must be greater than zero.");

        if (mode == LoanRepaymentMode.FIXED)
        {
            var count = installmentCount ?? 0;
            if (count <= 0)
            {
                throw new PayrollLoanException("Number of installments must be at least 1.");
            }

            return new Terms(Money.Round2(principal / count), count);
        }

        var amount = Money.Round2(installmentAmount ?? 0m);
        if (amount <= 0m)
        {
            throw new PayrollLoanException("Monthly repayment amount must be greater than zero.");
        }

        // A repayment larger than the loan settles it in one go rather than
        // deducting more than was borrowed.
        if (amount >= principal) return new Terms(principal, 1);

        return new Terms(amount, (int)Math.Ceiling(principal / amount));
    }

    public static (Terms Terms, IReadOnlyList<decimal> Schedule) BuildFromTerms(
        LoanRepaymentMode mode, decimal principal, int? installmentCount, decimal? installmentAmount)
    {
        var terms = ComputeTerms(mode, principal, installmentCount, installmentAmount);

        return (terms, BuildEqualSchedule(
            Money.Round2(principal), terms.InstallmentAmount, terms.InstallmentCount));
    }

    // A hand-edited schedule has to still add up. Off by more than a sen and
    // the employee repays the wrong amount, which is the whole failure mode
    // this module exists to prevent.
    public static void ValidateSchedule(IReadOnlyList<decimal> schedule, decimal principal)
    {
        if (schedule.Count == 0)
        {
            throw new PayrollLoanException("A loan needs at least one installment.");
        }

        if (schedule.Any(n => n <= 0m))
        {
            throw new PayrollLoanException("Every installment must be greater than zero.");
        }

        var total = Money.Round2(schedule.Sum());
        var expected = Money.Round2(principal);

        if (Math.Abs(total - expected) > 0.01m)
        {
            throw new PayrollLoanException(
                $"Installments must add up to the loan amount ({expected:0.00}). "
                + $"They currently total {total:0.00}.");
        }
    }

    // ─── JSON ───────────────────────────────────────────────────────────

    // An unreadable schedule falls back to the equal split rather than
    // throwing: the loan still has a principal and a count, so it can still
    // be repaid correctly, and failing the whole payroll run over a malformed
    // column would be a far worse outcome.
    public static IReadOnlyList<decimal>? ParseSchedule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<List<decimal>>(json, PayrollSnapshotJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerialiseSchedule(IReadOnlyList<decimal> schedule) =>
        JsonSerializer.Serialize(schedule, PayrollSnapshotJson.Options);

    public static string PeriodLabel(int year, int month) =>
        $"{PayrollPeriodLabel.ShortMonth(month)} {year}";
}

// A loan the admin described in a way that cannot be repaid. Carries a message
// written for them, so the controller can answer 400 with it directly.
public class PayrollLoanException : InvalidOperationException
{
    public PayrollLoanException(string message) : base(message) { }
}
