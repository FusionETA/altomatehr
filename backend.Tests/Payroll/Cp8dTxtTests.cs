using System.Text;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The CP8D upload pair.
//
// These are pipe-delimited rather than fixed-width, so a short field does not
// shift the ones after it — but the COLUMN COUNT and their ORDER are still the
// contract. A dropped column silently re-reads every later value as the wrong
// field, so the assertions here index into the split row rather than checking
// that a substring appears somewhere.
public class Cp8dTxtTests
{
    private const int ExpectedColumns = 16;

    private static AnnualEmployeeRow Employee(
        string name = "Aisyah Binti Rahman",
        string? taxNo = "SG 12345678901",
        string? ic = "900101-14-5567",
        IdType? idType = IdType.NRIC,
        MaritalStatus? maritalStatus = MaritalStatus.SINGLE,
        bool? spouseWorking = null,
        int children = 0,
        decimal childRelief = 0m,
        bool pcbBorneByEmployer = false,
        decimal gross = 60000m,
        decimal bonus = 0m,
        decimal bik = 0m,
        decimal epf = 6600m,
        decimal pcb = 1320m) => new()
        {
            EmployeeProfileId = "emp-1",
            EmployeeName = name,
            EmployeeCode = "E-001",
            IncomeTaxNumber = taxNo,
            IdNumber = ic,
            IdType = idType,
            MaritalStatus = maritalStatus,
            SpouseWorking = spouseWorking,
            QualifyingChildren = children,
            AnnualChildRelief = childRelief,
            PcbBorneByEmployer = pcbBorneByEmployer,
            GrossSalary = gross,
            BonusAndCommission = bonus,
            TotalBik = bik,
            TotalEpfEmployee = epf,
            TotalPcb = pcb,
        };

    private static PayrollAnnualPayload Payload(
        string? employerTin = "E 1234567890",
        string? employerName = "Globe Engineering Sdn Bhd",
        AnnualEmployeeRow[]? employees = null) => new()
        {
            Year = 2026,
            OrganizationName = "Globe Engineering",
            CompanyInfo = employerName is null
                ? null
                : new PayrollCompanyInfo { OrganizationId = "org-1", EmployerName = employerName },
            EmployerNo = PayrollAnnualReports.EmployerNumber(employerTin),
            Employees = employees is null or [] ? [Employee()] : employees,
        };

    private static string Text(StatutoryFileResult result) =>
        Encoding.ASCII.GetString(result.Content!);

    private static string[] FirstRow(StatutoryFileResult result) =>
        Text(result).Split("\r\n")[0].Split('|');

    // ─── M — the employer master ────────────────────────────────────────

    [Fact]
    public void TheEmployerRecordIsThreeFields()
    {
        var result = Cp8dTxt.RenderEmployer(Payload());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("1234567890|GLOBE ENGINEERING SDN BHD|2026\r\n", Text(result));
    }

    // LHDN parsers are Windows-era and reject a file whose last line has no
    // terminator.
    [Fact]
    public void EveryFileEndsWithCrLf()
    {
        Assert.EndsWith("\r\n", Text(Cp8dTxt.RenderEmployer(Payload())));
        Assert.EndsWith("\r\n", Text(Cp8dTxt.RenderEmployees(Payload())));
    }

    // The filename carries the employer number — it is the only thing telling
    // two orgs' downloads apart.
    [Fact]
    public void TheFileNamesFollowLhdnsConvention()
    {
        Assert.Equal("M1234567890_2026.TXT", Cp8dTxt.RenderEmployer(Payload()).FileName);
        Assert.Equal("P1234567890_2026.TXT", Cp8dTxt.RenderEmployees(Payload()).FileName);
    }

    // Without an E-number the upload has nothing to attach to, and the
    // filename would be wrong too. A refusal naming the setting beats a file
    // LHDN rejects.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingEmployerNumber_IsRefused(string? tin)
    {
        var employer = Cp8dTxt.RenderEmployer(Payload(employerTin: tin));
        var employees = Cp8dTxt.RenderEmployees(Payload(employerTin: tin));

        Assert.False(employer.Ok);
        Assert.False(employees.Ok);
        Assert.Contains("E-number", employer.Error);
    }

    // Falls back to the org's own name when no company profile exists, rather
    // than filing a blank employer name.
    [Fact]
    public void WithNoCompanyProfile_TheOrgNameIsUsed()
    {
        var result = Cp8dTxt.RenderEmployer(Payload(employerName: null));

        Assert.Contains("|GLOBE ENGINEERING|", Text(result));
    }

    // ─── P — the columns ────────────────────────────────────────────────

    [Fact]
    public void EveryRowHasSixteenColumnsAndATrailingPipe()
    {
        var line = Text(Cp8dTxt.RenderEmployees(Payload())).Split("\r\n")[0];

        Assert.EndsWith("|", line);
        // The trailing pipe produces a 17th, empty, element on split.
        Assert.Equal(ExpectedColumns + 1, line.Split('|').Length);
    }

