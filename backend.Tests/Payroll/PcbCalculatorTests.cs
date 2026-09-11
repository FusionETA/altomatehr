using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

public class PcbCalculatorTests
{
    // A child under 18, fully claimed — RM 2,000 of QC relief.
    private static ChildRelief Child() => new()
    {
        AbilityStatus = ChildAbilityStatus.NORMAL,
        CurrentlyStudying = ChildStudyingLevel.UNDER_18,
        PcbDeduction = ChildPcbDeductionLevel.FULL,
    };

    // `SpouseWorking = true` means LHDN Category 1 or 3: no S relief, rebate
    // stays at RM 400. Only an explicit false opens the spouse claim.
    private static PcbCalculator.Input Resident(
        int periodMonth = 1,
        decimal thisMonthTaxable = 8000m,
        decimal thisMonthEpf = 880m,
        decimal ytdTaxable = 0m,
        decimal ytdEpf = 0m,
        decimal ytdPcb = 0m,
        decimal additionalRemuneration = 0m,
        decimal epfFromAr = 0m,
        decimal ytdZakat = 0m,
        decimal thisMonthSocsoEis = 0m,
        decimal ytdSocsoEis = 0m,
        decimal thisMonthAllowableDeductions = 0m,
        decimal ytdAllowableDeductions = 0m,
        bool isOku = false,
        bool? spouseWorking = true,
        bool? spouseDisabled = false,
        IReadOnlyList<ChildRelief>? children = null) => new()
        {
            IsResident = true,
            PeriodMonth = periodMonth,
            ThisMonthTaxable = thisMonthTaxable,
            ThisMonthEpf = thisMonthEpf,
            YtdTaxable = ytdTaxable,
            YtdEpf = ytdEpf,
            YtdPcb = ytdPcb,
            ThisMonthAdditionalRemuneration = additionalRemuneration,
            ThisMonthEpfFromAr = epfFromAr,
            YtdZakat = ytdZakat,
            ThisMonthSocsoEis = thisMonthSocsoEis,
            YtdSocsoEis = ytdSocsoEis,
            ThisMonthAllowableDeductions = thisMonthAllowableDeductions,
            YtdAllowableDeductions = ytdAllowableDeductions,
            IsOku = isOku,
            SpouseWorking = spouseWorking,
            SpouseDisabled = spouseDisabled,
            Children = children ?? [],
        };

    private static decimal Ceil5Sen(decimal value) => Math.Ceiling(value * 20m) / 20m;

    // ─── LHDN PCB 2026 worked example ───────────────────────────────────
    //
    // "Spesifikasi Kaedah Pengiraan Berkomputer PCB 2026", pages 45–50.
    // Married, spouse working (Cat 3), 3 children under 18, RM 5,500/month,
    // EPF RM 605/month. LHDN publishes the MTD for Jan, Feb, Mar and the April
    // bonus month — this is the gold-standard validation for the whole engine.
    //
    // The worked example does NOT include the RM 350/year PERKESO auto-relief
    // our engine applies by default, so these pass 0 for SOCSO/EIS to match
    // LHDN to the sen. With real contributions the engine produces a slightly
    // lower MTD — the same outcome HReasily, BrioHR and Talenox produce.

    private static PcbCalculator.Input LhdnExample(
        int periodMonth, decimal ytdTaxable, decimal ytdEpf, decimal ytdPcb,
        decimal additionalRemuneration = 0m, decimal epfFromAr = 0m) =>
        Resident(
            periodMonth: periodMonth,
            thisMonthTaxable: 5500m,
            thisMonthEpf: 605m,
            ytdTaxable: ytdTaxable,
            ytdEpf: ytdEpf,
            ytdPcb: ytdPcb,
            additionalRemuneration: additionalRemuneration,
            epfFromAr: epfFromAr,
            children: [Child(), Child(), Child()]);

    // Page 45. K2 = (4,000 − 605) ÷ 11 = 308.63; P = 47,000.07;
    // MTD = [(47,000.07 − 35,000) × 6% + 600] ÷ 12 = 110.00.
    [Fact]
    public void Lhdn_January()
    {
        var result = PcbCalculator.Calculate(LhdnExample(1, 0m, 0m, 0m));

        Assert.Equal(110m, result.Total);
        Assert.Equal(110m, result.Normal);
        Assert.Equal(0m, result.Additional);
    }

