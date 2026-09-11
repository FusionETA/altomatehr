using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The LHDN form decomposition.
//
// Two things are being defended here. First, that the published intermediates
// — LHDN prints K2, P, M, R and B in its own worked example — come out right,
// not just the final ringgit. Second, and more importantly, that the numbers
// on the form RECONCILE: someone doing the printed arithmetic by hand has to
// arrive at the sum that actually left the employee's pay.
//
// The reference app cannot make that promise. It computes the money in
// `calcPcb` and the form in `calcPcbBreakdown`, and the two have already
// drifted — the form subtracts a rounded CS, the money an unrounded one. This
// port derives both from one pass, and these tests are what keeps it that way.
public class PcbBreakdownTests
{
    private static ChildRelief Child() => new()
    {
        AbilityStatus = ChildAbilityStatus.NORMAL,
        CurrentlyStudying = ChildStudyingLevel.UNDER_18,
        PcbDeduction = ChildPcbDeductionLevel.FULL,
    };

    // The LHDN "Spesifikasi Kaedah Pengiraan Berkomputer PCB 2026" worked
    // example, pages 45–50 — the same fixture PcbCalculatorTests validates the
    // money against. Married, spouse working, 3 children, RM 5,500/month.
    private static PcbCalculator.Input LhdnExample(
        int periodMonth, decimal ytdTaxable, decimal ytdEpf, decimal ytdPcb,
        decimal additionalRemuneration = 0m, decimal epfFromAr = 0m) => new()
    {
        IsResident = true,
        PeriodMonth = periodMonth,
        ThisMonthTaxable = 5500m,
        ThisMonthEpf = 605m,
        YtdTaxable = ytdTaxable,
        YtdEpf = ytdEpf,
        YtdPcb = ytdPcb,
        ThisMonthAdditionalRemuneration = additionalRemuneration,
        ThisMonthEpfFromAr = epfFromAr,
        IsOku = false,
        SpouseWorking = true,
        SpouseDisabled = false,
        Children = [Child(), Child(), Child()],
    };

    // ─── The invariant ──────────────────────────────────────────────────

    // The breakdown IS the deduction, not a description of it. If these ever
    // diverge, someone has reintroduced a second implementation.
    [Theory]
    [InlineData(1, 0, 0, 0, 0, 0)]
    [InlineData(4, 16500, 1815, 330, 8250, 908)]
    [InlineData(12, 60500, 6655, 1210, 0, 0)]
    [InlineData(6, 33000, 3630, 660, 2000, 220)]
    public void Explain_AgreesWithCalculateToTheSen(
        int month, double ytdTaxable, double ytdEpf, double ytdPcb, double ar, double arEpf)
    {
        var input = LhdnExample(
            month, (decimal)ytdTaxable, (decimal)ytdEpf, (decimal)ytdPcb,
            (decimal)ar, (decimal)arEpf);

        var money = PcbCalculator.Calculate(input);
        var form = PcbCalculator.Explain(input);

        Assert.Equal(money.Normal, form.PcbNormal);
        Assert.Equal(money.Additional, form.PcbAdditional);
        Assert.Equal(money.Total, form.PcbTotal);
    }

    // ─── LHDN's published intermediates ─────────────────────────────────

    // Page 45 prints these: K2 = (4,000 − 605) ÷ 11 = 308.63, P = 47,000.07,
    // and the band {M = 35,000, R = 6%, B = 600}.
    [Fact]
    public void January_ReproducesLhdnsPublishedIntermediates()
    {
        var b = PcbCalculator.Explain(LhdnExample(1, 0m, 0m, 0m));

        Assert.Equal(PcbFormula.Resident, b.Formula);
        Assert.Equal(11, b.N);
        Assert.Equal(0m, b.Y);
        Assert.Equal(5500m, b.Y1);
        Assert.Equal(605m, b.K1);
        Assert.Equal(308.63m, b.K2);
        Assert.Equal(47_000.07m, b.P);
        Assert.Equal(35_000m, b.M);
        Assert.Equal(0.06m, b.R);
        Assert.Equal(600m, b.B);
        Assert.Equal(110m, b.PcbTotal);
    }

    // ─── The printed arithmetic has to work ─────────────────────────────

