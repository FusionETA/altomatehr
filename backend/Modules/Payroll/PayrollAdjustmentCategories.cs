using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// What a category is worth to each agency.
//
// Every allowance and deduction on a payslip routes through six wage bases at
// once — EPF, SOCSO, EIS, PCB, HRDF, and the payslip's own gross/net — and each
// agency defines "wages" differently. Putting one row of booleans next to each
// category is what keeps a travel allowance out of the EPF base and a bonus out
// of the HRDF levy without every call site re-deciding.
//
// Sources are on the individual entries in PayrollAdjustmentCategories below.
public sealed record PayrollAdjustmentCategoryMeta
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required PayslipLineKind Kind { get; init; }

    public required bool SubjectToEpf { get; init; }
    public required bool SubjectToSocso { get; init; }
    public required bool SubjectToEis { get; init; }
    public required bool SubjectToPcb { get; init; }

    // PSMB Act 2001 s.2 defines the levy's "wages" narrowly: basic salary, fixed
    // allowances of a like nature, leave pay and arrears — and explicitly NOT
    // travel allowance, expense reimbursements, gratuity, bonus, commission or
    // apprentice allowances.
    public required bool SubjectToHrdf { get; init; }

    // Annual ringgit ceiling below which the row is PCB-exempt. Once the year's
    // total for the category passes it, only the overflow feeds the PCB base —
    // the row stays fully wage-like for EPF/SOCSO/EIS either way.
    //
    // On a `FeedsLp1Relief` deduction this is the LHDN per-item TP1 cap instead:
    // the most of that item the employee may claim in a year.
    public decimal? TaxExemptLimit { get; init; }

    // A deduction that shrinks the wage bases themselves rather than only the
    // payout — unpaid leave was never earned, so no agency should see it.
    public bool ReducesBase { get; init; }

    // Lost earnings. Comes off GROSS, not take-home, and is therefore kept out
    // of the deductions total — counting it in both halves docks the employee
    // twice for one absence.
    public bool ReducesGross { get; init; }

    // Already expressed at the full daily rate, so the join/leave proration
    // factor must not be applied a second time. Under-deducting a late joiner's
    // unpaid leave is exactly the bug this prevents.
    public bool SkipProration { get; init; }

    // The employee paid this to a third party themselves. It lowers the PCB the
    // employer withholds, but nothing comes out of the payslip — the money has
    // already left their pocket.
    public bool CashNeutral { get; init; }

    // A Borang TP1 declaration. Feeds LP1 (this month) and ΣLP (the year) in the
    // LHDN formula, lowering chargeable income and so the month's PCB.
    public bool FeedsLp1Relief { get; init; }

    // Court-ordered tax arrears. Remitted through CP39's own CP38 column and
    // kept out of `Payslip.Pcb`, because the MTD spec's X — accumulated PCB paid
    // this year — excludes tax instalments. Folding it in would suppress next
    // month's withholding.
    public bool AddsToCp38Field { get; init; }

    // A one-off payment. LHDN taxes it as the delta it adds to annual chargeable
    // income rather than projecting it across the remaining months, so a RM
    // 10,000 bonus is not withheld against as a RM 10,000/month salary.
    public bool IsAdditionalRemuneration { get; init; }

    // Zakat. Comes off the month's PCB ringgit for ringgit, capped at the PCB.
    public bool OffsetsPcb { get; init; }

    // A benefit in kind or perquisite. The employee never receives cash, so it
    // stays out of gross and net — but it is still taxable income and still
    // appears on Form EA.
    public bool NonCash { get; init; }
}

// The catalogue of adjustment categories, ported from the reference app's
// `PAYROLL_ADJUSTMENT_CATEGORY_META`.
//
// Codes are strings, not an enum: they come out of JSON written by an older
// system, and an unknown code must be skippable rather than fatal. `Find`
// returns null for anything unrecognised and the calculator drops the row.
public static class PayrollAdjustmentCategories
{
    // ---- Allowances / recurring monthly ----
    public const string AllowanceStandard = "allowance_standard";
    public const string AllowanceTravelOfficial = "allowance_travel_official";
    public const string AllowanceTravelPrivate = "allowance_travel_private";
    public const string AllowanceParking = "allowance_parking";
    public const string AllowanceMeal = "allowance_meal";
    public const string AllowanceChildcare = "allowance_childcare";
    public const string AllowancePhoneBill = "allowance_phone_bill";
    public const string AllowancePhoneFixed = "allowance_phone_fixed";

    // ---- Remuneration ----
    public const string WagesBonusAnnual = "wages_bonus_annual";
    public const string WagesBonusNonAnnual = "wages_bonus_non_annual";
    public const string WagesCommission = "wages_commission";
    public const string WagesIncentive = "wages_incentive";
    public const string WagesArrears = "wages_arrears";
    public const string WagesOvertime = "wages_overtime";
    public const string WagesServiceCharge = "wages_service_charge";
    public const string WagesLeavePay = "wages_leave_pay";
    public const string WagesGratuity = "wages_gratuity";
    public const string WagesCompensationLossEmployment = "wages_compensation_loss_employment";
    public const string WagesExGratia = "wages_ex_gratia";
    public const string WagesTaxBorneByEmployer = "wages_tax_borne_by_employer";
    public const string WagesDirectorFee = "wages_director_fee";
    public const string WagesExpenseClaim = "wages_expense_claim";