    // Page 46. K2 = (4,000 − 1,210) ÷ 10 = 279.00; P = 47,000.00;
    // MTD = [720 + 600 − 110] ÷ 11 = 110.00.
    [Fact]
    public void Lhdn_February()
    {
        var result = PcbCalculator.Calculate(LhdnExample(2, 5500m, 605m, 110m));

        Assert.Equal(110m, result.Total);
    }

    // Page 47. LHDN's own figure is RM 108.20 WITH RM 300 of TP1 (books +
    // parents' medical); without TP1 the same scenario reaches RM 110.00, and
    // the RM 1.80 delta is exactly the TP1 effect.
    [Fact]
    public void Lhdn_March_WithoutTp1()
    {
        var result = PcbCalculator.Calculate(LhdnExample(3, 11000m, 1210m, 220m));

        Assert.Equal(110m, result.Total);
    }

    // The same March scenario WITH LHDN's RM 300 TP1 declaration must land on
    // LHDN's published RM 108.20.
    [Fact]
    public void Lhdn_March_WithTp1_MatchesThePublishedFigure()
    {
        var input = LhdnExample(3, 11000m, 1210m, 220m) with
        {
            ThisMonthAllowableDeductions = 300m,
        };

        Assert.Equal(108.20m, PcbCalculator.Calculate(input).Total);
    }

    // Pages 48–50. RM 8,250 bonus in April on top of the recurring RM 5,500.
    //   normal:     (1,320 − 330) ÷ 9 = 110.00
    //   additional: 2,077.50 − 1,320 = 757.50
    [Fact]
    public void Lhdn_AprilWithBonus()
    {
        var result = PcbCalculator.Calculate(
            LhdnExample(4, 16500m, 1815m, 330m, additionalRemuneration: 8250m, epfFromAr: 908m));

        Assert.Equal(110m, result.Normal);
        Assert.Equal(757.5m, result.Additional);
        Assert.Equal(867.5m, result.Total);
    }

    // ─── LHDN Category 1 — single, no children ──────────────────────────

    [Fact]
    public void Category1_Rm4000_RebateAppliesAtTheBoundary()
    {
        // Chargeable lands at 35,000.07 under the K decomposition; the floored
        // threshold test keeps the RM 400 rebate.
        var result = PcbCalculator.Calculate(Resident(thisMonthTaxable: 4000m, thisMonthEpf: 440m));

        Assert.Equal(16.7m, result.Total);
    }

    [Fact]
    public void Category1_Rm5000()
    {
        var result = PcbCalculator.Calculate(Resident(thisMonthTaxable: 5000m, thisMonthEpf: 550m));

        Assert.Equal(110m, result.Total);
    }

    [Fact]
    public void Category1_Oku_AddsTheDuRelief()
    {
        var result = PcbCalculator.Calculate(
            Resident(thisMonthTaxable: 5000m, thisMonthEpf: 550m, isOku: true));

        Assert.Equal(75m, result.Total);
    }

    // ─── LHDN Category 2 — married, spouse not working ──────────────────

