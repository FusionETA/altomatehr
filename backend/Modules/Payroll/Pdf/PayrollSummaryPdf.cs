using System.Globalization;
using AltomateHR.Api.Modules.Payroll.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// The whole run on one sheet, laid out as the previous system's
// `Payroll_Summary_<Month_Year>.pdf`:
//
//   ┌──────────────────────────────────────────────────────────────────────┐
//   │ ORGANISATION                                                         │
//   │ Payroll January 2026                                                 │
//   │                    EMPLOYEE CONTRIBUTIONS        EMPLOYER CONTRIBUTIONS │
//   │ Employee │ GROSS │ PCB EPF SOCSO EIS SKBBK │ NET │ EPF SOCSO EIS HRDF │ COST │
//   │          │ total │ totals …                │ …   │ totals …           │ …    │
//   ├──────────┼───────┼─────────────────────────┼─────┼────────────────────┼──────┤
//   │ Name  E-001 · Position                                                  │
//   │   Base salary        2,500.00                                           │
//   │   Other Allowance  + 250.00   (green)                                   │
//   │   Loan Repayment   − 100.00   (red)                                     │
//   ├─────────────────────────────────────────────────────────────────────────┤
//   │ Summary block: headcount, net, PCB, HRDF, EPF/SOCSO/EIS/SKBBK, zakat…   │
//   └─────────────────────────────────────────────────────────────────────────┘
//
// There is no "other deductions" column, as there was not before: every
// deduction line is itemised, in red, in the employee's own breakdown.
//
// The totals are summed from the ROWS shown rather than read off the run's
// cached figures. If those ever disagreed, printing the cached one would hide
// it — the payslips are the source of truth.
public static class PayrollSummaryPdf
{
    public const string ContentType = "application/pdf";

    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Faint = "#94a3b8";
    private const string Divider = "#e2e8f0";
    private const string Rule = "#cbd5e1";
    private const string EmpBand = "#0e7490";
    private const string EmpBandBg = "#ecfeff";
    private const string EmpBandUnderline = "#67e8f9";
    private const string ErBand = "#c2410c";
    private const string ErBandBg = "#fff7ed";
    private const string ErBandUnderline = "#fdba74";
    private const string Positive = "#047857";
    private const string Negative = "#be123c";

    // Relative widths, the previous system's flex values. The employee column
    // gets the most room so a long name fits on one line.
    private const float ColEmployee = 4.2f;
    private const float ColGross = 1.05f;
    private const float ColPcb = 0.95f;
    private const float ColEpfEmp = 1.0f;
    private const float ColSocsoEmp = 0.95f;
    private const float ColEisEmp = 0.85f;
    private const float ColSkbbkEmp = 0.85f;
    private const float ColNet = 1.1f;
    private const float ColEpfEr = 1.0f;
    private const float ColSocsoEr = 0.95f;
    private const float ColEisEr = 0.85f;
    private const float ColHrdf = 0.85f;
    private const float ColCost = 1.15f;

    private enum Tint { None, Emp, Er }

    public sealed record BreakdownLine(string Label, decimal Amount, bool Signed);

    public static byte[] Render(PayrollDocumentModel model) => Build(model).GeneratePdf();

    // The document itself, for rasterising in a visual check.
    public static IDocument Build(PayrollDocumentModel model)
    {
        var title = $"{model.OrganizationName} Payroll {model.PeriodLabel}";

        return Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A3.Landscape());
                page.MarginTop(32);
                page.MarginBottom(28);
                page.MarginHorizontal(28);
                page.DefaultTextStyle(t => t.FontFamily(PdfFont.Family).FontSize(8.5f).FontColor(Ink));

