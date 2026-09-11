using System.Globalization;

namespace AltomateHR.Api.Modules.Payroll;

// How a payroll period is spelled, everywhere.
//
// Rendered server-side and in one place so the runs list, the payslip PDF, the
// audit trail and the statutory submission files cannot end up naming the same
// month differently. Invariant culture on purpose: these labels reach
// paperwork, not just a screen, and a server's locale must not decide what
// LHDN reads.
public static class PayrollPeriodLabel
{
    // The abbreviated form, for places where the full month name does not fit
    // — a loan's installment schedule runs to a dozen rows or more.
    public static string ShortMonth(int month) =>
        month is < 1 or > 12
            ? $"M{month}"
            : CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);

    public static string For(int year, int month) =>
        month is < 1 or > 12
            ? $"{year}-{month:D2}"
            : $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {year}";
}
