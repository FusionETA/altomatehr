using System.Globalization;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Unpaid leave decided AFTER its month was filed — approved late, cancelled
// after it was docked, or edited — is settled on the person's next run of the
// same year, as its own line.
//
// What each filed payslip settled is recorded per leave application
// (UnpaidLeaveSettlement), and only an approved run's records count. For each
// application touching an earlier filed month:
//
//   - settled before, and still approved with the same type and dates → done.
//     Never recounted, so changing the leave type, the work week or the
//     holidays later can't re-price a filed month.
//   - settled before, and since cancelled or changed → owed = what it counts
//     now − what was settled (a refund if it's gone).
//   - never settled → owed. That month's payslip recorded every application
//     it docked, so one with no record wasn't docked: approved late, or
//     moved into the month or made unpaid by an admin's edit since.
//     A payslip from before these records existed (TracksUnpaidLeave false)
//     only knows when leave was approved, so there it's owed only if approved
//     after the payslip was generated — and its cancellations aren't
//     refunded automatically.
//
// owed > 0 → a deduction: "Unpaid Leave – Sep 2026 (approved late, 2 days)".
// owed < 0 → a refund, as arrears of wages.
//
// Priced at THAT month's rate — its salary ÷ its calendar days (EA s.18A, as
// PayPeriod.UnpaidLeaveDeduction) — because the day belongs to that month's
// salary. Imported months and months whose unpaid days were typed on the run
// are never revisited: the caller leaves them out.
public static class UnpaidLeaveCarryForward
{
    // One filed month of one person.
    public sealed record FiledMonth(
        int Year,
        int Month,
        decimal MonthlySalary,
        DateTime PayslipGeneratedAt,
        bool TracksUnpaidLeave,
        IReadOnlyList<UnpaidLeavePartDto> Parts,
        IReadOnlyList<UnpaidLeaveSettlement> Settled);

    // What one application owes for one month, with the leave as it is now
    // (null when the application no longer touches that month).
    public sealed record Owed(string ApplicationId, decimal Days, UnpaidLeavePartDto? Part, UnpaidLeaveSettlement? Last);

    public sealed record Carry(int Year, int Month, decimal Days, FixedAllowance Row, IReadOnlyList<Owed> ByApplication);

    // A month's lines, plus every application to record a settlement for —
    // including a changed one that owes nothing (0 days), so its new type and
    // dates are what a later change is compared with.
    public sealed record MonthResult(IReadOnlyList<Carry> Lines, IReadOnlyList<Owed> Owed);

    public static IReadOnlyList<Owed> OwedFor(FiledMonth m)
    {
        var parts = m.Parts.ToDictionary(p => p.ApplicationId, StringComparer.Ordinal);
        var settled = m.Settled
            .GroupBy(s => s.LeaveApplicationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var owed = new List<Owed>();
        foreach (var id in parts.Values.Where(p => p.IsApprovedUnpaid).Select(p => p.ApplicationId)
                     .Union(settled.Keys, StringComparer.Ordinal))
        {
            parts.TryGetValue(id, out var part);

            if (settled.TryGetValue(id, out var rows))
            {
                var last = rows.MaxBy(r => r.CreatedAt)!;
                var unchanged = part is { Status: LeaveStatus.APPROVED }
                                && part.LeaveTypeId == last.LeaveTypeId
                                && part.StartDate == last.LeaveStartDate.Date
                                && part.EndDate == last.LeaveEndDate.Date;
                if (unchanged) continue;

                var now = part is { IsApprovedUnpaid: true } ? (decimal)part.Days : 0m;
                var diff = now - rows.Sum(r => r.Days);
                // Gone and already refunded: nothing left to record.
                if (diff == 0m && part is not { Status: LeaveStatus.APPROVED }) continue;
                owed.Add(new Owed(id, diff, part, last));
            }
            else if (part!.Days > 0 && (m.TracksUnpaidLeave || part.DecidedAt > m.PayslipGeneratedAt))
            {
                owed.Add(new Owed(id, (decimal)part.Days, part, null));
            }
        }
        return owed;
    }

    // The lines for one filed month: a late deduction and/or a refund.
    public static MonthResult For(FiledMonth m)
    {
        var owed = OwedFor(m);
        var carries = new List<Carry>();

        var late = owed.Where(o => o.Days > 0m).ToList();
        if (late.Count > 0 && Line(m, late.Sum(o => o.Days), refund: false) is { } dock)
            carries.Add(new Carry(m.Year, m.Month, late.Sum(o => o.Days), dock, late));

        var back = owed.Where(o => o.Days < 0m).ToList();
        if (back.Count > 0 && Line(m, back.Sum(o => o.Days), refund: true) is { } refund)
            carries.Add(new Carry(m.Year, m.Month, back.Sum(o => o.Days), refund, back));

        return new MonthResult(carries, owed);
    }

    private static FixedAllowance? Line(FiledMonth m, decimal days, bool refund)
    {
        var amount = PayPeriod.UnpaidLeaveDeduction(m.MonthlySalary, Math.Abs(days), m.Year, m.Month);
        if (amount <= 0m) return null;

        var when = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m.Month)} {m.Year}";
        var count = $"{Math.Abs(days):0.##} day{(Math.Abs(days) == 1m ? "" : "s")}";
        return refund
            ? new FixedAllowance
            {
                Category = PayrollAdjustmentCategories.WagesArrears,
                Name = $"Unpaid Leave refund – {when} ({count})",
                Amount = amount,
            }
            : new FixedAllowance
            {
                Category = PayrollAdjustmentCategories.DeductUnpaidLeave,
                Name = $"Unpaid Leave – {when} (approved late, {count})",
                Amount = amount,
            };
    }
}
