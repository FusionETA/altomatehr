using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Tests.Payroll;

// The three bank formats other than Public Bank's ECP.
//
// What is pinned here is what a bank's own parser would reject the file for:
// field COUNTS and WIDTHS (all three are positional — one missing field shifts
// every later value into the wrong column), the intra-bank vs interbank split
// that decides the fee, the amounts, and the refusals that stop a file going
// out wrong. Not the cosmetics.
public class BankFileFormatsTests
{
    // ─── Fixtures ───────────────────────────────────────────────────────

    private static Payslip Payslip(decimal net) => new()
    {
        OrganizationId = "org-1",
        PayrollRunId = "run-1",
        EmployeeProfileId = "emp-1",
        UserId = "usr-1",
        SnapshotName = "Aisyah Binti Rahman",
        SnapshotEmployeeNumber = "E-001",
        GrossPay = net,
        NetPay = net,
        ProratedPay = net,
    };

    private static StatutoryEmployeeRow Row(
        string name = "Aisyah Binti Rahman",
        string? bankName = "Maybank",
        string? accountNumber = "1234 5678 9012",
        decimal net = 3108.10m,
        string? idNumber = "900101-14-5567",
        IdType? idType = IdType.NRIC,
        string? accountHolderName = null,
        string nationality = "Malaysian") => new()
    {
        Payslip = Payslip(net),
        EmployeeName = name,
        EmployeeCode = "E-001",
        BankName = bankName,
        BankAccountNumber = accountNumber,
        BankAccountHolderName = accountHolderName,
        IdNumber = idNumber,
        IdType = idType,
        Nationality = nationality,
    };

    private static PayrollDocumentModel Model(IEnumerable<StatutoryEmployeeRow>? rows = null) => new()
    {
        Run = new PayrollRun
        {
            Id = "run-1",
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = 3,
            Status = PayrollRunStatus.SUBMITTED,
        },
        OrganizationName = "Globe Engineering Sdn Bhd",
        PeriodLabel = "March 2026",
        StatusLabel = "Submitted",
        IssueDate = new DateTime(2026, 3, 31),
        Rows = rows?.ToList() ?? [Row()],
    };

    private static readonly DateTime ValueDate = new(2026, 3, 31);

    private static string[] Lines(byte[] content) =>
        System.Text.Encoding.Latin1.GetString(content)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    // ─── The bank register's code columns ───────────────────────────────

    [Fact]
    public void EveryBankCarriesBothRoutingCodes()
    {
        Assert.All(MalaysianBanks.All, b =>
        {
            Assert.Equal(2, b.BnmCode.Length);
            Assert.All(b.BnmCode, c => Assert.True(char.IsAsciiDigit(c)));
            Assert.InRange(b.HlbCode.Length, 3, 4);
        });
    }

    // PABB is AFFIN, from its old name Perwira Affin Bank — not Public Bank,
    // which is PBBB. Confusing the two pays every Public Bank employee into
    // Affin accounts.
    [Fact]
    public void TheHlbCodesThatLookLikeEachOtherAreNotSwapped()
    {
        Assert.Equal("PABB", MalaysianBanks.Find("Affin Bank")!.HlbCode);
        Assert.Equal("PBBB", MalaysianBanks.Find("Public Bank")!.HlbCode);
    }

    // An Islamic subsidiary shares its parent's codes — that is how the IBG
    // scheme treats them, and it is what makes a Maybank Islamic employee come
    // out as an intra-bank book transfer rather than a chargeable IBG.
    [Theory]
    [InlineData("Maybank", "Maybank Islamic")]
    [InlineData("CIMB", "CIMB Islamic")]
    [InlineData("Public Bank", "Public Islamic")]
    [InlineData("Hong Leong", "Hong Leong Islamic")]
    public void AnIslamicArmSharesItsParentsCodes(string parent, string islamic)
    {
        var a = MalaysianBanks.Find(parent)!;
        var b = MalaysianBanks.Find(islamic)!;

        Assert.Equal(a.BnmCode, b.BnmCode);
        Assert.Equal(a.HlbCode, b.HlbCode);
        Assert.True(MalaysianBanks.IsSameInstitution(islamic, a));
    }

    // ─── Maybank M2E ────────────────────────────────────────────────────

