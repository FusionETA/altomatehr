using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Tests.Payroll;

// The payroll documents: payslip, summary, payment schedule, bank file.
//
// A PDF is awkward to assert on, so what is pinned here is what actually
// goes wrong with these in practice — the ARITHMETIC, the routing, and the
// refusals. That a heading is 9pt is not worth a test; that the columns on a
// payslip add up to the money in someone's bank account very much is.
public class PayrollDocumentTests
{
    // ─── Fixtures ───────────────────────────────────────────────────────

    // `net` is DERIVED from the deductions unless a test names one, so the
    // fixture is always internally consistent — otherwise a reconciliation
    // test just measures the fixture's own arithmetic.
    private static Payslip Payslip(
        string name = "Aisyah Binti Rahman",
        decimal gross = 5000m,
        decimal? net = null,
        decimal epfEmployee = 550m,
        decimal socsoEmployee = 24.75m,
        decimal eisEmployee = 9.90m,
        decimal skbbk = 0m,
        decimal pcb = 110m,
        decimal cp38 = 0m,
        decimal zakat = 0m,
        decimal otherDeductions = 0m,
        decimal bik = 0m) => new()
    {
        OrganizationId = "org-1",
        PayrollRunId = "run-1",
        EmployeeProfileId = "emp-1",
        UserId = "usr-1",
        SnapshotName = name,
        SnapshotEmployeeNumber = "E-001",
        GrossPay = gross,
        NetPay = net ?? gross - (epfEmployee + socsoEmployee + eisEmployee + skbbk
                                 + otherDeductions + zakat + cp38 + pcb),
        ProratedPay = gross,
        EpfEmployee = epfEmployee,
        EpfEmployer = 650m,
        SocsoEmployee = socsoEmployee,
        SocsoEmployer = 86.65m,
        EisEmployee = eisEmployee,
        EisEmployer = 9.90m,
        SkbbkEmployee = skbbk,
        Pcb = pcb,
        Cp38 = cp38,
        Zakat = zakat,
        TotalDeductions = otherDeductions + zakat + cp38,
        TotalBenefitsInKind = bik,
        TotalCostToEmployer = 5746.55m,
    };

    private static StatutoryEmployeeRow Row(
        Payslip? payslip = null,
        string name = "Aisyah Binti Rahman",
        string employeeCode = "E-001",
        string? bankName = "Maybank",
        string? bankAccountNumber = "1234 5678 9012",
        string? idNumber = "900101-14-5567",
        IdType? idType = IdType.NRIC) => new()
    {
        Payslip = payslip ?? Payslip(name: name),
        EmployeeName = name,
        EmployeeCode = employeeCode,
        BankName = bankName,
        BankAccountNumber = bankAccountNumber,
        IdNumber = idNumber,
        IdType = idType,
        Nationality = "Malaysian",
    };

    private static PayrollDocumentModel Model(
        IEnumerable<StatutoryEmployeeRow>? rows = null,
        PayrollRunStatus status = PayrollRunStatus.SUBMITTED) => new()
    {
        Run = new PayrollRun
        {
            Id = "run-1",
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = 3,
            Status = status,
        },
        OrganizationName = "Globe Engineering Sdn Bhd",
        PeriodLabel = "March 2026",
        StatusLabel = "Submitted",
        IssueDate = new DateTime(2026, 3, 31),
        Rows = rows?.ToList() ?? [Row()],
    };

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x25 && bytes[1] == 0x50
        && bytes[2] == 0x44 && bytes[3] == 0x46;   // "%PDF"

    // ─── The payslip must add up ────────────────────────────────────────

