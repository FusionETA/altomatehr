using System.Globalization;
using System.Text;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// OCBC Velocity — "GIRO (with 2nd ID validation & payment type)" v2.1, the
// fixed-width TXT Velocity takes for payroll via GIRO. Also valid against the
// older "Giro Payment With Check ID (MY)" layout (2013): v2.1 only carves the
// account's last 3 characters, the payment type (248-249) and the trailer's
// hash total out of filler, and this file leaves all three blank.
//
// Every record is exactly 480 characters, CRLF-terminated:
//   01  header   — the company and its debiting account, one per file
//   02  detail   — one per employee
//   03  trailer  — count and total, one per file
// (The 77-character "21" invoice-detail record is for supplier payments and
// isn't used for payroll.)
//
// General rule from the spec: numeric fields right-justified and zero-padded,
// character fields left-justified and space-padded.
//
// ── Routing ─────────────────────────────────────────────────────────────
// Each payee is routed by a 9-digit FI ID from OCBC's "IBG Product Offering
// and Account Structure" list (27 Oct 2023) — OCBC's own numbering, NOT the
// BIC or the BNM code. The conventional and Islamic arms of a bank share one.
// OCBC accounts go through the same GIRO file under OCBC's own ID.
//
// ── Per file ────────────────────────────────────────────────────────────
// Tape ID must differ between files with the same value date and debiting
// account, whenever they were uploaded — the admin types it (default 001).
// ID check asks the employee's bank to match the IC/passport to the account
// before crediting: safer, but OCBC warns it's prone to rejections, so it is
// off unless the admin ticks it for the file.
public static class PayrollBankFileOcbc
{
    public const string ContentType = "text/plain";

    private const int RecordWidth = 480;

    // OCBC's default Company CIF for Velocity files.
    private const string CompanyCif = "A999999";

    // Field widths the spec gives that the generic Reference doesn't fit.
    private const int MaxAccount = 17;   // the 20-character field keeps its last 3 as filler
    private const int MaxBeneficiaryName = 22;
    private const int MaxReference = 20;
    private const int MaxCustomerRef = 16;