    private static StatutoryFileResult Mbb(
        PayrollDocumentModel? model = null,
        string? payor = "514012345678",
        string? orgCode = "MBB0099") =>
        PayrollBankFileMbbTxt.Render(model ?? Model(), ValueDate, payor, orgCode);

    // The parser is positional and the spec numbers fillers 148-336 as real
    // fields: a record one field short puts the bene BIC in the wrong column.
    [Fact]
    public void Mbb_EmitsEveryFieldPositionIncludingTheBlankOnes()
    {
        var lines = Lines(Mbb().Content!);

        Assert.Equal(3, lines.Length);
        Assert.Equal(29, lines[0].Split('|').Length);
        Assert.Equal(337, lines[1].Split('|').Length);
        Assert.Equal(29, lines[2].Split('|').Length);
        Assert.StartsWith("00|", lines[0]);
        Assert.StartsWith("01|", lines[1]);
        Assert.StartsWith("99|", lines[2]);
    }

    // IT is a free book transfer and must leave the bene bank code blank; IG
    // goes over ACH and is rejected without the beneficiary's BIC.
    [Theory]
    [InlineData("Maybank", "IT", "")]
    [InlineData("Maybank Islamic", "IT", "")]
    [InlineData("Public Bank", "IG", "PBBEMYKL")]
    [InlineData("CIMB", "IG", "CIBBMYKL")]
    public void Mbb_RoutesIntraBankAsBookTransferAndEveryoneElseOverIbg(
        string bank, string mode, string beneBic)
    {
        var fields = Lines(Mbb(Model([Row(bankName: bank)])).Content![..])[1].Split('|');

        Assert.Equal(mode, fields[1]);
        Assert.Equal(beneBic, fields[36]);
    }

    [Fact]
    public void Mbb_WritesTheAmountAndTheTrailerTotalToTwoDecimals()
    {
        var model = Model([Row(net: 3108.10m), Row(name: "Bala", net: 1576.60m)]);
        var lines = Lines(Mbb(model).Content!);

        Assert.Equal("3108.10", lines[1].Split('|')[11]);
        Assert.Equal("1576.60", lines[2].Split('|')[11]);

        var trailer = lines[3].Split('|');
        Assert.Equal("2", trailer[1]);
        Assert.Equal("4684.70", trailer[2]);
    }

    // Maybank splits identification across four columns rather than PB's one
    // type+number pair — the number goes in the column matching its kind and
    // the others stay blank.
    [Theory]
    [InlineData(IdType.NRIC, "900101-14-5567", 24, "900101145567")]
    [InlineData(IdType.PASSPORT, "A12345678", 27, "A12345678")]
    [InlineData(IdType.ARMY_NO, "T1234567", 27, "T1234567")]
    public void Mbb_PutsAnIdInTheColumnMatchingItsKind(
        IdType idType, string idNumber, int index, string expected)
    {
        var fields = Lines(Mbb(Model([Row(idType: idType, idNumber: idNumber)])).Content!)[1]
            .Split('|');

        Assert.Equal(expected, fields[index]);
        // The other three ID columns stay empty.
        foreach (var other in new[] { 24, 25, 26, 27 }.Where(i => i != index))
        {
            Assert.Equal("", fields[other]);
        }
    }

    // A pipe inside a name would split the record into extra fields and shift
    // everything after it.
    [Fact]
    public void Mbb_StripsTheDelimiterOutOfAName()
    {
        var model = Model([Row(accountHolderName: "AISYAH | RAHMAN")]);
        var line = Lines(Mbb(model).Content!)[1];

        Assert.Equal(337, line.Split('|').Length);
        Assert.Equal("AISYAH RAHMAN", line.Split('|')[19]);
    }

    // The resident flag drives Maybank's own reporting, and a non-resident
    // marked resident is a filing error the company owns.
    [Theory]
    [InlineData("Malaysian", "Y")]
    [InlineData("Indonesian", "N")]
    public void Mbb_MarksResidencyFromTheProfile(string nationality, string expected)
    {
        var fields = Lines(Mbb(Model([Row(nationality: nationality)])).Content!)[1].Split('|');

        Assert.Equal(expected, fields[18]);
    }

