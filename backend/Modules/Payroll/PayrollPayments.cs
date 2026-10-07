using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// How each person on a run is paid: through the bank payroll file, or by hand.
//
// One rule, shared by the bank file, the Manual payments sheet and the payment
// schedule, so the three can never disagree about who goes where and between
// them account for every ringgit of net pay:
//
//   · In the bank file — paid by BANK_TRANSFER with an account number on file.
//   · Manual — everyone else owed money: Other bank / e-wallet (Merchantrade,
//     an overseas bank), Cash, Cheque, and a bank-transfer employee whose
//     account number is missing (flagged, so they aren't silently unpaid).
//
// Zero net pay is not a payment and appears in neither.
public static class PayrollPayments
{
    public sealed record ManualPayment(StatutoryEmployeeRow Row, string Method, string? Issue);

    public static bool InBankFile(StatutoryEmployeeRow r) =>
        r.Payslip.NetPay > 0m
        && r.PaymentMethod == PaymentMethod.BANK_TRANSFER
        && !string.IsNullOrWhiteSpace(r.BankAccountNumber);

    public static IReadOnlyList<ManualPayment> Manual(PayrollDocumentModel model) =>
        model.Rows
            .Where(r => r.Payslip.NetPay > 0m && !InBankFile(r))
            .Select(r => new ManualPayment(
                r,
                MethodLabel(r.PaymentMethod),
                r.PaymentMethod == PaymentMethod.BANK_TRANSFER
                    ? "No bank account on file — add it, or pay by hand"
                    : r.PaymentMethod == PaymentMethod.OTHER_TRANSFER && string.IsNullOrWhiteSpace(r.BankAccountNumber)
                        ? "No account number on file"
                        : null))
            .OrderBy(m => m.Method, StringComparer.Ordinal)
            .ThenBy(m => m.Row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.BANK_TRANSFER => "Bank transfer",
        PaymentMethod.OTHER_TRANSFER => "Other bank / e-wallet",
        PaymentMethod.CASH => "Cash",
        PaymentMethod.CHEQUE => "Cheque",
        _ => method.ToString(),
    };
}
