using System.Globalization;
using AltomateHR.Api.Modules.Employees.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// Form EA (C.P.8A - Pin. 2023) — the statement of remuneration an employer
// must hand each employee by 28 February, one page each.
//
// Drawn as LHDN's own form: the same header, the same lettered sections, the
// same numbered lines in the same words, and the officer's box at the foot, so
// it can be signed and handed over as it is. The figures come line by line
// from FormEaLines; a line payroll never records (B2 arrears for earlier
// years, B5 unapproved-fund refunds, C pensions, D4 donations via salary) is
// left blank, exactly as an employer with nothing to declare there would.
//
// The year's SUBMITTED runs only: a draft month is not remuneration that was
// paid, and a number on an employee's tax return must have reached their bank.
public static class FormEaPdf
{
    public const string ContentType = "application/pdf";

    private const float Amount = 88;     // the RM column
    private const float Indent = 14;     // item number gutter
    private const float Sub = 20;        // (a)/(b) gutter
    private const float TextSize = 8.2f;

    public static byte[] Render(PayrollAnnualPayload payload) => Build(payload).GeneratePdf();

    public static IDocument Build(PayrollAnnualPayload payload) => Build(payload, MalaysiaToday());

    public static IDocument Build(PayrollAnnualPayload payload, DateTime issued) =>
        Document.Create(doc =>
        {
            foreach (var employee in payload.Employees)
            {
                doc.Page(page =>
                {
                    Setup(page);
                    page.Content().Element(c => Page(c, payload, employee, issued));
                });
            }

            // A year with no submitted runs is a real state — the page has to
            // say so rather than QuestPDF refusing an empty document.
            if (payload.Employees.Count == 0)
            {
                doc.Page(page =>
                {
                    Setup(page);
                    page.Content().Text(
                            $"No payroll was submitted for {payload.Year}, so there is nothing to report on Form EA.")
                        .FontSize(9);
                });
            }
        });

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginVertical(26);
        page.MarginHorizontal(34);
        page.DefaultTextStyle(t => t.FontFamily(PdfFont.Family).FontSize(TextSize).FontColor(Colors.Black));
    }

    private static void Page(IContainer container, PayrollAnnualPayload payload, AnnualEmployeeRow e, DateTime issued) =>
        container.Column(col =>
        {
            var ea = e.Ea;
            var info = payload.CompanyInfo;

            col.Item().Element(c => Header(c, payload, e));

            // The instruction band across the page.
            col.Item().PaddingTop(5).Background(Colors.Black).PaddingVertical(2.5f).AlignCenter()
                .Text("THIS FORM EA MUST BE PREPARED AND PROVIDED TO THE EMPLOYEE FOR INCOME TAX PURPOSE")
                .FontColor(Colors.White).FontSize(8.5f);

            // ── A ──────────────────────────────────────────────────────
            Section(col, "A", "PARTICULARS OF EMPLOYEE");
            col.Item().PaddingLeft(Indent + 12).Column(a =>
            {
                a.Item().Element(c => Fill(c, "1.", "Full Name of Employee / Pensioner (Mr./Miss/Madam)", e.EmployeeName));
                Pair(a, ("2.", "Job Designation", e.JobTitle), ("3.", "Staff No. / Payroll No.", e.EmployeeCode));
                Pair(a, ("4.", "New I.C. No", IsIc(e) ? e.IdNumber : null), ("5.", "Passport No.", IsIc(e) ? null : e.IdNumber));
                Pair(a, ("6.", "EPF No.", e.EpfNumber), ("7.", "SOCSO No.", e.SocsoNumber));

                a.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().PaddingTop(3).Row(eight =>
                    {
                        eight.ConstantItem(Indent).Text("8.");
                        eight.ConstantItem(100).Text("Number of children qualified for tax relief");
                        eight.RelativeItem().AlignBottom().BorderBottom(0.5f).PaddingLeft(3)
                            .Text(e.QualifyingChildren > 0 ? e.QualifyingChildren.ToString(CultureInfo.InvariantCulture) : "");
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(nine =>
                    {
                        nine.Item().Row(r =>
                        {
                            r.ConstantItem(Indent).Text("9.");
                            r.RelativeItem().Text("If the period of employment is less than a year, please state:");
                        });
                        nine.Item().PaddingLeft(Indent).Element(c => Fill(c, "(a)", "Date of commencement", Commenced(e, payload.Year)));
                        nine.Item().PaddingLeft(Indent).Element(c => Fill(c, "(b)", "Date of cessation", Ceased(e, payload.Year)));
                    });
                });
            });

            // ── B ──────────────────────────────────────────────────────
            col.Item().PaddingTop(9).Row(row =>
            {
                row.ConstantItem(Indent + 12).Element(c => Letter(c, "B"));
                row.RelativeItem().Column(t =>
                {
                    t.Item().Text("EMPLOYMENT INCOME, BENEFITS AND LIVING ACCOMMODATION").FontSize(8.8f);
                    t.Item().Text("(Excluding Tax Exempt Allowances / Perquisites / Gifts / Benefits)").FontSize(7);
                });
                row.ConstantItem(Amount).AlignBottom().AlignCenter().Text("RM").FontSize(8.8f);
            });
            col.Item().PaddingLeft(Indent + 12).Column(b =>
            {
                Line(b, "1.", "(a)", "Gross salary, wages or leave pay (including overtime pay)", ea.B1a);
                Line(b, "", "(b)", "Fees (including director fees), commission or bonus", ea.B1b);
                Line(b, "", "(c)", $"Gross tips, perquisites, awards / rewards or other allowances (Details of payment: {Details(ea.B1cDetails)})", ea.B1c, lines: 2);
                Line(b, "", "(d)", "Income tax borne by the employer in respect of his employee", ea.B1d);
                Line(b, "", "(e)", "Employee Share Option Scheme (ESOS) benefit", ea.B1e);
                Line(b, "", "(f)", "Gratuity for the period from .................................. to ..................................", ea.B1f);
                Line(b, "2.", "", "Details of arrears and others for preceding years paid in the current year", null, amountColumn: false);
                b.Item().Row(r =>
                {
                    r.ConstantItem(Indent);
                    r.ConstantItem(80).Text("Type of income");
                    r.ConstantItem(Sub).Text("(a)");
                    r.ConstantItem(190).BorderBottom(0.5f).Text("");
                    r.RelativeItem();
                    r.ConstantItem(Amount);
                });
                b.Item().Row(r =>
                {
                    r.ConstantItem(Indent + 80);
                    r.ConstantItem(Sub).Text("(b)");
                    r.ConstantItem(190).BorderBottom(0.5f).Text("");
                    r.RelativeItem();
                    r.ConstantItem(Amount).BorderBottom(0.5f).Text("");
                });
                Line(b, "3.", "", $"Benefits in kind (Specify: {Details(ea.B3Details)})", ea.B3, lines: 2);
                Line(b, "4.", "", "Value of living accommodation provided (Address: ...............................................................)", ea.B4);
                Line(b, "5.", "", "Refund from unapproved Provident / Pension Fund", null);
                Line(b, "6.", "", "Compensation for loss of employment", ea.B6);
            });

            // ── C ──────────────────────────────────────────────────────
            Section(col, "C", "PENSION AND OTHERS");
            col.Item().PaddingLeft(Indent + 12).Column(c =>
            {
                Line(c, "1.", "", "Pension", null);
                Line(c, "2.", "", "Annuities or other periodical payments", null);
                c.Item().PaddingTop(2).Row(r =>
                {
                    r.ConstantItem(Indent);
                    r.RelativeItem().Text("TOTAL");
                    r.ConstantItem(Amount).BorderTop(0.75f).BorderBottom(1.25f).PaddingVertical(1).AlignRight()
                        .Text(Rm(ea.Total)).Bold();
                });
            });

            // ── D ──────────────────────────────────────────────────────
            Section(col, "D", "TOTAL DEDUCTION");
            col.Item().PaddingLeft(Indent + 12).Column(d =>
            {
                Line(d, "1.", "", "Monthly tax deductions (MTD) remitted to LHDNM", e.TotalMtdRemitted, always: true);
                Line(d, "2.", "", "CP38 deductions remitted to LHDNM", e.TotalCp38);
                Line(d, "3.", "", "Zakat paid via salary deduction", ea.D3ZakatViaSalary);
                Line(d, "4.", "", "Approved donations / gifts / contributions via salary deduction", null);
                Line(d, "5.", "", "Total claim for deduction by employee via Form TP1 in respect of:", null, amountColumn: false);
                MidAmount(d, "(a)", "Relief", ea.D5aTp1Relief);
                MidAmount(d, "(b)", "Zakat other than that paid via monthly salary deduction", ea.D5bZakatSelfPaid);
                Line(d, "6.", "", "Total qualifying child relief", e.AnnualChildRelief);
            });

            // ── E ──────────────────────────────────────────────────────
            Section(col, "E", "CONTRIBUTIONS PAID BY EMPLOYEE TO APPROVED PROVIDENT / PENSION FUND AND SOCSO", bold: true);
            col.Item().PaddingLeft(Indent + 12).Column(x =>
            {
                x.Item().Element(c => Fill(c, "1.", "Name of Provident Fund",
                    e.TotalEpfEmployee > 0 ? "Kumpulan Wang Simpanan Pekerja (KWSP)" : null));
                RmLine(x, "", "Amount of compulsory contribution paid (state the employee's share of contribution only)", e.TotalEpfEmployee);
                // SOCSO here is PERKESO's whole employee share: SOCSO (with the
                // SKBBK collected alongside it) and EIS, the s.46(1)(n) relief.
                RmLine(x, "2.", "SOCSO: Amount of compulsory contribution paid (state the employee's share of contribution only)",
                    e.TotalSocsoEmployee + e.TotalEisEmployee);
            });

            // ── F ──────────────────────────────────────────────────────
            col.Item().PaddingTop(9).Row(row =>
            {
                row.ConstantItem(Indent + 12).Element(c => Letter(c, "F"));
                row.RelativeItem().AlignMiddle().Text("TOTAL TAX EXEMPT ALLOWANCES / PERQUISITES / GIFTS / BENEFITS").FontSize(8.8f);
                row.ConstantItem(18).AlignMiddle().Text("RM");
                row.ConstantItem(Amount).BorderBottom(0.5f).AlignRight().AlignBottom().Text(Blank(ea.F));
            });

            // ── Employer's declaration ─────────────────────────────────
            col.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem(1).AlignBottom().Row(r =>
                {
                    r.AutoItem().Text("Date: ");
                    r.ConstantItem(110).BorderBottom(0.5f).Text(issued.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                });
                row.ConstantItem(16);
                row.RelativeItem(2.2f).Border(1).Padding(7).Column(box =>
                {
                    var (address1, address2) = Address(info);
                    Signed(box, "Name of Officer", Clean(info?.DeclarantName));
                    Signed(box, "Designation", Clean(info?.DeclarantPosition));
                    Signed(box, "Name and Address of Employer", EmployerName(payload));
                    Signed(box, "", address1);
                    Signed(box, "", address2);
                    Signed(box, "Employer's Telephone No.", Clean(info?.Phone) ?? Clean(info?.Handphone));
                });
            });
        });

    // The three-part head of the form: code and references left, the title
    // centred, the "EA" box and the employee's TIN right.
    private static void Header(IContainer container, PayrollAnnualPayload payload, AnnualEmployeeRow e) =>
        container.Row(row =>
        {
            row.RelativeItem(0.95f).Column(left =>
            {
                left.Item().Text("(C.P.8A - Pin. 2023)").FontSize(7.5f);
                left.Item().PaddingTop(12).Row(r =>
                {
                    r.ConstantItem(62).Text("Serial No.");
                    r.RelativeItem().BorderBottom(0.5f).Text("");
                });
                left.Item().PaddingTop(4).Row(r =>
                {
                    r.ConstantItem(62).Text("Employer's No. E");
                    r.RelativeItem().BorderBottom(0.5f).Text(EmployerNoE(payload.CompanyInfo?.EmployerTin));
                });
            });

            row.RelativeItem(1.75f).AlignCenter().Column(mid =>
            {
                mid.Item().AlignCenter().Text("MALAYSIA").FontSize(7.5f);
                mid.Item().AlignCenter().Text("INCOME TAX").FontSize(11);
                mid.Item().AlignCenter().Text("STATEMENT OF REMUNERATION FROM EMPLOYMENT").FontSize(7.8f);
                mid.Item().AlignCenter().Row(r =>
                {
                    r.AutoItem().Text("FOR THE YEAR ENDED 31 DECEMBER ");
                    r.ConstantItem(34).BorderBottom(0.5f).AlignCenter().Text(payload.Year.ToString(CultureInfo.InvariantCulture)).Bold();
                });
            });

            row.RelativeItem(1.1f).Column(right =>
            {
                right.Item().Row(r =>
                {
                    r.RelativeItem().Background(Colors.Black).PaddingHorizontal(4).PaddingVertical(1.5f).Column(box =>
                    {
                        box.Item().Text("PRIVATE SECTOR Employee's").FontColor(Colors.White).Bold().FontSize(7.8f);
                        box.Item().Text("Statement of Remuneration").FontColor(Colors.White).FontSize(7.8f);
                    });
                    r.AutoItem().PaddingLeft(3).AlignMiddle().Text("EA").FontSize(22);
                });
                right.Item().PaddingTop(1).Text("Employee's Tax Identification No. (TIN)").FontSize(7.8f);
                right.Item().BorderBottom(0.5f).Text(e.IncomeTaxNumber ?? "");
                right.Item().PaddingTop(3).Row(r =>
                {
                    r.ConstantItem(62).Text("LHDNM State");
                    r.RelativeItem().BorderBottom(0.5f).Text("");
                });
            });
        });

    // ─── Building blocks ────────────────────────────────────────────────

    // A black square holding the section letter, then the section title.
    private static void Section(ColumnDescriptor col, string letter, string title, bool bold = false) =>
        col.Item().PaddingTop(9).Row(row =>
        {
            row.ConstantItem(Indent + 12).Element(c => Letter(c, letter));
            var text = row.RelativeItem().AlignMiddle().Text(title).FontSize(8.8f);
            if (bold) text.Bold();
        });

    private static void Letter(IContainer container, string letter) =>
        container.Width(13).Height(13).Background(Colors.Black).AlignCenter().AlignMiddle()
            .Text(letter).FontColor(Colors.White).Bold().FontSize(8);

    // "N.  Label ___value___" — a particulars row.
    private static void Fill(IContainer container, string number, string label, string? value) =>
        container.PaddingTop(3).Row(row =>
        {
            row.ConstantItem(number.StartsWith('(') ? Sub : Indent).Text(number);
            row.AutoItem().Text(label + " ");
            row.RelativeItem().BorderBottom(0.5f).PaddingLeft(3).Text(value ?? "");
        });

    private static void Pair(ColumnDescriptor col, (string N, string L, string? V) left, (string N, string L, string? V) right) =>
        col.Item().Row(row =>
        {
            row.RelativeItem().Element(c => Fill(c, left.N, left.L, left.V));
            row.ConstantItem(12);
            row.RelativeItem().Element(c => Fill(c, right.N, right.L, right.V));
        });

    // "N. (x) Description ........................ amount" — an income or
    // deduction line with its figure in the RM column. Zero prints blank, as
    // on a hand-filled form; `always` prints 0.00 where the form expects one.
    private static void Line(
        ColumnDescriptor col, string number, string sub, string text, decimal? amount,
        bool always = false, bool amountColumn = true, int lines = 1) =>
        col.Item().PaddingTop(2.6f).Row(row =>
        {
            row.ConstantItem(Indent).Text(number);
            if (sub.Length > 0) row.ConstantItem(Sub).Text(sub);
            row.RelativeItem().Text(text).ClampLines(lines);
            row.ConstantItem(8);
            if (amountColumn)
            {
                row.ConstantItem(Amount).AlignBottom().BorderBottom(0.5f).AlignRight()
                    .Text(always ? Rm(amount ?? 0) : Blank(amount));
            }
            else
            {
                row.ConstantItem(Amount);
            }
        });

    // D5(a)/(b): the TP1 claims carry their own "RM ____" in the middle of
    // the line, not in the RM column.
    private static void MidAmount(ColumnDescriptor col, string sub, string text, decimal amount) =>
        col.Item().PaddingTop(2.6f).Row(row =>
        {
            row.ConstantItem(Indent);
            row.ConstantItem(Sub).Text(sub);
            row.RelativeItem().Text(text);
            row.ConstantItem(16).Text("RM");
            row.ConstantItem(150).BorderBottom(0.5f).AlignRight().Text(Blank(amount));
            row.ConstantItem(Amount + 8);
        });

    // "N. Text ................. RM ____" — section E's lines.
    private static void RmLine(ColumnDescriptor col, string number, string text, decimal amount) =>
        col.Item().PaddingTop(2.6f).Row(row =>
        {
            row.ConstantItem(Indent).Text(number);
            row.RelativeItem().Text(text);
            row.ConstantItem(18).Text("RM");
            row.ConstantItem(Amount).BorderBottom(0.5f).AlignRight().Text(Rm(amount));
        });

    private static void Signed(ColumnDescriptor col, string label, string? value) =>
        col.Item().PaddingTop(2).Row(row =>
        {
            row.ConstantItem(126).Text(label);
            row.RelativeItem().BorderBottom(0.5f).PaddingLeft(2).Text(value ?? "").ClampLines(1);
        });

    // ─── Values ─────────────────────────────────────────────────────────

    private static string Rm(decimal v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Blank(decimal? v) => v is null or 0 ? "" : Rm(v.Value);

    private static string Details(IEnumerable<string> labels)
    {
        var joined = string.Join(", ", labels);
        return joined.Length == 0 ? "............................" : joined;
    }

    private static bool IsIc(AnnualEmployeeRow e) => e.IdType is null or IdType.NRIC;

    // A9 is only filled when the employment began or ended inside the year.
    private static string? Commenced(AnnualEmployeeRow e, int year) =>
        e.JoinDate is { } d && d.Year == year ? Dmy(d) : null;

    private static string? Ceased(AnnualEmployeeRow e, int year) =>
        e.LeaveDate is { } d && d.Year == year ? Dmy(d) : null;

    private static string Dmy(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // The form prints the "E" itself; the box holds the number after it.
    private static string EmployerNoE(string? tin)
    {
        var v = Clean(tin);
        if (v is null) return "";
        return v.StartsWith('E') || v.StartsWith('e') ? v[1..].TrimStart(' ', '-') : v;
    }

    private static (string?, string?) Address(Entities.PayrollCompanyInfo? info)
    {
        if (info is null) return (null, null);
        var street = string.Join(", ", new[] { info.AddressLine1, info.AddressLine2 }
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
        var town = string.Join(", ", new[]
        {
            string.Join(" ", new[] { info.Postcode, info.City }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())),
            info.State?.Trim(),
        }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return (street.Length == 0 ? null : street, town.Length == 0 ? null : town);
    }

    // The EA is dated where it is issued.
    private static DateTime MalaysiaToday() => DateTime.UtcNow.AddHours(8).Date;

    internal static string EmployerName(PayrollAnnualPayload payload) =>
        string.IsNullOrWhiteSpace(payload.CompanyInfo?.EmployerName)
            ? payload.OrganizationName
            : payload.CompanyInfo!.EmployerName!;
}
