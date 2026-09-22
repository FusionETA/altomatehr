using System.Text;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// Hong Leong Bank bulk payroll — one renderer per upload channel:
//
//   ConnectFirstTxt → HLB Connect First (fixed-width TXT)
//   ConnectBizXlsx  → HLB ConnectBiz (CBIZ template spreadsheet)
//
// The ConnectBiz sheet reproduces HLB's own "CBIZ Bulk Payroll Template"
// column for column. A file produced by another payroll vendor carried extra
// columns (Currency, ID Validation, Transaction Type/Code, Purpose Of
// Transfer) and compact ID codes like NEWIC — that is NOT this template, so
// don't "correct" this layout to match one of those.
//
// Both describe the same payment, so the row building is shared and only the
// serialisation differs.
//
// ── Payment mode ────────────────────────────────────────────────────────
// Per the CBIZ template HLB has three modes:
//   FT  — intra-HLB transfer, any amount
//   IBG — Interbank GIRO to any other bank, capped at RM 1,000,000
//   RTS — RENTAS, minimum RM 10,000 (high-value; not used for payroll)
// Payroll routes FT for Hong Leong accounts and IBG for everyone else — the
// same split as Maybank's IT/IG and PB ECP's PBB/IBG. A salary above the IBG
// ceiling is refused rather than silently emitted, because the bank would
// reject the whole file.
//
// ── Bank codes ──────────────────────────────────────────────────────────
// HLB uses its own 4-character IBG codes (MalaysianBank.HlbCode), NOT the BNM
// numeric codes. They are genuinely confusable: PABB is AFFIN (from its old
// name Perwira Affin Bank) while Public Bank is PBBB.
//
// ── Recipient Reference ─────────────────────────────────────────────────
// Mandatory on both channels and NOT derivable from payroll data — it's what
// the employee sees on their bank statement — so the admin types it per run
// and it is threaded in here.
public static class PayrollBankFileHlb
{
    public const string TxtContentType = "text/plain";

    public const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // The ceiling the CBIZ template states ("IBG: Max RM1Mil").
    private const decimal IbgMaxAmount = 1_000_000m;

    // Max length the template gives for the Recipient Reference column.
    private const int MaxRecipientReference = 20;

    // Connect First fixed-width record widths (204 chars total).
    private const int WMode = 3;
    private const int WBankCode = 8;
    private const int WAccount = 20;
    private const int WName = 100;
    private const int WAmount = 11;
    private const int WRecipientRef = 20;
    private const int WOtherDetails = 20;
    private const int WIdType = 2;
    private const int WIdValue = 20;

    private sealed record HlbRow(string Mode, string BankCode, string Account, string Name,
        decimal Amount, IdType? IdType, string IdNumber);

    // ─── Connect First (fixed-width TXT) ─────────────────────────────────

    // 204-character records, CRLF-terminated, no header or trailer:
    //   1-3       3  Payment mode (FT / IBG)
    //   4-11      8  Beneficiary bank code
    //   12-31    20  Beneficiary account no.
    //   32-131  100  Beneficiary name
    //   132-142  11  Amount in SEN (zero-padded, no decimal point)
    //   143-162  20  Recipient reference
    //   163-182  20  Other payment details
    //   183-184   2  Validation ID type
    //   185-204  20  Validation ID value
    public static StatutoryFileResult RenderConnectFirstTxt(
        PayrollDocumentModel model, string? recipientReference)
    {
        var (rows, reference, refusal) = BuildRows(model, recipientReference);
        if (refusal is not null) return refusal;

        var lines = rows.Select(r => string.Concat(
            PadRight(r.Mode, WMode),
            PadRight(r.BankCode, WBankCode),
            PadRight(r.Account, WAccount),
            PadRight(r.Name, WName),
            ZeroPad(PayrollBankRows.ToSen(r.Amount), WAmount),
            PadRight(reference, WRecipientRef),
            new string(' ', WOtherDetails),
            PadRight(TxtIdType(r.IdType, r.IdNumber), WIdType),
            PadRight(r.IdNumber, WIdValue)));

        var content = Encoding.Latin1.GetBytes(string.Join("\r\n", lines) + "\r\n");
        var fileName =
            $"HLB_ConnectFirst_Payroll_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}.txt";
        return new StatutoryFileResult(true, fileName, content, TxtContentType, null);
    }

    // ─── ConnectBiz (CBIZ template spreadsheet) ──────────────────────────

    // The template's exact column order (v1.3 — v1.1 is identical minus the
    // trailing e-mail column). The asterisks mark the template's mandatory
    // columns and are reproduced so the sheet reads like the one HLB hands out.
    private static readonly string[] CbizHeaders =
    [
        "*Payment Mode",
        "*Beneficiary Bank Code",
        "*Beneficiary Account No.",
        "*Beneficiary Name",
        "*Amount (RM)",
        "*Recipient Reference",
        "Other Payment Details",
        "Validation ID Type",
        "Validation ID Value",
        "Beneficiary E-mail Address",
    ];

