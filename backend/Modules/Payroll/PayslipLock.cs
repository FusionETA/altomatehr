using System.Security.Cryptography;
using AltomateHR.Api.Modules.Payroll.Entities;
using QuestPDF.Fluent;

namespace AltomateHR.Api.Modules.Payroll;

// Password-protecting a payslip PDF. Pure: no EF, no HTTP, no clock.
//
// The password is on the FILE (AES-256, via the qpdf that QuestPDF ships
// natively for every platform we run on), so a payslip stays protected
// wherever it ends up — an inbox, a phone's Downloads, a shared drive.
//
// Applied to the finished bytes, so PayslipPdf itself never changes.
public static class PayslipLock
{
    // The employee's password under the company's rule. Null when the field
    // the rule needs is missing — the caller refuses, naming the person,
    // rather than guessing a password nobody could know.
    //
    // `companyPassword` is the COMPANY rule's — the same for everyone.
    public static string? PasswordFor(
        PayslipPasswordRule rule, string? idNumber, DateTime? dateOfBirth, string? companyPassword = null)
    {
        switch (rule)
        {
            case PayslipPasswordRule.COMPANY:
                return string.IsNullOrEmpty(companyPassword) ? null : companyPassword;

            case PayslipPasswordRule.DOB_DDMMYYYY:
                return dateOfBirth?.ToString("ddMMyyyy");

            case PayslipPasswordRule.IC_LAST6:
            {
                var id = Normalise(idNumber);
                return id is null ? null : id.Length <= 6 ? id : id[^6..];
            }

            default: // IC_DIGITS
                return Normalise(idNumber);
        }
    }

    // What the employee is told the password is — never the password itself.
    public static string Hint(PayslipPasswordRule rule) => rule switch
    {
        PayslipPasswordRule.IC_LAST6 =>
            "the last 6 digits of your IC number (or passport number)",
        PayslipPasswordRule.DOB_DDMMYYYY =>
            "your date of birth as DDMMYYYY (for example 01011990)",
        PayslipPasswordRule.COMPANY =>
            "the payslip password your company gave you",
        _ => "your IC number with no dashes (or your passport number)",
    };

    // The field an admin must fill in before the rule can work.
    public static string MissingField(PayslipPasswordRule rule) => rule switch
    {
        PayslipPasswordRule.DOB_DDMMYYYY => "date of birth",
        PayslipPasswordRule.COMPANY => "company payslip password",
        _ => "IC / passport number",
    };

    // The company password: at least 6 characters, at most 64. Payslips
    // aren't a login, but a 4-digit PIN is guessed in seconds offline.
    public const int MinCompanyPasswordLength = 6;

    // "900101-01-0001" → "900101010001"; a passport keeps its letters,
    // upper-cased: "a1234 5678" → "A12345678". Null when nothing is left.
    private static string? Normalise(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber)) return null;
        var kept = new string(idNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return kept.Length == 0 ? null : kept;
    }

    // The payslip, locked. Opening it needs `password`; printing is allowed,
    // changing it or copying from it isn't. The owner password is random and
    // thrown away — nobody needs to lift the restrictions on a payslip.
    //
    // qpdf works on files, so this goes through two temp files, always
    // deleted.
    public static byte[] Lock(byte[] pdf, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var input = Path.Combine(Path.GetTempPath(), $"payslip-{Guid.NewGuid():N}.pdf");
        var output = Path.Combine(Path.GetTempPath(), $"payslip-{Guid.NewGuid():N}-locked.pdf");
        try
        {
            File.WriteAllBytes(input, pdf);

            DocumentOperation
                .LoadFile(input)
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = password,
                    OwnerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
                    AllowPrinting = true,
                    AllowContentExtraction = false,
                    AllowModification = false,
                    AllowAnnotation = false,
                    AllowAssembly = false,
                    AllowFillingForms = false,
                    EncryptMetadata = true,
                })
                .Save(output);

            return File.ReadAllBytes(output);
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
