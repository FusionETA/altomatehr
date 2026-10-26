using System.Globalization;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Unpaid leave decided AFTER its month was filed — approved late, or cancelled
// after it was docked — is settled on the person's next run, as its own line.
//
// For each earlier month of the same year whose payslip was approved here
// (never an imported month: the old system's figures may already include it):
//
//     owed = unpaid days approved for that month now
//          − days that month's payslip docked
//          − days already settled on another run
//
// owed > 0 → a deduction: "Unpaid Leave – Sep 2026 (approved late, 2 days)".
// owed < 0 → a refund, as arrears of wages, after the leave was cancelled.
//
// Priced at THAT month's rate — its salary ÷ its calendar days (EA s.18A, as
// PayPeriod.UnpaidLeaveDeduction) — because the day belongs to that month's
// salary. A month where the admin typed the unpaid days on the run is left
// alone: the typed figure replaced the Leave module's on purpose.
public static class UnpaidLeaveCarryForward
{
    // One filed month of one person, as the carry needs it.
    public sealed record FiledMonth(
        int Year,
        int Month,
        decimal MonthlySalary,
        decimal DockedDays,
        bool TypedOnRun);

    public sealed record Carry(int Year, int Month, decimal Days, FixedAllowance Row);

    public static IReadOnlyList<Carry> For(
        IEnumerable<FiledMonth> filed,
        Func<int, int, decimal> approvedDaysIn,
        Func<int, int, decimal> alreadySettledFor)
    {
        var carries = new List<Carry>();
        foreach (var m in filed.OrderBy(f => f.Month))
        {
            if (m.TypedOnRun || m.MonthlySalary <= 0m) continue;

            var owed = approvedDaysIn(m.Year, m.Month) - m.DockedDays - alreadySettledFor(m.Year, m.Month);
            if (owed == 0m) continue;

            var amount = PayPeriod.UnpaidLeaveDeduction(m.MonthlySalary, Math.Abs(owed), m.Year, m.Month);
            if (amount <= 0m) continue;

            var when = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m.Month)} {m.Year}";
            var days = $"{Math.Abs(owed):0.##} day{(Math.Abs(owed) == 1m ? "" : "s")}";
            carries.Add(new Carry(m.Year, m.Month, owed, owed > 0m
                ? new FixedAllowance
                {
                    Category = PayrollAdjustmentCategories.DeductUnpaidLeave,
                    Name = $"Unpaid Leave – {when} (approved late, {days})",
                    Amount = amount,
                }
                : new FixedAllowance
                {
                    Category = PayrollAdjustmentCategories.WagesArrears,
                    Name = $"Unpaid Leave refund – {when} (cancelled, {days})",
                    Amount = amount,
                }));
        }
        return carries;
    }
}
