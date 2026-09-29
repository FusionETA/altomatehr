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
    private const int ExpectedColumns = 22;

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
        decimal pcb = 1320m,
        FormEaFigures? ea = null,
        DateTime? dateOfBirth = null,
        DateTime? leaveDate = null) => new()
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
            Ea = ea ?? new FormEaFigures { B1a = gross, B1b = bonus, B3 = bik },
            TotalEpfEmployee = epf,
            TotalPcb = pcb,
            TotalMtdRemitted = pcb,
            DateOfBirth = dateOfBirth ?? new DateTime(1990, 1, 1),
            LeaveDate = leaveDate,
        };

    private static PayrollAnnualPayload Payload(
        string? employerTin = "E 1234567890",
        string? employerName = "Acme Engineering Sdn Bhd",
        AnnualEmployeeRow[]? employees = null) => new()
        {
            Year = 2026,
            OrganizationName = "Acme Engineering",
            CompanyInfo = employerName is null
                ? null
                : new PayrollCompanyInfo { OrganizationId = "org-1", EmployerName = employerName },
            EmployerNo = PayrollAnnualReports.EmployerNumber(employerTin),
            Employees = employees is null or [] ? [Employee()] : employees,
        };

    private static string Text(StatutoryFileResult result) =>
        Encoding.UTF8.GetString(result.Content!);

    private static string[] FirstRow(StatutoryFileResult result) =>
        Text(result).Split("\r\n")[0].Split('|');

    // ─── M — the employer master ────────────────────────────────────────

    [Fact]
    public void TheEmployerRecordIsThreeFields()
    {
        var result = Cp8dTxt.RenderEmployer(Payload());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("1234567890|ACME ENGINEERING SDN BHD|2026\r\n", Text(result));
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

        Assert.Contains("|ACME ENGINEERING|", Text(result));
    }

    // ─── P — the columns (C.P.8D Pin. 2025) ────────────────────────────

    [Fact]
    public void EveryRowHasTwentyTwoFieldsAndATrailingPipe()
    {
        var line = Text(Cp8dTxt.RenderEmployees(Payload())).Split("\r\n")[0];

        Assert.EndsWith("|", line);
        // The trailing pipe produces one more, empty, element on split.
        Assert.Equal(ExpectedColumns + 1, line.Split('|').Length);
    }

    // LHDN's own worked example, field for field ("Contoh data txt 1").
    [Fact]
    public void TheFieldsAreInLhdnsOrder()
    {
        var ea = new FormEaFigures
        {
            B1a = 50000m, B3 = 4200m, B4 = 12000m, B1e = 1300m, F = 445m,
            D5aTp1Relief = 2200m, D5bZakatSelfPaid = 1400.30m, D3ZakatViaSalary = 1700.20m,
        };
        var row = Employee(name: "Ali bin Ahmad", taxNo: "03770324020", ic: "730510125580",
            maritalStatus: MaritalStatus.MARRIED, spouseWorking: true, children: 1, childRelief: 2000m,
            epf: 3600m, pcb: 2555.25m, ea: ea, dateOfBirth: new DateTime(1967, 12, 15)) with
        {
            TotalCp38 = 1822.63m,
            TotalSocsoEmployee = 120m,
            TotalEisEmployee = 30m,
        };

        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [row])));

        Assert.Equal(
            ["ALI BIN AHMAD", "03770324020", "730510125580", "3", "2", "15-12-2027", "2", "1", "2000",
             "50000", "4200", "12000", "1300", "445", "2200", "1400.30", "3600", "1700.20", "2555.25",
             "1822.63", "", "150"],
            cols[..ExpectedColumns]);
    }

    // A nil optional amount is left empty, as in LHDN's "Contoh data txt 2";
    // EPF and PCB are always written.
    [Fact]
    public void NilOptionalAmounts_AreLeftEmpty()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [Employee(epf: 0m, pcb: 0m)])));

        foreach (var i in new[] { 10, 11, 12, 13, 14, 15, 17, 19, 20, 21 })
        {
            Assert.Equal(string.Empty, cols[i]);
        }
        Assert.Equal("0", cols[16]);       // 17 EPF
        Assert.Equal("0.00", cols[18]);    // 19 PCB
    }

    // Field 10 is the remuneration LESS what fields 11–13 declare, so a
    // benefit in kind is never counted twice.
    [Fact]
    public void GrossRemuneration_LeavesOutWhatHasItsOwnField()
    {
        var ea = new FormEaFigures { B1a = 50000m, B1b = 8000m, B1c = 1200m, B3 = 2000m, B4 = 6000m, B1e = 900m, F = 3000m };
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [Employee(ea: ea)])));

        Assert.Equal("59200", cols[9]);    // 10 gross: 50,000 + 8,000 + 1,200
        Assert.Equal("2000", cols[10]);    // 11 BIK
        Assert.Equal("6000", cols[11]);    // 12 accommodation
        Assert.Equal("900", cols[12]);     // 13 ESOS
        Assert.Equal("3000", cols[13]);    // 14 tax-exempt
    }

    // Rounded, not truncated: this declares income, and rounding every
    // employee down would under-declare the employer's total.
    [Fact]
    public void TheWholeRinggitColumnsRound()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(gross: 60000.60m, epf: 6600.40m)])));

        Assert.Equal("60001", cols[9]);
        Assert.Equal("6600", cols[16]);
    }

    [Fact]
    public void PcbKeepsItsSen()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(pcb: 1320.55m)])));

        Assert.Equal("1320.55", cols[18]);
    }

    // Field 19 is every ringgit remitted as MTD in CP39 — the Additional PCB
    // the employee asked for included — matching EA D1 and PCB 2(II).
    [Fact]
    public void Pcb_IsAllMtdRemitted_IncludingAdditionalPcb()
    {
        var row = Employee(pcb: 1200m) with { TotalMtdRemitted = 1500m };
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [row])));

        Assert.Equal("1500.00", cols[18]);
    }

    // ─── Fields 5 and 6 ─────────────────────────────────────────────────

    [Fact]
    public void Status_IsPermanentUnlessRecorded()
    {
        Assert.Equal("2", FirstRow(Cp8dTxt.RenderEmployees(Payload()))[4]);

        var contract = Employee() with { Cp8dStatusOverride = 3 };
        Assert.Equal("3", FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [contract])))[4]);
    }

    [Theory]
    [InlineData(EmploymentStatus.MANAGEMENT, 1)]
    [InlineData(EmploymentStatus.PERMANENT, 2)]
    [InlineData(EmploymentStatus.CONTRACT, 3)]
    [InlineData(EmploymentStatus.PART_TIME, 4)]
    [InlineData(EmploymentStatus.INDUSTRIAL_TRAINEE, 5)]
    [InlineData(EmploymentStatus.OTHER, 6)]
    public void EmploymentStatus_MapsToLhdnsStatusCodes(EmploymentStatus status, int code)
    {
        Assert.Equal(code, PayrollAnnualReports.Cp8dStatus(status));
    }

    [Fact]
    public void NoRecordedStatus_LeavesTheFileDefault()
    {
        Assert.Null(PayrollAnnualReports.Cp8dStatus(null));
    }

    // LHDN: someone who left in the year is filed with their cessation date.
    [Fact]
    public void TheDate_IsTheCessationForSomeoneWhoLeft()
    {
        var left = Employee(leaveDate: new DateTime(2026, 6, 30));
        Assert.Equal("30-06-2026", FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [left])))[5]);
    }

    // Otherwise the contract end as typed, or the day they turn 60.
    [Fact]
    public void TheDate_IsTheContractEnd_OrTheStatutoryRetirementAge()
    {
        var contract = Employee() with { Cp8dRetirementDateOverride = new DateTime(2027, 3, 31) };
        Assert.Equal("31-03-2027", FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [contract])))[5]);

        var permanent = Employee(dateOfBirth: new DateTime(1985, 8, 9));
        Assert.Equal("09-08-2045", FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [permanent])))[5]);
    }

    // ─── Tax category ───────────────────────────────────────────────────

    [Theory]
    // Single, no children.
    [InlineData(MaritalStatus.SINGLE, null, 0, "1")]
    // Married, spouse has no income — the household claims on one return.
    [InlineData(MaritalStatus.MARRIED, false, 0, "2")]
    // Married with a working spouse.
    [InlineData(MaritalStatus.MARRIED, true, 0, "3")]
    // Married, spouse's status unanswered — 2, as the previous system filed it.
    [InlineData(MaritalStatus.MARRIED, null, 0, "2")]
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

        Assert.Equal("1", borne[6]);
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

    // Field 3: the new IC as digits, a passport as written — digits alone
    // would file someone else's number — and twelve zeros when there is none.
    [Theory]
    [InlineData("900101-14-5567", IdType.NRIC, "900101145567")]
    [InlineData("A2855084", IdType.PASSPORT, "A2855084")]
    [InlineData("a 2855084", IdType.PASSPORT, "A2855084")]
    [InlineData(null, IdType.NRIC, "000000000000")]
    public void TheIdField_IsTheIcDigits_OrThePassportAsWritten(string? id, IdType type, string expected)
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees: [Employee(ic: id, idType: type)])));

        Assert.Equal(expected, cols[2]);
    }

    // UTF-8, so an accented name survives instead of becoming "?".
    [Fact]
    public void AnAccentedNameIsWrittenAsUtf8()
    {
        var cols = FirstRow(Cp8dTxt.RenderEmployees(Payload(employees:
            [Employee(name: "José Ramírez")])));

        Assert.Equal("JOSÉ RAMÍREZ", cols[0]);
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
            OrganizationName = "Acme Engineering",
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
