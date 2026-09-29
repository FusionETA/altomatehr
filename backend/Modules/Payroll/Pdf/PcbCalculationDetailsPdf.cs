using AltomateHR.Api.Modules.Employees.Entities;   // SpecialTaxScheme
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// The LHDN MTD §E worksheet — one A4 page per employee, showing the whole PCB
// calculation in the layout the official form uses: each variable with its
// abbreviation, LHDN's own description, and its amount, plus the formula
// expansions for P, Yearly Tax and Current Month PCB written out with the
// numbers substituted in.
//
// The point is that an employee — or an LHDN officer — can take this page, do
// the arithmetic by hand, and land on the figure that was deducted. Every
// number therefore comes from the payslip's stored `PcbBreakdown` SNAPSHOT.
//
// ⚠ Never recompute the breakdown here. The snapshot is the month's law;
// running today's engine over a historical payslip is how a filed figure
// quietly changes months after it was filed.
public static class PcbCalculationDetailsPdf
{
    public const string ContentType = "application/pdf";

    public static byte[] Render(PcbDetailsModel model) => Build(model).GeneratePdf();

    // The document itself, for rasterising in a visual check.
    public static IDocument Build(PcbDetailsModel model) =>
        Document.Create(doc =>
        {
            foreach (var employee in model.Employees)
            {
                doc.Page(page =>
                {
                    PayrollPdfShared.Frame(page);
                    page.Header().Element(c => Header(c, model, employee));
                    page.Content().Element(c => Body(c, employee));
                    page.Footer().Element(c => PayrollPdfShared.Footer(
                        c, $"{model.OrganizationName} · PCB calculation details · {model.PeriodLabel}"));
                });
            }

            // QuestPDF will not produce a document with no pages, and a run
            // with no payslips is a real state (created but never generated).
            if (model.Employees.Count == 0)
            {
                doc.Page(page =>
                {
                    PayrollPdfShared.Frame(page);
                    page.Header().Element(c => Header(c, model, null));
                    page.Content().PaddingTop(20).Text(
                            "This run has no payslips yet. Generate the run to produce PCB calculation details.")
                        .FontSize(9).FontColor(PayrollPdfShared.Muted);
                });
            }
        });

    // The navy bar is the form's own signature — an officer recognises the
    // document before reading a word of it.
    private const string HeaderBar = "#1f3a5f";

    private static void Header(IContainer container, PcbDetailsModel model, PcbDetailsEmployee? employee) =>
        container.Column(column =>
        {
            column.Item().Background(HeaderBar).PaddingVertical(8).PaddingHorizontal(12).Row(row =>
            {
                row.RelativeItem().Text("PCB Calculation Details")
                    .FontSize(12).Bold().FontColor("#ffffff");
                row.ConstantItem(150).AlignRight().Text(model.OrganizationName)
                    .FontSize(8.5f).FontColor("#dbeafe");
            });

            if (employee is null) return;

            column.Item().PaddingTop(8).Text(employee.Name).FontSize(11).Bold();
            column.Item().PaddingBottom(6).Text(Subtitle(model, employee))
                .FontSize(8).FontColor(PayrollPdfShared.Muted);
        });