                page.Content().Element(c => Body(c, model));
                page.Footer().Element(c => Footer(c, model));
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = title,
                Author = model.OrganizationName,
                Subject = $"Payroll summary — {model.PeriodLabel}",
            });
    }

    // Base salary, overtime with the hours behind it, then every line item
    // signed by kind — a benefit in kind unsigned, because it is disclosure
    // only and never part of gross. The previous system's order and wording.
    public static IReadOnlyList<BreakdownLine> Breakdown(
        Payslip payslip, IReadOnlyList<PayslipLineItem> lineItems)
    {
        var lines = new List<BreakdownLine>();

        if (payslip.ProratedPay > 0m)
            lines.Add(new BreakdownLine("Base salary", payslip.ProratedPay, false));

        if (payslip.OtPay > 0m)
        {
            var parts = new List<string>();
            if (payslip.OtNormalHours > 0m) parts.Add($"{Hours(payslip.OtNormalHours)} normal");
            if (payslip.OtRestHours > 0m) parts.Add($"{Hours(payslip.OtRestHours)} rest");
            if (payslip.OtPublicHours > 0m) parts.Add($"{Hours(payslip.OtPublicHours)} PH");
            var tail = parts.Count > 0 ? $" ({string.Join(" + ", parts)})" : string.Empty;

            lines.Add(new BreakdownLine($"Overtime{tail}", payslip.OtPay, true));
        }

        foreach (var li in lineItems)
        {
            var nonCash = li.Kind == PayslipLineKind.ALLOWANCE
                          && li.Category is not null
                          && PayrollAdjustmentCategories.Find(li.Category)?.NonCash == true;

            if (nonCash)
            {
                lines.Add(new BreakdownLine($"{li.Label} (BIK · non-cash)", li.Amount, false));
                continue;
            }

            var sign = li.Kind == PayslipLineKind.DEDUCTION ? -1m : 1m;
            lines.Add(new BreakdownLine(li.Label, sign * li.Amount, true));
        }

        return lines;
    }

    private static void Body(IContainer container, PayrollDocumentModel model)
    {
        var payslips = model.Rows.Select(r => r.Payslip).ToList();
        var totals = Totals.Of(payslips);

        container.Column(col =>
        {
            col.Item().Text(model.OrganizationName).FontSize(13).Bold();
            col.Item().PaddingTop(2).Text($"Payroll {model.PeriodLabel}").FontSize(9.5f).FontColor(Muted);

            // Column-group bands over the employee and employer contributions.
            col.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem(ColEmployee + ColGross);
                Band(row.RelativeItem(ColPcb + ColEpfEmp + ColSocsoEmp + ColEisEmp + ColSkbbkEmp),
                    "Employee contributions", EmpBand, EmpBandBg, EmpBandUnderline);
                row.RelativeItem(ColNet);
                Band(row.RelativeItem(ColEpfEr + ColSocsoEr + ColEisEr + ColHrdf),
                    "Employer contributions", ErBand, ErBandBg, ErBandUnderline);
                row.RelativeItem(ColCost);
            });

            // The heading row (with the run totals) repeats on every page; each
            // employee's block is kept whole rather than split across a break.
            col.Item().Decoration(d =>
            {
                d.Before().BorderBottom(1).BorderColor(Rule).Row(h =>
                {
                    h.RelativeItem(ColEmployee).Element(HeadCell).PaddingHorizontal(3)
                        .Text("EMPLOYEE NAME").Style(HeadLabel);
                    ColHead(h.RelativeItem(ColGross), "GROSS", totals.Gross, Tint.None);
                    ColHead(h.RelativeItem(ColPcb), "PCB", totals.Pcb, Tint.Emp);
                    ColHead(h.RelativeItem(ColEpfEmp), "EPF", totals.EpfEmp, Tint.Emp);
                    ColHead(h.RelativeItem(ColSocsoEmp), "SOCSO", totals.SocsoEmp, Tint.Emp);
                    ColHead(h.RelativeItem(ColEisEmp), "EIS", totals.EisEmp, Tint.Emp);
                    ColHead(h.RelativeItem(ColSkbbkEmp), "SKBBK", totals.SkbbkEmp, Tint.Emp);
                    ColHead(h.RelativeItem(ColNet), "NET", totals.Net, Tint.None);
                    ColHead(h.RelativeItem(ColEpfEr), "EPF", totals.EpfEr, Tint.Er);
                    ColHead(h.RelativeItem(ColSocsoEr), "SOCSO", totals.SocsoEr, Tint.Er);
                    ColHead(h.RelativeItem(ColEisEr), "EIS", totals.EisEr, Tint.Er);
                    ColHead(h.RelativeItem(ColHrdf), "HRDF", totals.Hrdf, Tint.Er);
                    ColHead(h.RelativeItem(ColCost), "COST", totals.Cost, Tint.None);
                });

                d.Content().Column(body =>
                {
                    foreach (var p in payslips)
                    {
                        var items = model.LineItems.GetValueOrDefault(p.Id) ?? [];
                        body.Item().ShowEntire().Element(c => EmployeeRow(c, p, items));
                    }
                });
            });

            col.Item().PaddingTop(14).ShowEntire().Element(c => SummaryBlock(c, payslips.Count, totals));
        });
    }

    private static void SummaryBlock(IContainer container, int employees, Totals t)
    {
        var rows = new List<(string Label, string Value)>
        {
            ("Number of employees", employees.ToString(CultureInfo.InvariantCulture)),
            ("Total employee net pay", Fmt(t.Net)),
            ("Total PCB payment", Fmt(t.Pcb)),
            ("Employees subject to HRDF", t.HrdfCount.ToString(CultureInfo.InvariantCulture)),
            ("Total wages subject to HRDF", Fmt(t.HrdfWage)),
            ("Total EPF payment", Fmt(t.EpfEmp + t.EpfEr)),
            ("Total SOCSO payment", Fmt(t.SocsoEmp + t.SocsoEr)),
            ("Total EIS payment", Fmt(t.EisEmp + t.EisEr)),
        };

        // SKBBK started Jun 2026; earlier months should not show a zero line.
        if (t.SkbbkEmp > 0m) rows.Add(("Total SKBBK payment", Fmt(t.SkbbkEmp)));
        rows.Add(("Total HRDF payment", Fmt(t.Hrdf)));
        rows.Add(("Total Zakat payment", Fmt(t.Zakat)));
        if (t.Bik > 0m) rows.Add(("Total Benefits in Kind (non-cash, for tax)", Fmt(t.Bik)));

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
            });

            foreach (var (label, value) in rows)
            {
                table.Cell().PaddingHorizontal(6).PaddingVertical(2).PaddingBottom(2).Row(r =>
                {
                    r.RelativeItem().Text(label).FontSize(8).FontColor(Muted);
                    r.AutoItem().Text(value).FontSize(8).Bold();
                });
            }
        });
    }

    private static void Footer(IContainer container, PayrollDocumentModel model) =>
        container.PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text($"{model.OrganizationName} · Payroll {model.PeriodLabel}")
                .FontSize(7).FontColor(Faint);
            row.AutoItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(Faint));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
                t.Span($" · Generated {Generated(model.GeneratedAt)}");
            });
        });

    // ─── Cells ──────────────────────────────────────────────────────────

    private static void Band(IContainer container, string text, string ink, string bg, string underline) =>
        container.Background(bg).BorderBottom(1).BorderColor(underline).PaddingBottom(2)
            .AlignCenter().Text(text.ToUpperInvariant())
            .FontSize(7.5f).Bold().FontColor(ink).LetterSpacing(0.06f);

    private static IContainer HeadCell(IContainer c) => c.PaddingTop(4).PaddingBottom(6);

    private static TextStyle HeadLabel =>
        TextStyle.Default.FontSize(7).Bold().FontColor(Muted).LetterSpacing(0.05f);

    private static void ColHead(IContainer container, string label, decimal total, Tint tint) =>
        Tinted(container, tint).Element(HeadCell).PaddingHorizontal(3).AlignRight().Column(col =>
        {
            col.Item().AlignRight().Text(label).Style(HeadLabel);
            col.Item().PaddingTop(1.5f).AlignRight().Text(Fmt(total)).FontSize(8.5f).Bold();
        });

    private static void EmployeeRow(IContainer container, Payslip p, IReadOnlyList<PayslipLineItem> items) =>
        container.BorderBottom(0.5f).BorderColor(Divider).Row(row =>
        {
            row.RelativeItem(ColEmployee).PaddingVertical(5).PaddingHorizontal(3).Column(cell =>
            {
                cell.Item().Text(p.SnapshotName).FontSize(9).Bold();

                var meta = p.SnapshotEmployeeNumber ?? string.Empty;
                if (!string.IsNullOrEmpty(p.SnapshotPosition)) meta += $" · {p.SnapshotPosition}";
                cell.Item().PaddingTop(1).Text(meta).FontSize(7.5f).FontColor(Muted);

                foreach (var line in Breakdown(p, items))
                {
                    cell.Item().PaddingTop(2).Row(r =>
                    {
                        r.RelativeItem().Text(line.Label).FontSize(7.5f).FontColor(Muted);

                        var amount = r.AutoItem()
                            .Text(line.Signed ? Signed(line.Amount) : Amount(line.Amount))
                            .FontSize(7.5f);

                        if (line.Signed && line.Amount > 0m) amount.FontColor(Positive);
                        if (line.Signed && line.Amount < 0m) amount.FontColor(Negative);
                    });
                }
            });

            AmountCell(row.RelativeItem(ColGross), p.GrossPay, Tint.None, bold: true);
            AmountCell(row.RelativeItem(ColPcb), p.Pcb, Tint.Emp);
            AmountCell(row.RelativeItem(ColEpfEmp), p.EpfEmployee, Tint.Emp);
            AmountCell(row.RelativeItem(ColSocsoEmp), p.SocsoEmployee, Tint.Emp);
            AmountCell(row.RelativeItem(ColEisEmp), p.EisEmployee, Tint.Emp);
            AmountCell(row.RelativeItem(ColSkbbkEmp), p.SkbbkEmployee, Tint.Emp);
            AmountCell(row.RelativeItem(ColNet), p.NetPay, Tint.None, bold: true);
            AmountCell(row.RelativeItem(ColEpfEr), p.EpfEmployer, Tint.Er);
            AmountCell(row.RelativeItem(ColSocsoEr), p.SocsoEmployer, Tint.Er);
            AmountCell(row.RelativeItem(ColEisEr), p.EisEmployer, Tint.Er);
            AmountCell(row.RelativeItem(ColHrdf), p.Hrdf, Tint.Er);
            AmountCell(row.RelativeItem(ColCost), p.TotalCostToEmployer, Tint.None, bold: true);
        });

    private static void AmountCell(IContainer container, decimal value, Tint tint, bool bold = false)
    {
        var text = Tinted(container, tint).PaddingVertical(5).PaddingHorizontal(3).AlignRight()
            .Text(Fmt(value)).FontSize(8.5f);

        if (bold) text.Bold();
        if (value == 0m) text.FontColor(Faint);
    }

    private static IContainer Tinted(IContainer c, Tint tint) => tint switch
    {
        Tint.Emp => c.Background(EmpBandBg),
        Tint.Er => c.Background(ErBandBg),
        _ => c,
    };

    // ─── Formatting ─────────────────────────────────────────────────────

    // Zero prints as a dash, as the previous system printed it.
    private static string Fmt(decimal value) => value == 0m ? "—" : Amount(value);

    private static string Amount(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Signed(decimal value) =>
        (value < 0m ? "− " : "+ ") + Amount(Math.Abs(value));

    private static string Hours(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    // "25/09/2026 11:38 am", the previous system's en-MY short form.
    private static string Generated(DateTime value) =>
        value.ToString("dd/MM/yyyy h:mm ", CultureInfo.InvariantCulture)
        + value.ToString("tt", CultureInfo.InvariantCulture).ToLowerInvariant();

    private sealed class Totals
    {
        public decimal Gross, Bik, Pcb, EpfEmp, SocsoEmp, EisEmp, SkbbkEmp, Net;
        public decimal EpfEr, SocsoEr, EisEr, Hrdf, Cost, Zakat, HrdfWage;
        public int HrdfCount;

        public static Totals Of(IEnumerable<Payslip> payslips)
        {
            var t = new Totals();
            foreach (var p in payslips)
            {
                t.Gross += p.GrossPay;
                t.Bik += p.TotalBenefitsInKind;
                t.Pcb += p.Pcb;
                t.EpfEmp += p.EpfEmployee;
                t.SocsoEmp += p.SocsoEmployee;
                t.EisEmp += p.EisEmployee;
                t.SkbbkEmp += p.SkbbkEmployee;
                t.Net += p.NetPay;
                t.EpfEr += p.EpfEmployer;
                t.SocsoEr += p.SocsoEmployer;
                t.EisEr += p.EisEmployer;
                t.Hrdf += p.Hrdf;
                t.Cost += p.TotalCostToEmployer;
                t.Zakat += p.Zakat;
                t.HrdfWage += p.HrdfWage;
                if (p.Hrdf > 0m) t.HrdfCount++;
            }
            return t;
        }
    }
}
