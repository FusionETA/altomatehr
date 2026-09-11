using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The orchestrator. These cover the routing decisions it owns — which wage each
// agency sees, and which statute supplies which divisor — rather than the
// statutory arithmetic, which has its own suites next door.
public class PayslipCalculatorTests
{
    // A full-month, mid-salary Malaysian on the 26-day basis. Deliberately plain:
    // each test perturbs exactly one thing.
    private static PayslipCalculator.Input Make(
        int year = 2026,
        int month = 1,
        SalaryType salaryType = SalaryType.MONTHLY,
        decimal? monthlySalary = 5000m,
        decimal? hourlyRate = null,
        DateTime? joinDate = null,
        DateTime? leaveDate = null,
        string? nationality = "Malaysian",
        WorkingDaysRule rule = WorkingDaysRule.TWENTY_SIX,
        IReadOnlyList<FixedAllowance>? allowances = null,
        bool hrdfEnabled = false,
        decimal? hrdfRate = null,
        decimal otNormalHours = 0m,
        decimal? workedHours = null,
        decimal ytdTaxable = 0m,
        decimal ytdEpf = 0m,
        IReadOnlyDictionary<string, decimal>? ytdByCategory = null) => new()
        {
            PeriodYear = year,
            PeriodMonth = month,
            SalaryType = salaryType,
            MonthlySalary = monthlySalary,
            HourlyRate = hourlyRate,
            JoinDate = joinDate,
            LeaveDate = leaveDate,
            FixedAllowances = allowances ?? [],
            Nationality = nationality,
            DateOfBirth = new DateTime(1990, 6, 15),
            IsResident = true,
            EpfEmployeeRate = 11m,
            SocsoScheme = SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            ContributeToEis = true,
            IncomeTaxNumber = "SG12345678",
            EpfNumber = "1234567",
            SocsoNumber = "900615012345",
            WorkingDaysRule = rule,
            HrdfEnabled = hrdfEnabled,
            HrdfRate = hrdfRate,
            OtNormalHours = otNormalHours,
            WorkedHours = workedHours,
            YtdTaxable = ytdTaxable,
            YtdEpf = ytdEpf,
            YtdAllowanceByCategory = ytdByCategory ?? new Dictionary<string, decimal>(),
        };

    private static FixedAllowance Allowance(
        string category, decimal amount, bool treatAsRecurring = false) => new()
        {
            Category = category,
            Name = null,
            Amount = amount,
            TreatAsRecurring = treatAsRecurring,
        };

    // ─── Proration: two divisors, two statutes ──────────────────────────

    [Fact]
    public void FullMonth_PaysTheWholeSalary()
    {
        var r = PayslipCalculator.Calculate(Make());

        Assert.Equal(5000m, r.BasicPay);
        Assert.Equal(5000m, r.ProratedPay);
        Assert.Equal(1m, r.ProratedFactor);
        Assert.Equal(31, r.ProratedDays);
        Assert.Equal(31, r.ProrationDaysInPeriod);
    }

    // s.18A opens "Notwithstanding section 60I" precisely so the ÷26 basis does
    // NOT govern an incomplete month. The org rule still sets the s.60I basis
    // that drives the hourly rate, so the two figures deliberately disagree.
    [Fact]
    public void Proration_UsesCalendarDays_EvenOnTheTwentySixRule()
    {
        var r = PayslipCalculator.Calculate(Make(
            rule: WorkingDaysRule.TWENTY_SIX,
            joinDate: new DateTime(2026, 1, 2)));

        Assert.Equal(26, r.TotalWorkingDays);        // s.60I, for the hourly rate
        Assert.Equal(30, r.ProratedDays);            // s.18A, calendar days
        Assert.Equal(31, r.ProrationDaysInPeriod);
        Assert.Equal(Money.Round2(5000m * 30m / 31m), r.ProratedPay);
    }

