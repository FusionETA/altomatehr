using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The monthly submission files.
//
// KWSP, PERKESO and LHDN parse these by BYTE POSITION. A field one character
// short shifts every column after it, and the file is either rejected or —
// far worse — silently misread into the wrong employee's account. So the
// assertions here are on exact widths and exact slices, not on "contains".
public class StatutoryFileTests
{
    // ─── Fixtures ───────────────────────────────────────────────────────

    private static PayrollCompanyInfo CompanyInfo() => new()
    {
        OrganizationId = "org-1",
        EmployerName = "Globe Engineering Sdn Bhd",
        EmployerTin = "E1234567890",
        RegistrationNo = "202001012345",
        PerkesoEmployerCode = "A1234567890",
    };

    private static Payslip Payslip(
        decimal gross = 5000m,
        decimal epfEmployee = 550m,
        decimal epfEmployer = 650m,
        decimal socsoEmployee = 24.75m,
        decimal socsoEmployer = 86.65m,
        decimal eisEmployee = 9.90m,
        decimal eisEmployer = 9.90m,
        decimal skbbk = 0m,
        decimal pcb = 110m,
        decimal cp38 = 0m,
        string name = "Aisyah Binti Rahman") => new()
    {
        OrganizationId = "org-1",
        PayrollRunId = "run-1",
        EmployeeProfileId = "emp-1",
        UserId = "usr-1",
        SnapshotName = name,
        GrossPay = gross,
        EpfEmployee = epfEmployee,
        EpfEmployer = epfEmployer,
        SocsoEmployee = socsoEmployee,
        SocsoEmployer = socsoEmployer,
        EisEmployee = eisEmployee,
        EisEmployer = eisEmployer,
        SkbbkEmployee = skbbk,
        Pcb = pcb,
        Cp38 = cp38,
    };

    private static StatutoryEmployeeRow Row(
        Payslip? payslip = null,
        string name = "Aisyah Binti Rahman",
        string employeeCode = "E-001",
        string? idNumber = "900101-14-5567",
        string? epfNumber = "12345678",
        string? socsoNumber = null,
        string? ssfwNumber = null,
        string? incomeTaxNumber = "SG12345678900",
        string? nationality = "Malaysian",
        bool hasPr = false,
        Gender? gender = Gender.FEMALE,
        MaritalStatus? maritalStatus = MaritalStatus.SINGLE) => new()
    {
        Payslip = payslip ?? Payslip(name: name),
        EmployeeName = name,
        EmployeeCode = employeeCode,
        IdNumber = idNumber,
        EpfNumber = epfNumber,
        SocsoNumber = socsoNumber,
        SsfwNumber = ssfwNumber,
        IncomeTaxNumber = incomeTaxNumber,
        Nationality = nationality,
        HasPr = hasPr,
        Gender = gender,
        MaritalStatus = maritalStatus,
    };

    private static StatutoryRunPayload Payload(
        IEnumerable<StatutoryEmployeeRow>? rows = null,
        PayrollCompanyInfo? companyInfo = null,
        int year = 2026,
        int month = 3) => new()
    {
        Run = new PayrollRun
        {
            Id = "run-1",
            OrganizationId = "org-1",
            PeriodYear = year,
            PeriodMonth = month,
        },
        CompanyInfo = companyInfo ?? CompanyInfo(),
        Rows = rows?.ToList() ?? [Row()],
    };

