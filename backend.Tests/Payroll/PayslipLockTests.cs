using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using QuestPDF.Fluent;

namespace AltomateHR.Api.Tests.Payroll;

// A password-protected payslip: the password everyone starts with, what an
// employee may change theirs to, and that the locked file really needs it.
public class PayslipLockTests
{
    // ─── A password the employee chooses ────────────────────────────────

    [Theory]
    [InlineData("my-payslips")]
    [InlineData("8 chars!")]                         // spaces inside are fine
    [InlineData("Pässwörd")]                         // so are accents
    public void AReasonablePassword_IsAccepted(string password) =>
        Assert.Null(PayslipLock.Problem(password));

    [Theory]
    [InlineData(null, "at least 8")]
    [InlineData("", "at least 8")]
    [InlineData("1234567", "at least 8")]
    [InlineData(" leading-space", "start or end with a space")]
    [InlineData("trailing-space ", "start or end with a space")]
    [InlineData("tab\there!", "letters, numbers")]
    public void AnUnusablePassword_IsRefused_SayingWhy(string? password, string why)
    {
        var problem = PayslipLock.Problem(password);
        Assert.NotNull(problem);
        Assert.Contains(why, problem);
    }

    [Fact]
    public void APasswordTooLongToType_IsRefused()
    {
        Assert.Null(PayslipLock.Problem(new string('x', PayslipLock.MaxLength)));
        Assert.Contains("at most", PayslipLock.Problem(new string('x', PayslipLock.MaxLength + 1)));
    }

    // "K7PM-X4QR": four, a dash, four — and never a look-alike character.
    [Fact]
    public void ARandomPassword_IsEightReadableCharactersWithADash()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = PayslipLock.NewRandomPassword();
            Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", password);
            Assert.DoesNotMatch("[01OIL]", password);
        }
    }

    [Fact]
    public void RandomPasswords_DontRepeat()
    {
        var seen = Enumerable.Range(0, 1000).Select(_ => PayslipLock.NewRandomPassword()).ToHashSet();
        Assert.Equal(1000, seen.Count);
    }

    // The email says what opens the PDF — only when it is locked — and never
    // carries the password.
    [Fact]
    public void TheEmail_SaysWhatOpensALockedPayslip_AndNothingOtherwise()
    {
        var locked = PayslipEmailTemplate.BuildHtml("Aisyah", "Globe Engineering", "January 2026", PayslipLock.Hint);
        Assert.Contains("password-protected", locked);
        Assert.Contains("Payslips page", locked);

        var open = PayslipEmailTemplate.BuildHtml("Aisyah", "Globe Engineering", "January 2026");
        Assert.DoesNotContain("password", open);
    }

    // ─── The lock ───────────────────────────────────────────────────────

    private static byte[] APayslip() => PayslipPdf.Render(new PayslipPdfModel
    {
        Payslip = new Payslip
        {
            SnapshotName = "Aisyah Binti Rahman", BasicPay = 5000m, ProratedPay = 5000m,
            GrossPay = 5000m, NetPay = 4400m, EpfEmployee = 550m, SocsoEmployee = 40m, EisEmployee = 10m,
        },
        OrganizationName = "Globe Engineering Sdn Bhd",
        PeriodLabel = "January 2026",
        IssueDate = new DateTime(2026, 1, 31),
        Ytd = new PayslipYtdSummary(),
    });

    // Opening a PDF with qpdf, the way a viewer would: true when `password`
    // opens it.
    internal static bool Opens(byte[] pdf, string? password)
    {
        var input = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.pdf");
        var output = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}-out.pdf");
        File.WriteAllBytes(input, pdf);
        try
        {
            DocumentOperation.LoadFile(input, password).Decrypt().Save(output);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void ALockedPayslip_IsEncrypted_AndOpensOnlyWithItsPassword()
    {
        var plain = APayslip();
        var locked = PayslipLock.Lock(plain, "900101145567");

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(locked, 0, 4));
        Assert.Contains("/Encrypt", System.Text.Encoding.Latin1.GetString(locked));
        Assert.DoesNotContain("/Encrypt", System.Text.Encoding.Latin1.GetString(plain));

        Assert.True(Opens(plain, null));
        Assert.False(Opens(locked, null));
        Assert.False(Opens(locked, "145567"));
        Assert.True(Opens(locked, "900101145567"));
    }

    [Fact]
    public void Lock_LeavesNoTempFilesBehind()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "payslip-*.pdf").Length;
        PayslipLock.Lock(APayslip(), "secret");
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "payslip-*.pdf").Length);
    }
}