    [Fact]
    public void TheColumnsAreInLhdnsOrder()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(children: 2, childRelief: 4000m, gross: 60000m, bonus: 5000m, bik: 1000m)])));

        Assert.Equal("AISYAH BINTI RAHMAN", cols[0]);   // 1  name, uppercase
        Assert.Equal("12345678901", cols[1]);           // 2  tax ref, digits only
        Assert.Equal("900101145567", cols[2]);          // 3  IC, no dashes
        Assert.Equal("3", cols[3]);                     // 4  category — single with children
        Assert.Equal("2", cols[4]);                     // 5  tax borne by employer: no
        Assert.Equal("2", cols[5]);                     // 6  qualifying children
        Assert.Equal("4000", cols[6]);                  // 7  child relief
        Assert.Equal("66000", cols[7]);                 // 8  gross + bonus + BIK
        Assert.Equal("6600", cols[13]);                 // 14 EPF
        Assert.Equal("1320.00", cols[15]);              // 16 PCB, two decimals
    }

    // Columns 9–13 and 15 are reserved by LHDN. Putting anything in them
    // shifts nothing but is read as data that was never meant to be sent.
    [Fact]
    public void TheReservedColumnsAreEmpty()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload()));

        foreach (var i in new[] { 8, 9, 10, 11, 12, 14 })
        {
            Assert.Equal(string.Empty, cols[i]);
        }
    }

    // The income column is the whole reportable income, not the salary line.
    [Fact]
    public void TheIncomeColumnIncludesBonusAndBenefitsInKind()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(gross: 50000m, bonus: 8000m, bik: 2000m)])));

        Assert.Equal("60000", cols[7]);
    }

    // Rounded, not truncated: this declares income, and rounding every
    // employee down would under-declare the employer's total.
    [Fact]
    public void TheWholeRinggitColumnsRound()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(gross: 60000.60m, epf: 6600.40m)])));

        Assert.Equal("60001", cols[7]);
        Assert.Equal("6600", cols[13]);
    }

    [Fact]
    public void PcbKeepsItsSen()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(pcb: 1320.55m)])));

        Assert.Equal("1320.55", cols[15]);
    }

    // ─── Tax category ───────────────────────────────────────────────────

    [Theory]
    // Single, no children.
    [InlineData(MaritalStatus.SINGLE, null, 0, "1")]
    // Married, spouse has no income — the household claims on one return.
    [InlineData(MaritalStatus.MARRIED, false, 0, "2")]
    // Married with a working spouse.
    [InlineData(MaritalStatus.MARRIED, true, 0, "3")]
    // Married but the spouse's status is unknown — NOT category 2, which
    // would claim a relief nobody has established.
    [InlineData(MaritalStatus.MARRIED, null, 0, "3")]
    [InlineData(MaritalStatus.DIVORCED, null, 0, "3")]
    [InlineData(MaritalStatus.WIDOWED, null, 0, "3")]
    // Single but claiming a child.
    [InlineData(MaritalStatus.SINGLE, null, 2, "3")]
    public void TheTaxCategoryFollowsMaritalStatusAndChildren(
        MaritalStatus status, bool? spouseWorking, int children, string expected)
    {
        Assert.Equal(expected,
            PayrollAnnualReports.TaxCategory(status, spouseWorking, children));
    }

    [Fact]
    public void TaxBorneByEmployer_IsOneNotTrue()
    {
        var borne = FirstRow(Cp8dTxt.RenderEmployees(
            Payload(employees: [Employee(pcbBorneByEmployer: true)])));

        Assert.Equal("1", borne[4]);
    }

    // ─── Identifier normalisation ───────────────────────────────────────

    [Theory]
    [InlineData("SG 12345678901", "12345678901")]
    [InlineData("OG12345678901", "12345678901")]
    [InlineData("12345678901", "12345678901")]
    [InlineData("SG-1234-5678-901", "12345678901")]
    [InlineData(null, "")]
    public void TheTaxReferenceIsDigitsOnly(string? input, string expected)
    {
        Assert.Equal(expected, PayrollAnnualReports.NormaliseTaxRef(input));
    }

    // A passport is not an IC. Coercing one into the IC column files a wrong
    // identifier against a real person.
    [Fact]
    public void APassportDoesNotBecomeAnIc()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(ic: "A12345678", idType: IdType.PASSPORT)])));

        Assert.Equal(string.Empty, cols[2]);
    }

    // ─── Who appears ────────────────────────────────────────────────────

    // Somebody with no tax reference and no PCB has nothing for LHDN to match
    // against. An empty row invites a rejection on a record that should never
    // have been sent.
    [Fact]
    public void SomeoneWithNoTaxRefAndNoPcb_IsOmitted()
    {
        var result = Cp8dTxt.RenderEmployees(Payload(
            employees:
            [
                Employee(name: "Reported", taxNo: "SG12345678901", pcb: 1320m),
                Employee(name: "Nothing To Report", taxNo: null, pcb: 0m),
            ]));

        Assert.Single(Text(result).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("NOTHING TO REPORT", Text(result));
    }

    // But someone who HAD tax withheld is reported even without a reference —
    // the withholding happened and LHDN has to see it.
    [Fact]
    public void SomeoneWithPcbButNoTaxRef_IsStillReported()
    {
        var result = Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(taxNo: null, pcb: 500m)]));

        Assert.Single(Text(result).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void AYearWithNoEmployees_IsAnEmptyFileNotAFailure()
    {
        var result = Cp8dTxt.RenderEmployees(new PayrollAnnualPayload
        {
            Year = 2026,
            OrganizationName = "Globe Engineering",
            EmployerNo = "1234567890",
            Employees = [],
        });

        Assert.True(result.Ok);
        Assert.Empty(result.Content!);
    }

    // A byte-order mark at the head of the first field is read as part of the
    // employer number.
    [Fact]
    public void TheFilesHaveNoByteOrderMark()
    {
        var bytes = Cp8dTxt.RenderEmployer(Payload()).Content!;

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'1', bytes[0]);
    }
}
