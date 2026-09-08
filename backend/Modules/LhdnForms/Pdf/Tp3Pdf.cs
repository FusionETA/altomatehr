using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static AltomateHR.Api.Modules.LhdnForms.Pdf.LhdnPdfShared;

namespace AltomateHR.Api.Modules.LhdnForms.Pdf;

// PCB/TP3 — Individual Tax Deduction and Rebate Claim Form (1/2026), Income
// Tax (Deductions from Remuneration) Regulations 1994. Hands YTD income/EPF/
// zakat/PCB figures to an employee's next employer so PCB carries over
// correctly. Section D (17 personal reliefs) is left blank for the employee
// to self-declare — this app doesn't hold the underlying receipts.
public static class Tp3Pdf
{
    private static readonly string[] Reliefs =
    [
        "D1 Parents medical / dental / check-up (8,000)",
        "D2 Basic support equipment for disabled self/spouse/child/parents (6,000)",
        "D3 Self study fees / Master's / skills enhancement (7,000)",
        "D4 Serious illness / fertility / vaccination / dental / check-up (10,000)",
        "D5 Lifestyle — books / PC / smartphone / internet / self-improvement (2,500)",
        "D6 Lifestyle — sport equipment / gym / training / facility rental (1,000)",
        "D7 Breastfeeding equipment (1,000)",
        "D8 Childcare centre / kindergarten fees (3,000)",
        "D9 National Education Savings Scheme (8,000)",
        "D10 Alimony to ex-wife (4,000)",
        "D11 Voluntary EPF + life insurance (7,000 combined)",
        "D12 Private retirement schemes + deferred annuities (3,000)",
        "D13 Education and medical insurance (4,000)",
        "D14 Contributions to PERKESO / EIS (350)",
        "D15 EV charging equipment / food-waste composter (2,500)",
        "D16 First-home loan interest (7,000 or 5,000 by price band)",
        "D17 Domestic tourism — entry fees to tourist / cultural centres (1,000)",
    ];

    public static byte[] Render(LhdnFormPayload p) =>
        Document.Create(doc => doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Header(h, p));
            page.Content().Element(c => Body(c, p));
            page.Footer().Element(f => Footer(f, "TP3", p.Employer.EmployerName ?? p.OrganizationName, p.Employee.Name));
        })).GeneratePdf();

    private static void Header(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            col.Item().Element(c => BrandHeader(c, p, "PCB FORM / TP3", "1/2026"));
            col.Item().Element(c => TitleBlock(c,
                "INDIVIDUAL TAX DEDUCTION AND REBATE CLAIM FORM",
                "For Monthly Tax Deduction (PCB) Purposes",
                "Income Tax (Deductions from Remuneration) Regulations 1994",
                $"Year of Assessment: {p.Year}"));
        });

    private static void Body(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            var e = p.Employee;
            var accumulatedGross = p.Ytd.GrossSalary + p.Ytd.BonusAndCommission + p.Ytd.TotalBik;

            col.Item().Element(c => SectionHeader(c, "SECTION A: Employer Information"));
            col.Item().PaddingBottom(3).Text(
                "AltomateHR pre-fills our company as \"Previous Employer 1\". If the employee worked elsewhere " +
                "earlier in the year, they should add that employer in lines A3 / A4 by hand.")
                .FontSize(8.5f).FontColor("#64748b").Italic();
            col.Item().Element(c => KvRow(c, "A1. Previous Employer Name 1", p.Employer.EmployerName ?? p.OrganizationName));
            col.Item().Element(c => KvRow(c, "A2. Tax Identification Number (TIN)", p.Employer.EmployerTin));
            col.Item().Element(c => KvRow(c, "A3. Previous Employer Name 2", null));
            col.Item().Element(c => KvRow(c, "A4. Tax Identification Number (TIN)", null));

            col.Item().Element(c => SectionHeader(c, "SECTION B: Employee Information"));
            col.Item().Element(c => KvRow(c, "B1. Name", e.Name));
            col.Item().Element(c => KvRow(c, "B2. Identity Card / Passport Number", e.IdNumber));
            col.Item().Element(c => KvRow(c, "B3. Tax Identification Number (TIN)", e.IncomeTaxNumber));

            col.Item().Element(c => SectionHeader(c, "SECTION C: Remuneration / EPF / Zakat / PCB"));
            if (!p.HasPayrollHistory) col.Item().Element(PayrollHistoryDisclaimer);
            col.Item().PaddingBottom(3).Text(
                $"Accumulated deductions for the period worked at this employer during {p.Year}. RM.")
                .FontSize(8.5f).FontColor("#64748b");
            col.Item().Element(c => AmtRow(c,
                "C1. Total monthly gross + additional remuneration (incl. allowances / perquisites / gifts / BIK)",
                accumulatedGross));
            col.Item().Element(c => AmtRow(c, "C2. Tax-exempt allowances / perquisites / gifts / benefits", null));
            col.Item().PaddingLeft(12).PaddingTop(1).Text(
                "C2 i–v: travel allowance, child-care allowance, employer's discounted products, long-service " +
                "awards, other tax-exempt benefits. AltomateHR does not categorise tax-exempt portions separately " +
                "— the employee fills these in by hand if any.").FontSize(8.5f).FontColor("#64748b");
            col.Item().Element(c => AmtRow(c, "C3. Total approved EPF contributions (employee share)", p.Ytd.TotalEpfEmployee));
            col.Item().Element(c => AmtRow(c, "C4 i). Total Zakat (via payroll)", p.Ytd.TotalZakat));
            col.Item().Element(c => AmtRow(c, "C4 ii). Relief for departure levy (Umrah / religious travel)", null));
            col.Item().Element(c => AmtRow(c, "C5. Total PCB (excluding CP38)", p.Ytd.TotalPcb));

            col.Item().Element(c => SectionHeader(c, "SECTION D: Personal Reliefs"));
            col.Item().PaddingBottom(4).Text(
                "D1–D17 cover personal tax reliefs (medical / lifestyle / sport / insurance / EPF / home-loan " +
                "interest etc.). These are self-declared by the employee — they should fill in any relevant " +
                "amounts in the official TP3 form before passing it on. We leave them blank here because we " +
                "don't hold the underlying receipts.").FontSize(8.5f).FontColor("#64748b");
            col.Item().Border(0.5f).BorderColor("#cbd5e1").Padding(8).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8.5f).LineHeight(1.5f));
                foreach (var relief in Reliefs) t.Line(relief);
            });

            col.Item().Element(c => SectionHeader(c, "SECTION E: Employee Declaration"));
            col.Item().Text(
                "I acknowledge that all the information stated in this form is true, correct, and complete. If " +
                "found false, legal action may be taken under paragraph 113(1)(b) of the Income Tax Act 1967.")
                .FontSize(8.5f);
            col.Item().Element(c => SignatureBlock(c,
                left: [("Employee signature:", e.Name)],
                right: [("Date:", null)]));

            col.Item().Element(c => ClosingNote(c,
                $"Generated by AltomateHR on {FmtDate(p.GeneratedAt)} for the year of assessment {p.Year}. The " +
                "employee should review Section C, fill in any applicable Section D reliefs, sign Section E, and " +
                "hand to their next employer. The next employer keeps this form for 7 years and submits to HASiL " +
                "on request."));
        });
}