    public static StatutoryFileResult RenderConnectBizXlsx(
        PayrollDocumentModel model, string? recipientReference)
    {
        var (rows, reference, refusal) = BuildRows(model, recipientReference);
        if (refusal is not null) return refusal;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");

        for (var i = 0; i < CbizHeaders.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = CbizHeaders[i];
            sheet.Column(i + 1).Width = Math.Max(12, CbizHeaders[i].Length + 2);
        }

        var rowNumber = 2;
        foreach (var r in rows)
        {
            sheet.Cell(rowNumber, 1).Value = r.Mode;
            sheet.Cell(rowNumber, 2).Value = r.BankCode;
            // Account numbers and IDs are digit strings Excel would otherwise
            // render in scientific notation, so they are forced to text.
            sheet.Cell(rowNumber, 3).SetValue(r.Account);
            // The template notes only the first 20 characters reach the
            // beneficiary's statement for FT/IBG; the full name is still sent.
            sheet.Cell(rowNumber, 4).Value = Clean(r.Name, 100);
            // Numeric, NOT text — the template's Amount column is a number and
            // a string uploads as an invalid amount. (The opposite of PB ECP,
            // whose validator rejects a numeric amount.)
            sheet.Cell(rowNumber, 5).Value = decimal.Round(r.Amount, 2);
            sheet.Cell(rowNumber, 6).Value = reference;
            sheet.Cell(rowNumber, 7).Value = string.Empty;
            // ID validation applies to FT and IBG only, and only when we
            // actually hold an ID for the employee.
            sheet.Cell(rowNumber, 8).Value = XlsxIdType(r.IdType, r.IdNumber);
            sheet.Cell(rowNumber, 9).SetValue(r.IdNumber);
            sheet.Cell(rowNumber, 10).Value = string.Empty;
            rowNumber++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName =
            $"HLB_ConnectBiz_Payroll_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}.xlsx";
        return new StatutoryFileResult(true, fileName, stream.ToArray(), XlsxContentType, null);
    }

    // ─── Shared ──────────────────────────────────────────────────────────

    // Applies the skip rules, resolves each employee's bank to an HLB code and
    // picks FT vs IBG. Refuses on anything the bank would reject the file for,
    // so the admin fixes the data rather than discovering it at upload time.
    private static (IReadOnlyList<HlbRow> Rows, string Reference, StatutoryFileResult? Refusal)
        BuildRows(PayrollDocumentModel model, string? recipientReference)
    {
        var reference = Clean(recipientReference, MaxRecipientReference);
        if (reference.Length == 0)
        {
            return ([], string.Empty, StatutoryFileResult.Refused(
                "A recipient reference is required for the Hong Leong payroll file — enter one "
                + "in the download panel. It appears on your employees' bank statements."));
        }

        var (selected, refusal) = PayrollBankRows.Select(model);
        if (refusal is not null) return ([], string.Empty, refusal);

        var hlb = MalaysianBanks.Find("Hong Leong Bank Berhad")!;
        var rows = new List<HlbRow>();
        var overLimit = new List<string>();

        foreach (var row in selected)
        {
            var intra = row.Bank.HlbCode == hlb.HlbCode;
            if (!intra && row.Amount > IbgMaxAmount)
            {
                overLimit.Add($"{row.Source.EmployeeName} (RM {row.Amount:N2})");
                continue;
            }

            rows.Add(new HlbRow(
                intra ? "FT" : "IBG",
                row.Bank.HlbCode,
                row.Account,
                PayrollBankRows.PayeeName(row.Source),
                row.Amount,
                row.Source.IdType,
                row.Source.IdNumber?.Trim() ?? string.Empty));
        }

        if (overLimit.Count > 0)
        {
            return ([], string.Empty, StatutoryFileResult.Refused(
                "Hong Leong caps an IBG payment at RM 1,000,000 and these exceed it: "
                + string.Join("; ", overLimit) + ". Pay them separately via RENTAS."));
        }

        return (rows, reference, null);
    }

    // Two-character ID type for the Connect First record. Only NI (New IC) is
    // corroborated by HLB's own sample output; the rest follow the convention
    // Public Bank's ECP spec uses, since both are Malaysian bulk-payment files
    // sharing the NI/OI/BR/PL/ML/PP vocabulary.
    private static string TxtIdType(IdType? idType, string idNumber) =>
        idNumber.Length == 0 ? string.Empty : idType switch
        {
            IdType.NRIC => "NI",
            IdType.PASSPORT => "PP",
            IdType.POLICE_NO => "PL",
            IdType.ARMY_NO => "ML",
            _ => string.Empty,
        };

    // The spreadsheet equivalent: the literal dropdown labels in HLB's own
    // CBIZ template, not compact codes. Anything that isn't an NRIC maps to
    // "Others", which the template documents as up to 20 alphanumeric
    // characters — the only bucket that accepts a passport or service number.
    private static string XlsxIdType(IdType? idType, string idNumber) =>
        idNumber.Length == 0 ? string.Empty : idType switch
        {
            IdType.NRIC => "New IC No.",
            IdType.PASSPORT or IdType.POLICE_NO or IdType.ARMY_NO => "Others",
            _ => string.Empty,
        };

    // Printable ASCII only, collapsed whitespace, clamped.
    private static string Clean(string? value, int width)
    {
        var kept = (value ?? string.Empty)
            .Select(c => c is >= ' ' and <= '~' ? c : ' ')
            .ToArray();

        var collapsed = string.Join(" ", new string(kept)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return PayrollBankRows.Clamp(collapsed, width);
    }

    private static string PadRight(string value, int width) => Clean(value, width).PadRight(width);

    private static string ZeroPad(long value, int width)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .PadLeft(width, '0');
        return text.Length <= width ? text : text[^width..];
    }
}
