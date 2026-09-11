using AltomateHR.Api.Modules.Payroll.Entities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// One employee's payslip.
//
// The one arithmetic rule this document must never break: the earnings total
// minus the deductions total has to equal the net pay printed at the bottom,
// which is the figure that reaches the employee's bank. That is a test
// (`PayslipPdfTests`), not a hope — a payslip whose columns do not add up is
// the single most common payroll support ticket there is.
//
// Everything shown comes from the payslip SNAPSHOT, so reprinting January in
// December reproduces January.
public static class PayslipPdf
{
    public const string ContentType = "application/pdf";

    public static byte[] Render(PayslipPdfModel model) => Build(model).GeneratePdf();

    // The document itself, so it can be rasterised for a visual check as
    // well as written to PDF.
    public static IDocument Build(PayslipPdfModel model) =>
        Document.Create(doc => doc.Page(page =>
        {
            PayrollPdfShared.Frame(page);
            page.Content().Element(c => Body(c, model));
            page.Footer().Element(c => PayrollPdfShared.Footer(
                c, "Computer-generated payslip — no signature required."));
        }));

    private static void Body(IContainer container, PayslipPdfModel model)
    {
        var p = model.Payslip;

        container.Column(col =>
        {
            // ---- Header ----
            col.Item().PaddingBottom(6).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(model.OrganizationName).FontSize(13).Bold();
                });
                row.ConstantItem(180).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text($"Payslip for {model.PeriodLabel}")
                        .FontSize(9).SemiBold();
                    right.Item().AlignRight()
                        .Text($"Issued {PayrollPdfShared.Date(model.IssueDate)}")
                        .FontSize(8).FontColor(PayrollPdfShared.Muted);
                });
            });

            col.Item().BorderBottom(1.5f).BorderColor(PayrollPdfShared.Accent).PaddingBottom(2);

            // ---- Identity ----
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    PayrollPdfShared.Field(left, "Employee", p.SnapshotName);
                    PayrollPdfShared.Field(left, "Employee ID", p.SnapshotEmployeeNumber);
                    PayrollPdfShared.Field(left, "IC / Passport", model.IdNumber);
                    PayrollPdfShared.Field(left, "Join date",
                        model.JoinDate is null ? null : PayrollPdfShared.Date(model.JoinDate.Value));
                });
                row.ConstantItem(16);
                row.RelativeItem().Column(right =>
                {
                    PayrollPdfShared.Field(right, "Designation", p.SnapshotPosition);
                    PayrollPdfShared.Field(right, "EPF ref", model.EpfNumber);
                    PayrollPdfShared.Field(right, "SOCSO ref", model.SocsoNumber);
                    PayrollPdfShared.Field(right, "Tax ref", model.IncomeTaxNumber);
                    PayrollPdfShared.Field(right, "Bank account",
                        PayrollPdfShared.MaskedAccount(model.BankName, model.BankAccountNumber));
                });
            });

            // ---- Earnings and deductions ----
            col.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Column(earnings =>
                {
                    PayrollPdfShared.SectionLabel(earnings, "Earnings");
                    PayrollPdfShared.AmountRow(earnings, "Basic pay", p.ProratedPay);

                    // Zero rows are hidden rather than printed as RM 0.00 —
                    // an ordinary month should not read like an exception.
                    if (p.OtPay != 0m) PayrollPdfShared.AmountRow(earnings, "Overtime", p.OtPay);
                    if (p.TotalAllowances != 0m)
                        PayrollPdfShared.AmountRow(earnings, "Allowances", p.TotalAllowances);
                    if (p.TotalReimbursements != 0m)
                        PayrollPdfShared.AmountRow(earnings, "Reimbursements", p.TotalReimbursements);

                    // GrossPay already sums the rows above — see
                    // PayslipCalculator. Re-adding them here would double it.
                    PayrollPdfShared.TotalRow(earnings, "Total earnings", p.GrossPay);
                });

                row.ConstantItem(16);

                row.RelativeItem().Column(deductions =>
                {
                    PayrollPdfShared.SectionLabel(deductions, "Deductions");
                    PayrollPdfShared.AmountRow(deductions, "Employee EPF", p.EpfEmployee);

                    // Post-zakat: the calculator has already offset it, so this
                    // is what is actually withheld.
                    PayrollPdfShared.AmountRow(deductions, "PCB / MTD", p.Pcb);

                    if (p.Cp38 > 0m)
                        PayrollPdfShared.AmountRow(deductions, "CP38 arrears", p.Cp38);

                    PayrollPdfShared.AmountRow(deductions, "Employee SOCSO", p.SocsoEmployee);
                    PayrollPdfShared.AmountRow(deductions, "Employee EIS", p.EisEmployee);

                    if (p.SkbbkEmployee > 0m)
                        PayrollPdfShared.AmountRow(
                            deductions, "SKBBK (LINDUNG 24 Jam)", p.SkbbkEmployee);

                    if (p.Zakat > 0m) PayrollPdfShared.AmountRow(deductions, "Zakat", p.Zakat);

                    // Zakat and CP38 are inside TotalDeductions and have their
                    // own rows above, so the catch-all nets them off rather
                    // than counting them twice.
                    var other = p.TotalDeductions - p.Zakat - p.Cp38;
                    if (other > 0m) PayrollPdfShared.AmountRow(deductions, "Other deductions", other);

                    PayrollPdfShared.TotalRow(deductions, "Total deductions", TotalDeductions(p));
                });
            });

            // ---- Net pay ----
            col.Item().PaddingTop(12).Background(PayrollPdfShared.PanelBg)
                .Border(1).BorderColor(PayrollPdfShared.Rule).Padding(10).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text("Net pay").FontSize(11).Bold();
                    row.ConstantItem(160).AlignRight().AlignMiddle()
                        .Text($"MYR {PayrollPdfShared.Rm(p.NetPay)}").FontSize(13).Bold();
                });

            // ---- Employer contributions ----
            // Shown so the employee can see the full cost of employing them,
            // and check their own EPF statement against it.
            col.Item().PaddingTop(14).Column(section =>
            {
                PayrollPdfShared.SectionLabel(section, "Employer contributions");
                section.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        PayrollPdfShared.AmountRow(left, "Employer EPF", p.EpfEmployer);
                        PayrollPdfShared.AmountRow(left, "Employer SOCSO", p.SocsoEmployer);
                    });
                    row.ConstantItem(16);
                    row.RelativeItem().Column(right =>
                    {
                        PayrollPdfShared.AmountRow(right, "Employer EIS", p.EisEmployer);
                        PayrollPdfShared.AmountRow(right, "HRDF", p.Hrdf);
                    });
                });
            });

            // ---- Benefits in kind ----
            // Never paid in cash, so they are outside gross — but they are
            // taxed, which is why an employee needs to see what drove it.
            if (p.TotalBenefitsInKind > 0m)
            {
                var bik = model.LineItems
                    .Where(li => li.Category?.StartsWith("bik_", StringComparison.Ordinal) == true)
                    .ToList();

                col.Item().PaddingTop(12).Column(section =>
                {
                    PayrollPdfShared.SectionLabel(section, "Benefits in kind (non-cash)");

                    foreach (var line in bik)
                    {
                        PayrollPdfShared.AmountRow(section, line.Label, line.Amount);
                    }

                    // A payslip imported from another system may carry the
                    // total with no itemisation behind it.
                    if (bik.Count == 0)
                    {
                        PayrollPdfShared.AmountRow(section, "Total", p.TotalBenefitsInKind);
                    }
                    else
                    {
                        PayrollPdfShared.TotalRow(section, "Total BIK", p.TotalBenefitsInKind);
                    }
                });
            }

            // ---- Year to date ----
            col.Item().PaddingTop(14).Column(section =>
            {
                PayrollPdfShared.SectionLabel(section, $"Year to date (through {model.PeriodLabel})");

                PayrollPdfShared.AmountRow(section, "Gross pay", model.Ytd.Gross);
                PayrollPdfShared.AmountRow(section, "Net pay", model.Ytd.Net);

                section.Item().PaddingTop(6).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(70);
                        c.ConstantColumn(70);
                        c.ConstantColumn(70);
                    });

                    table.Header(h =>
                    {
                        HeaderCell(h, "");
                        HeaderCell(h, "Employee", right: true);
                        HeaderCell(h, "Employer", right: true);
                        HeaderCell(h, "Total", right: true);
                    });

                    MatrixRow(table, "EPF", model.Ytd.EpfEmployee, model.Ytd.EpfEmployer);
                    MatrixRow(table, "SOCSO", model.Ytd.SocsoEmployee, model.Ytd.SocsoEmployer);
                    MatrixRow(table, "EIS", model.Ytd.EisEmployee, model.Ytd.EisEmployer);

                    if (model.Ytd.SkbbkEmployee > 0m)
                        MatrixRow(table, "SKBBK", model.Ytd.SkbbkEmployee, 0m);

                    MatrixRow(table, "PCB", model.Ytd.Pcb, 0m);
                    MatrixRow(table, "HRDF", 0m, model.Ytd.Hrdf);
                });
            });
        });
    }

    // A bank account as its owner sees it: enough to recognise, not enough
    // to be useful to whoever picks the payslip up off a printer.
    public static string MaskedAccount(string? bankName, string? accountNumber) =>
        PayrollPdfShared.MaskedAccount(bankName, accountNumber);

    // What the employee actually loses from gross. Mirrors
    // PayslipCalculator's net-pay arithmetic exactly — the statutory items,
    // PCB, and the line-item deductions bucket (which already contains zakat
    // and CP38).
    public static decimal TotalDeductions(Payslip p) =>
        p.EpfEmployee + p.SocsoEmployee + p.EisEmployee + p.SkbbkEmployee
        + p.TotalDeductions + p.Pcb;

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().PaddingBottom(2)
            .BorderBottom(1).BorderColor(PayrollPdfShared.Rule);

        (right ? cell.AlignRight() : cell)
            .Text(text).FontSize(7.5f).FontColor(PayrollPdfShared.Muted);
    }

    private static void MatrixRow(TableDescriptor table, string label, decimal employee, decimal employer)
    {
        table.Cell().PaddingVertical(1.5f).Text(label).FontSize(8.5f);
        table.Cell().PaddingVertical(1.5f).AlignRight()
            .Text(PayrollPdfShared.Rm(employee)).FontSize(8.5f);
        table.Cell().PaddingVertical(1.5f).AlignRight()
            .Text(PayrollPdfShared.Rm(employer)).FontSize(8.5f);
        table.Cell().PaddingVertical(1.5f).AlignRight()
            .Text(PayrollPdfShared.Rm(employee + employer)).FontSize(8.5f).SemiBold();
    }
}

// Everything one payslip needs, assembled by the service so the renderer
// stays pure.
public sealed record PayslipPdfModel
{
    public required Payslip Payslip { get; init; }
    public required string OrganizationName { get; init; }
    public required string PeriodLabel { get; init; }
    public required DateTime IssueDate { get; init; }
    public required PayslipYtdSummary Ytd { get; init; }

    public IReadOnlyList<PayslipLineItem> LineItems { get; init; } = [];

    // Live identity, as with the statutory files — a corrected EPF number
    // should reach the next reprint.
    public string? IdNumber { get; init; }
    public string? EpfNumber { get; init; }
    public string? SocsoNumber { get; init; }
    public string? IncomeTaxNumber { get; init; }
    public string? BankName { get; init; }
    public string? BankAccountNumber { get; init; }
    public DateTime? JoinDate { get; init; }
}