    [Fact]
    public void Mbb_RefusesWithoutACorporateId()
    {
        var result = Mbb(orgCode: "  ");

        Assert.False(result.Ok);
        Assert.Contains("Corporate ID", result.Error!);
        Assert.Contains("Organisation code", result.Error!);
    }

    [Fact]
    public void Mbb_RefusesWithoutADebitingAccount()
    {
        var result = Mbb(payor: null);

        Assert.False(result.Ok);
        Assert.Contains("debiting account", result.Error!);
    }

    // ─── CIMB BizChannel (Excel template) ───────────────────────────────

    private static ClosedXML.Excel.IXLWorksheet CimbSheet(PayrollDocumentModel? model = null)
    {
        var result = PayrollBankFileCimbXlsx.Render(model ?? Model());
        Assert.True(result.Ok, result.Error);
        return new ClosedXML.Excel.XLWorkbook(new MemoryStream(result.Content!)).Worksheet(1);
    }

    // The customer's own template: row 1 the field rules, row 2 the headers,
    // payees from row 3.
    [Fact]
    public void Cimb_KeepsTheTemplatesTwoHeaderRows()
    {
        var sheet = CimbSheet();

        Assert.Equal("Length : 40 (Char without '-' or '/')", sheet.Cell(1, 1).GetString());
        Assert.Equal(
            ["*Beneficiary Name", "Beneficiary ID", "*BNM Code", "*Account Number", "*Payment Amount", "Reference Number"],
            Enumerable.Range(1, 6).Select(c => sheet.Cell(2, c).GetString()).ToArray());
    }

    // An NRIC holder and a passport holder go in the same way — Beneficiary
    // ID blank, as in the customer's working file. The .txt couldn't carry a
    // passport at all.
    [Fact]
    public void Cimb_PaysIcAndPassportHoldersAlike()
    {
        var sheet = CimbSheet(Model([
            Row(name: "Aisyah Binti Rahman", net: 1800m),
            Row(name: "Saw Zin Mu Htoo", idType: IdType.PASSPORT, idNumber: "MH441016", net: 1925.15m,
                bankName: "CIMB Bank", accountNumber: "7076-926751"),
        ]));

        Assert.Equal("AISYAH BINTI RAHMAN", sheet.Cell(3, 1).GetString().ToUpperInvariant());
        Assert.Equal("Saw Zin Mu Htoo", sheet.Cell(4, 1).GetString());
        Assert.True(sheet.Cell(3, 2).IsEmpty());
        Assert.True(sheet.Cell(4, 2).IsEmpty());
        Assert.Equal("35-CIMB Bank Berhad", sheet.Cell(4, 3).GetString());
        Assert.Equal("7076926751", sheet.Cell(4, 4).GetString());     // text, digits only
        Assert.Equal(1925.15m, (decimal)sheet.Cell(4, 5).GetDouble());
        Assert.True(sheet.Cell(5, 1).IsEmpty());
    }

    // The dropdown labels from CIMB's template where they differ from the
    // register, the code + register name otherwise.
    [Theory]
    [InlineData("CIMB Bank", "35-CIMB Bank Berhad")]
    [InlineData("Public Bank", "33-Public Bank Berhad/ Public Finance Berhad")]
    [InlineData("Maybank", "27-Malayan Banking Berhad")]
    public void Cimb_WritesTheBnmCodeAsTheTemplatesLabel(string bank, string expected)
    {
        Assert.Equal(expected, CimbSheet(Model([Row(bankName: bank)])).Cell(3, 3).GetString());
    }

    // Never cut an account to fit: a 17-digit number would become a different
    // account. Refused, naming the person.
    [Fact]
    public void Cimb_RefusesAnAccountLongerThanSixteenDigits()
    {
        var result = PayrollBankFileCimbXlsx.Render(Model([Row(name: "Bala", accountNumber: "12345678901234567")]));

        Assert.False(result.Ok);
        Assert.Contains("Bala (17 digits)", result.Error!);
    }

    // "Char without '-' or '/'" on the name and reference columns.
    [Fact]
    public void Cimb_DropsTheSeparatorsTheTemplateForbids()
    {
        var sheet = CimbSheet(Model([Row(accountHolderName: "AISYAH A/P RAHMAN-ALI")]));

        Assert.Equal("AISYAH A P RAHMAN ALI", sheet.Cell(3, 1).GetString());
        Assert.DoesNotContain("-", sheet.Cell(3, 6).GetString());
    }

