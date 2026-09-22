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

    // ─── CIMB BizChannel ────────────────────────────────────────────────

    private static StatutoryFileResult Cimb(
        PayrollDocumentModel? model = null, string? orgCode = "12345") =>
        PayrollBankFileCimbTxt.Render(model ?? Model(), ValueDate, orgCode);

    // Fixed-width: every record is exactly its stated length or the columns
    // after the first short one are read from the wrong offsets.
    [Fact]
    public void Cimb_EmitsExactlyTheStatedRecordWidths()
    {
        var lines = Lines(Cimb().Content!);

        Assert.Equal(3, lines.Length);
        Assert.Equal(73, lines[0].Length);
        Assert.Equal(127, lines[1].Length);
        Assert.Equal(21, lines[2].Length);
    }

    [Fact]
    public void Cimb_WritesTheHeaderFieldsAtTheirDocumentedOffsets()
    {
        var header = Lines(Cimb().Content!)[0];

        Assert.Equal("01", header[..2]);
        Assert.Equal("12345", header[2..7]);
        Assert.Equal("Globe Engineering Sdn Bhd", header[7..47].TrimEnd());
        Assert.Equal("31032026", header[47..55]);
        Assert.Equal(new string('0', 16), header[55..71]);
    }

    // Routed by BNM code, not BIC — a wrong two digits sends the money to a
    // different institution entirely.
    [Theory]
    [InlineData("Maybank", "27")]
    [InlineData("CIMB", "35")]
    [InlineData("Public Bank", "33")]
    [InlineData("Hong Leong", "24")]
    public void Cimb_RoutesByBnmCode(string bank, string expected)
    {
        var detail = Lines(Cimb(Model([Row(bankName: bank)])).Content!)[1];

        Assert.Equal(expected, detail[2..4]);
    }

    // Sen, zero-padded, no decimal point. 3108.10 must not arrive as 310810
    // in the wrong width or as 3108.1.
    [Fact]
    public void Cimb_WritesAmountsInSenAndTotalsThemInTheTrailer()
    {
        var model = Model([Row(net: 3108.10m), Row(name: "Bala", net: 1576.60m)]);
        var lines = Lines(Cimb(model).Content!);

        Assert.Equal("00000310810", lines[1][65..76]);
        Assert.Equal("00000157660", lines[2][65..76]);

        Assert.Equal("000002", lines[3][2..8]);
        Assert.Equal("0000000468470", lines[3][8..21]);
    }

    // The template types these three columns as "Char without '-' or '/'", so
    // a hyphenated IC must not pass through as typed.
    [Fact]
    public void Cimb_DropsTheSeparatorsTheTemplateForbids()
    {
        var detail = Lines(Cimb(Model([Row(accountHolderName: "AISYAH A/P RAHMAN")])).Content!)[1];

        Assert.Equal("AISYAH A P RAHMAN", detail[25..65].TrimEnd());
        Assert.Equal("900101145567", detail[106..126].TrimEnd());
    }

    [Theory]
    [InlineData(null, "Organisation Code")]
    [InlineData("123456", "at most 5 digits")]
    public void Cimb_RefusesAnOrganisationCodeItCannotWrite(string? orgCode, string expected)
    {
        var result = Cimb(orgCode: orgCode);

        Assert.False(result.Ok);
        Assert.Contains(expected, result.Error!);
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

    // A row skipped for zero pay is correct; a row skipped because the bank
    // name didn't resolve is someone who doesn't get paid.
    public static TheoryData<string> Renderers => ["mbb", "cimb", "hlb-txt", "hlb-xlsx"];

    private static StatutoryFileResult RenderBy(string which, PayrollDocumentModel model) =>
        which switch
        {
            "mbb" => PayrollBankFileMbbTxt.Render(model, ValueDate, "514012345678", "MBB0099"),
            "cimb" => PayrollBankFileCimbTxt.Render(model, ValueDate, "12345"),
            "hlb-txt" => PayrollBankFileHlb.RenderConnectFirstTxt(model, "SALARY"),
            _ => PayrollBankFileHlb.RenderConnectBizXlsx(model, "SALARY"),
        };

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
        Assert.Contains("Nothing to disburse", result.Error!);
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
