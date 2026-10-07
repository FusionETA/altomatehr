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

    // A stated ringgit amount, not a monthly entitlement, so the join/leave
    // proration factor must not touch it. A mid-month joiner's RM 1,000 bonus
    // is RM 1,000, not RM 516; their RM 300 TP1 claim is still RM 300; unpaid
    // leave is already at the daily rate. Only recurring monthly figures (the
    // allowances) shrink with the days worked. Same 30 categories as v1.
    public bool SkipProration { get; init; }

    // The employee paid this to a third party themselves. It lowers the PCB the
    // employer withholds, but nothing comes out of the payslip — the money has
    // already left their pocket.
    public bool CashNeutral { get; init; }

    // A Borang TP1 declaration. Feeds LP1 (this month) and ΣLP (the year) in the
    // LHDN formula, lowering chargeable income and so the month's PCB.
    public bool FeedsLp1Relief { get; init; }

    // A Borang TP1 item that shares ONE annual limit with other items — e.g.
    // C4 medical: vaccination, dental and a full medical exam are RM 1,000 each
    // but all of C4 together is RM 10,000. `TaxExemptLimit` stays the item's
    // own cap; `Tp1ReliefGroups` holds the shared one. Null = the item stands
    // alone.
    public string? ReliefGroup { get; init; }

    // Court-ordered tax arrears. Remitted through CP39's own CP38 column and
    // kept out of `Payslip.Pcb`, because the MTD spec's X — accumulated PCB paid
    // this year — excludes tax instalments. Folding it in would suppress next
    // month's withholding.
    public bool AddsToCp38Field { get; init; }

    // Additional PCB (Employment Income): a manual top-up the admin adds to the
    // month's PCB. Comes out of take-home pay like any cash deduction and is
    // remitted in CP39's STANDARD PCB field (no column of its own — that is
    // what separates it from CP38). Held apart from `Payslip.Pcb` in
    // `Payslip.VoluntaryPcb` for the same reason as CP38: the MTD spec's X
    // excludes "additional Monthly Tax Deduction requested by the employee",
    // so folding it in would suppress next month's withholding.
    public bool AddsToStandardPcb { get; init; }

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
    public const string DeductAdditionalPcb = "deduct_additional_pcb";
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

    // The rest of Borang TP1 (1/2026), LHDN MTD Spec 2026 pages 28–35 and 37–38.
    public const string DeductTp1ParentsMedical = "deduct_tp1_parents_medical";            // C1a
    public const string DeductTp1ParentsDental = "deduct_tp1_parents_dental";              // C1b
    public const string DeductTp1ParentsMedicalExam = "deduct_tp1_parents_medical_exam";   // C1c
    public const string DeductTp1SupportingEquipment = "deduct_tp1_supporting_equipment";  // C2
    public const string DeductTp1EducationFees = "deduct_tp1_education_fees";              // C3a/b
    public const string DeductTp1Upskilling = "deduct_tp1_upskilling";                     // C3c
    public const string DeductTp1Vaccination = "deduct_tp1_vaccination";                   // C4c
    public const string DeductTp1Dental = "deduct_tp1_dental";                             // C4d
    public const string DeductTp1MedicalExam = "deduct_tp1_medical_exam";                  // C4e
    public const string DeductTp1LearningDisability = "deduct_tp1_learning_disability";    // C4f
    public const string DeductTp1Breastfeeding = "deduct_tp1_breastfeeding";               // C7
    public const string DeductTp1ChildcareFees = "deduct_tp1_childcare_fees";              // C8
    public const string DeductTp1Sspn = "deduct_tp1_sspn";                                 // C9
    public const string DeductTp1Alimony = "deduct_tp1_alimony";                           // C10
    public const string DeductTp1VoluntaryEpf = "deduct_tp1_voluntary_epf";                // C11a
    public const string DeductTp1EvCharging = "deduct_tp1_ev_charging";                    // C15
    public const string DeductTp1HousingLoan500k = "deduct_tp1_housing_loan_500k";         // C16a
    public const string DeductTp1HousingLoan750k = "deduct_tp1_housing_loan_750k";         // C16b
    public const string DeductTp1Tourism = "deduct_tp1_tourism";                           // C17
    public const string DeductDepartureLevyTp1 = "deduct_departure_levy_tp1";              // D1b

    // Borang TP1 groups whose items share one annual limit.
    public const string ReliefGroupParents = "C1";
    public const string ReliefGroupEducation = "C3";
    public const string ReliefGroupMedical = "C4";
    public const string ReliefGroupLifeInsuranceEpf = "C11";
    public const string ReliefGroupHousingLoan = "C16";

    // The shared limits. C11's is not a fixed figure: RM 3,000 plus whatever of
    // the RM 4,000 EPF relief compulsory EPF leaves unused — see
    // PayslipCalculator.ApplyVoluntaryEpfClaims. The value here is its ceiling.
    public static readonly IReadOnlyDictionary<string, decimal> Tp1ReliefGroups =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [ReliefGroupParents] = 8000m,
            [ReliefGroupEducation] = 7000m,
            [ReliefGroupMedical] = 10000m,
            [ReliefGroupLifeInsuranceEpf] = 7000m,
            [ReliefGroupHousingLoan] = 7000m,
        };

    public static PayrollAdjustmentCategoryMeta? Find(string? code) =>
        code is not null && All.TryGetValue(code, out var meta) ? meta : null;

    // What a previous employer's TP3 can declare item by item: everything whose
    // limit runs for the whole YEAR, not per employer — a PCB-exempt allowance
    // with a ceiling (travel for official duty, RM 6,000) and every TP1 relief.
    // Items with no yearly limit have nothing to carry and stay in the totals.
    public static bool CarriesFromPreviousEmployer(PayrollAdjustmentCategoryMeta meta) =>
        meta.FeedsLp1Relief
        || (meta.Kind == PayslipLineKind.ALLOWANCE && meta.SubjectToPcb && meta.TaxExemptLimit is > 0m);

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

            // Childcare for a child aged 12 or under — PCB-exempt to a yearly
            // ceiling (PR 5/2019 §7.2.4; the figure is below).
            new PayrollAdjustmentCategoryMeta
            {
                Code = AllowanceChildcare,
                Label = "Childcare Allowance",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
                // RM 3,000 a year, children up to 12 (LHDN MTD Spec 2026 p.21;
                // was RM 2,400).
                TaxExemptLimit = 3000m,
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
                // Not HRDF wages: PSMB Act 2001 s.2(c) excludes "any sum payable
                // to the employee to defray special expenses entailed on him by
                // the nature of his employment", and reimbursing the actual
                // bill is exactly that. The flat phone ALLOWANCE is wages and
                // stays in. (v1 made this change on 2026-07-10.)
                SubjectToPcb = false, SubjectToHrdf = false,
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
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesBonusNonAnnual,
                Label = "Non-Annual Bonus",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesCommission,
                Label = "Commission",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesIncentive,
                Label = "Incentive",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
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
                SkipProration = true,
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
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesServiceCharge,
                Label = "Service Charge",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesLeavePay,
                Label = "Unutilized Leave Pay",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = true,
                IsAdditionalRemuneration = true,
                SkipProration = true,
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
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesCompensationLossEmployment,
                Label = "Compensation for Loss of Employment",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesExGratia,
                Label = "Ex-gratia",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = WagesTaxBorneByEmployer,
                Label = "Tax Borne by Employer (perquisite)",
                Kind = PayslipLineKind.ALLOWANCE,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = true, SubjectToHrdf = false,
                IsAdditionalRemuneration = true,
                SkipProration = true,
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
                SkipProration = true,
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
                SkipProration = true,
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

            // Recovering a salary advance that was itself paid through payroll.
            // As in v1, it comes off the displayed GROSS, not only take-home,
            // so gross, statutory, cost and net all show the reduced wage —
            // behaviourally the same as Salary Adjustment. An advance paid
            // OUTSIDE payroll (never taxed) belongs in Miscellaneous instead,
            // so statutory stays on the full salary.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductAdvance,
                Label = "Advance Deduction",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = true, SubjectToSocso = true, SubjectToEis = true,
                SubjectToPcb = true, SubjectToHrdf = false,
                ReducesBase = true, ReducesGross = true,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductLoanRepayment,
                Label = "Loan Repayment",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductMiscellaneous,
                Label = "Miscellaneous / Other Deduction",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductCp38,
                Label = "CP38 Arrears (LHDN Order)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                AddsToCp38Field = true,
                // A stated ringgit amount from LHDN's order, not a monthly
                // entitlement — prorating it would under-remit for a mid-month
                // joiner or leaver.
                SkipProration = true,
            },

            // The employee asked for extra tax to be withheld, or an
            // under-deduction is being trued up. Touches no wage base.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductAdditionalPcb,
                Label = "Additional PCB",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                AddsToStandardPcb = true,
                // A stated ringgit amount, not a monthly entitlement.
                SkipProration = true,
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
                SkipProration = true,
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
                SkipProration = true,
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
                SkipProration = true,
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
                // C11b — self, spouse and (from 2026) children. Shares C11's
                // RM 7,000 with voluntary EPF.
                TaxExemptLimit = 3000m, ReliefGroup = ReliefGroupLifeInsuranceEpf,
                SkipProration = true,
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
                SkipProration = true,
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
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1SeriousDiseaseMedical,
                Label = "TP1 · Serious Disease / Fertility / Medical",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                // C4a serious disease + C4b fertility; the rest of C4 has its
                // own rows below and shares this RM 10,000.
                TaxExemptLimit = 10000m, ReliefGroup = ReliefGroupMedical,
                SkipProration = true,
            },

            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Lifestyle,
                Label = "TP1 · Lifestyle (Books / Computer / Smartphone / Tablet / Internet / Courses)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 2500m,
                SkipProration = true,
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
                SkipProration = true,
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
                SkipProration = true,
            },

            // C1 parents / grandparents: RM 8,000 across treatment, dental and a full
            // medical exam (the exam itself RM 1,000). They must be resident in Malaysia.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1ParentsMedical,
                Label = "TP1 · Parents — Medical, Special Needs & Carer",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 8000m, ReliefGroup = ReliefGroupParents,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1ParentsDental,
                Label = "TP1 · Parents — Dental Treatment",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 8000m, ReliefGroup = ReliefGroupParents,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1ParentsMedicalExam,
                Label = "TP1 · Parents — Full Medical Exam & Vaccination",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m, ReliefGroup = ReliefGroupParents,
                SkipProration = true,
            },
            // C2 — for a disabled self, spouse, child or parent. Not spectacles.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1SupportingEquipment,
                Label = "TP1 · Basic Supporting Equipment (Disabled)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 6000m,
                SkipProration = true,
            },
            // C3 education fees for self: RM 7,000, of which up-skilling courses RM 2,000.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1EducationFees,
                Label = "TP1 · Education Fees (Self)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 7000m, ReliefGroup = ReliefGroupEducation,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Upskilling,
                Label = "TP1 · Up-skilling / Self-enhancement Course",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 2000m, ReliefGroup = ReliefGroupEducation,
                SkipProration = true,
            },
            // C4 medical sub-items, each inside C4's RM 10,000.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Vaccination,
                Label = "TP1 · Vaccination (Self / Spouse / Child)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m, ReliefGroup = ReliefGroupMedical,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Dental,
                Label = "TP1 · Dental Exam & Treatment (Self / Spouse / Child)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m, ReliefGroup = ReliefGroupMedical,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1MedicalExam,
                Label = "TP1 · Full Medical Exam / Mental Health / Test Kits",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m, ReliefGroup = ReliefGroupMedical,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1LearningDisability,
                Label = "TP1 · Learning Disability Diagnosis & Intervention (Child)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 10000m, ReliefGroup = ReliefGroupMedical,
                SkipProration = true,
            },
            // C7 — a working mother, child aged 2 or under; once every 2 years.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Breastfeeding,
                Label = "TP1 · Breastfeeding Equipment",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m,
                SkipProration = true,
            },
            // C8 — registered centre, child aged 12 or under (2026). Either parent.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1ChildcareFees,
                Label = "TP1 · Childcare / Kindergarten / After-school Centre Fees",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 3000m,
                SkipProration = true,
            },
            // C9 — the year's deposits less withdrawals.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Sspn,
                Label = "TP1 · SSPN Net Savings",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 8000m,
                SkipProration = true,
            },
            // C10 — not allowed alongside the wife (spouse) relief; PayslipCalculator
            // drops it when that relief applies.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Alimony,
                Label = "TP1 · Alimony to Former Wife",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 4000m,
                SkipProration = true,
            },
            // C11a — voluntary EPF the employee paid themselves (i-Saraan etc). Counts
            // against the RM 4,000 EPF relief together with compulsory EPF, and any
            // excess can use C11b's RM 3,000 if life insurance leaves room.
            // Applied after EPF is known: PayslipCalculator.ApplyVoluntaryEpfClaims.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1VoluntaryEpf,
                Label = "TP1 · Voluntary EPF (paid outside payroll)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 7000m, ReliefGroup = ReliefGroupLifeInsuranceEpf,
                SkipProration = true,
            },
            // C15 — EV charger install, rental or subscription; composting machine or
            // food-waste grinder; home CCTV.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1EvCharging,
                Label = "TP1 · EV Charging / Food Waste Machine / Home CCTV",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 2500m,
                SkipProration = true,
            },
            // C16 — first home, SPA dated 2025–2027, three years from the first year of
            // interest. RM 7,000 up to RM 500k; RM 5,000 for RM 500,001–750,000.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1HousingLoan500k,
                Label = "TP1 · First Home Loan Interest (Home up to RM500k)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 7000m, ReliefGroup = ReliefGroupHousingLoan,
                SkipProration = true,
            },
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1HousingLoan750k,
                Label = "TP1 · First Home Loan Interest (Home RM500k–750k)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 5000m, ReliefGroup = ReliefGroupHousingLoan,
                SkipProration = true,
            },
            // C17 — admission fees for domestic tourism (2026).
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductTp1Tourism,
                Label = "TP1 · Tourist Attraction & Cultural Programme Fees",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                FeedsLp1Relief = true, CashNeutral = true,
                TaxExemptLimit = 1000m,
                SkipProration = true,
            },
            // D1b — departure levy for umrah or another religious pilgrimage. A
            // REBATE like self-paid zakat (MTD Spec Section E item 5 treats the
            // two together), not a relief. LHDN allows two claims in a lifetime;
            // that is on the admin to check against the employee's history.
            new PayrollAdjustmentCategoryMeta
            {
                Code = DeductDepartureLevyTp1,
                Label = "Departure Levy — Umrah / Religious Travel (TP1)",
                Kind = PayslipLineKind.DEDUCTION,
                SubjectToEpf = false, SubjectToSocso = false, SubjectToEis = false,
                SubjectToPcb = false, SubjectToHrdf = false,
                OffsetsPcb = true, CashNeutral = true,
                SkipProration = true,
            },
        }.ToDictionary(m => m.Code, StringComparer.Ordinal);
}