    // ---- Benefits in kind / perquisites ----
    public const string BikCar = "bik_car";
    public const string BikMedical = "bik_medical";
    public const string BikAward = "bik_award";
    public const string BikLivingAccommodation = "bik_living_accommodation";
    public const string BikShareScheme = "bik_share_scheme";
    public const string BikSubsidisedLoan = "bik_subsidised_loan";
    public const string BikPhonePdaGift = "bik_phone_pda_gift";
    public const string BikOtherExempt = "bik_other_exempt";

    // ---- Deductions ----
    public const string DeductUnpaidLeave = "deduct_unpaid_leave";
    public const string DeductSalaryAdjustment = "deduct_salary_adjustment";
    public const string DeductAdvance = "deduct_advance";
    public const string DeductLoanRepayment = "deduct_loan_repayment";
    public const string DeductMiscellaneous = "deduct_miscellaneous";
    public const string DeductCp38 = "deduct_cp38";
    public const string DeductZakat = "deduct_zakat";
    public const string DeductZakatTp1 = "deduct_zakat_tp1";
    public const string DeductTp1 = "deduct_tp1";
    public const string DeductTp1LifeInsurance = "deduct_tp1_life_insurance";
    public const string DeductTp1MedicalInsurance = "deduct_tp1_medical_insurance";
    public const string DeductTp1Prs = "deduct_tp1_prs";
    public const string DeductTp1SeriousDiseaseMedical = "deduct_tp1_serious_disease_medical";
    public const string DeductTp1Lifestyle = "deduct_tp1_lifestyle";
    public const string DeductTp1SportsEquipment = "deduct_tp1_sports_equipment";
    public const string DeductTp1Other = "deduct_tp1_other";

    public static PayrollAdjustmentCategoryMeta? Find(string? code) =>
        code is not null && All.TryGetValue(code, out var meta) ? meta : null;

    public static readonly IReadOnlyDictionary<string, PayrollAdjustmentCategoryMeta> All =
        new[]
        {
            // ─── Allowances / recurring monthly ─────────────────────────────

            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceStandard,
                Label = "Standard Allowance",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
            },

