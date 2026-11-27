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
    // What the employee is told opens the file — never the password itself.
    // One password per person (EmployeeProfile.PayslipPasswordEncrypted).
    public const string Hint =
        "your payslip password, shown at the top of your Payslips page in AltomateHR";

    // ─── A password the employee chooses ────────────────────────────────

    public const int MinLength = 8;
    public const int MaxLength = 64;

    // Why `password` can't be someone's payslip password, or null when it can.
    // Typed into a PDF viewer on a phone, so: long enough not to be guessed
    // against a forwarded file offline, short enough to type, and no spaces
    // at either end that nobody would know were there.
    public static string? Problem(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Your payslip password must be at least {MinLength} characters.";
        if (password.Length > MaxLength)
            return $"Your payslip password can be at most {MaxLength} characters.";
        if (char.IsWhiteSpace(password[0]) || char.IsWhiteSpace(password[^1]))
            return "Your payslip password can't start or end with a space.";
        if (password.Any(char.IsControl))
            return "Your payslip password can only use letters, numbers, symbols and spaces.";
        return null;
    }

    // ─── The random password everyone starts with ──────────────────────

    // No look-alikes: no 0/O, 1/I/L — it's read off a screen and typed.
    private const string RandomAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // A new random password for one person, e.g. "K7PM-X4QR": 8 characters
    // from 31 (about 40 bits — far beyond guessing one PDF offline in any
    // useful time), with a dash in the middle so it's easy to read off a
    // screen. The dash is PART of the password: what the employee sees is
    // exactly what they type.
    public static string NewRandomPassword()
    {
        var chars = RandomNumberGenerator.GetString(RandomAlphabet, 8);
        return $"{chars[..4]}-{chars[4..]}";
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
