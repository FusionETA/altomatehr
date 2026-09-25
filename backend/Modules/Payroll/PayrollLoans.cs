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

    // Paused: the loan is PAUSED and this month falls on or after the pause,
    // so nothing is taken and it is not paid, whatever run exists for it.
    public sealed record Installment(
        int Index, int Year, int Month, decimal Amount, bool Paid, bool Paused = false);

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

    // Where a PAUSED loan's pause begins in its schedule. Null otherwise.
    public static int? PausedIndex(EmployeeLoan loan) =>
        loan.Status == LoanStatus.PAUSED && loan.PausedFromYear is { } y && loan.PausedFromMonth is { } m
            ? PeriodIndex(loan, y, m)
            : null;

    // What this loan deducts in this period. Zero when the loan is not active
    // or the period falls outside the window — a cancelled loan must stop
    // deducting immediately, not at the end of its schedule. A paused loan
    // still deducts the months BEFORE its pause.
    public static decimal InstallmentForPeriod(EmployeeLoan loan, int year, int month)
    {
        if (loan.Status is not (LoanStatus.ACTIVE or LoanStatus.PAUSED)) return 0m;
        if (loan.InstallmentCount <= 0 || loan.PrincipalAmount <= 0m) return 0m;

        var index = PeriodIndex(loan, year, month);
        if (index < 0 || index >= loan.InstallmentCount) return 0m;
        if (PausedIndex(loan) is { } paused && index >= paused) return 0m;

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
        var pausedFrom = PausedIndex(loan);

        return [.. ResolveSchedule(loan).Select((amount, index) =>
        {
            var period = PeriodAtIndex(loan.StartYear, loan.StartMonth, index);

            // A month under the pause took nothing, so a submitted run for it
            // is not a repayment.
            var paused = pausedFrom is { } from && index >= from;

            return new Installment(
                index, period.Year, period.Month, Money.Round2(amount),
                !paused && submitted.Contains(period), paused);
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

        // RM 0 is a skipped or paused month. Negative is never meaningful, and
        // a schedule ending on a skip would report an end date nothing is
        // deducted in.
        if (schedule.Any(n => n < 0m))
        {
            throw new PayrollLoanException("An installment cannot be negative.");
        }

        if (schedule[^1] <= 0m)
        {
            throw new PayrollLoanException("The last installment must be greater than zero.");
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

    // ─── Changing a loan that has started ───────────────────────────────
    //
    // One rule for re-planning, skipping and pausing: a month whose payroll is
    // SUBMITTED or awaiting approval is filed (or about to be), so its
    // installment is locked; everything after the last such month is the
    // admin's to change, and the schedule must still add up to the principal.

    // The first installment index that can still change. Periods BEFORE the
    // loan starts are ignored; one past the end of the schedule still counts,
    // so a loan whose last month is filed has nothing left to change.
    public static int FirstEditableIndex(EmployeeLoan loan, IEnumerable<Period> lockedPeriods)
    {
        var first = 0;
        foreach (var period in lockedPeriods)
        {
            var index = PeriodIndex(loan, period.Year, period.Month);
            if (index >= 0) first = Math.Max(first, index + 1);
        }

        return first;
    }

    // What the locked installments leave to repay.
    public static decimal RemainingToPlan(
        IReadOnlyList<decimal> schedule, decimal principal, int firstEditable) =>
        Math.Max(0m, Money.Round2(principal - schedule.Take(firstEditable).Sum()));

    // Keep the locked installments and replace everything after them with a
    // new plan for the balance: an equal split over N months (FIXED), RM X a
    // month (CUSTOM), or amounts the admin typed month by month (remainder).
    public static IReadOnlyList<decimal> Replan(
        IReadOnlyList<decimal> schedule,
        decimal principal,
        int firstEditable,
        LoanRepaymentMode mode,
        int? installmentCount,
        decimal? installmentAmount,
        IReadOnlyList<decimal>? remainder)
    {
        var locked = schedule.Take(firstEditable).ToList();
        var balance = Money.Round2(principal - locked.Sum());

        if (balance <= 0m)
        {
            throw new PayrollLoanException(
                "Every installment of this loan is already filed, so there is nothing left to re-plan.");
        }

        List<decimal> plan;
        if (remainder is { Count: > 0 })
        {
            plan = [.. remainder.Select(Money.Round2)];
            if (plan.Any(n => n <= 0m))
            {
                throw new PayrollLoanException(
                    "Every new installment must be greater than zero. Skip a month with Pause instead.");
            }

            var total = Money.Round2(plan.Sum());
            if (Math.Abs(total - balance) > 0.01m)
            {
                throw new PayrollLoanException(
                    $"The new installments must add up to the balance still owed ({balance:0.00}). "
                    + $"They currently total {total:0.00}.");
            }
        }
        else
        {
            var terms = ComputeTerms(mode, balance, installmentCount, installmentAmount);
            plan = [.. BuildEqualSchedule(balance, terms.InstallmentAmount, terms.InstallmentCount)];
        }

        List<decimal> result = [.. locked, .. plan];
        ValidateSchedule(result, principal);
        return result;
    }

    // RM 0 for `months` months from `atIndex`. Nothing is forgiven — the
    // installments still owed move into the next months that are not skipped,
    // so the loan just ends later.
    //
    // A skipped month is a CALENDAR decision ("nothing in November"), so a
    // later skip or pause must not shift an earlier one along with the money.
    // Skipping Oct–Dec over a loan already skipping Nov–Dec displaces one
    // installment, not three.
    public static IReadOnlyList<decimal> InsertSkipped(
        IReadOnlyList<decimal> schedule, int atIndex, int months)
    {
        if (months <= 0) return schedule;

        var skipped = new HashSet<int>(Enumerable.Range(atIndex, months));
        for (var i = atIndex; i < schedule.Count; i++)
        {
            if (schedule[i] == 0m) skipped.Add(i);
        }

        var owed = new Queue<decimal>(schedule.Skip(atIndex).Where(n => n > 0m));

        List<decimal> result = [.. schedule.Take(atIndex)];
        for (var i = atIndex; owed.Count > 0; i++)
        {
            result.Add(skipped.Contains(i) ? 0m : owed.Dequeue());
        }

        return result;
    }

    // The headline "per month" figure: the next non-zero installment from
    // `fromIndex`, since a skipped month is not what the loan costs.
    public static decimal HeadlineInstallment(IReadOnlyList<decimal> schedule, int fromIndex)
    {
        var next = schedule.Skip(fromIndex).FirstOrDefault(n => n > 0m);
        return next > 0m ? next : schedule.LastOrDefault(n => n > 0m);
    }

    // ─── Warnings ───────────────────────────────────────────────────────

    // Things an admin should see before relying on a plan. Advisory — the
    // plan is still saved, because the admin may already have agreed a
    // settlement outside payroll.
    //
    //   • repayments that run past the employee's last day, which payroll can
    //     never collect;
    //   • a month where this employee's loan deductions come to more than half
    //     their monthly salary. The Employment Act 1955 generally caps total
    //     deductions at 50% of wages, and loans alone reaching it is a strong
    //     sign the plan is too aggressive.
    public static IReadOnlyList<string> Warnings(
        EmployeeLoan loan,
        IEnumerable<EmployeeLoan> employeesOtherLoans,
        IEnumerable<Period> submittedPeriods,
        decimal? monthlySalary,
        DateTime? leaveDate)
    {
        if (loan.Status is not (LoanStatus.ACTIVE or LoanStatus.PAUSED)) return [];

        var warnings = new List<string>();
        var upcoming = Breakdown(loan, submittedPeriods)
            .Where(i => !i.Paid && !i.Paused && i.Amount > 0m)
            .ToList();

        if (leaveDate is { } leaves)
        {
            var afterLeaving = upcoming
                .Where(i => (i.Year, i.Month).CompareTo((leaves.Year, leaves.Month)) > 0)
                .ToList();
            if (afterLeaving.Count > 0)
            {
                var last = afterLeaving[^1];
                warnings.Add(
                    $"Repayments run to {PeriodLabel(last.Year, last.Month)}, after the last day on file "
                    + $"({leaves:d MMM yyyy}). RM {afterLeaving.Sum(i => i.Amount):N2} would still be "
                    + "owed then — settle it from the final pay or agree a plan outside payroll.");
            }
        }

        if (monthlySalary is > 0m and var salary)
        {
            var others = employeesOtherLoans.Where(l => l.Id != loan.Id).ToList();
            foreach (var installment in upcoming)
            {
                var total = installment.Amount + others.Sum(l =>
                    InstallmentForPeriod(l, installment.Year, installment.Month));

                if (total > salary / 2m)
                {
                    warnings.Add(
                        $"{PeriodLabel(installment.Year, installment.Month)}: loan deductions of "
                        + $"RM {total:N2} are more than half of the RM {salary:N2} monthly salary. The "
                        + "Employment Act 1955 generally limits total deductions to 50% of wages.");
                    break;
                }
            }
        }

        return warnings;
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
