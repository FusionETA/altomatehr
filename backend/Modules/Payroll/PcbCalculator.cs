using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// PCB (Potongan Cukai Bulanan) — Malaysian Monthly Tax Deduction.
//
//   Non-resident        → flat 30% of the month's remuneration, no reliefs.
//   Resident, normal    → annualise, deduct reliefs, apply the progressive
//                         bands, subtract YTD zakat and PCB, divide across the
//                         months remaining.
//   Resident, additional→ the tax DELTA of layering the one-off amount on top
//                         of the annual chargeable income. Deliberately not
//                         projected forward: a RM 10,000 bonus must not be
//                         taxed as a RM 10,000/month recurring allowance.
//
// Zakat for the CURRENT month is not applied here — the orchestrator offsets it
// after this returns. `YtdZakat` (prior months) does feed the annual figure.
//
// ⚠️ Known gaps, carried over from the reference implementation:
//   - TP1 items the employer cannot know (life insurance, lifestyle, parents'
//     medical) are employee-declared and out of scope; pass them via
//     ThisMonthAllowableDeductions if collected.
//   - Returning Expert Programme, Knowledge Worker and the approved
//     non-citizen C-suite category have special rates — not implemented, they
//     compute as standard residents.
//   - `PcbBorneByEmployer` gross-up is NOT implemented; the figure is the same
//     whoever legally pays it.
//
// Validate against LHDN's published test cases before relying on this for a
// live submission.
public static class PcbCalculator
{
    public sealed record Input
    {
        // Drives the whole formula — non-residents take the flat rate.
        public required bool IsResident { get; init; }

        // 1–12.
        public required int PeriodMonth { get; init; }

        // This month's NORMAL taxable wage: prorated pay + recurring allowances
        // + OT. Reimbursements and deductions are not taxable wage, and one-off
        // amounts belong in ThisMonthAdditionalRemuneration, not here — putting
        // a bonus in this field projects it across the rest of the year.
        public required decimal ThisMonthTaxable { get; init; }

        // This month's employee EPF from normal pay.
        public required decimal ThisMonthEpf { get; init; }

        // One-off remuneration: bonus, commission, arrears, director fee,
        // gratuity — already filtered for `subjectToPcb`.
        public decimal ThisMonthAdditionalRemuneration { get; init; }

        // Employee EPF arising from the additional remuneration alone.
        public decimal ThisMonthEpfFromAr { get; init; }

        // Totals for this calendar year from previously submitted payslips. For
        // a mid-year joiner the caller folds in the previous employer's figures
        // from the employee's TP3 carryover.
        public required decimal YtdTaxable { get; init; }
        public required decimal YtdEpf { get; init; }
        public required decimal YtdPcb { get; init; }

        // Z in the LHDN formula — zakat paid earlier this year, EXCLUDING this
        // month. Zakat fully offsets the MTD obligation.
        public decimal YtdZakat { get; init; }

        // Employee SOCSO + EIS (+ SKBBK) for the relief bucket. Actuals only —
        // see the projection note in Calculate.
        public decimal ThisMonthSocsoEis { get; init; }
        public decimal YtdSocsoEis { get; init; }

        // TP1-declared deductions, each already clamped to its per-item LHDN cap
        // by the caller. LP1 is this month, ΣLP the year to date.
        public decimal ThisMonthAllowableDeductions { get; init; }
        public decimal YtdAllowableDeductions { get; init; }

        // Relief inputs from the employee's profile.
        public required bool IsOku { get; init; }
        public bool? SpouseWorking { get; init; }
        public bool? SpouseDisabled { get; init; }
        public IReadOnlyList<ChildRelief> Children { get; init; } = [];
    }

    // `Normal` is PCB(A), `Additional` is PCB(C) in LHDN's notation.
    public readonly record struct Result(decimal Normal, decimal Additional, decimal Total);

    private const decimal NonResidentRate = 0.30m;

    // LHDN's minimum-deduction threshold: a component below RM 10 is not
    // required to be deducted (Section E items 3–4).
    private const decimal MinimumDeduction = 10m;

    // The money. A thin read of the breakdown below — the two cannot disagree,
    // because there is only one computation.
    public static Result Calculate(Input input)
    {
        var b = Explain(input);
        return new Result(b.PcbNormal, b.PcbAdditional, b.PcbTotal);
    }

