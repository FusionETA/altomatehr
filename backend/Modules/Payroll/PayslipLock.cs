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
    // The employee's password under an employee-detail rule. Null when the
    // field the rule needs is missing — the caller refuses, naming the
    // person, rather than guessing a password nobody could know.
    //
    // RANDOM isn't derived from anything: it's the payslip's own
    // (Payslip.PdfPasswordEncrypted, made by NewRandomPassword), so this
    // returns null for it and the caller reads the stored one.
    public static string? PasswordFor(PayslipPasswordRule rule, string? idNumber, DateTime? dateOfBirth)
    {
        switch (rule)
        {
            case PayslipPasswordRule.RANDOM:
                return null;

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
        PayslipPasswordRule.RANDOM =>
            "the password shown next to this payslip on your Payslips page in AltomateHR (each payslip has its own)",
        _ => "your IC number with no dashes (or your passport number)",
    };

    // The field an admin must fill in before an employee-detail rule can work.
    public static string MissingField(PayslipPasswordRule rule) =>
        rule == PayslipPasswordRule.DOB_DDMMYYYY ? "date of birth" : "IC / passport number";

    // ─── Random passwords (the RANDOM rule) ─────────────────────────────

    // No look-alikes: no 0/O, 1/I/L — it's read off a screen and typed.
    private const string RandomAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // A new random password for one payslip, e.g. "K7PM-X4QR": 8 characters
    // from 31 (about 40 bits — far beyond guessing one PDF offline in any
    // useful time), with a dash in the middle so it's easy to read off a
    // screen. The dash is PART of the password: what the employee sees is
    // exactly what they type.
    public static string NewRandomPassword()
    {
        var chars = RandomNumberGenerator.GetString(RandomAlphabet, 8);
        return $"{chars[..4]}-{chars[4..]}";
    }

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
