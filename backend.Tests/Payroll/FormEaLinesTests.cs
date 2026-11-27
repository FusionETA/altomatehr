using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using C = AltomateHR.Api.Modules.Payroll.PayrollAdjustmentCategories;

namespace AltomateHR.Api.Tests.Payroll;

// Which line of Form EA (C.P.8A) each part of a payslip lands on.
//
// The invariant: nothing is lost and nothing is counted twice. Every ringgit
// of remuneration is on exactly one of the B lines or in F, and money that is
// not remuneration (claim reimbursements, expense claims) is on none.
public class FormEaLinesTests
{
    private static PayslipLineItem Allowance(string? category, decimal amount, decimal? taxable = null, bool subjectToPcb = true, string? label = null) => new()
    {
        Kind = PayslipLineKind.ALLOWANCE,
        Category = category,
        Amount = amount,
        PcbTaxableAmount = taxable,
        SubjectToPcb = subjectToPcb,
        Label = label ?? category ?? "Allowance",
    };

    private static PayslipLineItem Deduction(string category, decimal amount, decimal? granted = null) => new()
    {
        Kind = PayslipLineKind.DEDUCTION,
        Category = category,
        Amount = amount,
        PcbTaxableAmount = granted,
        Label = category,
    };

    // GrossPay = basic + computed OT + cash allowances + reimbursements.
    private static Payslip Slip(decimal gross, decimal reimbursements = 0m, decimal zakat = 0m) => new()
    {
        GrossPay = gross,
        TotalReimbursements = reimbursements,
        Zakat = zakat,
    };

    [Fact]
    public void BasicAndComputedOvertime_AreSalary_AndAllowancesAreB1c()
    {
        // 5,000 basic + 300 computed OT + 400 taxable phone allowance.
        var f = FormEaLines.For(Slip(5700m), [Allowance(C.AllowancePhoneFixed, 400m)]);

        Assert.Equal(5300m, f.B1a);
        Assert.Equal(400m, f.B1c);
        Assert.Contains(C.AllowancePhoneFixed, f.B1cDetails);
    }

    // B is "excluding tax exempt allowances": a clamped allowance splits, its
    // taxable part on its own line and the rest in F.
    [Fact]
    public void AnAllowancePastItsExemptionCeiling_Splits_BetweenB1cAndF()
    {
        var f = FormEaLines.For(Slip(5500m), [Allowance(C.AllowanceTravelOfficial, 500m, taxable: 200m)]);

        Assert.Equal(200m, f.B1c);
        Assert.Equal(300m, f.F);
        Assert.Equal(5000m, f.B1a);
    }

    [Fact]
    public void AnAllowanceThatIsNeverTaxable_IsAllExempt()
    {
        var f = FormEaLines.For(Slip(5200m), [Allowance(C.AllowanceMeal, 200m, subjectToPcb: false)]);

        Assert.Equal(0m, f.B1c);
        Assert.Equal(200m, f.F);
    }

    [Fact]
    public void BonusCommissionAndDirectorFees_AreB1b_AndLineItemOvertimeIsSalary()
    {
        var f = FormEaLines.For(Slip(9800m),
        [
            Allowance(C.WagesBonusAnnual, 2000m),
            Allowance(C.WagesCommission, 500m),
            Allowance(C.WagesDirectorFee, 1000m),
            Allowance(C.WagesOvertime, 300m),
        ]);

        Assert.Equal(3500m, f.B1b);
        Assert.Equal(6300m, f.B1a);   // 6,000 basic + the 300 overtime line
    }

    // Benefits in kind never reach gross, but are income all the same. A
    // medical benefit is exempt AND outside the nine kinds LHDN's EA guide
    // lists for F, so it is on no line at all.
    [Fact]
    public void BenefitsInKind_AreB3_AndAMedicalBenefitIsLeftOff()
    {
        var f = FormEaLines.For(Slip(5000m),
        [
            Allowance(C.BikCar, 800m, label: "Car/Petrol BIK"),
            Allowance(C.BikMedical, 300m, subjectToPcb: false),
            Allowance(C.BikLivingAccommodation, 1200m),
        ]);


        Assert.Equal(5000m, f.B1a);     // BIK is not taken out of salary
        Assert.Equal(800m, f.B3);
        Assert.Equal(["Car/Petrol BIK"], f.B3Details);
        Assert.Equal(1200m, f.B4);
        Assert.Equal(0m, f.F);
    }