    // ─── Hong Leong ─────────────────────────────────────────────────────

    // The reference is what the employee sees on their statement and nothing
    // in payroll data implies it, so a blank one is refused rather than filled
    // with something invented.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hlb_RefusesWithoutARecipientReference(string? reference)
    {
        var txt = PayrollBankFileHlb.RenderConnectFirstTxt(Model(), reference);
        var xlsx = PayrollBankFileHlb.RenderConnectBizXlsx(Model(), reference);

        Assert.False(txt.Ok);
        Assert.False(xlsx.Ok);
        Assert.Contains("recipient reference", txt.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HlbConnectFirst_EmitsFixedWidthRecordsWithNoHeaderOrTrailer()
    {
        var model = Model([Row(), Row(name: "Bala")]);
        var lines = Lines(
            PayrollBankFileHlb.RenderConnectFirstTxt(model, "SALARY MAR 2026").Content!);

        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.Equal(204, l.Length));
    }

    [Theory]
    [InlineData("Hong Leong", "FT ", "HLBB")]
    [InlineData("Hong Leong Islamic", "FT ", "HLBB")]
    [InlineData("Maybank", "IBG", "MBBB")]
    [InlineData("Public Bank", "IBG", "PBBB")]
    public void HlbConnectFirst_RoutesIntraBankAsFtAndEveryoneElseAsIbg(
        string bank, string mode, string code)
    {
        var line = Lines(PayrollBankFileHlb
            .RenderConnectFirstTxt(Model([Row(bankName: bank)]), "SALARY").Content!)[0];

        Assert.Equal(mode, line[..3]);
        Assert.Equal(code, line[3..11].TrimEnd());
    }

    [Fact]
    public void HlbConnectFirst_WritesTheFieldsAtTheirDocumentedOffsets()
    {
        var line = Lines(PayrollBankFileHlb
            .RenderConnectFirstTxt(Model(), "SALARY MAR 2026").Content!)[0];

        Assert.Equal("123456789012", line[11..31].TrimEnd());
        Assert.Equal("Aisyah Binti Rahman", line[31..131].TrimEnd());
        Assert.Equal("00000310810", line[131..142]);
        Assert.Equal("SALARY MAR 2026", line[142..162].TrimEnd());
        Assert.Equal("NI", line[182..184]);
        Assert.Equal("900101-14-5567", line[184..204].TrimEnd());
    }

    // The bank rejects the whole file over the ceiling, so one over-limit
    // salary must not take the other payments down with it silently.
    [Fact]
    public void Hlb_RefusesAnIbgPaymentOverTheMillionRinggitCeiling()
    {
        var model = Model([Row(), Row(name: "Bala", bankName: "CIMB", net: 1_000_000.01m)]);

        var result = PayrollBankFileHlb.RenderConnectFirstTxt(model, "SALARY");

        Assert.False(result.Ok);
        Assert.Contains("Bala", result.Error!);
        Assert.Contains("RENTAS", result.Error!);
    }

    // FT is a book transfer, not an IBG, so the ceiling does not apply to it.
    [Fact]
    public void Hlb_LetsAnIntraBankTransferExceedTheIbgCeiling()
    {
        var model = Model([Row(bankName: "Hong Leong", net: 1_500_000m)]);

        var result = PayrollBankFileHlb.RenderConnectFirstTxt(model, "SALARY");

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void HlbConnectBiz_ReproducesTheCbizTemplateColumns()
    {
        var result = PayrollBankFileHlb.RenderConnectBizXlsx(Model(), "SALARY MAR 2026");
        Assert.True(result.Ok, result.Error);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content!));
        var sheet = workbook.Worksheet(1);

        Assert.Equal("*Payment Mode", sheet.Cell(1, 1).GetString());
        Assert.Equal("*Beneficiary Bank Code", sheet.Cell(1, 2).GetString());
        Assert.Equal("*Amount (RM)", sheet.Cell(1, 5).GetString());
        Assert.Equal("*Recipient Reference", sheet.Cell(1, 6).GetString());
        Assert.Equal("Beneficiary E-mail Address", sheet.Cell(1, 10).GetString());

        Assert.Equal("IBG", sheet.Cell(2, 1).GetString());
        Assert.Equal("MBBB", sheet.Cell(2, 2).GetString());
        // Text, or Excel renders a long account number in scientific notation.
        Assert.Equal("123456789012", sheet.Cell(2, 3).GetString());
        // Numeric — the template's Amount column is a number, and a string
        // uploads as an invalid amount. (The opposite of PB ECP.)
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 5).DataType);
        Assert.Equal(3108.10m, sheet.Cell(2, 5).GetValue<decimal>());
        // The template's own dropdown label, not a compact code.
        Assert.Equal("New IC No.", sheet.Cell(2, 8).GetString());
    }

    // ─── Shared across the formats ──────────────────────────────────────

    // ─── OCBC Velocity GIRO ─────────────────────────────────────────────

    private static StatutoryFileResult Ocbc(
        PayrollDocumentModel? model = null, string? tapeId = "1", bool idCheck = false,
        string? reference = null, string? account = "701-123425-6") =>
        PayrollBankFileOcbc.RenderGiroTxt(model ?? Model(), ValueDate, account, tapeId, idCheck, reference);

    // 1-based, inclusive positions, as the spec writes them.
    private static string At(string line, int from, int to) => line[(from - 1)..to];

    // Every bank the register can resolve must be payable from OCBC.
    [Fact]
    public void Ocbc_HasARoutingIdForEveryBankInTheRegister()
    {
        var missing = MalaysianBanks.All.Where(b => PayrollBankFileOcbc.RoutingIdFor(b) is null).Select(b => b.Name);
        Assert.Empty(missing);
        Assert.All(MalaysianBanks.All, b => Assert.Matches("^[0-9]{9}$", PayrollBankFileOcbc.RoutingIdFor(b)!));
    }

    [Fact]
    public void Ocbc_EveryRecordIsExactly480Characters()
    {
        var lines = Lines(Ocbc(Model([Row(), Row(name: "Bala", bankName: "CIMB")])).Content!);

        Assert.Equal(4, lines.Length);   // header, two details, trailer
        Assert.All(lines, l => Assert.Equal(480, l.Length));
        Assert.Equal(["01", "02", "02", "03"], lines.Select(l => l[..2]));
    }

    // Laid out like OCBC's own sample: "0100200701A999999 … 00000000007011234256DN18102015".
    [Fact]
    public void Ocbc_HeaderFieldsSitAtTheirDocumentedPositions()
    {
        var header = Lines(Ocbc(tapeId: "2").Content!)[0];

        Assert.Equal("002", At(header, 3, 5));                       // tape id
        Assert.Equal("00701", At(header, 6, 10));                    // "00" + first 3 of the account
        Assert.Equal("A999999".PadRight(20), At(header, 11, 30));    // company CIF
        Assert.Equal("Globe Engineering Sdn Bhd".PadRight(30), At(header, 31, 60));
        Assert.Equal("00000000007011234256", At(header, 61, 80));    // debiting account, zero-padded
        Assert.Equal("DN", At(header, 81, 82));
        Assert.Equal("31032026", At(header, 83, 90));                // DDMMYYYY
        Assert.Equal("SALARY MAR 2026".PadRight(16), At(header, 131, 146));
    }

    [Fact]
    public void Ocbc_DetailFieldsSitAtTheirDocumentedPositions()
    {
        var detail = Lines(Ocbc(reference: "MARCH PAY").Content!)[1];

        Assert.Equal("123456789012".PadRight(20), At(detail, 3, 22));   // digits only
        Assert.Equal("00000000000310810", At(detail, 23, 39));          // RM 3,108.10 in sen
        Assert.Equal("C", At(detail, 40, 40));
        Assert.Equal("900101145567", At(detail, 41, 52));               // new IC, digits
        Assert.Equal("MARCH PAY".PadRight(20), At(detail, 61, 80));     // the statement reference
        Assert.Equal("100002270", At(detail, 121, 129));                // Maybank's routing ID
        Assert.Equal("Aisyah Binti Rahman".PadRight(22), At(detail, 130, 151));
        Assert.Equal("N", At(detail, 247, 247));
        Assert.Equal(new string(' ', 233), At(detail, 248, 480));       // payment type + filler
    }

    [Fact]
    public void Ocbc_TrailerCountsAndTotalsTheDetails()
    {
        var model = Model([Row(net: 1000.50m), Row(name: "Bala", bankName: "RHB", net: 2000.25m)]);

        var trailer = Lines(Ocbc(model).Content!)[^1];

        Assert.Equal("000002", At(trailer, 3, 8));
        Assert.Equal("0000000000000300075", At(trailer, 9, 27));
        Assert.Equal(new string(' ', 453), At(trailer, 28, 480));       // hash total unused on Velocity
    }

    // A passport holder's number goes in the Police/Army/Passport field, not New IC.
    [Fact]
    public void Ocbc_PutsAPassportInItsOwnField()
    {
        var detail = Lines(Ocbc(Model([Row(idType: IdType.PASSPORT, idNumber: "A1234567")])).Content!)[1];

        Assert.Equal(new string(' ', 12), At(detail, 41, 52));
        Assert.Equal("A1234567".PadRight(20), At(detail, 152, 171));
    }

    [Fact]
    public void Ocbc_IdCheckIsOffUnlessTicked_AndThenNeedsAnId()
    {
        Assert.Equal("Y", At(Lines(Ocbc(idCheck: true).Content!)[1], 247, 247));

        var result = Ocbc(Model([Row(), Row(name: "Bala", idNumber: null)]), idCheck: true);
        Assert.False(result.Ok);
        Assert.Contains("Bala", result.Error!);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1000")]
    [InlineData("abc")]
    public void Ocbc_RefusesATapeIdOutside001To999(string tapeId)
    {
        Assert.False(Ocbc(tapeId: tapeId).Ok);
    }

    [Fact]
    public void Ocbc_RefusesWithoutADebitingAccount()
    {
        var result = Ocbc(account: null);
        Assert.False(result.Ok);
        Assert.Contains("debiting account", result.Error!);
    }

    // The field is 20 wide but the spec keeps its last 3 as filler.
    [Fact]
    public void Ocbc_RefusesAnAccountLongerThanSeventeenDigits()
    {
        var result = Ocbc(Model([Row(accountNumber: "123456789012345678")]));
        Assert.False(result.Ok);
        Assert.Contains("17 digits", result.Error!);
    }

    // The run's name is added once, by StatutoryFileService — not here too.
    [Fact]
    public void Ocbc_FileNameCarriesTheTapeIdButNotTheRunName()
    {
        var model = Model();
        model.Run.Name = "Workers";

        Assert.Equal("OCBC_GIRO_Payroll_032026_T002.txt", Ocbc(model, tapeId: "2").FileName);
    }

    // "Only characters visible in standard English keyboard."
    [Fact]
    public void Ocbc_KeepsNamesToKeyboardCharacters()
    {
        var detail = Lines(Ocbc(Model([Row(name: "José Müller")])).Content!)[1];
        Assert.Equal("Jose Muller".PadRight(22), At(detail, 130, 151));
    }

    // A row skipped for zero pay is correct; a row skipped because the bank
    // name didn't resolve is someone who doesn't get paid.
    public static TheoryData<string> Renderers => ["mbb", "cimb", "hlb-txt", "hlb-xlsx", "ocbc"];

    private static StatutoryFileResult RenderBy(string which, PayrollDocumentModel model) =>
        which switch
        {
            "mbb" => PayrollBankFileMbbTxt.Render(model, ValueDate, "514012345678", "MBB0099"),
            "cimb" => PayrollBankFileCimbXlsx.Render(model),
            "hlb-txt" => PayrollBankFileHlb.RenderConnectFirstTxt(model, "SALARY"),
            "ocbc" => PayrollBankFileOcbc.RenderGiroTxt(model, ValueDate, "7011234256", "1", false, null),
            _ => PayrollBankFileHlb.RenderConnectBizXlsx(model, "SALARY"),
        };

    // "N/A" as an account would go out as a blank required cell, and the
    // person would be on neither the bank file nor Manual payments.
    [Theory]
    [MemberData(nameof(Renderers))]
    public void EveryFormatRefusesAnAccountWithNoDigits(string which)
    {
        var result = RenderBy(which, Model([Row(), Row(name: "Bala", accountNumber: "N/A")]));

        Assert.False(result.Ok);
        Assert.Contains("Bala (\"N/A\")", result.Error!);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void EveryFormatRefusesRatherThanOmittingAnUnmatchedBank(string which)
    {
        var model = Model([Row(), Row(name: "Bala", bankName: "Some Credit Union")]);

        var result = RenderBy(which, model);

        Assert.False(result.Ok);
        Assert.Contains("Bala", result.Error!);
        Assert.Contains("Some Credit Union", result.Error!);
    }

    // Zero net pay and no account are real skips — there is nothing to pay, or
    // nowhere to pay it. A file of only those is refused, not emitted empty.
    [Theory]
    [MemberData(nameof(Renderers))]
    public void EveryFormatRefusesAFileWithNothingToDisburse(string which)
    {
        var model = Model([Row(net: 0m), Row(name: "Bala", accountNumber: "  ")]);

        var result = RenderBy(which, model);

        Assert.False(result.Ok);
        Assert.Contains("Nothing to put in the bank file", result.Error!);
    }

    // Other bank / e-wallet, Cash and Cheque are paid by hand: left out of the
    // bank file, and an overseas bank or Merchantrade no longer blocks it.
    [Theory]
    [MemberData(nameof(Renderers))]
    public void EveryFormatLeavesManualPaymentsOutAndStillPaysTheRest(string which)
    {
        var model = Model([
            Row(name: "Bank Person", net: 2000m),
            Row(name: "Wallet Person", bankName: "Merchantrade", net: 1500m) with
            {
                PaymentMethod = AltomateHR.Api.Modules.Employees.Entities.PaymentMethod.OTHER_TRANSFER,
            },
            Row(name: "Cash Person", net: 1200m) with
            {
                PaymentMethod = AltomateHR.Api.Modules.Employees.Entities.PaymentMethod.CASH,
            },
        ]);

        var result = RenderBy(which, model);

        Assert.True(result.Ok, result.Error);
    }

    // A bank-transfer employee whose bank isn't Malaysian is still a refusal —
    // a typo must not quietly skip someone — and the message points at the fix.
    [Fact]
    public void An_unrecognised_bank_on_a_bank_transfer_still_refuses_and_says_why()
    {
        var model = Model([Row(name: "Typo Person", bankName: "Union Bank of the Philippines")]);

        var result = RenderBy("mbb", model);

        Assert.False(result.Ok);
        Assert.Contains("Other bank / e-wallet", result.Error!);
    }

    // The manual list is everyone owed pay who isn't in the bank file — so the
    // two together account for every ringgit, and a missing account is flagged.
    [Fact]
    public void Manual_payments_are_everyone_the_bank_file_does_not_pay()
    {
        var model = Model([
            Row(name: "Bank Person", net: 2000m),
            Row(name: "No Account", accountNumber: null, net: 900m),
            Row(name: "Wallet Person", bankName: "Merchantrade", net: 1500m) with
            {
                PaymentMethod = AltomateHR.Api.Modules.Employees.Entities.PaymentMethod.OTHER_TRANSFER,
            },
            Row(name: "Zero Person", net: 0m) with
            {
                PaymentMethod = AltomateHR.Api.Modules.Employees.Entities.PaymentMethod.CASH,
            },
        ]);

        var manual = PayrollPayments.Manual(model);

        Assert.Equal(["No Account", "Wallet Person"], manual.Select(m => m.Row.EmployeeName).OrderBy(n => n).ToArray());
        Assert.NotNull(manual.Single(m => m.Row.EmployeeName == "No Account").Issue);
        Assert.Equal(
            model.Rows.Sum(r => r.Payslip.NetPay),
            model.Rows.Where(PayrollPayments.InBankFile).Sum(r => r.Payslip.NetPay) + manual.Sum(m => m.Row.Payslip.NetPay));

        Assert.NotEmpty(ManualPaymentsXlsx.Render(model));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void EveryFormatSkipsAnUnpayableRowButStillPaysTheRest(string which)
    {
        var model = Model([Row(net: 0m), Row(name: "Bala", net: 2000m)]);

        var result = RenderBy(which, model);

        Assert.True(result.Ok, result.Error);
    }
}
