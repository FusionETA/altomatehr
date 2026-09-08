using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static AltomateHR.Api.Modules.LhdnForms.Pdf.LhdnPdfShared;

namespace AltomateHR.Api.Modules.LhdnForms.Pdf;

// CP21 — Notification of Employee's Departure from Malaysia (Pin.1/2021),
// subsection 83(4) of the Income Tax Act 1967. Archived employees only.
// Travel-specific fields (expected departure date, place of birth, reason,
// overseas address, return date) aren't modelled on the employee profile —
// they render as "—" for the admin to fill in by hand, same as the reference
// this was ported from.
public static class Cp21Pdf
{
    public static byte[] Render(LhdnFormPayload p) =>
        Document.Create(doc => doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Header(h, p));
            page.Content().Element(c => Body(c, p));
            page.Footer().Element(f => Footer(f, "CP21", p.Employer.EmployerName ?? p.OrganizationName, p.Employee.Name));
        })).GeneratePdf();

    private static void Header(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            col.Item().Element(c => BrandHeader(c, p, "CP21", "Pin.1/2021"));
            col.Item().Element(c => TitleBlock(c,
                "NOTIFICATION OF EMPLOYEE'S DEPARTURE FROM MALAYSIA",
                "Borang Pemberitahuan Pekerja Yang Hendak Meninggalkan Malaysia",
                "Subsection 83(4) of the Income Tax Act 1967",
                "Submit at least 30 days before expected date of departure"));
        });

    private static void Body(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            var e = p.Employee;

            col.Item().Element(c => SectionHeader(c, "A. Employee Particulars / Butir-butir Pekerja"));
            col.Item().Element(c => KvRow(c, "1. Full name / Nama Penuh", e.Name));
            col.Item().Element(c => KvRowDual(c,
                "2. Date commenced / Tarikh Mula Bekerja", FmtDate(e.JoinDate),
                "3. Expected date to leave Malaysia", null));
            col.Item().Element(c => KvRowDual(c,
                "4. ID no. (IC / Police / Army / Passport)", e.IdNumber,
                "5. Income tax no. (TIN)", e.IncomeTaxNumber));
            col.Item().Element(c => KvRowDual(c,
                "6. Citizenship / Warganegara", e.Nationality,
                "7. Date of birth / Tarikh Lahir", FmtDate(e.DateOfBirth)));
            col.Item().Element(c => KvRowDual(c,
                "8. Place of birth / Tempat Lahir", null,
                "9. Nature of employment / Jenis Pekerjaan", e.JobTitle));
            col.Item().Element(c => KvRowDual(c,
                "10. Telephone no.", e.Phone,
                "12. Reason for departure / Alasan Meninggalkan", null));
            col.Item().Element(c => KvRow(c, "11. Current address in Malaysia",
                JoinAddress(e.AddressLine1, e.AddressLine2, $"{e.Postcode} {e.City}".Trim(), e.State)));
            col.Item().Element(c => KvRow(c, "13. Correspondence address outside Malaysia", null));
            col.Item().Element(c => KvRow(c, "14. If returning to Malaysia, expected date of return", null));
            col.Item().PaddingTop(2).Text(
                "Lines 3, 8, 12–14 are travel-specific and not stored on the payroll profile. Fill in by hand " +
                "before submitting.").FontSize(8.5f).FontColor("#64748b");

            col.Item().Element(c => SectionHeader(c, "B. Remuneration Particulars / Butir-butir Saraan"));
            if (!p.HasPayrollHistory) col.Item().Element(PayrollHistoryDisclaimer);
            col.Item().PaddingBottom(3).Text(
                $"If not returning, state emoluments + approved-fund contributions for the year of departure " +
                $"({p.Year}). RM.").FontSize(8.5f).FontColor("#64748b");
            col.Item().Element(c => AmtRow(c, "1. Salary, wages, overtime / Gaji, upah, kerja lebih masa", p.Ytd.GrossSalary));
            col.Item().Element(c => AmtRow(c, "2-3. Leave pay / commission / bonus", p.Ytd.BonusAndCommission));
            col.Item().Element(c => AmtRow(c, "4. Gratuity / Ganjaran", null));
            col.Item().Element(c => AmtRow(c, "5. Compensation for loss of employment", null));
            col.Item().Element(c => AmtRow(c, "6. Cash allowances incl. tax borne by employer", e.FixedAllowancesTotal));
            col.Item().Element(c => AmtRow(c, "7. Pension from employer", null));
            col.Item().Element(c => AmtRow(c, "8. BIK subject to tax", p.Ytd.TotalBik));
            col.Item().Element(c => AmtRow(c, "9. Value of employer-provided accommodation", null));
            col.Item().Element(c => AmtRow(c, "10. Allowances in kind (food, clothing, lodging, servants)", null));
            col.Item().Element(c => AmtRow(c, "11. Other payments", null));
            col.Item().Element(c => TotalAmtRow(c, "TOTAL / JUMLAH",
                p.Ytd.GrossSalary + p.Ytd.BonusAndCommission + p.Ytd.TotalBik));

            col.Item().Element(c => SectionHeader(c, "C. Income of Preceding Years Not Declared"));
            col.Item().PaddingBottom(3).Text($"Fill in by hand if any preceding-year income was paid out in {p.Year}.")
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
                $"Generated by AltomateHR on {FmtDate(p.GeneratedAt)}. Transcribe onto the official LHDN CP21 form " +
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
                c.RelativeColumn(1.2f);
                c.RelativeColumn(1);
                c.RelativeColumn(1);
            });
            table.Header(h =>
            {
                Head(h, "#");
                Head(h, "Type of Income");
                Head(h, "Year for which Paid");
                Head(h, "Income (RM)", right: true);
                Head(h, "EPF (RM)", right: true);
            });
            for (var i = 0; i < 3; i++)
            {
                Cell(table).Text($"{i + 1}.").FontSize(8.5f);
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
