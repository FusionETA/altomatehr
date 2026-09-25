using System.Text;

namespace AltomateHR.Api.Modules.Payroll;

// EPF contribution CSV for KWSP i-Akaun Majikan bulk upload.
//
// Columns, with a header row:
//   Member EPF No., Member IC No., Member Name, Member Wage,
//   Employer Contribution Amount, Member Contribution Amount
//
// Wage carries 2 decimals; the two contribution columns are WHOLE ringgit —
// KWSP's Third Schedule rounds contributions up to the ringgit and the engine
// has already done that, so these are exact, not rounded here.
//
// UTF-8, no BOM, CRLF.
public static class EpfContributionCsv
{
    public const string ContentType = "text/csv";

    private static readonly string[] Header =
    [
        "Member EPF No.",
        "Member IC No.",
        "Member Name",
        "Member Wage",
        "Employer Contribution Amount",
        "Member Contribution Amount",
    ];

    // `generatedOn` only names the file ({DDMMYYYY}-EPF_iAkaun-{YYYY}_{MM}.csv,
    // the previous system's pattern); the content never depends on it.
    public static StatutoryFileResult Render(StatutoryRunPayload payload, DateOnly generatedOn)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', Header)).Append(StatutoryFileFields.LineEnding);

        foreach (var row in payload.Rows)
        {
            // i-Akaun rejects a row with no EPF number outright, so omitting it
            // here is the difference between a file that uploads and one that
            // does not. The readiness check is what tells the admin who is
            // missing one.
            if (string.IsNullOrWhiteSpace(row.EpfNumber)) continue;

            // Nothing to remit — a foreigner on EIS only, or a stub payslip.
            if (row.Payslip.EpfEmployee == 0m && row.Payslip.EpfEmployer == 0m) continue;

            sb.Append(string.Join(',',
                // Written as stored, untrimmed — the previous system's bytes.
                StatutoryFileFields.CsvField(row.EpfNumber),
                StatutoryFileFields.CsvField(
                    StatutoryFileFields.AlphanumericOnly(row.IdNumber)),
                StatutoryFileFields.CsvField(row.EmployeeName),
                // Informational on the upload — the contributions above are
                // what KWSP actually books.
                row.Payslip.GrossPay.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                WholeRinggit(row.Payslip.EpfEmployer),
                WholeRinggit(row.Payslip.EpfEmployee)));

            sb.Append(StatutoryFileFields.LineEnding);
        }

        var fileName =
            $"{generatedOn:ddMMyyyy}-EPF_iAkaun-{payload.Run.PeriodYear:D4}_{payload.Run.PeriodMonth:D2}.csv";
        return StatutoryFileResult.Text(fileName, sb.ToString(), ContentType);
    }

    private static string WholeRinggit(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero)
            .ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
}