    // P = [(Y − K) + (Y1 − K1) + (Y2 − K2 × N)] − (D + S + Du + Su + QC + ΣLP + LP1)
    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(6, 33000, 3630, 660)]
    [InlineData(12, 60500, 6655, 1210)]
    public void P_IsExactlyTheFormulaTheFormPrints(
        int month, double ytdTaxable, double ytdEpf, double ytdPcb)
    {
        var b = PcbCalculator.Explain(
            LhdnExample(month, (decimal)ytdTaxable, (decimal)ytdEpf, (decimal)ytdPcb));

        var net = (b.Y - b.K) + (b.Y1 - b.K1) + (b.Y2 - b.K2 * b.N);
        var reliefs = b.D + b.S + b.Du + b.Su + b.QC + b.SumLp + b.Lp1;

        Assert.Equal(b.P, Math.Max(0m, net - reliefs));
    }

    // YearlyTax = (P − M) × R + B, and the month's figure is that spread over
    // the months remaining, net of what zakat and earlier PCB already covered.
    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(6, 33000, 3630, 660)]
    [InlineData(12, 60500, 6655, 1210)]
    public void TheBandAndTheDivisorReconcile(
        int month, double ytdTaxable, double ytdEpf, double ytdPcb)
    {
        var b = PcbCalculator.Explain(
            LhdnExample(month, (decimal)ytdTaxable, (decimal)ytdEpf, (decimal)ytdPcb));

        Assert.Equal(b.YearlyTax, Math.Max(0m, (b.P - b.M) * b.R + b.B));

        var stillOwed = Math.Max(0m, b.YearlyTax - b.Z - b.X);
        Assert.Equal(b.CurrentMonthPcb, stillOwed / (b.N + 1));

        // PCB(A) is the truncated monthly figure, with the RM 10 floor.
        var expected = Money.Trunc2(b.CurrentMonthPcb);
        Assert.Equal(expected < 10m ? 0m : expected, b.PcbNormal);
    }

    // ─── Additional remuneration ────────────────────────────────────────

    // Pages 48–50: an RM 8,250 bonus in April. The AR section has to tie out
    // step by step, because the PDF prints each step as a line.
    [Fact]
    public void AprilBonus_ArSectionReconcilesStepByStep()
    {
        var b = PcbCalculator.Explain(
            LhdnExample(4, 16500m, 1815m, 330m, additionalRemuneration: 8250m, epfFromAr: 908m));

        var ar = Assert.IsType<PcbArBreakdown>(b.Ar);

        Assert.Equal(8250m, ar.Yt);

        // Step 3 — CS = (P₂ − M₂) × R₂ + B₂.
        Assert.Equal(ar.Cs, Money.Round2(Math.Max(0m, (ar.ChargeableWithAr - ar.M2) * ar.R2 + ar.B2)));

        // Step 2 — PCB(B) = X + trunc2(monthly) × (N + 1).
        Assert.Equal(ar.PcbB, b.X + Money.Trunc2(b.CurrentMonthPcb) * (b.N + 1));

        // Step 4 — PCB(C) = CS − PCB(B) − Z.
        Assert.Equal(ar.PcbCBeforeRounding, Math.Max(0m, ar.Cs - ar.PcbB - b.Z));
        Assert.Equal(757.5m, ar.PcbC);

        // Step 5 — the net figure the employee sees.
        Assert.Equal(110m, b.PcbNormal);
        Assert.Equal(867.5m, b.PcbTotal);
    }

    [Fact]
    public void NoAdditionalRemuneration_LeavesTheArSectionAbsent()
    {
        var b = PcbCalculator.Explain(LhdnExample(1, 0m, 0m, 0m));

        Assert.Null(b.Ar);
        Assert.Equal(0m, b.PcbAdditional);
    }

    // Kt is what the employee actually contributed on the bonus; KtEffective is
    // how much of it still fits under the RM 4,000 annual relief cap. When the
    // normal projection has already used the budget the two diverge, and the
    // form must show both — otherwise the printed P (with AR) looks wrong to
    // anyone subtracting the contribution they can see on their payslip.
    [Fact]
    public void Kt_ShowsTheContributionWhileKtEffectiveShowsTheReliefItEarns()
    {
        var b = PcbCalculator.Explain(
            LhdnExample(4, 16500m, 1815m, 330m, additionalRemuneration: 8250m, epfFromAr: 908m));

        var ar = Assert.IsType<PcbArBreakdown>(b.Ar);

        // By April the normal projection (K + K1 + K2 x N) has already claimed
        // the whole RM 4,000 budget, so the bonus EPF earns no further relief.
        // The contribution is still real and still shown.
        Assert.Equal(908m, ar.Kt);
        Assert.Equal(0m, ar.KtEffective);

        // The chargeable figure uses the EFFECTIVE relief, so the bonus lands
        // on P in full: 47,000.07 + 8,250 - 0.
        Assert.Equal(b.P + ar.Yt - ar.KtEffective, ar.ChargeableWithAr);
    }

    // The EPF relief budget is RM 4,000 a year and the form's four parts must
    // never claim more between them.
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(12)]
    public void TheEpfPartsNeverExceedTheAnnualReliefCap(int month)
    {
        var b = PcbCalculator.Explain(
            LhdnExample(month, 5000m * month, 550m * month, 100m * month,
                additionalRemuneration: 3000m, epfFromAr: 330m));

        var claimed = b.K + b.K1 + b.K2 * b.N + (b.Ar?.KtEffective ?? 0m);

        Assert.True(claimed <= PcbReliefs.EpfCap,
            $"EPF relief claimed {claimed} exceeds the RM {PcbReliefs.EpfCap} cap");
    }

    // ─── Reliefs ────────────────────────────────────────────────────────

    // The form shows child relief as "Q × C". Real children do not share one
    // rate, so QC is the truth and Q/C are the readable presentation of it.
    [Fact]
    public void ChildReliefIsReportedAsBothTheProductAndTheTrueSum()
    {
        var b = PcbCalculator.Explain(LhdnExample(1, 0m, 0m, 0m));

        Assert.Equal(2000m, b.Q);
        Assert.Equal(3m, b.C);
        Assert.Equal(6000m, b.QC);
        Assert.Equal(b.QC, b.Q * b.C);
    }

    [Fact]
    public void SpouseReliefFollowsTheSameGateAsTheRebate()
    {
        var working = PcbCalculator.Explain(LhdnExample(1, 0m, 0m, 0m));
        var notWorking = PcbCalculator.Explain(
            LhdnExample(1, 0m, 0m, 0m) with { SpouseWorking = false });

        Assert.Equal(0m, working.S);
        Assert.Equal(4000m, notWorking.S);
    }

    // PERKESO relief is capped at RM 350 for the year. The accumulated part
    // fills first and this month claims the headroom that is left, so ΣLP and
    // LP1 never double-count it.
    [Fact]
    public void PerkesoReliefSplitsBetweenAccumulatedAndThisMonthWithoutExceedingTheCap()
    {
        var b = PcbCalculator.Explain(LhdnExample(6, 33000m, 3630m, 660m) with
        {
            YtdSocsoEis = 300m,
            ThisMonthSocsoEis = 100m,
        });

        Assert.Equal(300m, b.SumLp);
        Assert.Equal(50m, b.Lp1);           // only RM 50 of headroom left
        Assert.Equal(PcbReliefs.PerkesoCap, b.SumLp + b.Lp1);
    }

    // TP1 items have no combined cap — each was clamped to its own LHDN limit
    // upstream — so they add on top of the PERKESO bucket.
    [Fact]
    public void Tp1DeductionsJoinTheSameBucketWithoutACombinedCap()
    {
        var b = PcbCalculator.Explain(LhdnExample(3, 11000m, 1210m, 220m) with
        {
            ThisMonthAllowableDeductions = 300m,
        });

        Assert.Equal(300m, b.Lp1);
        Assert.Equal(108.20m, b.PcbTotal);   // LHDN's published figure, page 47
    }

    // ─── Non-residents ──────────────────────────────────────────────────

    [Fact]
    public void NonResident_ReportsTheFlatRateAndNothingElse()
    {
        var b = PcbCalculator.Explain(new PcbCalculator.Input
        {
            IsResident = false,
            PeriodMonth = 5,
            ThisMonthTaxable = 10_000m,
            ThisMonthEpf = 0m,
            ThisMonthAdditionalRemuneration = 2_000m,
            YtdTaxable = 0m,
            YtdEpf = 0m,
            YtdPcb = 0m,
            IsOku = false,
        });

        Assert.Equal(PcbFormula.NonResident, b.Formula);
        Assert.Equal(0.30m, b.Rate);
        Assert.Equal(3000m, b.PcbNormal);
        Assert.Equal(600m, b.PcbAdditional);
        Assert.Equal(3600m, b.PcbTotal);

        // No annualisation, no reliefs, no band — reporting any would imply the
        // flat rate came from somewhere it did not.
        Assert.Equal(0m, b.P);
        Assert.Equal(0m, b.D);
        Assert.Equal(0m, b.R);
        Assert.Null(b.Ar);
    }
}