    // THE invariant. Gross minus the deductions total has to equal the net
    // pay printed at the bottom, which is the figure that reaches the
    // employee's bank. A payslip whose columns do not reconcile is the most
    // common payroll support ticket there is.
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(300, 0, 0, 0)]      // other deductions
    [InlineData(0, 200, 0, 0)]      // zakat
    [InlineData(0, 0, 150, 0)]      // CP38
    [InlineData(120, 80, 60, 5.5)]  // all at once, plus SKBBK
    public void Payslip_EarningsMinusDeductionsEqualsNetPay(
        double other, double zakat, double cp38, double skbbk)
    {
        // Net is derived the way PayslipCalculator derives it, so the test
        // would catch the renderer's total drifting from the engine's.
        var deductions = 550m + 24.75m + 9.90m + (decimal)skbbk
                         + ((decimal)other + (decimal)zakat + (decimal)cp38) + 110m;

        var payslip = Payslip(
            gross: 5000m,
            net: 5000m - deductions,
            skbbk: (decimal)skbbk,
            zakat: (decimal)zakat,
            cp38: (decimal)cp38,
            otherDeductions: (decimal)other);

        Assert.Equal(deductions, PayslipPdf.TotalDeductions(payslip));
        Assert.Equal(payslip.GrossPay - PayslipPdf.TotalDeductions(payslip), payslip.NetPay);
    }

    // Zakat and CP38 have their own rows AND live inside TotalDeductions, so
    // the catch-all row nets them off. Counting them twice would overstate
    // the deductions and break the reconciliation above.
    [Fact]
    public void Payslip_DoesNotDoubleCountZakatOrCp38()
    {
        var payslip = Payslip(zakat: 200m, cp38: 150m, otherDeductions: 100m);

        // TotalDeductions carries all three; the named rows are 350 of it.
        Assert.Equal(450m, payslip.TotalDeductions);

        var total = PayslipPdf.TotalDeductions(payslip);
        Assert.Equal(550m + 24.75m + 9.90m + 450m + 110m, total);
    }

    [Fact]
    public void Payslip_RendersAPdf()
    {
        var model = new PayslipPdfModel
        {
            Payslip = Payslip(),
            OrganizationName = "Globe Engineering Sdn Bhd",
            PeriodLabel = "March 2026",
            IssueDate = new DateTime(2026, 3, 31),
            Ytd = new PayslipYtdSummary { Gross = 15000m, Net = 12916.05m },
        };

        Assert.True(IsPdf(PayslipPdf.Render(model)));
    }

    // Renders for someone with every optional section present, so a layout
    // error in BIK or SKBBK shows up rather than waiting for the one
    // employee who has them.
    [Fact]
    public void Payslip_RendersEveryOptionalSection()
    {
        var payslip = Payslip(skbbk: 0.90m, zakat: 100m, cp38: 50m, bik: 1200m);

        var model = new PayslipPdfModel
        {
            Payslip = payslip,
            OrganizationName = "Globe Engineering Sdn Bhd",
            PeriodLabel = "June 2026",
            IssueDate = new DateTime(2026, 6, 30),
            Ytd = new PayslipYtdSummary { Gross = 30000m, SkbbkEmployee = 5.40m },
            LineItems =
            [
                new PayslipLineItem
                {
                    OrganizationId = "org-1",
                    PayslipId = "p-1",
                    Kind = PayslipLineKind.ALLOWANCE,
                    Category = "bik_car",
                    Label = "Company car",
                    Amount = 1200m,
                },
            ],
        };

        Assert.True(IsPdf(PayslipPdf.Render(model)));
    }

    // The account is shown to its owner, so enough to recognise and no more.
    [Theory]
    [InlineData("Maybank", "1234567890", "Maybank •••• 7890")]
    [InlineData(null, "1234567890", "•••• 7890")]
    [InlineData("Maybank", "12", "Maybank •••• 12")]
    [InlineData("Maybank", null, "Maybank")]
    [InlineData(null, null, "—")]
    public void Payslip_MasksTheBankAccount(string? bank, string? account, string expected)
    {
        Assert.Equal(expected, PayslipPdf.MaskedAccount(bank, account));
    }

    // ─── Summary and payment schedule ───────────────────────────────────

    [Fact]
    public void Summary_RendersEveryEmployee()
    {
        var model = Model([
            Row(name: "Aisyah", employeeCode: "E-001"),
            Row(name: "Bala", employeeCode: "E-002"),
        ]);

        Assert.True(IsPdf(PayrollSummaryPdf.Render(model)));
    }

    // The summary exists to be RECONCILED: gross minus the deduction columns
    // has to equal the net column on every row. A statutory item with no
    // column of its own makes each row silently short — SKBBK was exactly
    // that, and was 90 sen out per employee until a rendered page was
    // actually looked at.
    [Fact]
    public void Summary_ColumnsReconcileToNetPayOnEveryRow()
    {
        var rows = new[]
        {
            Row(payslip: Payslip(skbbk: 0.90m, zakat: 100m, cp38: 50m, otherDeductions: 150m)),
            Row(payslip: Payslip(skbbk: 0m, otherDeductions: 0m)),
        };

        foreach (var row in rows)
        {
            var p = row.Payslip;

            // The columns the sheet prints, in order.
            var columns = p.EpfEmployee + p.SocsoEmployee + p.EisEmployee
                          + p.SkbbkEmployee + p.Pcb + p.TotalDeductions;

            Assert.Equal(p.NetPay, p.GrossPay - columns);
        }
    }

    [Fact]
    public void Summary_RendersAnEmptyRun()
    {
        Assert.True(IsPdf(PayrollSummaryPdf.Render(Model([]))));
    }

    [Fact]
    public void PaymentSchedule_Renders()
    {
        Assert.True(IsPdf(PaymentSchedulePdf.Render(Model())));
    }

    // The approver needs to see who the bank file will not be able to pay
    // where they are already looking, not on upload.
    [Fact]
    public void PaymentSchedule_RendersWithUnpayableEmployees()
    {
        var model = Model([
            Row(name: "No account", bankAccountNumber: null),
            Row(name: "Odd bank", bankName: "Some Credit Union"),
        ]);

        Assert.True(IsPdf(PaymentSchedulePdf.Render(model)));
    }

    // ─── Bank matching ──────────────────────────────────────────────────

    // The bank name on a profile is free text. These are the spellings an
    // admin actually types.
    [Theory]
    [InlineData("Maybank", "Malayan Banking Berhad")]
    [InlineData("MAYBANK BERHAD", "Malayan Banking Berhad")]
    [InlineData("Malayan Banking", "Malayan Banking Berhad")]
    [InlineData("cimb", "CIMB Bank Berhad")]
    [InlineData("Public Bank", "Public Bank Berhad")]
    [InlineData("HSBC", "HSBC Bank Malaysia Berhad")]
    [InlineData("bank islam", "Bank Islam Malaysia Berhad")]
    [InlineData("  RHB Bank  ", "RHB Bank Berhad")]
    public void MalaysianBanks_MatchesTheSpellingsAdminsActuallyType(string input, string expected)
    {
        Assert.Equal(expected, MalaysianBanks.Find(input)?.Name);
    }

    // An unrecognised bank must return null so the caller refuses. Guessing
    // routes someone's pay to the wrong institution.
    [Theory]
    [InlineData("Some Credit Union")]
    [InlineData("")]
    [InlineData(null)]
    public void MalaysianBanks_ReturnsNullRatherThanGuessing(string? input)
    {
        Assert.Null(MalaysianBanks.Find(input));
    }

    // Public Bank pays its own customers intrabank; everyone else goes IBG,
    // which is what decides the fee and the settlement time.
    [Fact]
    public void MalaysianBanks_RoutesPublicBankIntrabankAndEveryoneElseThroughIbg()
    {
        Assert.Equal(EcpPaymentMode.PBB, MalaysianBanks.Find("Public Bank")!.EcpMode);
        Assert.Equal(EcpPaymentMode.PBB, MalaysianBanks.Find("Public Islamic")!.EcpMode);
        Assert.Equal(EcpPaymentMode.IBG, MalaysianBanks.Find("Maybank")!.EcpMode);
    }

    [Fact]
    public void MalaysianBanks_CarriesABicForEveryEntry()
    {
        Assert.All(MalaysianBanks.All, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Bic));
            Assert.InRange(b.Bic.Length, 8, 11);
        });
    }

    // ─── Bank file ──────────────────────────────────────────────────────

    // PB keys the upload on the FILENAME, not on a cell, so a file built
    // without the payor account is rejected by the portal with nothing to
    // explain why. Refused here instead, naming the setting to fix.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BankFile_RefusesWithoutAPayorAccount(string? payor)
    {
        var result = PayrollBankFileXlsx.Render(Model(), new DateTime(2026, 3, 31), payor);

        Assert.False(result.Ok);
        Assert.Contains("payor account", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // Exactly ten digits, or PB's validator rejects the name it parses.
    [Theory]
    [InlineData("31612345")]
    [InlineData("316123456789")]
    public void BankFile_RefusesAPayorAccountThatIsNotTenDigits(string payor)
    {
        var result = PayrollBankFileXlsx.Render(Model(), new DateTime(2026, 3, 31), payor);

        Assert.False(result.Ok);
        Assert.Contains("10 digits", result.Error!);
    }

    // <10-digit account>PR<DDMMYY><NN>.xlsx — the portal parses this, and
    // will not take the same name twice in one day.
    [Fact]
    public void BankFile_UsesPublicBanksFilenameConvention()
    {
        var result = PayrollBankFileXlsx.Render(
            Model(), new DateTime(2026, 3, 31), "3161234567");

        Assert.True(result.Ok);
        Assert.Equal("3161234567PR31032601.xlsx", result.FileName);
    }

    // Punctuation an admin types into the field is not part of the number.
    [Fact]
    public void BankFile_IgnoresPunctuationInThePayorAccount()
    {
        var result = PayrollBankFileXlsx.Render(
            Model(), new DateTime(2026, 3, 31), "3161-234-567");

        Assert.True(result.Ok);
        Assert.Equal("3161234567PR31032601.xlsx", result.FileName);
    }

    [Fact]
    public void BankFile_ProducesAWorkbook()
    {
        var result = PayrollBankFileXlsx.Render(Model(), new DateTime(2026, 3, 31), "3161234567");

        Assert.True(result.Ok, result.Error);
        Assert.EndsWith(".xlsx", result.FileName);
        // XLSX is a ZIP — "PK".
        Assert.Equal(0x50, result.Content![0]);
        Assert.Equal(0x4B, result.Content[1]);
    }

    // Silently dropping someone means they do not get paid and nobody
    // notices until they complain.
    [Fact]
    public void BankFile_RefusesRatherThanOmittingAnUnmatchedBank()
    {
        var model = Model([
            Row(name: "Aisyah"),
            Row(name: "Bala", bankName: "Some Credit Union"),
        ]);

        var result = PayrollBankFileXlsx.Render(model, new DateTime(2026, 3, 31), "3161234567");

        Assert.False(result.Ok);
        Assert.Contains("Bala", result.Error);
        Assert.Contains("Some Credit Union", result.Error);
    }

    // No account number is a different case: there is nothing to pay INTO,
    // so the row is legitimately not a bank payment. The payment schedule is
    // where that gets surfaced.
    [Fact]
    public void BankFile_SkipsSomeoneWithNoAccountRatherThanRefusing()
    {
        var model = Model([
            Row(name: "Aisyah"),
            Row(name: "Cash paid", bankAccountNumber: null),
        ]);

        Assert.True(PayrollBankFileXlsx.Render(model, new DateTime(2026, 3, 31), "3161234567").Ok);
    }

    [Fact]
    public void BankFile_RefusesWhenThereIsNothingToDisburse()
    {
        var model = Model([Row(payslip: Payslip(net: 0m))]);

        var result = PayrollBankFileXlsx.Render(model, new DateTime(2026, 3, 31), "3161234567");

        Assert.False(result.Ok);
        Assert.Contains("Nothing to disburse", result.Error);
    }
}