    [Fact]
    public void Category2_Rm5500_NoChildren()
    {
        var result = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 5500m, thisMonthEpf: 605m, spouseWorking: false));

        Assert.Equal(120m, result.Total);
    }

    [Fact]
    public void Category2_DoubledRebate_WipesOutTheTax()
    {
        var result = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 3500m, thisMonthEpf: 385m,
            spouseWorking: false, children: [Child()]));

        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void Category2_DisabledSpouse_AddsTheSuRelief()
    {
        var result = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 5500m, thisMonthEpf: 605m,
            spouseWorking: false, spouseDisabled: true));

        Assert.Equal(90m, result.Total);
    }

    // An unknown spouse status must NOT open the claim — only an explicit false.
    [Fact]
    public void UnknownSpouseStatus_ClaimsNothing()
    {
        var unknown = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 5500m, thisMonthEpf: 605m, spouseWorking: null));
        var working = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 5500m, thisMonthEpf: 605m, spouseWorking: true));

        Assert.Equal(working.Total, unknown.Total);
    }

    // ─── Resident, normal remuneration ──────────────────────────────────

    // A third-party payslip: basic 3,000 + travel 300 taxable (parking excluded),
    // EPF 385. Chargeable = 26,600 → marginal 348 − rebate 400 → nothing owed.
    [Fact]
    public void BelowTheRebateLine_NothingIsDeducted()
    {
        var result = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 3300m, thisMonthEpf: 385m));

        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void BelowTheReliefFloor_NothingIsDeducted()
    {
        var result = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 500m, thisMonthEpf: 55m));

        Assert.Equal(0m, result.Total);
        Assert.Equal(0m, result.Normal);
        Assert.Equal(0m, result.Additional);
    }

    // RM 8,000/month: chargeable 83,000, annual tax 6,170, ÷ 12 ≈ 514.17.
    [Fact]
    public void January_SpreadsTheYearEvenly()
    {
        var result = PcbCalculator.Calculate(Resident());

        Assert.Equal(514.20m, result.Total);
        Assert.Equal(0m, result.Additional);

        // `Normal` is the TRUNCATED PCB(A) per Section E item 1, not the
        // deducted amount — in a no-bonus month the total is its 5-sen ceiling.
        Assert.Equal(Ceil5Sen(result.Normal), result.Total);
    }

    // Mid-year, the same salary: the balance spreads over what's left.
    [Fact]
    public void June_SpreadsTheRemainderOverTheRemainingMonths()
    {
        var result = PcbCalculator.Calculate(Resident(
            periodMonth: 6, ytdTaxable: 40000m, ytdEpf: 4400m, ytdPcb: 2570.85m));

        Assert.InRange(result.Total, 514m, 514.25m);
    }

    // Zakat already paid this year reduces the annual liability directly.
    [Fact]
    public void YtdZakat_ReducesTheAnnualTaxOwed()
    {
        var withoutZakat = PcbCalculator.Calculate(Resident());
        var withZakat = PcbCalculator.Calculate(Resident(ytdZakat: 1200m));

        Assert.True(withZakat.Total < withoutZakat.Total);
        // The full RM 1,200 comes off the year, spread over 12 months.
        Assert.Equal(100m, Math.Round(withoutZakat.Total - withZakat.Total, 0));
    }

    // ─── Additional remuneration ────────────────────────────────────────

    // The whole point of the AR path: a RM 10,000 bonus is taxed as a one-off,
    // not as a RM 10,000/month recurring allowance.
    [Fact]
    public void ABonus_IsNotProjectedForward()
    {
        var result = PcbCalculator.Calculate(Resident(
            periodMonth: 3,
            additionalRemuneration: 10000m, epfFromAr: 1100m,
            ytdTaxable: 16000m, ytdEpf: 1760m, ytdPcb: 1028.33m));

        Assert.InRange(result.Normal, 514m, 514.25m);
        Assert.InRange(result.Additional, 1900m, 1900.20m);
        Assert.Equal(Ceil5Sen(result.Normal + result.Additional), result.Total);
    }

    [Fact]
    public void AZeroBonus_ChangesNothing()
    {
        var withoutAr = PcbCalculator.Calculate(Resident());
        var withZeroAr = PcbCalculator.Calculate(
            Resident(additionalRemuneration: 0m, epfFromAr: 0m));

        Assert.Equal(withoutAr.Total, withZeroAr.Total);
        Assert.Equal(0m, withZeroAr.Additional);
    }

    // A bonus in a year that has already overpaid must not produce a refund.
    [Fact]
    public void NeitherComponentEverGoesNegative()
    {
        var result = PcbCalculator.Calculate(Resident(
            periodMonth: 12,
            thisMonthTaxable: 1000m, thisMonthEpf: 110m,
            additionalRemuneration: 500m, epfFromAr: 55m,
            ytdTaxable: 12000m, ytdEpf: 1320m));

        Assert.True(result.Normal >= 0m);
        Assert.True(result.Additional >= 0m);
    }

    // ─── Non-resident ───────────────────────────────────────────────────

    [Fact]
    public void NonResident_IsFlatThirtyPercentOnBothComponents()
    {
        var result = PcbCalculator.Calculate(Resident(
            periodMonth: 5, thisMonthTaxable: 7000m, thisMonthEpf: 0m,
            additionalRemuneration: 3000m) with { IsResident = false });

        Assert.Equal(2100m, result.Normal);
        Assert.Equal(900m, result.Additional);
        Assert.Equal(3000m, result.Total);
    }

    [Fact]
    public void NonResident_WithNoWage_DeductsNothing()
    {
        var result = PcbCalculator.Calculate(
            Resident(thisMonthTaxable: 0m, thisMonthEpf: 0m) with { IsResident = false });

        Assert.Equal(0m, result.Total);
    }

    // Reliefs don't exist for non-residents; passing them must change nothing.
    [Fact]
    public void NonResident_IgnoresEveryRelief()
    {
        var bare = PcbCalculator.Calculate(
            Resident(thisMonthTaxable: 7000m, thisMonthEpf: 0m) with { IsResident = false });
        var withReliefs = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 7000m, thisMonthEpf: 0m,
            thisMonthSocsoEis: 88m, isOku: true, spouseWorking: false,
            children: [Child(), Child()]) with { IsResident = false });

        Assert.Equal(bare.Total, withReliefs.Total);
        Assert.Equal(2100m, withReliefs.Total);
    }

    // ─── PERKESO relief (actuals-only, RM 350 cap) ──────────────────────

    [Fact]
    public void PerkesoRelief_CountsThisMonthOnly_NotAProjection()
    {
        var without = PcbCalculator.Calculate(Resident());
        var with = PcbCalculator.Calculate(Resident(thisMonthSocsoEis: 87.95m));

        Assert.True(with.Total < without.Total);

        // One month's contribution, not twelve — a projected relief would move
        // the monthly PCB far more than this.
        Assert.InRange(without.Total - with.Total, 0.01m, 2.5m);
    }

    [Fact]
    public void PerkesoRelief_ClampsAtTheCap()
    {
        var atCap = PcbCalculator.Calculate(Resident(
            periodMonth: 8, thisMonthSocsoEis: 87.95m, ytdSocsoEis: 615.65m,
            ytdTaxable: 56000m, ytdEpf: 4400m, ytdPcb: 3600m));
        var wayOver = PcbCalculator.Calculate(Resident(
            periodMonth: 8, thisMonthSocsoEis: 87.95m, ytdSocsoEis: 1000m,
            ytdTaxable: 56000m, ytdEpf: 4400m, ytdPcb: 3600m));

        Assert.Equal(atCap.Total, wayOver.Total);
    }

    // ─── TP1 allowable deductions ───────────────────────────────────────

    [Fact]
    public void Tp1Deductions_ReduceTheTax()
    {
        var without = PcbCalculator.Calculate(Resident());
        var with = PcbCalculator.Calculate(Resident(thisMonthAllowableDeductions: 3000m));

        Assert.True(with.Total < without.Total);
    }

    // This month and the year to date land in the same bucket, so RM 3,000 of
    // either must move the annual chargeable income identically.
    [Fact]
    public void Tp1Deductions_ThisMonthAndYtdAreTheSameBucket()
    {
        var thisMonth = PcbCalculator.Calculate(Resident(thisMonthAllowableDeductions: 3000m));
        var ytd = PcbCalculator.Calculate(Resident(ytdAllowableDeductions: 3000m));

        Assert.Equal(thisMonth.Total, ytd.Total);
    }

    // ─── LHDN rounding rules ────────────────────────────────────────────

    [Theory]
    // Already on a 5-sen boundary — unchanged.
    [InlineData(287.05, 287.05)]
    [InlineData(152.10, 152.10)]
    // 1–4 sen rounds up to 5.
    [InlineData(287.02, 287.05)]
    // 6–9 sen rounds up to 10.
    [InlineData(152.06, 152.10)]
    // Truncate past 2dp first, THEN ceil — 155.994 must reach 156.00, not
    // 156.05, which is what double-rounding would produce.
    [InlineData(155.994, 156.00)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void RoundMtd_TruncatesThenCeilsToFiveSen(decimal value, decimal expected)
    {
        Assert.Equal(expected, PcbCalculator.RoundMtd(value));
    }

    // Section E items 3–4: a component below RM 10 is not deducted. Applied per
    // component, so a sub-RM-10 normal MTD is dropped even when AR is present.
    [Fact]
    public void ComponentsBelowTenRinggit_AreNotDeducted()
    {
        // RM 3,770/month: chargeable 32,240, annual tax 117.20, so the monthly
        // PCB truncates to RM 9.76 — under the threshold, nothing is deducted.
        var belowThreshold = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 3770m, thisMonthEpf: 414.70m));

        Assert.Equal(0m, belowThreshold.Normal);
        Assert.Equal(0m, belowThreshold.Total);

        // A little more wage clears RM 10 and the whole amount becomes payable —
        // the threshold zeroes the component, it is not an allowance.
        var justAbove = PcbCalculator.Calculate(Resident(
            thisMonthTaxable: 3800m, thisMonthEpf: 418m));

        Assert.Equal(10.66m, justAbove.Normal);
    }
}
