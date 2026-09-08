using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static AltomateHR.Api.Modules.LhdnForms.Pdf.LhdnPdfShared;

namespace AltomateHR.Api.Modules.LhdnForms.Pdf;

// CP22 — Notification of New Employee by Employer (Pin.1/2021), subsection
// 83(2) of the Income Tax Act 1967. Forward-looking: uses only onboarding-time
// data already on the employee's profile, so — unlike the other four forms —
// it needs no payroll-run history and is fully populated today.
public static class Cp22Pdf
{
    public static byte[] Render(LhdnFormPayload p) =>
        Document.Create(doc => doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Header(h, p));
            page.Content().Element(c => Body(c, p));
            page.Footer().Element(f => Footer(f, "CP22", p.Employer.EmployerName ?? p.OrganizationName, p.Employee.Name));
        })).GeneratePdf();

    private static void Header(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            col.Item().Element(c => BrandHeader(c, p, "CP22", "Pin.1/2021"));
            col.Item().Element(c => TitleBlock(c,
                "NOTIFICATION OF NEW EMPLOYEE BY EMPLOYER",
                "Borang Pemberitahuan Oleh Majikan Bagi Pekerja Baharu",
                "Subsection 83(2) of the Income Tax Act 1967",
                "Submit within 30 days from the date of commencement of employment"));
        });

    private static void Body(IContainer container, LhdnFormPayload p) =>
        container.Column(col =>
        {
            var e = p.Employee;

            col.Item().Element(c => SectionHeader(c, "B. New Employee Particulars / Maklumat Pekerja Baharu"));
            col.Item().Element(c => KvRow(c, "B1. Full name / Nama penuh", e.Name));
            col.Item().Element(c => KvRowDual(c,
                "B2. Income tax no. (TIN)", e.IncomeTaxNumber,
                "B3. Identification no.", e.IdNumber));
            col.Item().Element(c => KvRowDual(c,
                "B4. Current passport no.", e.IdType == "PASSPORT" ? e.IdNumber : null,
                "B5. Passport registered with IRBM", null));
            col.Item().Element(c => KvRowDual(c,
                "B6. Citizenship / Warganegara", e.Nationality,
                "B7. Gender / Jantina", e.Gender is { Length: > 0 } g ? GenderLabels.GetValueOrDefault(g, g) : null));
            col.Item().Element(c => KvRowDual(c,
                "B8. Date of birth / Tarikh lahir", FmtDate(e.DateOfBirth),
                "B9. Marital status / Status perkahwinan",
                e.MaritalStatus is { Length: > 0 } m ? MaritalStatusLabels.GetValueOrDefault(m, m) : null));
            col.Item().Element(c => KvRowDual(c,
                "B10. Telephone no.", e.Phone,
                "B11. E-mail", e.AlternateEmail ?? e.Email));
            col.Item().Element(c => KvRowDual(c,
                "B12. Current residential address", JoinAddress(e.AddressLine1, e.AddressLine2,
                    $"{e.Postcode} {e.City}".Trim(), e.State),
                "B13. Correspondence address (same if blank)", null));
            col.Item().Element(c => KvRowDual(c,
                "B14. Commencement date", FmtDate(e.JoinDate),
                "B15. Designation / Jawatan", e.JobTitle));
            col.Item().Element(c => KvRowDual(c,
                "B16. Expected duration of employment", null,
                "B17. Nature of employment / Jenis pekerjaan", null));
            col.Item().PaddingTop(2).Text(
                "B16/B17 are not tracked in payroll. Fill in by hand (e.g. \"Permanent / Tetap\", \"Contract / Kontrak\").")
                .FontSize(8.5f).FontColor("#64748b");

            col.Item().Element(c => SectionHeader(c, "C. Spouse Particulars (If Married) / Maklumat Suami Isteri"));
            col.Item().Element(c => KvRow(c, "C1. Spouse full name", null));
            col.Item().Element(c => KvRowDual(c,
                "C2. Spouse ID / passport no.", e.SpouseIdNumber,
                "C3. Spouse income tax no.", e.SpousePcbNumber));
            col.Item().Element(c => KvRow(c, "C4. Spouse telephone no.", null));

            col.Item().Element(c => SectionHeader(c, "D. Monthly Remuneration / Maklumat Saraan Bulanan"));
            col.Item().Element(c => AmtRow(c, "D1. Salary, wages, overtime / Gaji, upah, kerja lebih masa", e.MonthlySalary));
            col.Item().Element(c => AmtRow(c, "D2. Leave pay / Gaji cuti", null));
            col.Item().Element(c => AmtRow(c, "D3. Commission and bonus / Komisen dan bonus", null));
            col.Item().Element(c => AmtRow(c, "D4. Cash allowances incl. tax borne by employer", e.FixedAllowancesTotal));
            col.Item().Element(c => AmtRow(c, "D5. Benefits-in-kind (BIK) subject to tax", null));
            col.Item().Element(c => AmtRow(c, "D6. Value of employer-provided accommodation", null));
            col.Item().Element(c => AmtRow(c, "D7. Allowances in kind (food, clothing, lodging, servants)", null));
            col.Item().Element(c => AmtRow(c, "D8. Other payments / Bayaran-bayaran lain", null));
            col.Item().Element(c => TotalAmtRow(c, "TOTAL / JUMLAH",
                (e.MonthlySalary ?? 0) + (e.FixedAllowancesTotal ?? 0)));
            col.Item().PaddingTop(2).Text(
                "D1 (basic salary) and D4 (cash allowances from the employee's recurring fixed allowances) are " +
                "auto-filled. Add D2, D3, D5–D8 by hand if applicable.").FontSize(8.5f).FontColor("#64748b");

            col.Item().Element(c => SectionHeader(c, "E. Previous Employer in Malaysia / Majikan Terdahulu"));
            col.Item().Element(c => KvRow(c, "E1. Employer name", null));
            col.Item().Element(c => KvRow(c, "E2. Employer address", null));
            if (e.PrevEmploymentYear is { } year && (e.PrevRemuneration is not null || e.PrevEpf is not null))
            {
                var grossPart = e.PrevRemuneration is not null ? $"gross RM {FmtRm(e.PrevRemuneration)}" : "";
                var sep = e.PrevRemuneration is not null && e.PrevEpf is not null ? ", " : "";
                var epfPart = e.PrevEpf is not null ? $"EPF RM {FmtRm(e.PrevEpf)}" : "";
                col.Item().PaddingTop(3).Text(
                    $"On-file previous-employer carry-over for {year}: {grossPart}{sep}{epfPart}. " +
                    "Fill in the previous employer's name and address by hand — only the YTD figures are stored " +
                    "on the profile.").FontSize(8.5f).FontColor("#64748b");
            }

            col.Item().Element(c => SectionHeader(c, "F. Authorised Officer Declaration / Akuan Pegawai"));
            col.Item().Element(c => SignatureBlock(c,
                left:
                [
                    ("Name / Nama:", p.Employer.DeclarantName),
                    ("ID / passport no.:", p.Employer.DeclarantIdNumber),
                ],
                right:
                [
                    ("Designation / Jawatan:", p.Employer.DeclarantPosition),
                    ("Date / Tarikh:", FmtDate(p.GeneratedAt)),
                ]));

            col.Item().Element(c => ClosingNote(c,
                $"Generated by AltomateHR on {FmtDate(p.GeneratedAt)}. Transcribe onto the official LHDN CP22 form " +
                $"before submission. Submit within 30 days of " +
                $"{(e.JoinDate is not null ? FmtDate(e.JoinDate) : "the commencement date")}."));
        });
}