    [Fact]
    public void Reimbursements_AreNotRemuneration()
    {
        // 5,000 basic + a 150 claim reimbursement + a 90 expense-claim line.
        var f = FormEaLines.For(Slip(5240m, reimbursements: 150m), [Allowance(C.WagesExpenseClaim, 90m, subjectToPcb: false)]);

        Assert.Equal(5000m, f.B1a);
        Assert.Equal(5000m, f.Total);
        Assert.Equal(0m, f.F);
    }

    // Payslip.Zakat also holds self-paid (TP1) zakat and the departure-levy
    // rebate — all offset PCB — but D3 is zakat paid THROUGH salary only.
    [Fact]
    public void Zakat_IsSplitBySalaryAndSelfPaid_AndTp1ReliefIsWhatWasGranted()
    {
        var f = FormEaLines.For(Slip(5000m, zakat: 250m),
        [
            Deduction(C.DeductZakat, 100m),
            Deduction(C.DeductZakatTp1, 120m),
            Deduction(C.DeductDepartureLevyTp1, 30m),
            Deduction(C.DeductTp1Lifestyle, 3000m, granted: 2500m),
        ]);

        Assert.Equal(100m, f.D3ZakatViaSalary);
        Assert.Equal(120m, f.D5bZakatSelfPaid);
        Assert.Equal(2500m, f.D5aTp1Relief);
    }

    // An imported month carries no zakat lines, only the payslip's figure.
    [Fact]
    public void AnImportedMonthsZakat_IsSalaryZakat()
    {
        Assert.Equal(80m, FormEaLines.For(Slip(5000m, zakat: 80m), []).D3ZakatViaSalary);
    }

    [Fact]
    public void AnUncategorisedAllowance_IsAnOtherAllowance()
    {
        var f = FormEaLines.For(Slip(5100m), [Allowance(null, 100m, label: "Site allowance")]);

        Assert.Equal(100m, f.B1c);
        Assert.Contains("Site allowance", f.B1cDetails);
    }

    // The months add up line by line, details included.
    [Fact]
    public void TheYear_IsTheSumOfItsMonths()
    {
        var year = new FormEaFigures();
        year.Add(FormEaLines.For(Slip(5400m), [Allowance(C.AllowancePhoneFixed, 400m)]));
        year.Add(FormEaLines.For(Slip(5500m), [Allowance(C.AllowanceTravelOfficial, 500m, taxable: 200m)]));

        Assert.Equal(10000m, year.B1a);
        Assert.Equal(600m, year.B1c);
        Assert.Equal(300m, year.F);
        Assert.Equal(2, year.B1cDetails.Count);
    }

    // ─── The income every other LHDN form prints ───────────────────────

    // CP8D's PDF, CP21, CP22A and TP3 used GrossPay − bonus, which kept the
    // claim and the exempt allowance in "salary". Read off the EA lines, the
    // three figures exclude both and add up to Form EA's total.
    [Fact]
    public void FormIncome_LeavesOutClaimsAndExemptAllowances_AndSumsToTheEaTotal()
    {
        // 8,000 basic + 300 standard allowance + 500 official travel (exempt)
        // + 1,000 bonus + 193.72 claim; plus a 400 car BIK (non-cash).
        var f = FormEaLines.For(Slip(9993.72m, reimbursements: 193.72m),
        [
            Allowance(C.AllowanceStandard, 300m),
            Allowance(C.AllowanceTravelOfficial, 500m, subjectToPcb: false),
            Allowance(C.WagesBonusNonAnnual, 1000m),
            Allowance(C.BikCar, 400m),
        ]);

        Assert.Equal(8300m, f.SalaryAndAllowances);
        Assert.Equal(1000m, f.FeesCommissionBonus);
        Assert.Equal(400m, f.BenefitsInKindTotal);
        Assert.Equal(500m, f.F);
        Assert.Equal(f.Total, f.SalaryAndAllowances + f.FeesCommissionBonus + f.BenefitsInKindTotal);
    }

    // Leave pay is salary on EA (B1a) but sits with commission and bonus on
    // CP21 / CP22A (line 2-3), so it is also kept apart.
    [Fact]
    public void LeavePay_IsInB1a_AndAlsoKeptApart()
    {
        var f = FormEaLines.For(Slip(7500m), [Allowance(C.WagesLeavePay, 2500m)]);

        Assert.Equal(7500m, f.B1a);
        Assert.Equal(2500m, f.LeavePay);
    }
}
