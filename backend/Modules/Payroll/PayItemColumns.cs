using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The fixed pay-item columns — Basic, U/L, Travelling, Meal, Parking, Hours,
// OT, Comm, Bonus, Deduction — and how a payslip fills them. One mapping,
// shared by the two files that print it, so they cannot drift apart:
//
//   AbPayTimesheetXlsx   Sheet1, Ayu Borneo's monthly timesheet layout (the
//                        mapping is ABPay's default column → category mapping
//                        in reverse, ABPay domain/posting.ts)
//   PayrollSummaryXlsx   the Breakdown sheet, every company's payroll summary
//
//   Basic       ProratedPay (what was actually paid; an ABPay run skips
//               proration, so for those it is the salary)
//   U/L         − deduct_unpaid_leave        (negative)
//   Travelling  allowance_travel_private
//   Meal        allowance_meal
//   Parking     allowance_parking
//   Hours       OT hours (normal + rest + public) the engine paid
//   OT          OtPay + wages_overtime
//   Comm        wages_commission
//   Bonus       wages_bonus_non_annual
//   Deduction   − deduct_miscellaneous       (negative)
//
// Hours is the only column that is not money.
public static class PayItemColumns
{
    public static readonly IReadOnlyList<string> Headers =
        ["Basic", "U/L", "Travelling", "Meal", "Parking", "Hours", "OT", "Comm", "Bonus", "Deduction"];

    // Position of Hours in Headers / Values.InOrder().
    public const int HoursIndex = 5;

    // The line-item categories the columns absorb (Of below sums exactly
    // these). Anything not listed has no fixed column.
    private static readonly HashSet<string> MappedCategories = new(StringComparer.Ordinal)
    {
        PayrollAdjustmentCategories.DeductUnpaidLeave,
        PayrollAdjustmentCategories.AllowanceTravelPrivate,
        PayrollAdjustmentCategories.AllowanceMeal,
        PayrollAdjustmentCategories.AllowanceParking,
        PayrollAdjustmentCategories.WagesOvertime,
        PayrollAdjustmentCategories.WagesCommission,
        PayrollAdjustmentCategories.WagesBonusNonAnnual,
        PayrollAdjustmentCategories.DeductMiscellaneous,
    };

    // True when a line of this category lands in one of the fixed columns.
    public static bool HasColumn(string? category) =>
        category is not null && MappedCategories.Contains(category);

    public sealed record Values
    {
        public decimal Basic { get; init; }
        public decimal UnpaidLeave { get; init; }   // ≤ 0
        public decimal Travelling { get; init; }
        public decimal Meal { get; init; }
        public decimal Parking { get; init; }
        public decimal OtHours { get; init; }
        public decimal Ot { get; init; }
        public decimal Commission { get; init; }
        public decimal Bonus { get; init; }
        public decimal Deduction { get; init; }     // ≤ 0

        // In Headers order.
        public decimal[] InOrder() =>
            [Basic, UnpaidLeave, Travelling, Meal, Parking, OtHours, Ot, Commission, Bonus, Deduction];

        // The money columns added up (Hours left out).
        public decimal Money =>
            Basic + UnpaidLeave + Travelling + Meal + Parking + Ot + Commission + Bonus + Deduction;
    }

    public static Values Of(Payslip p, IReadOnlyList<PayslipLineItem> lineItems)
    {
        decimal Sum(string category) => lineItems
            .Where(li => string.Equals(li.Category, category, StringComparison.Ordinal))
            .Sum(li => li.Amount);

        return new Values
        {
            Basic = p.ProratedPay,
            UnpaidLeave = -Sum(PayrollAdjustmentCategories.DeductUnpaidLeave),
            Travelling = Sum(PayrollAdjustmentCategories.AllowanceTravelPrivate),
            Meal = Sum(PayrollAdjustmentCategories.AllowanceMeal),
            Parking = Sum(PayrollAdjustmentCategories.AllowanceParking),
            OtHours = p.OtNormalHours + p.OtRestHours + p.OtPublicHours,
            Ot = p.OtPay + Sum(PayrollAdjustmentCategories.WagesOvertime),
            Commission = Sum(PayrollAdjustmentCategories.WagesCommission),
            Bonus = Sum(PayrollAdjustmentCategories.WagesBonusNonAnnual),
            Deduction = -Sum(PayrollAdjustmentCategories.DeductMiscellaneous),
        };
    }

    // The Group and Outlet columns: where the employee works, read live from
    // their profile (StatutoryEmployeeRow), trimmed.
    public static string GroupOf(StatutoryEmployeeRow row) => row.Department?.Trim() ?? string.Empty;

    public static string OutletOf(StatutoryEmployeeRow row) => row.Location?.Trim() ?? string.Empty;
}
