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

    // A run's own label: "October 2026 – Workers", or just the month for a
    // run without a name. Shown wherever runs of one month sit side by side.
    public static string ForRun(Entities.PayrollRun run) =>
        string.IsNullOrWhiteSpace(run.Name)
            ? For(run.PeriodYear, run.PeriodMonth)
            : $"{For(run.PeriodYear, run.PeriodMonth)} – {run.Name}";

    // The run's name for a file name: letters and digits only (LHDN's e-PCB
    // refuses anything else), or "Run2" for an unnamed later run. Empty for
    // an unnamed first run, whose files keep the names they always had.
    public static string FileTag(Entities.PayrollRun run)
    {
        var tag = new string((run.Name ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());
        if (tag.Length > 30) tag = tag[..30];
        return tag.Length > 0 ? tag : run.RunNumber > 1 ? $"Run{run.RunNumber}" : string.Empty;
    }
}
