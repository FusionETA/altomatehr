using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using QuestPDF.Fluent;

namespace AltomateHR.Api.Tests.Payroll;

// A password-protected payslip: what the password is under each company rule,
// and that the locked file really needs it to open.
public class PayslipLockTests
{
    // ─── The password rule ──────────────────────────────────────────────

    [Theory]
    [InlineData("900101-14-5567", "900101145567")]
    [InlineData(" 900101 14 5567 ", "900101145567")]
    [InlineData("a1234 5678", "A12345678")]          // a passport keeps its letters
    public void IcDigits_IsTheIcWithOnlyLettersAndDigits(string id, string expected) =>
        Assert.Equal(expected, PayslipLock.PasswordFor(PayslipPasswordRule.IC_DIGITS, id, null));

    [Theory]
    [InlineData("900101-14-5567", "145567")]
    [InlineData("A12345678", "345678")]
    [InlineData("12-34", "1234")]                     // shorter than 6: all of it
    public void IcLast6_IsTheLastSixOfThat(string id, string expected) =>
        Assert.Equal(expected, PayslipLock.PasswordFor(PayslipPasswordRule.IC_LAST6, id, null));

    [Fact]
    public void DateOfBirth_IsDdMmYyyy() =>
        Assert.Equal("15061990",
            PayslipLock.PasswordFor(PayslipPasswordRule.DOB_DDMMYYYY, "900101-14-5567", new DateTime(1990, 6, 15)));

    [Theory]
    [InlineData(PayslipPasswordRule.IC_DIGITS, null)]
    [InlineData(PayslipPasswordRule.IC_DIGITS, "  - ")]
    [InlineData(PayslipPasswordRule.IC_LAST6, "")]
    public void NoIc_IsNoPassword_NotAGuess(PayslipPasswordRule rule, string? id) =>
        Assert.Null(PayslipLock.PasswordFor(rule, id, new DateTime(1990, 6, 15)));

    // RANDOM is the payslip's own, not derived from the employee.
    [Fact]
    public void TheRandomRule_IsNotDerivedFromTheEmployee() =>
        Assert.Null(PayslipLock.PasswordFor(PayslipPasswordRule.RANDOM, "900101-14-5567", new DateTime(1990, 6, 15)));

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

    [Fact]
    public void NoDateOfBirth_IsNoPassword_UnderTheDobRule() =>
        Assert.Null(PayslipLock.PasswordFor(PayslipPasswordRule.DOB_DDMMYYYY, "900101-14-5567", null));

    // The hint never contains a password — it describes the rule.
    [Theory]
    [InlineData(PayslipPasswordRule.IC_DIGITS, "IC number")]
    [InlineData(PayslipPasswordRule.IC_LAST6, "last 6 digits")]
    [InlineData(PayslipPasswordRule.DOB_DDMMYYYY, "date of birth")]
    public void Hint_DescribesTheRule(PayslipPasswordRule rule, string expected) =>
        Assert.Contains(expected, PayslipLock.Hint(rule));

    // The email says what opens the PDF — only when it is locked.
    [Fact]
    public void TheEmail_SaysWhatOpensALockedPayslip_AndNothingOtherwise()
    {
        var hint = PayslipLock.Hint(PayslipPasswordRule.IC_DIGITS);

        var locked = PayslipEmailTemplate.BuildHtml("Aisyah", "Globe Engineering", "January 2026", hint);
        Assert.Contains("password-protected", locked);
        Assert.Contains("IC number with no dashes", locked);

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