    // OCBC's routing ID per bank, keyed by the register's canonical name.
    private static readonly IReadOnlyDictionary<string, string> RoutingIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Affin Bank Berhad"] = "100002322",
            ["Affin Islamic Bank Berhad"] = "100002322",
            ["Agrobank Berhad"] = "100033061",
            ["Al-Rajhi Bank (Malaysia) Berhad"] = "100003509",
            ["Alliance Bank Malaysia Berhad"] = "100002128",
            ["Alliance Islamic Bank Malaysia Berhad"] = "100002128",
            ["AmBank (M) Berhad"] = "100002089",
            ["AmIslamic Bank (M) Berhad"] = "100002089",
            ["Bangkok Bank Berhad"] = "100002047",
            ["Bank Islam Malaysia Berhad"] = "100002458",
            ["Bank Rakyat (Bank Kerjasama Rakyat Malaysia Berhad)"] = "100016020",
            ["Bank Muamalat (Malaysia) Berhad"] = "100003415",
            ["Bank of America (Malaysia) Berhad"] = "100002076",
            ["Bank of China (Malaysia) Berhad"] = "100002429",
            ["MUFG Bank (Malaysia) Berhad"] = "100002102",
            ["Bank Simpanan Nasional Berhad"] = "100016017",
            ["BNP Paribas Malaysia Berhad"] = "100002636",
            ["China Construction Bank (Malaysia) Berhad"] = "100002652",
            ["CIMB Bank Berhad"] = "100002050",
            ["CIMB Islamic Bank Berhad"] = "100002050",
            ["Citibank Berhad"] = "100002173",
            ["Deutsche Bank (Malaysia) Berhad"] = "100002199",
            ["Hong Leong Bank Berhad"] = "100002241",
            ["Hong Leong Islamic Bank Berhad"] = "100002241",
            ["HSBC Bank Malaysia Berhad"] = "100002225",
            ["HSBC Amanah Malaysia Berhad"] = "100002225",
            ["Industrial and Commercial Bank of China (Malaysia) Berhad"] = "100002597",
            ["JP Morgan Chase Bank Berhad"] = "100002157",
            ["Kuwait Finance House (Malaysia) Berhad"] = "100003460",
            ["Malayan Banking Berhad"] = "100002270",
            ["Maybank Islamic Berhad"] = "100002270",
            ["MBSB Bank Berhad"] = "100003525",
            ["Mizuho Bank (Malaysia) Berhad"] = "100002610",
            ["OCBC Bank (Malaysia) Berhad"] = "100002296",
            ["OCBC Al-Amin Bank Berhad"] = "100002296",
            ["Public Bank Berhad"] = "100002335",
            ["Public Islamic Bank Berhad"] = "100002335",
            ["RHB Bank Berhad"] = "100002186",
            ["RHB Islamic Bank Berhad"] = "100002186",
            ["Standard Chartered Bank (Malaysia) Berhad"] = "100002144",
            ["Standard Chartered Saadiq (Malaysia) Berhad"] = "100002144",
            ["Sumitomo Mitsui Banking Corporation Malaysia Berhad"] = "100002623",
            ["United Overseas Bank (Malaysia) Berhad"] = "100002267",
        };

    public static string? RoutingIdFor(MalaysianBank bank) => RoutingIds.GetValueOrDefault(bank.Name);

    public static StatutoryFileResult RenderGiroTxt(
        PayrollDocumentModel model,
        DateTime valueDate,
        string? payorAccountNo,
        string? tapeId,
        bool idCheck,
        string? recipientReference)
    {
        var debitAccount = StatutoryFileFields.DigitsOnly(payorAccountNo);
        if (debitAccount.Length < 3 || debitAccount.Length > 20)
        {
            return StatutoryFileResult.Refused(
                "No OCBC debiting account number is set. Add it under Payroll Settings → Bank "
                + "before generating the OCBC file.");
        }

        // Three digits, 001–999. Blank means the first file of the day.
        var tape = string.IsNullOrWhiteSpace(tapeId) ? "1" : tapeId.Trim();
        if (!int.TryParse(tape, NumberStyles.None, CultureInfo.InvariantCulture, out var tapeNumber)
            || tapeNumber is < 1 or > 999)
        {
            return StatutoryFileResult.Refused(
                "The Tape ID must be a number from 001 to 999. Use a different one for each file "
                + "you send OCBC for the same payment date.");
        }

        var (selected, refusal) = PayrollBankRows.Select(model);
        if (refusal is not null) return refusal;

        // Txn description: what the employee sees on their statement.
        var reference = Clean(recipientReference, MaxReference);
        if (reference.Length == 0) reference = PayrollBankRows.Reference(model);

        var problems = new List<string>();
        var details = new List<string>();
        long totalSen = 0;
        foreach (var row in selected)
        {
            var name = row.Source.EmployeeName;
            if (RoutingIdFor(row.Bank) is not { } routing)
            {
                problems.Add($"{name} — OCBC has no routing ID for {row.Bank.Name}");
                continue;
            }
            if (row.Account.Length > MaxAccount)
            {
                problems.Add($"{name} — account number is longer than {MaxAccount} digits");
                continue;
            }

            var (newIc, passport) = Identity(row.Source.IdType, row.Source.IdNumber);
            if (idCheck && newIc.Length == 0 && passport.Length == 0)
            {
                problems.Add($"{name} — no IC (12 digits) or passport number for the ID check");
                continue;
            }

            var sen = PayrollBankRows.ToSen(row.Amount);
            totalSen += sen;
            details.Add(Record(
                "02",
                Text(row.Account, 20),
                Number(sen, 17),
                "C",
                Text(newIc, 12),
                Text(string.Empty, 8),               // old IC
                Text(reference, 20),                 // txn description / recipient's reference
                Text(string.Empty, 20),              // business registration no.
                Text(string.Empty, 20),              // reference number
                routing,
                Text(PayrollBankRows.PayeeName(row.Source), MaxBeneficiaryName),
                Text(passport, 20),                  // police / army ID / passport no.
                Text(string.Empty, 1),               // send advice via
                Text(string.Empty, 50),              // e-mail
                Text(string.Empty, 24),              // fax
                idCheck ? "Y" : "N",
                Text(string.Empty, 2)));             // payment type (unused)
        }

        if (problems.Count > 0)
        {
            return StatutoryFileResult.Refused(
                "The OCBC file can't include: " + string.Join("; ", problems)
                + ". Fix their bank details or ID on their profile, or pay them from the Manual "
                + "payments sheet.");
        }

        var header = Record(
            "01",
            tapeNumber.ToString("D3", CultureInfo.InvariantCulture),
            // Branch: "00" + the first three digits of the company's account.
            "00" + debitAccount[..3],
            Text(CompanyCif, 20),
            Text(model.OrganizationName, 30),
            debitAccount.PadLeft(20, '0'),
            "D",
            "N",
            valueDate.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
            Text(string.Empty, 40),
            Text(Clean(PayrollBankRows.Reference(model), MaxCustomerRef), 16));

        var trailer = Record(
            "03",
            Number(details.Count, 6),
            Number(totalSen, 19),
            Text(string.Empty, 20));                 // hash total — not used by Velocity

        var lines = new List<string>(details.Count + 2) { header };
        lines.AddRange(details);
        lines.Add(trailer);

        var content = Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
        var fileName =
            $"OCBC_GIRO_Payroll_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}"
            + $"{(PayrollPeriodLabel.FileTag(model.Run) is { Length: > 0 } tag ? "_" + tag : "")}"
            + $"_T{tapeNumber:D3}.txt";
        return new StatutoryFileResult(true, fileName, content, ContentType, null);
    }

    // An NRIC goes in New IC (digits only, 12); anything else — passport,
    // police or army number — in the Police/Army/Passport field.
    private static (string NewIc, string Passport) Identity(IdType? type, string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return (string.Empty, string.Empty);
        return type == IdType.NRIC
            ? (StatutoryFileFields.DigitsOnly(number) is { Length: 12 } ic ? ic : string.Empty, string.Empty)
            : (string.Empty, StatutoryFileFields.AlphanumericOnly(number));
    }

    // The fields joined and padded to the full record width with filler.
    private static string Record(params string[] fields) =>
        string.Concat(fields).PadRight(RecordWidth);

    private static string Text(string? value, int width) => Clean(value, width).PadRight(width);

    private static string Number(long value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    // "Only characters visible in standard English keyboard" — printable
    // ASCII. Accents are folded to their letter first (José → Jose), anything
    // else becomes a space; whitespace is collapsed, then clamped.
    private static string Clean(string? value, int width)
    {
        var folded = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var kept = folded
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c is >= ' ' and <= '~' ? c : ' ')
            .ToArray();
        var collapsed = string.Join(" ", new string(kept).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return PayrollBankRows.Clamp(collapsed, width);
    }
}