    private static string[] Lines(StatutoryFileResult result)
    {
        Assert.True(result.Ok, result.Error);
        return System.Text.Encoding.UTF8.GetString(result.Content!)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    // ─── EPF CSV ────────────────────────────────────────────────────────

    [Fact]
    public void EpfCsv_WritesTheHeaderAndOneRowPerContributingEmployee()
    {
        var lines = Lines(EpfContributionCsv.Render(Payload()));

        Assert.Equal(
            "Member EPF No.,Member IC No.,Member Name,Member Wage,"
            + "Employer Contribution Amount,Member Contribution Amount",
            lines[0]);

        // Contributions are WHOLE ringgit — KWSP's Third Schedule already
        // rounded them up, so the file must not re-decimalise them.
        Assert.Equal("12345678,900101145567,Aisyah Binti Rahman,5000.00,650,550", lines[1]);
    }

    // i-Akaun rejects a row with no EPF number, so the file omits it rather
    // than shipping something that fails on upload.
    [Fact]
    public void EpfCsv_OmitsEmployeesWithNoEpfNumber()
    {
        var lines = Lines(EpfContributionCsv.Render(Payload([
            Row(name: "Has EPF"),
            Row(name: "No EPF", epfNumber: null),
        ])));

        Assert.Equal(2, lines.Length);          // header + one row
        Assert.Contains("Has EPF", lines[1]);
    }

    // A foreigner on EIS only, or an archived stub — nothing to remit.
    [Fact]
    public void EpfCsv_OmitsEmployeesWithNothingToContribute()
    {
        var lines = Lines(EpfContributionCsv.Render(Payload([
            Row(name: "Zero", payslip: Payslip(epfEmployee: 0m, epfEmployer: 0m)),
        ])));

        Assert.Single(lines);                   // header only
    }

    [Fact]
    public void EpfCsv_QuotesOnlyTheFieldsThatNeedIt()
    {
        var lines = Lines(EpfContributionCsv.Render(Payload([
            Row(name: "Rahman, Aisyah"),
        ])));

        Assert.Contains("\"Rahman, Aisyah\"", lines[1]);
    }

    // ─── PERKESO SOCSO / EIS / SKBBK TXT ────────────────────────────────

    [Fact]
    public void PerkesoTxt_LaysEveryFieldOutAtItsSpecifiedPosition()
    {
        var line = Assert.Single(Lines(PerkesoContributionTxt.Render(Payload())));

        Assert.Equal(278, line.Length);

        // Positions from the ASSIST spec, 1-indexed inclusive.
        Assert.Equal("A1234567890 ", line[0..12]);       // 001-012 employer code
        Assert.Equal("202001012345        ", line[12..32]);  // 013-032 MyCoID
        Assert.Equal("900101145567", line[32..44]);      // 033-044 IC, dashes stripped
        Assert.Equal("Aisyah Binti Rahman", line[44..63].TrimEnd());  // 045-194 name
        Assert.Equal("032026", line[194..200]);          // 195-200 MMYYYY
        Assert.Equal("        500000", line[200..214]);  // 201-214 salary in sen
        Assert.Equal("  8665", line[214..220]);          // 215-220 SOCSO employer
        Assert.Equal("  2475", line[220..226]);          // 221-226 SOCSO employee
        Assert.Equal("   990", line[226..232]);          // 227-232 EIS employer
        Assert.Equal("   990", line[232..238]);          // 233-238 EIS employee
    }

    // SKBBK began 1 Jun 2026. Before then the column does not exist and those
    // bytes are filler — a rerun of an earlier month has to produce the layout
    // that month was actually filed under.
    [Fact]
    public void PerkesoTxt_OmitsTheSkbbkColumnForPeriodsBeforeItExisted()
    {
        var line = Assert.Single(Lines(
            PerkesoContributionTxt.Render(Payload(year: 2026, month: 5))));

        Assert.Equal(278, line.Length);
        Assert.Equal(new string(' ', 40), line[238..278]);   // all filler
    }

    [Fact]
    public void PerkesoTxt_WritesTheSkbbkColumnFromJune2026()
    {
        var payload = Payload(
            [Row(payslip: Payslip(skbbk: 0.90m))], year: 2026, month: 6);

        var line = Assert.Single(Lines(PerkesoContributionTxt.Render(payload)));

        Assert.Equal(278, line.Length);
        Assert.Equal("    90", line[238..244]);              // 239-244 SKBBK, in sen
        Assert.Equal(new string(' ', 34), line[244..278]);   // filler, 6 bytes shorter
    }

    // Foreigners have no NRIC, so PERKESO keys them by their SOCSO number.
    [Fact]
    public void PerkesoTxt_KeysForeignersByTheirSocsoNumberRatherThanAnIc()
    {
        var payload = Payload([Row(
            nationality: "Nepalese",
            idNumber: "P1234567",
            socsoNumber: "F0099887766")]);

        var line = Assert.Single(Lines(PerkesoContributionTxt.Render(payload)));

        Assert.Equal("F0099887766 ", line[32..44]);
    }

    // A PR is treated as local for identification even though they are not
    // Malaysian by nationality.
    [Fact]
    public void PerkesoTxt_TreatsAPermanentResidentAsLocal()
    {
        var payload = Payload([Row(nationality: "Indonesian", hasPr: true)]);

        var line = Assert.Single(Lines(PerkesoContributionTxt.Render(payload)));

        Assert.Equal("900101145567", line[32..44]);
    }

    [Fact]
    public void PerkesoTxt_OmitsEmployeesWithNothingToRemit()
    {
        var payload = Payload([Row(payslip: Payslip(
            socsoEmployee: 0m, socsoEmployer: 0m,
            eisEmployee: 0m, eisEmployer: 0m, skbbk: 0m))]);

        Assert.Empty(Lines(PerkesoContributionTxt.Render(payload)));
    }

    [Fact]
    public void PerkesoTxt_RefusesWithoutAnEmployerCode()
    {
        var payload = Payload(companyInfo: new PayrollCompanyInfo { OrganizationId = "org-1" });

        var result = PerkesoContributionTxt.Render(payload);

        Assert.False(result.Ok);
        Assert.Contains("PERKESO Employer Code", result.Error);
    }

    // A long name must be truncated, never allowed to push the columns along.
    [Fact]
    public void PerkesoTxt_TruncatesRatherThanShiftingTheColumnsAfterAName()
    {
        var payload = Payload([Row(name: new string('X', 200))]);

        var line = Assert.Single(Lines(PerkesoContributionTxt.Render(payload)));

        Assert.Equal(278, line.Length);
        Assert.Equal("032026", line[194..200]);   // the month is still where it belongs
    }

    // ─── LHDN CP39 PCB TXT ──────────────────────────────────────────────

    [Fact]
    public void PcbTxt_WritesAHeaderCarryingTheTotalsOfTheRowsBelow()
    {
        var payload = Payload([
            Row(name: "Aisyah", employeeCode: "E-001", payslip: Payslip(pcb: 110m, name: "Aisyah")),
            Row(name: "Bala", employeeCode: "E-002", payslip: Payslip(pcb: 250.55m, name: "Bala")),
        ]);

        var lines = Lines(PcbCp39Txt.Render(payload));
        var header = lines[0];

        Assert.Equal(57, header.Length);
        Assert.Equal("H", header[0..1]);
        Assert.Equal("1234567890", header[1..11]);    // 002-011 HQ number
        Assert.Equal("1234567890", header[11..21]);   // 012-021 branch number
        Assert.Equal("2026", header[21..25]);         // 022-025 year
        Assert.Equal("03", header[25..27]);           // 026-027 month
        Assert.Equal("0000036055", header[27..37]);   // 028-037 total PCB in sen
        Assert.Equal("00002", header[37..42]);        // 038-042 PCB record count
        Assert.Equal("0000000000", header[42..52]);   // 043-052 total CP38
        Assert.Equal("00000", header[52..57]);        // 053-057 CP38 count
    }

    [Fact]
    public void PcbTxt_LaysTheDetailRowOutAtItsSpecifiedPositions()
    {
        var lines = Lines(PcbCp39Txt.Render(Payload()));
        var detail = lines[1];

        Assert.Equal(136, detail.Length);
        Assert.Equal("D", detail[0..1]);
        Assert.Equal("1234567890", detail[1..11]);           // 002-011 tax ref, wife code stripped
        Assert.Equal("0", detail[11..12]);                   // 012 wife code
        Assert.Equal("Aisyah Binti Rahman", detail[12..31]); // 013-072 name
        Assert.Equal(new string(' ', 12), detail[72..84]);   // 073-084 Old IC — always blank
        Assert.Equal("900101145567", detail[84..96]);        // 085-096 New IC
        Assert.Equal(new string(' ', 12), detail[96..108]);  // 097-108 passport
        Assert.Equal("00011000", detail[110..118]);          // 111-118 PCB in sen
        Assert.Equal("00000000", detail[118..126]);          // 119-126 CP38
        Assert.Equal("E-001     ", detail[126..136]);        // 127-136 employee number
    }

    // The code this was ported from wrote the New IC into the Old IC slot too,
    // filing everyone as having an old-format IC. Deliberately not reproduced.
    [Fact]
    public void PcbTxt_LeavesTheOldIcColumnBlank()
    {
        var detail = Lines(PcbCp39Txt.Render(Payload()))[1];

        Assert.Equal(new string(' ', 12), detail[72..84]);
        Assert.NotEqual(detail[72..84], detail[84..96]);
    }

    [Fact]
    public void PcbTxt_PutsAForeignerInThePassportColumnAndLeavesTheIcBlank()
    {
        var payload = Payload([Row(nationality: "Nepalese", idNumber: "P-1234567")]);

        var detail = Lines(PcbCp39Txt.Render(payload))[1];

        Assert.Equal(new string(' ', 12), detail[84..96]);   // no New IC
        Assert.Equal("P1234567    ", detail[96..108]);       // passport, separators stripped
    }

    // A married woman assessed with her husband carries a non-zero wife code,
    // taken from the tax reference when it has one.
    [Fact]
    public void PcbTxt_ReadsTheWifeCodeOffTheTaxReference()
    {
        var payload = Payload([Row(
            incomeTaxNumber: "SG12345678903",
            gender: Gender.FEMALE,
            maritalStatus: MaritalStatus.MARRIED)]);

        var detail = Lines(PcbCp39Txt.Render(payload))[1];

        Assert.Equal("1234567890", detail[1..11]);
        Assert.Equal("3", detail[11..12]);
    }

    // CP38 is a court-ordered arrears instalment, filed separately from PCB.
    // A row with CP38 and no PCB still belongs in the file.
    [Fact]
    public void PcbTxt_IncludesACp38OnlyRowAndCountsItSeparately()
    {
        var payload = Payload([Row(payslip: Payslip(pcb: 0m, cp38: 300m))]);

        var lines = Lines(PcbCp39Txt.Render(payload));

        Assert.Equal(2, lines.Length);
        Assert.Equal("0000000000", lines[0][27..37]);   // no PCB
        Assert.Equal("00000", lines[0][37..42]);
        Assert.Equal("0000030000", lines[0][42..52]);   // CP38 total
        Assert.Equal("00001", lines[0][52..57]);        // CP38 count
        Assert.Equal("00030000", lines[1][118..126]);
    }

    [Fact]
    public void PcbTxt_OmitsEmployeesWithNothingWithheld()
    {
        var payload = Payload([Row(payslip: Payslip(pcb: 0m, cp38: 0m))]);

        var lines = Lines(PcbCp39Txt.Render(payload));

        Assert.Single(lines);   // header only
    }

    // A new joiner whose TIN has not been issued only blocks the file if tax
    // was actually withheld from them.
    [Fact]
    public void PcbTxt_IgnoresAMissingTaxNumberOnSomeoneWithNoDeduction()
    {
        var payload = Payload([
            Row(name: "Withheld"),
            Row(name: "New joiner", incomeTaxNumber: null, payslip: Payslip(pcb: 0m, name: "New joiner")),
        ]);

        var lines = Lines(PcbCp39Txt.Render(payload));

        Assert.Equal(2, lines.Length);
        Assert.Contains("Withheld", lines[1]);
    }

    [Fact]
    public void PcbTxt_RefusesAndNamesAnEmployeeMissingATaxNumber()
    {
        var payload = Payload([Row(name: "Aisyah", employeeCode: "E-001", incomeTaxNumber: null)]);

        var result = PcbCp39Txt.Render(payload);

        Assert.False(result.Ok);
        Assert.Contains("Aisyah", result.Error);
        Assert.Contains("income tax number", result.Error);
    }

    [Fact]
    public void PcbTxt_RefusesWithoutAnEmployerNumber()
    {
        var payload = Payload(companyInfo: new PayrollCompanyInfo { OrganizationId = "org-1" });

        var result = PcbCp39Txt.Render(payload);

        Assert.False(result.Ok);
        Assert.Contains("E-number", result.Error);
    }

    // ─── Readiness ──────────────────────────────────────────────────────

    [Fact]
    public void Readiness_IsOkWhenEveryRequiredFieldIsPresent()
    {
        Assert.True(PayrollRunReadiness.Check(Payload()).Ok);
    }

    [Fact]
    public void Readiness_NamesTheCompanyFieldsTheFilesWillNeed()
    {
        var result = PayrollRunReadiness.Check(
            Payload(companyInfo: new PayrollCompanyInfo { OrganizationId = "org-1" }));

        Assert.False(result.Ok);
        Assert.Equal(4, result.OrgIssues.Count);
        Assert.Contains("PERKESO employer code", result.OrgIssues);
        Assert.Contains("Company Info is missing", result.Describe());
    }

    [Fact]
    public void Readiness_AsksForAnIcFromLocalsAndAPassportFromForeigners()
    {
        var result = PayrollRunReadiness.Check(Payload([
            Row(name: "Local", idNumber: null),
            Row(name: "Foreigner", nationality: "Nepalese", idNumber: null),
        ]));

        Assert.Equal(["IC number"], result.EmployeeIssues[0].Missing);
        Assert.Equal(["Passport number"], result.EmployeeIssues[1].Missing);
    }

    // PCB computes fine without a TIN, and a new joiner waiting on one must
    // not hold up the whole month's payroll. The CP39 renderer catches it
    // later, and only for people who actually had tax withheld.
    [Fact]
    public void Readiness_DoesNotBlockOnAMissingIncomeTaxNumber()
    {
        Assert.True(PayrollRunReadiness.Check(
            Payload([Row(incomeTaxNumber: null)])).Ok);
    }

    [Fact]
    public void Readiness_CountsEveryMissingFieldAcrossOrgAndEmployees()
    {
        var result = PayrollRunReadiness.Check(Payload(
            [Row(idNumber: null, employeeCode: "")],
            companyInfo: new PayrollCompanyInfo { OrganizationId = "org-1" }));

        Assert.Equal(6, result.TotalMissingCount);   // 4 org + 2 for the employee
    }
}
