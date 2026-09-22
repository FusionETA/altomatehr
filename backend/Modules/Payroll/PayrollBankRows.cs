using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// Which rows of a run become lines in a bank file, and what each one pays.
//
// Shared by all four bank formats because the rules are a property of PAYING
// PEOPLE, not of any one bank's layout: skip anyone with nothing to pay or
// nowhere to pay it, and REFUSE outright if a bank name can't be resolved.
//
// That last one is the important asymmetry. A row skipped for zero net pay is
// correct — there is no payment to make. A row skipped because "Mayban" didn't
// match the register is someone who doesn't get paid and nobody finds out
// until they say so, so it stops the whole file instead.
internal static class PayrollBankRows
{
    internal sealed record BankPayRow(
        StatutoryEmployeeRow Source,
        MalaysianBank Bank,
        // Digits only. Every format wants it that way, and an admin who typed
        // "1234-5678" shouldn't have their file rejected for the hyphen.
        string Account,
        decimal Amount,
        // 1-based position in the file. Some formats need a per-record unique
        // id and an employee code isn't reliably unique.
        int Sequence);

    internal static (IReadOnlyList<BankPayRow> Rows, StatutoryFileResult? Refusal) Select(
        PayrollDocumentModel model)
    {
        var candidates = model.Rows
            .Where(r => r.Payslip.NetPay > 0m && !string.IsNullOrWhiteSpace(r.BankAccountNumber))
            .ToList();

        var unmatched = candidates
            .Where(r => MalaysianBanks.Find(r.BankName) is null)
            .Select(r => $"{r.EmployeeName} (\"{r.BankName}\")")
            .ToList();

        if (unmatched.Count > 0)
        {
            return ([], StatutoryFileResult.Refused(
                "These employees' banks could not be matched to a recognised Malaysian bank: "
                + string.Join("; ", unmatched)
                + ". Correct the bank name on each affected employee's profile."));
        }

        if (candidates.Count == 0)
        {
            return ([], StatutoryFileResult.Refused(
                "Nothing to disburse — every employee on this run has zero net pay or no bank "
                + "account on file."));
        }

        var rows = candidates
            .Select((r, i) => new BankPayRow(
                r,
                MalaysianBanks.Find(r.BankName)!,
                StatutoryFileFields.DigitsOnly(r.BankAccountNumber!),
                r.Payslip.NetPay,
                i + 1))
            .ToList();

        return (rows, null);
    }

    // The name the employee sees on their own statement. Capped at 20
    // characters, which every format allows; the 3-letter month keeps even
    // September inside it ("SALARY SEP 2026" is 15).
    internal static string Reference(PayrollDocumentModel model) =>
        Clamp($"SALARY {MonthAbbreviation(model.Run.PeriodMonth)} {model.Run.PeriodYear}"
            .ToUpperInvariant(), 20);

    internal static string PayeeName(StatutoryEmployeeRow row) =>
        string.IsNullOrWhiteSpace(row.BankAccountHolderName)
            ? row.EmployeeName
            : row.BankAccountHolderName!;

    // Ringgit to sen as an integer. Done on the decimal, so there is no float
    // to drift on a value like 1576.60.
    internal static long ToSen(decimal amount) => (long)decimal.Round(amount * 100m, 0);

    internal static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..max];

    internal static string MonthAbbreviation(int month) =>
        month is < 1 or > 12
            ? month.ToString("D2")
            : System.Globalization.CultureInfo.InvariantCulture
                .DateTimeFormat.GetAbbreviatedMonthName(month);
}