    // Regression: the money must use the EXACT ratio, and the stored factor is
    // only a snapshot. Rounding the factor first and then multiplying loses sen
    // — at 4 dp this employee is short 21 sen.
    [Fact]
    public void Proration_MultipliesByTheExactRatio_NotTheRoundedFactor()
    {
        var r = PayslipCalculator.Calculate(Make(
            year: 2026, month: 2,
            monthlySalary: 4999.99m,
            joinDate: new DateTime(2026, 2, 19)));

        Assert.Equal(10, r.ProratedDays);
        Assert.Equal(28, r.ProrationDaysInPeriod);

        // 4999.99 × 10/28 = 1785.7107… → 1785.71
        Assert.Equal(1785.71m, r.ProratedPay);

        // Whereas 4999.99 × round4(10/28) = 4999.99 × 0.3571 = 1785.50.
        Assert.NotEqual(1785.50m, r.ProratedPay);
    }

    // Hourly pay is already the exact quantity worked. Applying the join/leave
    // factor on top would dock the same days a second time.
    [Fact]
    public void HourlyStaff_ArePaidForHoursAndNotProratedAgain()
    {
        var r = PayslipCalculator.Calculate(Make(
            salaryType: SalaryType.HOURLY,
            monthlySalary: null,
            hourlyRate: 20m,
            workedHours: 80m,
            joinDate: new DateTime(2026, 1, 15)));

        Assert.Equal(1600m, r.BasicPay);
        Assert.Equal(1600m, r.ProratedPay);
    }

    // ─── Overtime ───────────────────────────────────────────────────────

    // The hourly rate is the s.60I ordinary rate — monthly ÷ (basis × daily
    // hours) — so the org's rule reaches OT through the basis, not proration.
    [Fact]
    public void Overtime_PaysOffTheOrdinaryRateFromTheWorkingDaysBasis()
    {
        var r = PayslipCalculator.Calculate(Make(otNormalHours: 10m));

        // 5000 ÷ (26 × 8) = 24.0384…, × 10 h × 1.5 = 360.58
        Assert.Equal(360.58m, r.OtPay);
    }

    // EPF Act 1991 s.2 excludes overtime from wages; SOCSO and EIS include it.
    // If OT were leaking into the EPF base, the contribution would move.
    [Fact]
    public void Overtime_StaysOutOfTheEpfWage()
    {
        var withoutOt = PayslipCalculator.Calculate(Make());
        var withOt = PayslipCalculator.Calculate(Make(otNormalHours: 20m));

        Assert.Equal(withoutOt.EpfEmployee, withOt.EpfEmployee);
        Assert.Equal(withoutOt.EpfEmployer, withOt.EpfEmployer);
    }

    // Overtime is not a fixed monthly amount, so LHDN taxes it through the
    // additional-remuneration formula rather than projecting it forward.
    [Fact]
    public void Overtime_IsTaxedAsAdditionalRemuneration()
    {
        var r = PayslipCalculator.Calculate(Make(otNormalHours: 40m));

        Assert.True(r.PcbAdditional > 0m);
    }

    // ─── Allowance routing ──────────────────────────────────────────────