            // Travelling / petrol / toll incurred EXERCISING the employment, not
            // commuting to it. PCB-exempt to RM 6,000/year — LHDN Public Ruling
            // 5/2019 §7.2.1. Not wages for EPF/SOCSO/EIS, and excluded from the
            // HRDF levy by PSMB Act s.2.
            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceTravelOfficial,
                Label = "Travel/Petrol/Toll (Official Duty)",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                TaxExemptLimit = 6000m,
            },

            // Commuting and private use is a straight cash benefit — fully
            // wage-like, and fully taxable.
            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceTravelPrivate,
                Label = "Travel/Petrol Allowance (Private Use/Commuting)",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
            },

            // Parking is exempt outright, no ceiling — PR 5/2019 §7.2.2.
            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceParking,
                Label = "Parking Allowance",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = false, SubjectToHrdf = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceMeal,
                Label = "Meal Allowance",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = false, SubjectToHrdf = true,
            },

            // Childcare for a child under 12 — exempt to RM 2,400/year under
            // PR 5/2019 §7.2.4.
            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceChildcare,
                Label = "Childcare Allowance",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
                TaxExemptLimit = 2400m,
            },

            // Reimbursing an actual bill is exempt; a flat monthly phone
            // allowance is not. Same money, different tax treatment, hence two
            // categories.
            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowancePhoneBill,
                Label = "Phone/Internet Bill Payment",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = false, SubjectToHrdf = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowancePhoneFixed,
                Label = "Phone Allowance (Fixed)",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
            },

            // ─── Remuneration ───────────────────────────────────────────────

            // An annual bonus is outside the SOCSO and EIS wage definitions
            // (Act 4 s.2 excludes annual bonus by name) but is EPF-able and
            // taxable. A non-annual bonus is not excluded, so it contributes to
            // all four.
            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesBonusAnnual,
                Label = "Annual Bonus",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesBonusNonAnnual,
                Label = "Non-Annual Bonus",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesCommission,
                Label = "Commission",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesIncentive,
                Label = "Incentive",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            // Back pay is wages for every agency — including HRDF, which names
            // arrears in the levy base.
            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesArrears,
                Label = "Arrears of Wages",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
                IsAdditionalRemuneration = true,
            },

            // Overtime is expressly outside the EPF definition of wages, inside
            // SOCSO's and EIS's, and taxed as additional remuneration because it
            // is not a fixed monthly amount.
            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesOvertime,
                Label = "Overtime",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesServiceCharge,
                Label = "Service Charge",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesLeavePay,
                Label = "Unutilized Leave Pay",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
                IsAdditionalRemuneration = true,
            },

            // Gratuity on discharge or retirement is excluded from every
            // contribution base and from the HRDF levy; only tax touches it.
            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesGratuity,
                Label = "Gratuity",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesCompensationLossEmployment,
                Label = "Compensation for Loss of Employment",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesExGratia,
                Label = "Ex-gratia",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesTaxBorneByEmployer,
                Label = "Tax Borne by Employer (perquisite)",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            // A director who is not an employee has no contract of service, so
            // the fee sits outside every contribution scheme.
            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesDirectorFee,
                Label = "Director Fee",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
            },

            // Repaying an expense the employee fronted is not income at all — it
            // touches nothing, it just moves money back.
            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesExpenseClaim,
                Label = "Expense Claim",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
            },

            // ─── Benefits in kind / perquisites ─────────────────────────────
            //
            // `NonCash` is what keeps these out of gross and net: the employer
            // pays the lease or the rent, the employee receives a taxable
            // benefit but no money. They still feed the PCB base where taxable.

            new PayrollAdjustmentCategoryMeta
            {
                Code = BikCar,
                Label = "Car/Petrol BIK",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                NonCash = true,
            },

            // Medical and dental benefits are exempt under ITA Schedule 6
            // Para 2, so they are disclosed but not taxed.
            new PayrollAdjustmentCategoryMeta
            {
                Code = BikMedical,
                Label = "Medical/Dental Benefit",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                NonCash = true,
            },

            // A long-service or excellence award is exempt to RM 2,000/year
            // (PR 5/2019 §7.4). Cash awards are wages, so unlike the rest of
            // this group it is NOT flagged non-cash.
            new PayrollAdjustmentCategoryMeta
            {
                Code = BikAward,
                Label = "Awards/Rewards",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                TaxExemptLimit = 2000m,
                IsAdditionalRemuneration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = BikLivingAccommodation,
                Label = "Living Accommodation",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                NonCash = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = BikShareScheme,
                Label = "Share Scheme",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                NonCash = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = BikSubsidisedLoan,
                Label = "Subsidised Loan Interest",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                NonCash = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = BikPhonePdaGift,
                Label = "Gift of Phone / PDA (1 unit/category/year)",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                NonCash = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = BikOtherExempt,
                Label = "Other Tax Exempt Benefit",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                NonCash = true,
            },

            // ─── Deductions ─────────────────────────────────────────────────

            // Unpaid leave is the one row that needs all three deduction flags:
            // it never became wages (ReducesBase), the employee never earned it
            // (ReducesGross), and it is already stated at the daily rate so the
            // join/leave factor must not touch it again (SkipProration).
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductUnpaidLeave,
                Label = "Unpaid Leave deduction",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
                ReducesBase = true, ReducesGross = true, SkipProration = true,
            },

            // Correcting an overpayment — the money was never owed, so it comes
            // off gross and out of every wage base.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductSalaryAdjustment,
                Label = "Salary Adjustment",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                ReducesBase = true, ReducesGross = true,
            },

            // Recovering a cash advance. It shrinks the wage bases but the
            // employee did earn the gross, so gross stands.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductAdvance,
                Label = "Advance Deduction",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                ReducesBase = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductLoanRepayment,
                Label = "Loan Repayment",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductMiscellaneous,
                Label = "Miscellaneous / Other Deduction",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductCp38,
                Label = "CP38 Arrears (LHDN Order)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                AddsToCp38Field = true,
            },

            // Zakat deducted through payroll and remitted to the zakat centre by
            // the employer.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductZakat,
                Label = "Zakat — via salary deduction (PZB)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                OffsetsPcb = true,
            },

            // Zakat the employee already paid directly, declared on Borang TP1
            // §D1(a). Same PCB offset, but no money leaves this payslip.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductZakatTp1,
                Label = "Zakat — self-paid (TP1)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                OffsetsPcb = true, CashNeutral = true,
            },

            // Uncapped catch-all kept for rows written before the TP1 items were
            // split out. New declarations use one of the specific codes below.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1,
                Label = "TP1/TP3 Deduction (legacy)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
            },

            // The TP1 caps below are LHDN's per-item annual relief ceilings. An
            // over-claim clamps to the ceiling rather than under-withholding.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1LifeInsurance,
                Label = "TP1 · Life Insurance",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 3000m,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1MedicalInsurance,
                Label = "TP1 · Medical / Education Insurance",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 4000m,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Prs,
                Label = "TP1 · Private Retirement Scheme (PRS)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 3000m,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1SeriousDiseaseMedical,
                Label = "TP1 · Serious Disease / Fertility / Medical",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 10000m,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Lifestyle,
                Label = "TP1 · Lifestyle (Books / PC / Internet)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 2500m,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1SportsEquipment,
                Label = "TP1 · Sports Equipment / Gym",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m,
            },

            // Deliberately uncapped — the admin has seen the receipts.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Other,
                Label = "TP1 · Other (Admin-Trusted)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
            },
        }.ToDictionary(m => m.Code, StringComparer.Ordinal);
}