    private static string Subtitle(PcbDetailsModel model, PcbDetailsEmployee employee)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(employee.Position)) parts.Add(employee.Position!);
        if (!string.IsNullOrWhiteSpace(employee.EmployeeCode)) parts.Add(employee.EmployeeCode!);
        parts.Add(model.PeriodLabel);

        return string.Join(" · ", parts);
    }

    private static void Body(IContainer container, PcbDetailsEmployee employee) =>
        container.Column(column =>
        {
            var breakdown = employee.Breakdown;

            // A payslip generated before the breakdown existed, or one whose
            // snapshot failed to parse. Say so rather than printing zeroes —
            // a page of zeroes reads as "no tax due", which is a different
            // and much worse claim than "we do not have the working".
            if (breakdown is null)
            {
                column.Item().PaddingTop(12).Text(
                        "PCB calculation snapshot not available. Regenerate the payroll run to populate this report.")
                    .FontSize(9).FontColor(PayrollPdfShared.Muted);
                return;
            }

            if (breakdown.Formula == PcbFormula.NonResident)
            {
                NonResident(column, breakdown, employee.CurrentMonthZakat, employee.VoluntaryPcb);
                return;
            }

            SectionPcbA(column, breakdown);

            // Sections 2–4 only exist because of additional remuneration. On a
            // clean month there is no bonus to explain, and printing PCB(B)
            // and a zero PCB(C) invites the reader to look for a deduction
            // that was never made.
            if (breakdown.Ar is { } ar)
            {
                SectionYearlyPcb(column, breakdown, ar);
                SectionYearlyTax(column, breakdown, ar);
                SectionArPcb(column, ar, breakdown.Z);
            }

            SectionNetPcb(column, breakdown, employee.CurrentMonthZakat, employee.VoluntaryPcb);
            SectionAllowableDeductions(column, breakdown);
        });

    // Non-residents are a flat withholding with no reliefs and no bands.
    // Printing an M, an R or a relief here would imply the 30% came from
    // somewhere it did not.
    private static void NonResident(ColumnDescriptor column, PcbBreakdown b, decimal zakat, decimal voluntaryPcb)
    {
        SectionTitle(column, "Non-resident — flat-rate withholding");

        column.Item().PaddingBottom(6).Text(
                $"Non-residents are taxed at a flat {b.Rate * 100m:0.##}% of remuneration under the LHDN MTD "
                + "specification. Reliefs, rebates and the progressive bands do not apply, so the variable "
                + "breakdown below is not applicable to this employee.")
            .FontSize(8.5f).FontColor(PayrollPdfShared.Muted);

        Variable(column, "Normal remuneration", "Current month's normal remuneration.", b.NormalTaxable);
        Variable(column, "PCB — normal", "Flat-rate withholding on normal remuneration.", b.PcbNormal);
        Variable(column, "Additional remuneration",
            "Bonus, commission, arrears and other one-off payments this month.", b.AdditionalTaxable);
        Variable(column, "PCB — additional",
            "Flat-rate withholding on additional remuneration.", b.PcbAdditional);
        foreach (var row in NetPcbRows(b.PcbTotal, zakat, voluntaryPcb))
        {
            Variable(column, row.Label, row.Description, row.Amount, bold: row.Bold);
        }
    }

    // ─── 1. PCB(A) — the normal monthly deduction ───────────────────────

    private static void SectionPcbA(ColumnDescriptor column, PcbBreakdown b)
    {
        SectionTitle(column,
            "1. PCB on yearly net remuneration excluding the additional remuneration");
        Subsection(column, "PCB (A) / Net PCB");
        Formula(column, "[(P - M)R + B - (Z + X)] / (n + 1) - Zakat/Fitrah/Levy for current month");

        Intro(column, "P · Total chargeable income for a year excluding additional remuneration");
        Formula(column,
            "[Σ(Y-K) + (Y₁ - K₁) + (Y₂ - [K₂ × n])] - (D + S + Du + Su + Q×C + ΣLP + LP₁)");

        Variable(column, "Σ(Y-K)",
            "Total accumulated net remuneration including net additional remuneration which has been paid "
            + "to an employee until before current month including net remuneration which has been paid by "
            + "previous employer (if any).",
            b.Y - b.K);
        Variable(column, "Y",
            "Total monthly gross remuneration and additional remuneration which has been paid including "
            + "monthly gross remuneration paid by previous employer (if any).", b.Y);
        Variable(column, "K",
            "Total contribution to EPF or Other Approved Funds made on all remuneration (monthly "
            + "remuneration, additional remuneration and remuneration from previous employer on current "
            + "year) that was paid (including premium claimed under previous employment, if any) not "
            + "exceeding RM4,000.00 per year.", b.K);
        Variable(column, "Y₁", "Current month's normal remuneration.", b.Y1);
        Variable(column, "K₁",
            "Contribution to EPF or Other Approved Funds paid subject to total qualifying amount for "
            + "current month's remuneration not exceeding RM4,000.00 per year.", b.K1);
        Variable(column, "Y₂", "Estimated remuneration as per Y₁ for the following months.", b.Y2);
        Variable(column, "K₂",
            "Estimated balance of total contribution to EPF or other Approved Scheme paid for the "
            + "qualifying monthly balance [(RM4,000 (limited) - (K + K₁ + Kt)) ÷ n] or K₁, whichever is lower.",
            b.K2);
        Variable(column, "n", "Remaining working month in a year.", b.N, raw: true);

        Variable(column, "D", "Deduction for individual.", b.D);
        Variable(column, "S", "Deduction for spouse.", b.S);
        Variable(column, "Du", "Deduction for disabled individual.", b.Du);
        Variable(column, "Su", "Deduction for disabled spouse.", b.Su);
        Variable(column, "Q", "Deduction per eligible child.", b.Q);
        Variable(column, "C", "Number of eligible children.", b.C, raw: true);
        Variable(column, "ΣLP",
            "Other accumulated allowable deductions including from previous employment (if any).", b.SumLp);
        Variable(column, "LP₁", "Other allowable deductions for current month.", b.Lp1);

        // A 15% approval swaps "(P − M)R + B" for "P × 15% − T" (MTD Spec
        // D.b.3–5). Said plainly, so a reader does not look for a band.
        if (b.SpecialTaxScheme is { } scheme)
        {
            column.Item().PaddingBottom(6).Text(
                    $"Taxed at a flat 15% under {SchemeName(scheme)} (LHDN MTD specification, "
                    + $"{SchemeSection(scheme)}): Monthly Tax Deduction = [(P × R − T) − (Z + X)] ÷ (n + 1). "
                    + "M is 0 and B is −T, the individual and spouse rebate"
                    + (scheme == SpecialTaxScheme.C_SUITE
                        ? ", which does not apply to this category."
                        : " allowed when P is RM35,000 or less."))
                .FontSize(8.5f).FontColor(PayrollPdfShared.Muted);
        }

        var p = PBeforeFloor(b);
        Variable(column, "P",
            p < 0m
                ? ExpandP(b) + " — negative: the reliefs exceed the normal remuneration, so no tax is due on it "
                  + "and the yearly tax below takes P as 0.00. An additional remuneration is added to this figure "
                  + "(Section 3)."
                : ExpandP(b),
            p, bold: true);
        Variable(column, "M",
            "Amount of first chargeable income for every range of chargeable income a year.", b.M);
        Variable(column, "R", $"Percentage of tax rates. ({b.R * 100m:0.00}%)", b.R, raw: true);
        Variable(column, "B",
            b.SpecialTaxScheme is null
                ? "Amount of tax on M less tax rebate for individual and spouse (if qualified)."
                : "−T: the individual and spouse rebate (if qualified).",
            b.B);
        Variable(column, "Z",
            "Accumulated Zakat/Fitrah/Levy paid other than Zakat/Fitrah/Levy for current month.", b.Z);
        Variable(column, "X",
            "Accumulated PCB paid in respect of previous month(s) (excluding the current month).", b.X);

        Variable(column, "Yearly Tax",
            $"(P - M)R + B = ({Amount(b.P)} - {Amount(b.M)}) × {b.R:0.00} + ({Amount(b.B)})",
            b.YearlyTax, bold: true);
        Variable(column, "PCB(A)",
            $"Current Month PCB = (Yearly Tax - Z - X) ÷ (n + 1) = ({Amount(b.YearlyTax)} - {Amount(b.Z)} "
            + $"- {Amount(b.X)}) ÷ {b.N + 1}",
            b.CurrentMonthPcb, bold: true);
    }

    // P as the formula gives it, before the floor at zero — what Section 3 adds
    // the additional remuneration to. Snapshots taken before PBeforeFloor was
    // recorded only kept the floored P; for those, a floored (zero) P is shown
    // as the arithmetic printed beside it evaluates, and only when that is
    // negative, so the page still reads true without re-running the month.
    public static decimal PBeforeFloor(PcbBreakdown b)
    {
        if (b.PBeforeFloor is { } recorded) return recorded;
        if (b.P > 0m) return b.P;

        var derived = (b.Y - b.K) + (b.Y1 - b.K1) + (b.Y2 - b.K2 * b.N)
            - (b.D + b.S + b.Du + b.Su + b.Q * b.C + b.SumLp + b.Lp1);
        return derived < 0m ? Money.Round2(derived) : b.P;
    }

    // The arithmetic for P, written out with the numbers substituted, so the
    // reader can re-derive it from the primitives above rather than trusting it.
    private static string ExpandP(PcbBreakdown b) =>
        $"[{Amount(b.Y - b.K)} + ({Amount(b.Y1)} - {Amount(b.K1)}) + ({Amount(b.Y2)} - [{Amount(b.K2)} × {b.N}])] "
        + $"- [{Amount(b.D)} + {Amount(b.S)} + {Amount(b.Du)} + {Amount(b.Su)} "
        + $"+ ({Amount(b.Q)} × {b.C:0.##}) + {Amount(b.SumLp)} + {Amount(b.Lp1)}]";

    // ─── 2. PCB(B) — the annual projection ──────────────────────────────

    private static void SectionYearlyPcb(ColumnDescriptor column, PcbBreakdown b, PcbArBreakdown ar) =>
        column.Item().ShowEntire().Column(section =>
        {
        SectionTitle(section, "2. Yearly PCB");
        Subsection(section, "PCB (B)");
        Formula(section, "(X) + [Current Month PCB × (n + 1)]");
        Formula(section, $"({Amount(b.X)}) + [{Amount(b.CurrentMonthPcb)} × ({b.N} + 1)]");

        Variable(section, "PCB (B)",
            "Projected annual normal PCB — what the year's PCB would total if the employee earned this "
            + "month's normal PCB every remaining month, plus the amount already paid year to date.",
            ar.PcbB, bold: true);
    });

    // ─── 3. CS — the year's tax once the bonus is included ──────────────

    private static void SectionYearlyTax(ColumnDescriptor column, PcbBreakdown b, PcbArBreakdown ar) =>
        column.Item().ShowEntire().Column(section =>
        {
        SectionTitle(section, "3. Yearly Tax");
        Subsection(section, "CS");
        Formula(section, "(P - M₂)R₂ + B₂");
        Intro(section, "P · Total chargeable income for a year including current additional remuneration");
        Formula(section,
            "[Σ(Y-K) + (Y₁ - K₁) + (Y₂ - [K₂ × n]) + (Yt - Kt)] - (D + S + Du + Su + Q×C + ΣLP + LP₁)");

        Variable(section, "Yt", "Gross additional remuneration for current month.", ar.Yt);
        Variable(section, "Kt",
            "Contribution to EPF or Other Approved Funds for current month's additional remuneration "
            + "subject to total qualifying amount not exceeding RM4,000.00 per year.", ar.Kt);

        // Kt and KtEffective differ whenever the normal projection has already
        // claimed the RM 4,000 budget. Showing only Kt would make P look wrong
        // to anyone subtracting the figure printed on their payslip.
        Variable(section, "P",
            "Total chargeable income for a year including AR — Section 1's P with Yt added and Kt "
            + $"deducted = {Amount(PBeforeFloor(b))} + {Amount(ar.Yt)} - {Amount(ar.KtEffective)} "
            + $"(Kt {Amount(ar.Kt)} capped at the remaining RM 4,000 allowance = {Amount(ar.KtEffective)})",
            ar.ChargeableWithAr, bold: true);

        Variable(section, "M₂",
            "Amount of first chargeable income for the range that P (with AR) falls into. May differ from "
            + "Section 1's M when the additional remuneration pushes the chargeable income across a band.",
            ar.M2);
        Variable(section, "R₂", $"Percentage of tax rates. ({ar.R2 * 100m:0.00}%)", ar.R2, raw: true);
        Variable(section, "B₂",
            "Amount of tax on M₂ less tax rebate for individual and spouse (if qualified).", ar.B2);
        Variable(section, "CS",
            $"Yearly tax including AR = (P - M₂)R₂ + B₂ = ({Amount(ar.ChargeableWithAr)} - {Amount(ar.M2)}) "
            + $"× {ar.R2:0.00} + ({Amount(ar.B2)})",
            ar.Cs, bold: true);
    });

    // ─── 4. PCB(C) — the tax attributable to the bonus ──────────────────

    private static void SectionArPcb(ColumnDescriptor column, PcbArBreakdown ar, decimal z) =>
        column.Item().ShowEntire().Column(section =>
        {
        SectionTitle(section, "4. Additional Remuneration PCB");
        Subsection(section, "PCB (C)");
        Formula(section, "CS - [PCB (B) + Accumulated Zakat that has been paid]");
        Formula(section, $"{Amount(ar.Cs)} - [{Amount(ar.PcbB)} + {Amount(z)}]");

        Variable(section, "PCB (C)",
            "Additional Remuneration PCB — the marginal PCB owed because of the additional remuneration "
            + "paid this month, after the RM 10 threshold is applied.",
            ar.PcbC, bold: true);
    });

    // ─── 5. What was actually deducted ──────────────────────────────────

    private static void SectionNetPcb(ColumnDescriptor column, PcbBreakdown b, decimal zakat, decimal voluntaryPcb) =>
        column.Item().ShowEntire().Column(section =>
        {
        SectionTitle(section, "5. PCB Current Month");
        Subsection(section, "PCB (after rounding up to the nearest 5 cents)");
        Formula(section, "PCB (A) + PCB (C)");
        Formula(section, $"{Amount(b.PcbNormal)} + {Amount(b.PcbAdditional)}");

        foreach (var row in NetPcbRows(b.PcbTotal, zakat, voluntaryPcb))
        {
            Variable(section, row.Label, row.Description, row.Amount, bold: row.Bold);
        }
    });

    public sealed record NetPcbRow(string Label, string Description, decimal Amount, bool Bold);

    // From the formula's PCB to what actually left the employee's pay: less
    // this month's zakat (ringgit for ringgit, never below zero — as the
    // payslip computes it), plus any manual Additional PCB. The bold last row
    // is always the payslip's PCB and what went into CP39's PCB field.
    public static IReadOnlyList<NetPcbRow> NetPcbRows(decimal formulaPcb, decimal zakat, decimal voluntaryPcb)
    {
        var zakatOffset = Math.Min(formulaPcb, Math.Max(0m, zakat));

        if (zakat <= 0m && voluntaryPcb <= 0m)
        {
            return [new("PCB",
                "Net PCB this month — the amount actually deducted from the employee's pay and remitted to LHDN.",
                formulaPcb, true)];
        }

        var rows = new List<NetPcbRow>
        {
            new("PCB", "Formula PCB this month — before the adjustments below.", formulaPcb, false),
        };

        if (zakat > 0m)
        {
            // The whole amount paid, as on the payslip — not only the part the
            // PCB could absorb.
            rows.Add(new("- Zakat",
                "Zakat / fitrah / levy paid for the current month, taken off this month's PCB ringgit for "
                + "ringgit. PCB cannot go below zero.",
                zakat, false));

            var carriedForward = zakat - zakatOffset;
            if (carriedForward > 0m)
            {
                rows.Add(new("Zakat carried forward",
                    $"The part of this month's zakat larger than the PCB ({Amount(zakat)} paid, {Amount(zakatOffset)} "
                    + "used). It is not lost: it counts in Z, the accumulated zakat, from next month.",
                    carriedForward, false));
            }
        }

        if (voluntaryPcb > 0m)
        {
            rows.Add(new("+ Add. PCB",
                "Additional PCB (Employment Income) — a manual top-up added directly to this month's PCB, "
                + "remitted via the standard PCB field of the CP39 file. NOT part of the LHDN MTD formula, so "
                + "it does not carry into next month's calculation.",
                voluntaryPcb, false));
        }

        rows.Add(new("PCB payable",
            "Net PCB this month — the amount actually deducted from the employee's pay and remitted to LHDN, "
            + "as on the payslip.",
            Money.Round2(formulaPcb - zakatOffset + voluntaryPcb), true));
        return rows;
    }

    private static void SectionAllowableDeductions(ColumnDescriptor column, PcbBreakdown b) =>
        column.Item().ShowEntire().Column(section =>
        {
        SectionTitle(section, "ΣLP & LP₁ Details");

        Variable(section, "ΣLP + LP₁",
            "Allowable deductions: the employee's SOCSO, EIS and SKBBK contributions (capped at RM 350 a "
            + "year combined) plus any TP1-declared relief (life insurance, medical insurance, PRS, "
            + "serious-disease medical, lifestyle, sports equipment), each already capped per LHDN's public "
            + "ruling. ΣLP is the accumulated amount year to date; LP₁ is this month's.",
            b.SumLp + b.Lp1);
    });

    // ─── Layout primitives ──────────────────────────────────────────────

    private static void SectionTitle(ColumnDescriptor column, string text) =>
        column.Item().PaddingTop(8).PaddingBottom(3).Text(text).FontSize(9.5f).Bold();

    private static void Subsection(ColumnDescriptor column, string text) =>
        column.Item().PaddingBottom(2).Text(text).FontSize(9).SemiBold();

    private static void Formula(ColumnDescriptor column, string text) =>
        column.Item().PaddingBottom(3).Text(text)
            .FontSize(8).Italic().FontColor(PayrollPdfShared.Muted);

    private static void Intro(ColumnDescriptor column, string text) =>
        column.Item().PaddingTop(3).PaddingBottom(1).Text(text).FontSize(8.5f);

    // One variable: abbreviation, LHDN's description, and the amount right-aligned.
    private static void Variable(
        ColumnDescriptor column, string abbreviation, string description, decimal amount,
        bool bold = false, bool raw = false) =>
        column.Item().PaddingVertical(2f).BorderBottom(0.25f).BorderColor(PayrollPdfShared.Rule)
            .PaddingBottom(2f).Row(row =>
            {
                row.RelativeItem().PaddingRight(8).Column(box =>
                {
                    // The subscripts LHDN's notation depends on — Y₁, K₂, M₂,
                    // LP₁ — descend below the baseline, so the abbreviation
                    // needs its own leading or the glyph lands on the first
                    // line of the description underneath it.
                    box.Item().PaddingBottom(1.5f).Text(abbreviation).FontSize(8.5f).Bold();
                    box.Item().Text(description).FontSize(7.5f)
                        .FontColor(PayrollPdfShared.Muted).LineHeight(1.3f);
                });
                var cell = row.ConstantItem(84).AlignRight()
                    .Text(raw ? Raw(amount) : Amount(amount))
                    .FontSize(bold ? 9f : 8.5f);

                if (bold) cell.Bold();
            });

    // Amounts are TRUNCATED, not rounded, so that a reader doing the sums by
    // hand from this page arrives at the same figures — 15.098 prints as 15.09,
    // which is also what LHDN's own worksheet shows. Truncation is toward zero,
    // so a negative B of -250.00 does not drift to -250.01.
    private static string Amount(decimal value) =>
        Money.Trunc2(value).ToString("#,##0.00", CultureInfo.InvariantCulture);

    // Counts and rates are not money: n is a whole number of months and R is a
    // rate like 0.06, neither of which should be grouped or truncated.
    private static string Raw(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string SchemeName(SpecialTaxScheme scheme) => scheme switch
    {
        SpecialTaxScheme.RETURNING_EXPERT => "the Returning Expert Programme (REP)",
        SpecialTaxScheme.KNOWLEDGE_WORKER => "the knowledge worker (specified region) approval",
        _ => "the resident non-citizen C-suite approval",
    };

    private static string SchemeSection(SpecialTaxScheme scheme) => scheme switch
    {
        SpecialTaxScheme.RETURNING_EXPERT => "D.b.3, Table 2",
        SpecialTaxScheme.KNOWLEDGE_WORKER => "D.b.4, Table 3",
        _ => "D.b.5, Table 4",
    };
}