    // Parking is exempt outright (LHDN PR 5/2019 §7.2.2) but is still wages, so
    // it raises the EPF contribution while leaving PCB untouched.
    [Fact]
    public void PcbExemptAllowance_RaisesEpfButNotPcb()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withParking = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceParking, 300m)]));

        Assert.Equal(plain.Pcb, withParking.Pcb);
        Assert.True(withParking.EpfEmployee > plain.EpfEmployee);
        Assert.Equal(300m, withParking.TotalAllowances);
        Assert.Equal(5300m, withParking.GrossPay);
    }

    // Official-duty travel is outside every contribution base, so only gross and
    // the PCB ceiling see it.
    [Fact]
    public void OfficialTravelAllowance_TouchesNoContributionBase()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withTravel = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceTravelOfficial, 400m)]));

        Assert.Equal(plain.EpfEmployee, withTravel.EpfEmployee);
        Assert.Equal(plain.SocsoEmployee, withTravel.SocsoEmployee);
        Assert.Equal(plain.EisEmployee, withTravel.EisEmployee);
        Assert.Equal(5400m, withTravel.GrossPay);
    }

    // A bonus is a one-off, so its tax lands in PCB(C) — the delta it adds to
    // annual chargeable income — and not in the recurring monthly projection.
    [Fact]
    public void AnnualBonus_RoutesToAdditionalRemuneration()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.WagesBonusAnnual, 6000m)]));

        Assert.True(r.PcbAdditional > 0m);

        // The normal side must look like a bonus-free month.
        var plain = PayslipCalculator.Calculate(Make());
        Assert.Equal(plain.PcbNormal, r.PcbNormal);
    }

    // Ticking "treat as recurring" says the payment really is monthly, so it
    // belongs in the smoothed projection instead of the one-shot formula.
    [Fact]
    public void BonusTickedAsRecurring_RoutesToTheNormalPcbPath()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.WagesBonusAnnual, 6000m,
                    treatAsRecurring: true),
            ]));

        Assert.Equal(0m, r.PcbAdditional);
        Assert.True(r.PcbNormal > 0m);
    }

    // Whatever the tax treatment, every wage paid in the month joins the wage
    // that picks the KWSP tier — EPF Act s.2 reads "wages" broadly. The flag has
    // never controlled EPF, and letting it do so forced admins to choose between
    // correct EPF and correct PCB on the same line.
    [Fact]
    public void Bonus_JoinsTheEpfWageWhicheverWayItIsTaxedForPcb()
    {
        var asAdditional = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.WagesBonusAnnual, 2000m)]));

        var asRecurring = PayslipCalculator.Calculate(Make(
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.WagesBonusAnnual, 2000m,
                    treatAsRecurring: true),
            ]));

        var plain = PayslipCalculator.Calculate(Make());

        Assert.True(asAdditional.EpfEmployee > plain.EpfEmployee);
        Assert.Equal(asRecurring.EpfEmployee, asAdditional.EpfEmployee);
    }

    // The 13%→12% employer cliff is decided by the REGULAR wage. A bonus must
    // not tip a sub-RM-5,000 employee onto the lower employer rate for one month.
    [Fact]
    public void Bonus_DoesNotPushTheEmployerAcrossTheKwspCliff()
    {
        var r = PayslipCalculator.Calculate(Make(
            monthlySalary: 4000m,
            allowances: [Allowance(PayrollAdjustmentCategories.WagesBonusAnnual, 3000m)]));

        // 13% of the combined 7,000, ceiled — not 12%.
        Assert.Equal(910m, r.EpfEmployer);
    }

    // An unrecognised category is skipped, not fatal. These rows arrive as
    // free-form JSON from an older system; one bad code must not take a month's
    // payroll down.
    [Fact]
    public void UnknownCategory_IsSkipped()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances: [Allowance("allowance_from_the_future", 500m)]));

        Assert.Empty(r.LineItems);
        Assert.Equal(0m, r.TotalAllowances);
        Assert.Equal(5000m, r.GrossPay);
    }

    // ─── Annual exemption ceilings ──────────────────────────────────────

    // Childcare is exempt to RM 2,400/year (PR 5/2019 §7.2.4), so a RM 500 month
    // on a fresh year contributes nothing to the PCB base.
    //
    // PCB does not come out IDENTICAL to a no-allowance month, and should not:
    // childcare is still EPF-able, so the extra contribution buys a little more
    // EPF relief and the withholding dips. What matters is that it never rises,
    // and that the same RM 500 in a taxable category does raise it.
    [Fact]
    public void AllowanceUnderItsCeiling_IsFullyPcbExempt()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withChildcare = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceChildcare, 500m)]));
        var withTaxable = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceStandard, 500m)]));

        Assert.True(withChildcare.Pcb <= plain.Pcb);
        Assert.True(withChildcare.Pcb < withTaxable.Pcb);

        var line = Assert.Single(withChildcare.LineItems);
        Assert.Equal(500m, line.Amount);
        Assert.Equal(0m, line.PcbTaxableAmount);   // clamped to nothing taxable
    }

    // Once the year's headroom is used up, only the overflow is taxable — and
    // the exempt part must be recorded so it cannot leak into next month's Y.
    [Fact]
    public void AllowanceOverItsCeiling_TaxesOnlyTheOverflow()
    {
        var r = PayslipCalculator.Calculate(Make(
            month: 6,
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceChildcare, 500m)],
            ytdByCategory: new Dictionary<string, decimal>
            {
                [PayrollAdjustmentCategories.AllowanceChildcare] = 2300m,
            }));

        // RM 100 of headroom left, so RM 400 of the 500 is taxable.
        var line = Assert.Single(r.LineItems);
        Assert.Equal(500m, line.Amount);
        Assert.Equal(400m, line.PcbTaxableAmount);
    }

    // Two rows in the same category within ONE run must not each claim the whole
    // remaining headroom.
    [Fact]
    public void TwoRowsInOneCategory_ShareTheSameHeadroom()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.AllowanceChildcare, 2000m),
                Allowance(PayrollAdjustmentCategories.AllowanceChildcare, 1000m),
            ]));

        // Ceiling is 2,400: the first row is fully exempt, the second has only
        // RM 400 of headroom left, so RM 600 of it is taxable.
        Assert.Equal(0m, r.LineItems[0].PcbTaxableAmount);
        Assert.Equal(600m, r.LineItems[1].PcbTaxableAmount);
    }

    // No ceiling on the category means no clamp, and null is what tells the YTD
    // read to fall back to the full amount.
    [Fact]
    public void AllowanceWithNoCeiling_RecordsNoClamp()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceStandard, 500m)]));

        Assert.Null(Assert.Single(r.LineItems).PcbTaxableAmount);
    }

    // ─── Zakat ──────────────────────────────────────────────────────────

    // Zakat comes off the PCB ringgit for ringgit — the employee pays it OUT OF
    // the tax owed, not on top of it.
    [Fact]
    public void Zakat_OffsetsThePcbForTheMonth()
    {
        var plain = PayslipCalculator.Calculate(Make(monthlySalary: 9000m));
        var withZakat = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances: [Allowance(PayrollAdjustmentCategories.DeductZakat, 100m)]));

        Assert.True(plain.Pcb > 0m);
        Assert.Equal(Money.Round2(plain.Pcb - 100m), withZakat.Pcb);
        Assert.Equal(100m, withZakat.Zakat);
    }

    [Fact]
    public void Zakat_NeverPushesPcbBelowZero()
    {
        var r = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances: [Allowance(PayrollAdjustmentCategories.DeductZakat, 999_999m)]));

        Assert.Equal(0m, r.Pcb);
    }

    // Self-paid zakat declared on TP1 is cash-neutral: it lowers the withholding
    // but nothing leaves this payslip, because the employee already paid it.
    [Fact]
    public void SelfPaidZakat_LowersPcbWithoutTouchingTakeHome()
    {
        var plain = PayslipCalculator.Calculate(Make(monthlySalary: 9000m));
        var withTp1 = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances: [Allowance(PayrollAdjustmentCategories.DeductZakatTp1, 100m)]));

        Assert.Equal(Money.Round2(plain.Pcb - 100m), withTp1.Pcb);
        Assert.Equal(0m, withTp1.TotalDeductions);

        // Net rises by exactly the PCB no longer withheld.
        Assert.Equal(Money.Round2(plain.NetPay + 100m), withTp1.NetPay);
    }

    // ─── Unpaid leave ───────────────────────────────────────────────────

    // Lost earnings come off GROSS and are deliberately absent from the
    // deductions total — counting them in both halves docks the employee twice.
    [Fact]
    public void UnpaidLeave_ReducesGrossAndIsNotAlsoADeduction()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.DeductUnpaidLeave, 192.31m)]));

        Assert.Equal(Money.Round2(5000m - 192.31m), r.GrossPay);
        Assert.Equal(0m, r.TotalDeductions);

        // The base-salary figure still shows the full salary; the absence is its
        // own line.
        Assert.Equal(5000m, r.ProratedPay);
    }

    // It is already stated at the full daily rate, so the join/leave factor must
    // not be applied to it a second time.
    [Fact]
    public void UnpaidLeave_IsNotProratedAgainForALateJoiner()
    {
        var r = PayslipCalculator.Calculate(Make(
            joinDate: new DateTime(2026, 1, 16),
            allowances: [Allowance(PayrollAdjustmentCategories.DeductUnpaidLeave, 192.31m)]));

        Assert.Equal(192.31m, Assert.Single(r.LineItems).Amount);
    }

    // It never became wages, so no agency should see it.
    [Fact]
    public void UnpaidLeave_ShrinksEveryWageBase()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withLeave = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.DeductUnpaidLeave, 500m)]));

        Assert.True(withLeave.EpfEmployee < plain.EpfEmployee);
        Assert.True(withLeave.SocsoEmployee <= plain.SocsoEmployee);
        Assert.True(withLeave.Pcb < plain.Pcb);
    }

    // ─── Benefits in kind ───────────────────────────────────────────────

    // The employer pays the lease; the employee gets a taxable benefit but no
    // money. So it never reaches gross — yet the tax on it is real, and comes
    // out of the cash the employee does receive. Net therefore falls by exactly
    // the extra PCB, which is the whole reason a BIK needs its own subtotal.
    [Fact]
    public void BenefitInKind_IsTaxedButNeverPaid()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withCar = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.BikCar, 700m)]));

        Assert.Equal(700m, withCar.TotalBenefitsInKind);
        Assert.Equal(0m, withCar.TotalAllowances);
        Assert.Equal(plain.GrossPay, withCar.GrossPay);

        // Taxable income, but outside every contribution base.
        Assert.True(withCar.Pcb > plain.Pcb);
        Assert.Equal(plain.EpfEmployee, withCar.EpfEmployee);
        Assert.Equal(plain.SocsoEmployee, withCar.SocsoEmployee);

        Assert.Equal(Money.Round2(plain.NetPay - (withCar.Pcb - plain.Pcb)), withCar.NetPay);
    }

    // ─── HRDF ───────────────────────────────────────────────────────────

    // PSMB Act 2001 s.2 defines the levy's "employee" as a Malaysian citizen, so
    // a foreign worker is outside it however the org is configured.
    [Fact]
    public void Hrdf_AppliesToCitizensOnly()
    {
        var citizen = PayslipCalculator.Calculate(Make(
            hrdfEnabled: true, hrdfRate: 1m));

        var foreigner = PayslipCalculator.Calculate(Make(
            nationality: "Indonesian", hrdfEnabled: true, hrdfRate: 1m));

        Assert.Equal(5000m, citizen.HrdfWage);
        Assert.Equal(50m, citizen.Hrdf);

        Assert.Equal(0m, foreigner.HrdfWage);
        Assert.Equal(0m, foreigner.Hrdf);
    }

    // The Act names travel allowance and bonus among the exclusions.
    [Fact]
    public void Hrdf_ExcludesTravelAndBonusFromTheLevyWage()
    {
        var r = PayslipCalculator.Calculate(Make(
            hrdfEnabled: true, hrdfRate: 1m,
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.AllowanceTravelOfficial, 400m),
                Allowance(PayrollAdjustmentCategories.WagesBonusAnnual, 3000m),
                Allowance(PayrollAdjustmentCategories.AllowanceMeal, 200m),
            ]));

        // Only the meal allowance is of "a like nature" to fixed wages.
        Assert.Equal(5200m, r.HrdfWage);
        Assert.Equal(52m, r.Hrdf);
    }

    [Fact]
    public void Hrdf_IsZeroWhenTheLevyIsOff()
    {
        var r = PayslipCalculator.Calculate(Make(hrdfEnabled: false, hrdfRate: 1m));

        Assert.Equal(0m, r.Hrdf);
        Assert.Equal(0m, r.HrdfWage);
    }

    // ─── Aggregates ─────────────────────────────────────────────────────

    [Fact]
    public void NetPay_IsGrossLessEveryEmployeeSideAmount()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.DeductLoanRepayment, 250m)]));

        Assert.Equal(
            Money.Round2(
                r.GrossPay - r.EpfEmployee - r.SocsoEmployee - r.EisEmployee
                - r.SkbbkEmployee - r.TotalDeductions - r.Pcb),
            r.NetPay);
    }

    // SKBBK is employee-only, so it lowers net without raising the employer's
    // cost. HRDF is employer-only, so it does the reverse.
    [Fact]
    public void CostToEmployer_IsGrossPlusTheEmployerSideOnly()
    {
        var r = PayslipCalculator.Calculate(Make(hrdfEnabled: true, hrdfRate: 1m));

        Assert.Equal(
            Money.Round2(
                r.GrossPay + r.EpfEmployer + r.SocsoEmployer + r.EisEmployer + r.Hrdf),
            r.TotalCostToEmployer);
    }

    // ─── Statutory warnings ─────────────────────────────────────────────

    // LHDN's MTD spec never gates the calculation on holding a tax number — the
    // number is needed to FILE. So the money is still computed and the gap is
    // reported instead.
    [Fact]
    public void MissingTaxNumber_WarnsButStillComputesPcb()
    {
        var r = PayslipCalculator.Calculate(Make(monthlySalary: 9000m) with
        {
            IncomeTaxNumber = null,
        });

        Assert.Contains(PayslipCalculator.Warnings.MissingIncomeTaxNumber, r.StatutoryWarnings);
        Assert.True(r.Pcb > 0m);
    }

    [Fact]
    public void MissingMemberNumbers_AreReportedPerScheme()
    {
        var r = PayslipCalculator.Calculate(Make() with
        {
            EpfNumber = "  ",
            SocsoNumber = null,
        });

        Assert.Contains(PayslipCalculator.Warnings.MissingEpfNumber, r.StatutoryWarnings);
        Assert.Contains(PayslipCalculator.Warnings.MissingSocsoNumber, r.StatutoryWarnings);
    }

    // An employee outside a scheme cannot be missing its number.
    [Fact]
    public void NonContributor_IsNotWarnedAboutTheNumberTheyDoNotNeed()
    {
        var r = PayslipCalculator.Calculate(Make() with
        {
            ContributeToEpf = false,
            EpfNumber = null,
            SocsoScheme = null,
            SocsoNumber = null,
        });

        Assert.DoesNotContain(PayslipCalculator.Warnings.MissingEpfNumber, r.StatutoryWarnings);
        Assert.DoesNotContain(PayslipCalculator.Warnings.MissingSocsoNumber, r.StatutoryWarnings);
    }

    [Fact]
    public void CompleteProfile_ProducesNoWarnings()
    {
        Assert.Empty(PayslipCalculator.Calculate(Make()).StatutoryWarnings);
    }

    // ─── Citizenship detection ──────────────────────────────────────────

    // Free-text nationality arrives spelled every way there is, and getting it
    // wrong routes a citizen into the wrong EPF branch and drops them out of the
    // HRDF levy.
    [Theory]
    [InlineData("Malaysian")]
    [InlineData("malaysia")]
    [InlineData("MY")]
    [InlineData("mys")]
    [InlineData("Warganegara Malaysia")]
    [InlineData("  rakyat malaysia  ")]
    public void MalaysianNationality_IsRecognisedInItsCommonSpellings(string value) =>
        Assert.True(PayslipCalculator.IsMalaysianNationality(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Singaporean")]
    [InlineData("Indonesian")]
    public void NonMalaysianNationality_IsNotMistakenForCitizenship(string? value) =>
        Assert.False(PayslipCalculator.IsMalaysianNationality(value));

    // ─── EPF snapshot ───────────────────────────────────────────────────

    // The branch overrides the profile's declared rate everywhere but Part A, so
    // the snapshot records what was applied rather than what was configured.
    [Fact]
    public void EpfSnapshot_RecordsTheRatesTheBranchActuallyApplied()
    {
        var r = PayslipCalculator.Calculate(Make() with
        {
            DateOfBirth = new DateTime(1960, 1, 1),   // 66 at period end
        });

        Assert.Equal(EpfBranch.MALAYSIAN_CITIZEN_60_PLUS, r.EpfRates.Branch);
        Assert.Equal(0m, r.EpfRates.Employee);        // Part E: employer only
        Assert.Equal(4m, r.EpfRates.Employer);
        Assert.Equal(0m, r.EpfEmployee);
    }

    // The mandatory/voluntary split lets the payslip render two lines without
    // redoing the arithmetic from a single total.
    [Fact]
    public void EpfSnapshot_SplitsMandatoryFromVoluntary()
    {
        var r = PayslipCalculator.Calculate(Make() with
        {
            EpfEmployeeVoluntary = 3m,
        });

        Assert.Equal(150m, r.EpfRates.VoluntaryAmountEmployee);   // ceil(3% of 5,000)
        Assert.Equal(
            Money.Round2(r.EpfEmployee - 150m),
            r.EpfRates.MandatoryAmountEmployee);
    }

    // ─── SKBBK ──────────────────────────────────────────────────────────

    // SKBBK started in Jun 2026, so a rerun of an earlier month must produce 0
    // rather than today's phase — the period, not the clock, decides.
    [Fact]
    public void Skbbk_IsZeroForAPeriodBeforeItExisted()
    {
        var r = PayslipCalculator.Calculate(Make(year: 2026, month: 1) with
        {
            ContributeToSkbbk = true,
        });

        Assert.Equal(0m, r.SkbbkEmployee);
    }

    [Fact]
    public void Skbbk_IsChargedOnceTheSchemeStarts()
    {
        var r = PayslipCalculator.Calculate(Make(year: 2026, month: 6) with
        {
            ContributeToSkbbk = true,
        });

        Assert.True(r.SkbbkEmployee > 0m);
        Assert.Equal(5000m, r.SkbbkWage);
    }

    // Opting out means no contribution, whatever the period.
    [Fact]
    public void Skbbk_IsNotChargedWithoutTheOptIn()
    {
        var r = PayslipCalculator.Calculate(Make(year: 2026, month: 6));

        Assert.Equal(0m, r.SkbbkEmployee);
    }

    // ─── Line item snapshots ────────────────────────────────────────────

    // The catalogue can change; a filed payslip cannot. So the flags are copied
    // onto the row rather than re-read at query time.
    [Fact]
    public void LineItems_SnapshotTheCategorysFlags()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances: [Allowance(PayrollAdjustmentCategories.AllowanceTravelOfficial, 400m)]));

        var line = Assert.Single(r.LineItems);
        Assert.Equal(PayslipLineKind.ALLOWANCE, line.Kind);
        Assert.Equal("Travel/Petrol/Toll (Official Duty)", line.Label);
        Assert.False(line.SubjectToEpf);
        Assert.False(line.SubjectToSocso);
        Assert.False(line.SubjectToEis);
        Assert.True(line.SubjectToPcb);
    }

    [Fact]
    public void LineItems_PreferTheAdminsOwnLabel()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances:
            [
                new FixedAllowance
                {
                    Category = PayrollAdjustmentCategories.AllowanceStandard,
                    Name = "Site Allowance",
                    Amount = 100m,
                },
            ]));

        Assert.Equal("Site Allowance", Assert.Single(r.LineItems).Label);
    }

    // A zero or negative row is noise, not a deduction of the other sign.
    [Fact]
    public void NonPositiveRows_AreDropped()
    {
        var r = PayslipCalculator.Calculate(Make(
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.AllowanceStandard, 0m),
                Allowance(PayrollAdjustmentCategories.AllowanceStandard, -50m),
            ]));

        Assert.Empty(r.LineItems);
    }

    // ─── Per-run extras (the phase 4 inputs) ────────────────────────────

    // A reimbursement is not wage, so it reaches gross without touching a single
    // contribution base.
    [Fact]
    public void Reimbursement_AddsToGrossButNotToAnyWageBase()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withClaim = PayslipCalculator.Calculate(Make() with
        {
            Reimbursements = [new PayslipCalculator.Reimbursement("claim-1", "Taxi", 80m)],
        });

        Assert.Equal(80m, withClaim.TotalReimbursements);
        Assert.Equal(Money.Round2(plain.GrossPay + 80m), withClaim.GrossPay);
        Assert.Equal(plain.EpfEmployee, withClaim.EpfEmployee);
        Assert.Equal(plain.Pcb, withClaim.Pcb);

        var line = Assert.Single(withClaim.LineItems);
        Assert.Equal(PayslipLineKind.REIMBURSEMENT, line.Kind);
        Assert.Equal("claim-1", line.ClaimId);
    }

    [Fact]
    public void ManualDeduction_ComesOffTakeHomeOnly()
    {
        var plain = PayslipCalculator.Calculate(Make());
        var withDeduction = PayslipCalculator.Calculate(Make() with
        {
            ManualDeductions = [new PayslipCalculator.ManualDeduction("Canteen", 45m)],
        });

        Assert.Equal(45m, withDeduction.TotalDeductions);
        Assert.Equal(plain.GrossPay, withDeduction.GrossPay);
        Assert.Equal(Money.Round2(plain.NetPay - 45m), withDeduction.NetPay);
    }

    // ─── CP38 ───────────────────────────────────────────────────────────

    // Court-ordered arrears are withheld and remitted, but held apart from PCB:
    // the MTD spec's X excludes tax instalments, so folding CP38 in would
    // suppress next month's withholding.
    [Fact]
    public void Cp38_IsWithheldSeparatelyFromPcb()
    {
        var plain = PayslipCalculator.Calculate(Make(monthlySalary: 9000m));
        var withCp38 = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances: [Allowance(PayrollAdjustmentCategories.DeductCp38, 150m)]));

        Assert.Equal(150m, withCp38.Cp38);
        Assert.Equal(plain.Pcb, withCp38.Pcb);
        Assert.Equal(150m, withCp38.TotalDeductions);
        Assert.Equal(Money.Round2(plain.NetPay - 150m), withCp38.NetPay);
    }

    // ─── TP1 declarations ───────────────────────────────────────────────

    // A TP1 item lowers chargeable income, hence the month's PCB, but takes
    // nothing from the payslip — the employee already paid the third party.
    [Fact]
    public void Tp1Declaration_LowersPcbWithoutTouchingTakeHome()
    {
        var plain = PayslipCalculator.Calculate(Make(monthlySalary: 9000m));
        var withTp1 = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.DeductTp1LifeInsurance, 250m),
            ]));

        Assert.True(withTp1.Pcb < plain.Pcb);
        Assert.Equal(0m, withTp1.TotalDeductions);
    }

    // An over-claim clamps to LHDN's per-item ceiling rather than
    // under-withholding all year.
    [Fact]
    public void Tp1OverClaim_ClampsToTheItemCeiling()
    {
        var atCeiling = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances:
            [
                // Life insurance is capped at RM 3,000/year.
                Allowance(PayrollAdjustmentCategories.DeductTp1LifeInsurance, 3000m),
            ]));

        var wayOver = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances:
            [
                Allowance(PayrollAdjustmentCategories.DeductTp1LifeInsurance, 9000m),
            ]));

        Assert.Equal(atCeiling.Pcb, wayOver.Pcb);
    }

    // The admin-trusted catch-all is deliberately uncapped.
    [Fact]
    public void Tp1Other_HasNoCeiling()
    {
        var small = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances: [Allowance(PayrollAdjustmentCategories.DeductTp1Other, 1000m)]));

        var large = PayslipCalculator.Calculate(Make(
            monthlySalary: 9000m,
            allowances: [Allowance(PayrollAdjustmentCategories.DeductTp1Other, 5000m)]));

        Assert.True(large.Pcb < small.Pcb);
    }

    // ─── PERKESO relief toggle ──────────────────────────────────────────

    // The RM 350 SOCSO + EIS relief is applied without waiting for a TP1 because
    // the employer already knows the figure — unless the org opts out and leaves
    // it to the employee's year-end return.
    [Fact]
    public void PerkesoRelief_CanBeLeftToTheEmployee()
    {
        var autoApplied = PayslipCalculator.Calculate(Make(monthlySalary: 9000m));
        var leftToEmployee = PayslipCalculator.Calculate(Make(monthlySalary: 9000m) with
        {
            AutoApplySocsoEisRelief = false,
        });

        Assert.True(leftToEmployee.Pcb > autoApplied.Pcb);
    }

    // ─── Year to date ───────────────────────────────────────────────────

    // The whole point of carrying YTD: a mid-year joiner's earlier income lifts
    // the annualised estimate, so they are not under-withheld until December.
    [Fact]
    public void YearToDateIncome_RaisesTheMonthsWithholding()
    {
        var freshYear = PayslipCalculator.Calculate(Make(month: 7, monthlySalary: 9000m));
        var withHistory = PayslipCalculator.Calculate(Make(
            month: 7, monthlySalary: 9000m, ytdTaxable: 54_000m, ytdEpf: 5_940m));

        Assert.True(withHistory.Pcb > freshYear.Pcb);
    }
}
