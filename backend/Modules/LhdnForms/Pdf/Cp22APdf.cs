using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static AltomateHR.Api.Modules.LhdnForms.Pdf.LhdnPdfShared;

namespace AltomateHR.Api.Modules.LhdnForms.Pdf;

// CP22A — Notification of Cessation of Employment, Private Sector (Pin.1/2023),
// subsection 83(3) of the Income Tax Act 1967. Archived employees only — the
// remuneration section is year-to-date, so it's blank until this app has a
// payroll-run engine.
public static class Cp22APdf
{
    public static byte[] Render(LhdnFormPayload p) =>
        Document.Create(doc => doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Header(h, p));
            page.Content().Element(c => Body(c, p));
            page.Footer().Element(f => Footer(f, "CP22A", p.Employer.EmployerName ?? p.OrganizationName, p.Employee.Name));
        })).GeneratePdf();

    private static void Header(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            col.Item().Element(c => BrandHeader(c, p, "CP22A", "Pin.1/2023"));
            col.Item().Element(c => TitleBlock(c,
                "NOTIFICATION OF CESSATION OF EMPLOYMENT (PRIVATE SECTOR)",
                "Borang Pemberitahuan Pemberhentian Kerja (Swasta)",
                "Subsection 83(3) of the Income Tax Act 1967",
                "Submit at least 30 days before cessation or within 30 days of death notification"));
        });

    private static void Body(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            var e = p.Employee;
            var reason = (e.ArchiveReason ?? "").ToLowerInvariant();
            var probablyRetired = Regex.IsMatch(reason, "retir|persaraan|bersara");
            var probablyDied = Regex.IsMatch(reason, "died|kematian|meninggal");
            var probablyCeased = !probablyRetired && !probablyDied;

            col.Item().Element(c => SectionHeader(c, "A. Particulars of Employee Who Ceased / Retired / Died"));
            col.Item().Element(c => KvRow(c, "1. Full name / Nama penuh", e.Name));
            col.Item().PaddingVertical(1.5f).Column(cc =>
            {
                cc.Item().Text("2. Type of cessation / Jenis pemberhentian").FontSize(9);
                cc.Item().Element(c => CheckOptions(c,
                    ("Berhenti kerja / Ceased", probablyCeased),
                    ("Bersara / Retired", probablyRetired),
                    ("Meninggal dunia / Died", probablyDied)));
            });
            col.Item().Element(c => KvRowDual(c,
                "3. Date commenced / Tarikh mula bekerja", FmtDate(e.JoinDate),
                "4. Date of cessation / Tarikh berhenti", FmtDate(e.LeaveDate)));
            col.Item().Element(c => KvRow(c, "5. Date employer received notice of death (death cases only)", null));
            col.Item().PaddingVertical(1.5f).Column(cc =>
            {
                cc.Item().Text("6. Type of retirement").FontSize(9);
                cc.Item().Element(c => CheckOptions(c, ("Wajib / Mandatory", false), ("Pilihan / Optional", false)));
            });
            col.Item().PaddingVertical(1.5f).Column(cc =>
            {
                cc.Item().Text("7. Tax borne by employer").FontSize(9);
                cc.Item().Element(c => CheckOptions(c,
                    ("Ya / Yes", e.PcbBorneByEmployer), ("Tidak / No", !e.PcbBorneByEmployer)));
            });
            col.Item().PaddingVertical(1.5f).Column(cc =>
            {
                cc.Item().Text("8. Received offer of VSS / Skim pemberhentian").FontSize(9);
                cc.Item().Element(c => CheckOptions(c, ("Ya / Yes", false), ("Tidak / No", false)));
            });
            col.Item().Element(c => KvRowDual(c,
                "9. ID / passport no.", e.IdNumber,
                "10. Tax Identification No. (TIN)", e.IncomeTaxNumber));
            col.Item().Element(c => KvRowDual(c,
                "11. Date of birth", FmtDate(e.DateOfBirth),
                "12. Marital status",
                e.MaritalStatus is { Length: > 0 } m ? MaritalStatusLabels.GetValueOrDefault(m, m) : null));
            if (e.QualifyingChildren > 0)
                col.Item().Element(c => KvRowDual(c,
                    "13a. Number of qualifying children", e.QualifyingChildren.ToString(),
                    "13b. Total child-relief claim (RM)", FmtRm(e.AnnualChildRelief)));
            col.Item().Element(c => KvRow(c, "14. Spouse full name (if married)", null));
            col.Item().Element(c => KvRowDual(c,
                "15. Employee telephone no.", e.Phone,
                "16b. Employee e-mail", e.AlternateEmail ?? e.Email));
            col.Item().Element(c => KvRow(c, "16a. Current correspondence address",
                JoinAddress(e.AddressLine1, e.AddressLine2, $"{e.Postcode} {e.City}".Trim(), e.State)));
            col.Item().PaddingTop(2).Text(
                "17. Legal representative (for death cases) — name, ID, relationship, address, phone. Fill in by " +
                "hand if applicable.").FontSize(8.5f).FontColor("#64748b");

            col.Item().Element(c => SectionHeader(c, "B. Remuneration Particulars (YTD to Cessation Date) / Butir-butir Saraan"));
            if (!p.HasPayrollHistory) col.Item().Element(PayrollHistoryDisclaimer);
            col.Item().PaddingBottom(3).Text(
                $"From 1 January {p.Year} to {(e.LeaveDate is not null ? FmtDate(e.LeaveDate) : "cessation date")}. RM.")
                .FontSize(8.5f).FontColor("#64748b");
            col.Item().Element(c => AmtRow(c, "1. Salary, wages, overtime", p.Ytd.GrossSalary));
            col.Item().Element(c => AmtRow(c, "2-3. Leave pay / commission / bonus", p.Ytd.BonusAndCommission));
            col.Item().Element(c => AmtRow(c, "4. Gratuity (incl. tax-exempt portion)", null));
            col.Item().Element(c => AmtRow(c, "5. Compensation for loss of employment (incl. tax-exempt portion)", null));
            col.Item().Element(c => AmtRow(c, "6. Cash allowances incl. tax borne by employer", e.FixedAllowancesTotal));
            col.Item().Element(c => AmtRow(c, "7. Pension from employer", null));
            col.Item().Element(c => AmtRow(c, "8. BIK subject to tax", p.Ytd.TotalBik));
            col.Item().Element(c => AmtRow(c, "9. Value of employer-provided accommodation", null));
            col.Item().Element(c => AmtRow(c, "10. Allowances in kind (food, clothing, lodging, servants)", null));
            col.Item().Element(c => AmtRow(c, "11. Car and driver", null));
            col.Item().Element(c => AmtRow(c, "12. Other payments", null));
            col.Item().Element(c => AmtRow(c, "13. ESOS / ESPP share scheme benefits", null));
            col.Item().Element(c => TotalAmtRow(c, "TOTAL / JUMLAH",
                p.Ytd.GrossSalary + p.Ytd.BonusAndCommission + p.Ytd.TotalBik));

            col.Item().Element(c => SectionHeader(c, "C. Unreported Income from Preceding Years"));
            col.Item().PaddingBottom(3).Text(
                $"Fill in by hand if any backdated bonus or arrears were paid for years before {p.Year}.")
                .FontSize(8.5f).FontColor("#64748b");
            col.Item().Element(PreceedingYearsTable);

            col.Item().Element(c => SectionHeader(c, "D. Other Particulars"));
            col.Item().Element(c => AmtRow(c, "1. Amount withheld by employer pending tax clearance (RM)", null));
            col.Item().Element(c => AmtRow(c, "2. Total MTD (PCB) paid to LHDNM this year", p.Ytd.TotalPcb));
            col.Item().Element(c => AmtRow(c, "3. Total zakat deducted this year", p.Ytd.TotalZakat));
            col.Item().Element(c => AmtRow(c, "4. Employee EPF or approved-fund contributions", p.Ytd.TotalEpfEmployee));

            col.Item().Element(c => SectionHeader(c, "E. Authorised Officer Declaration"));
            col.Item().Element(c => SignatureBlock(c,
                left:
                [
                    ("Name / Nama:", p.Employer.DeclarantName),
                    ("Designation / Jawatan:", p.Employer.DeclarantPosition),
                ],
                right:
                [
                    ("E-mail address:", p.Employer.Email),
                    ("Date / Tarikh:", FmtDate(p.GeneratedAt)),
                ]));

            col.Item().Element(c => ClosingNote(c,
                $"Generated by AltomateHR on {FmtDate(p.GeneratedAt)}. Transcribe onto the official LHDN CP22A form " +
                "before submission. LHDN requires the final salary to be withheld for 90 days OR until tax " +
                "clearance is issued, whichever comes first — HR handles the actual withholding outside the " +
                "payroll system."));
        });

    private static void PreceedingYearsTable(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(0.6f);
                c.RelativeColumn(1.5f);
                c.RelativeColumn(1);
                c.RelativeColumn(1.2f);
                c.RelativeColumn(1);
            });
            table.Header(h =>
            {
                Head(h, "#");
                Head(h, "Type of Income");
                Head(h, "Period");
                Head(h, "Income (RM)", right: true);
                Head(h, "EPF (RM)", right: true);
            });
            for (var i = 0; i < 3; i++)
            {
                Cell(table).Text((i + 1).ToString()).FontSize(8.5f);
                Cell(table).Text("").FontSize(8.5f);
                Cell(table).Text("").FontSize(8.5f);
                Cell(table).Text("").FontSize(8.5f);
                Cell(table).Text("").FontSize(8.5f);
            }
        });

    private static void Head(TableCellDescriptor h, string text, bool right = false)
    {
        var cell = h.Cell().Background("#f8fafc").PaddingVertical(4).PaddingHorizontal(3).BorderBottom(1).BorderColor("#cbd5e1");
        (right ? cell.AlignRight() : cell).Text(text).FontSize(8.5f).Bold();
    }

    private static IContainer Cell(TableDescriptor t) =>
        t.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3).PaddingHorizontal(3);
}