    // The same formula, with every intermediate the LHDN form names.
    //
    // Everything here was already being computed to produce the three numbers
    // `Calculate` returns; this simply reports it rather than discarding it.
    // That is the point: a breakdown derived from a SECOND implementation can
    // drift from the deduction it claims to explain, and in the reference it
    // has.
    public static PcbBreakdown Explain(Input input)
    {
        var normalTaxable = Math.Max(0m, input.ThisMonthTaxable);
        var arTaxable = Math.Max(0m, input.ThisMonthAdditionalRemuneration);

        // Non-residents are a single-rate withholding with no reliefs, so the
        // normal/additional split makes no difference to the money — but it is
        // still reported split so the caller can file it correctly. The RM 10
        // threshold applies per component.
        if (!input.IsResident)
        {
            var nrNormal = ApplyThreshold(RoundMtd(normalTaxable * NonResidentRate));
            var nrAdditional = ApplyThreshold(RoundMtd(arTaxable * NonResidentRate));

            return new PcbBreakdown
            {
                Formula = PcbFormula.NonResident,
                Rate = NonResidentRate,
                NormalTaxable = normalTaxable,
                AdditionalTaxable = arTaxable,
                PcbNormal = nrNormal,
                PcbAdditional = nrAdditional,
                PcbTotal = RoundMtd(nrNormal + nrAdditional),
            };
        }

        var monthsRemaining = Math.Max(1, 13 - Math.Clamp(input.PeriodMonth, 1, 12));
        var futureMonths = monthsRemaining - 1;

        var epf = ProjectEpfRelief(input, futureMonths);

        // Project this month's level forward to year end.
        var annualTaxable = input.YtdTaxable + normalTaxable + normalTaxable * futureMonths;

        var reliefItems = PcbReliefs.Itemise(
            input.IsOku, input.SpouseWorking, input.SpouseDisabled, input.Children);
        var reliefs = reliefItems.Total;

        // PERKESO relief is actuals-only — `ytd + thisMonth`, capped — NOT
        // projected forward the way EPF is. That matches HReasily / BrioHR /
        // Talenox. The annual PCB total lands in the same place either way; only
        // the month-to-month distribution differs, with the relief growing until
        // the cap is hit rather than being applied in full from January.
        //
        // Split across ΣLP and LP1 for the form: the accumulated part fills
        // first, and this month claims whatever headroom is left.
        var ytdPerkeso = Math.Min(PcbReliefs.PerkesoCap, Math.Max(0m, input.YtdSocsoEis));
        var thisMonthPerkeso = Math.Max(0m, Math.Min(
            Math.Max(0m, input.ThisMonthSocsoEis),
            PcbReliefs.PerkesoCap - ytdPerkeso));

        // ΣLP + LP1. Same bucket as PERKESO but with no combined cap — each item
        // was clamped to its own LHDN cap upstream.
        var sumLp = ytdPerkeso + Math.Max(0m, input.YtdAllowableDeductions);
        var lp1 = thisMonthPerkeso + Math.Max(0m, input.ThisMonthAllowableDeductions);

        var perkesoRelief = ytdPerkeso + thisMonthPerkeso;
        var allowableDeductions =
            Math.Max(0m, input.YtdAllowableDeductions) +
            Math.Max(0m, input.ThisMonthAllowableDeductions);

        var ytdZakat = Math.Max(0m, input.YtdZakat);
        var ytdPcb = Math.Max(0m, input.YtdPcb);

        // The rebate doubles to RM 800 when the spouse has no income — the same
        // gate as the RM 4,000 S relief.
        var spouseClaimable = input.SpouseWorking == false;

        var chargeableNormal = Math.Max(0m,
            annualTaxable - epf.Normal - perkesoRelief - allowableDeductions - reliefs);

        var annualTaxNormal = PcbTaxBands.ApplyResidentBands(chargeableNormal, spouseClaimable);
        var (m, r, bandB) = PcbTaxBands.FindBand(chargeableNormal, spouseClaimable);

        // MTD = [(P−M)R + B − (Z + X)] / (n+1): the year's balance owed, spread
        // across the months left.
        var stillOwed = Math.Max(0m, annualTaxNormal - ytdZakat - ytdPcb);
        var monthlyNormal = stillOwed / monthsRemaining;

        // LHDN MTD Spec Section E, in order (pages 19–20):
        //   1. truncate each component to 2dp,
        //   2. zero anything below RM 10 — per component, not on the sum,
        //   3. round up to the next 5 sen ONCE, on the net figure.
        //
        // Deliberately no 5-sen ceiling on PCB(A) or PCB(C) before the sum:
        // double rounding pushes 155.99 — which should land on 156.00 — up to
        // 156.05.
        var truncatedMonthly = Money.Trunc2(monthlyNormal);
        var pcbA = ApplyThreshold(truncatedMonthly);

        var ar = arTaxable > 0m
            ? ExplainAr(
                input, arTaxable, epf, annualTaxable, perkesoRelief, allowableDeductions,
                reliefs, spouseClaimable, truncatedMonthly, monthsRemaining, ytdZakat, ytdPcb)
            : null;

        var pcbC = ar?.PcbC ?? 0m;

        // Q and C are display-only — see the note on PcbBreakdown.
        const decimal standardChildRelief = 2000m;
        var childCount = reliefItems.Children > 0m
            ? Math.Round(reliefItems.Children / standardChildRelief, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new PcbBreakdown
        {
            Formula = PcbFormula.Resident,

            NormalTaxable = normalTaxable,
            AdditionalTaxable = arTaxable,

            Y = Math.Max(0m, input.YtdTaxable),
            K = epf.K,
            Y1 = normalTaxable,
            K1 = epf.K1,
            Y2 = normalTaxable * futureMonths,
            K2 = epf.K2,
            N = futureMonths,

            D = reliefItems.Individual,
            Du = reliefItems.DisabledIndividual,
            S = reliefItems.Spouse,
            Su = reliefItems.DisabledSpouse,
            Q = standardChildRelief,
            C = childCount,
            QC = reliefItems.Children,

            SumLp = sumLp,
            Lp1 = lp1,

            P = chargeableNormal,
            M = m,
            R = r,
            B = bandB,

            Z = ytdZakat,
            X = ytdPcb,

            YearlyTax = annualTaxNormal,
            CurrentMonthPcb = monthlyNormal,

            Ar = ar,

            PcbNormal = pcbA,
            PcbAdditional = pcbC,
            PcbTotal = RoundMtd(pcbA + pcbC),
        };
    }

    // LHDN MTD Specification 2026 Section E, steps 2 to 4.
    private static PcbArBreakdown ExplainAr(
        Input input,
        decimal arTaxable,
        EpfProjection epf,
        decimal annualTaxable,
        decimal perkesoRelief,
        decimal allowableDeductions,
        decimal reliefs,
        bool spouseClaimable,
        decimal truncatedMonthly,
        int monthsRemaining,
        decimal ytdZakat,
        decimal ytdPcb)
    {
        var chargeableWithAr = Math.Max(0m,
            annualTaxable + arTaxable
                - epf.WithAr - perkesoRelief - allowableDeductions - reliefs);

        var (m2, r2, b2) = PcbTaxBands.FindBand(chargeableWithAr, spouseClaimable);

        // CS — the yearly tax including the AR. Rounded to 2dp: it is a ringgit
        // figure, and it is what the form prints. Rounding it HERE, once, is
        // what lets a reader subtract the printed numbers and land on the
        // printed PCB(C) — and, because the deduction reads the same value, on
        // the sum actually withheld.
        var cs = Money.Round2(
            PcbTaxBands.ApplyResidentBands(chargeableWithAr, spouseClaimable));

        // PCB(B) — the projected annual NORMAL deduction. Truncated monthly
        // figure, not the thresholded PCB(A); see the note on PcbArBreakdown.
        var pcbB = ytdPcb + truncatedMonthly * monthsRemaining;

        var beforeRounding = Math.Max(0m, cs - pcbB - ytdZakat);

        return new PcbArBreakdown
        {
            Yt = arTaxable,
            Kt = Math.Max(0m, input.ThisMonthEpfFromAr),
            KtEffective = epf.Kt,
            ChargeableWithAr = chargeableWithAr,
            M2 = m2,
            R2 = r2,
            B2 = b2,
            Cs = cs,
            PcbB = pcbB,
            PcbCBeforeRounding = beforeRounding,
            PcbC = ApplyThreshold(Money.Trunc2(beforeRounding)),
        };
    }

    // The K-decomposition, kept whole so the form can name each part.
    private readonly record struct EpfProjection(
        decimal K, decimal K1, decimal K2, decimal Kt, decimal Normal, decimal WithAr);

    private static EpfProjection ProjectEpfRelief(
        Input input, int futureMonths)
    {
        var thisMonthEpf = Math.Max(0m, input.ThisMonthEpf);
        var arEpf = Math.Max(0m, input.ThisMonthEpfFromAr);

        var k = Math.Min(PcbReliefs.EpfCap, Math.Max(0m, input.YtdEpf));
        var capAfterK = Math.Max(0m, PcbReliefs.EpfCap - k);

        var k1 = Math.Min(Math.Ceiling(thisMonthEpf), capAfterK);
        var capAfterK1 = Math.Max(0m, capAfterK - k1);

        var k2 = futureMonths > 0
            ? Money.Trunc2(Math.Min(thisMonthEpf, capAfterK1 / futureMonths))
            : 0m;

        var capAfterK2 = Math.Max(0m, capAfterK1 - k2 * futureMonths);
        var kt = Math.Min(arEpf, capAfterK2);

        var normal = k + k1 + k2 * futureMonths;

        return new EpfProjection(k, k1, k2, kt, normal, normal + kt);
    }

    // LHDN's MTD rounding (Section E items 1–2): truncate to sen, then round UP
    // to the next 5 sen. 287.02 → 287.05; 152.06 → 152.10; 152.05 stays put.
    public static decimal RoundMtd(decimal value)
    {
        if (value <= 0m) return 0m;

        var sen = Math.Floor(value * 100m);

        return Math.Ceiling(sen / 5m) * 5m / 100m;
    }

    // Below RM 10 the deduction is not required. Applied per component, before
    // the zakat offset — the "net MTD after zakat under RM 10 is still deducted"
    // rule belongs to the orchestrator, so we must not zero the offset result.
    private static decimal ApplyThreshold(decimal mtd) => mtd < MinimumDeduction ? 0m : mtd;
}
