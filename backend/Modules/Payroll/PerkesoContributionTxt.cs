using System.Text;

namespace AltomateHR.Api.Modules.Payroll;

// SOCSO + EIS (+ SKBBK) combined contribution TXT for PERKESO ASSIST.
//
// 278 characters per row, CRLF, parsed by byte position:
//
//   001-012  Employer Code              alphanumeric, left,  space-pad
//   013-032  MyCoID / SSM number        alphanumeric, left,  space-pad
//   033-044  IC / foreign-worker number alphanumeric, left,  space-pad
//   045-194  Employee name              alphanumeric, left,  space-pad
//   195-200  Contribution month         MMYYYY
//   201-214  Employee salary            sen, right, space-pad
//   215-220  SOCSO employer share       sen, right, space-pad
//   221-226  SOCSO employee share       sen, right, space-pad
//   227-232  EIS employer share         sen, right, space-pad
//   233-238  EIS employee share         sen, right, space-pad
//   239-244  SKBBK employee share       sen, right, space-pad   ← v2 only
//   245-258  Filler                     spaces (20 wide in v1, 14 in v2)
//   259-278  Filler                     spaces
//
// ⚠ ONE renderer for both layouts. They differ by exactly one field: v2
// (ASSIST 2.0, for SKBBK / Skim LINDUNG 24 Jam) spends 6 of v1's 20 filler
// bytes on the SKBBK column, which is what makes v2 backwards-compatible —
// a v1 parser reads those bytes as filler either way. The reference ships two
// near-identical files and a comment asking whoever edits one to diff the
// other; a single renderer cannot fall out of step with itself.
//
// WHICH layout is decided by the PERIOD, not by the caller. SKBBK began
// 1 Jun 2026, so a rerun of an earlier month must produce the v1 layout that
// month was actually filed under — the same rule the contribution tables
// follow.
public static class PerkesoContributionTxt
{
    public const string ContentType = "text/plain";

    private const int RowWidth = 278;

    public static StatutoryFileResult Render(StatutoryRunPayload payload)
    {
        var employerCode = payload.CompanyInfo?.PerkesoEmployerCode?.Trim() ?? string.Empty;
        if (employerCode.Length == 0)
        {
            return StatutoryFileResult.Refused(
                "PERKESO Employer Code is missing. Set it in Payroll Settings → Company Info "
                + "before generating the SOCSO/EIS file.");
        }

        var myCoId = payload.CompanyInfo?.RegistrationNo?.Trim() ?? string.Empty;

        // The period decides the layout — see the note above.
        var includeSkbbk = StatutoryTables.GetSkbbkPhaseForPeriod(
            payload.Run.PeriodYear, payload.Run.PeriodMonth) is not null;

        var contributionMonth =
            $"{payload.Run.PeriodMonth:D2}{payload.Run.PeriodYear:D4}";

        var sb = new StringBuilder();

        foreach (var row in payload.Rows)
        {
            var p = row.Payslip;

            // Nothing to remit for this person this month.
            if (p.SocsoEmployer == 0m && p.SocsoEmployee == 0m
                && p.EisEmployer == 0m && p.EisEmployee == 0m
                && p.SkbbkEmployee == 0m)
            {
                continue;
            }

            // Locals and PRs are keyed by IC. Everyone else has no NRIC, so
            // PERKESO keys them by their SOCSO number, then their foreign-worker
            // number, and only then falls back to whatever ID is on file.
            var identification = row.IsLocalOrPr
                ? StatutoryFileFields.DigitsOnly(row.IdNumber)
                : FirstNonBlank(row.SocsoNumber, row.SsfwNumber)
                  ?? StatutoryFileFields.DigitsOnly(row.IdNumber);

            var line = new StringBuilder(RowWidth)
                .Append(StatutoryFileFields.PadRight(employerCode, 12))
                .Append(StatutoryFileFields.PadRight(myCoId, 20))
                .Append(StatutoryFileFields.PadRight(identification, 12))
                .Append(StatutoryFileFields.PadRight(row.EmployeeName, 150))
                .Append(contributionMonth)
                .Append(Sen(p.GrossPay, 14))
                .Append(Sen(p.SocsoEmployer, 6))
                .Append(Sen(p.SocsoEmployee, 6))
                .Append(Sen(p.EisEmployer, 6))
                .Append(Sen(p.EisEmployee, 6));

            if (includeSkbbk)
            {
                line.Append(Sen(p.SkbbkEmployee, 6));
                line.Append(' ', 14);   // filler, shortened by the SKBBK column
            }
            else
            {
                line.Append(' ', 20);   // filler, full width
            }

            line.Append(' ', 20);

            sb.Append(Exactly(line.ToString(), RowWidth))
              .Append(StatutoryFileFields.LineEnding);
        }

        var fileName =
            $"socso-eis-{payload.Run.PeriodYear}-{payload.Run.PeriodMonth:D2}.txt";

        return StatutoryFileResult.Text(fileName, sb.ToString(), ContentType);
    }

    private static string Sen(decimal ringgit, int width) =>
        StatutoryFileFields.PadLeft(
            StatutoryFileFields.ToSen(ringgit)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            width);

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    // A row of the wrong length shifts every column after it. The field
    // helpers are already total, so this can only fire if the layout above is
    // edited wrongly — belt and braces on a file nobody re-reads before
    // submitting it.
    private static string Exactly(string line, int width) =>
        line.Length == width ? line : line.PadRight(width, ' ')[..width];
}
