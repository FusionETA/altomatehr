// The payroll adjustment catalogue, carried over verbatim from the previous
// system's `PAYROLL_ADJUSTMENT_CATEGORY_META`.
//
// Only code / label / kind live here. Each category also carries statutory
// treatment over there — whether it is subject to EPF, SOCSO, EIS, PCB and
// HRDF, whether it counts as additional remuneration, whether it is non-cash.
// That belongs to the payroll engine, not to a form: this screen records WHICH
// category an amount is, and the engine decides what that means. Keeping the
// codes identical is what makes that handover possible.
//
// Lives under employees/lib for now because the employee profile is the only
// thing that reads it. It moves to a payroll feature when one exists.

export type AdjustmentKind = "ALLOWANCE" | "DEDUCTION";

export type AdjustmentCategory = {
  code: string;
  label: string;
  /** How the previous system groups these on screen. */
  group: string;
};

export const ALLOWANCE_CATEGORIES: AdjustmentCategory[] = [
  { code: "allowance_standard", label: "Standard Allowance", group: "Allowances / Recurring Monthly" },
  { code: "allowance_travel_official", label: "Travel/Petrol/Toll (Official Duty)", group: "Allowances / Recurring Monthly" },
  { code: "allowance_travel_private", label: "Travel/Petrol Allowance (Private Use/Commuting)", group: "Allowances / Recurring Monthly" },
  { code: "allowance_parking", label: "Parking Allowance", group: "Allowances / Recurring Monthly" },
  { code: "allowance_meal", label: "Meal Allowance", group: "Allowances / Recurring Monthly" },
  { code: "allowance_childcare", label: "Childcare Allowance", group: "Allowances / Recurring Monthly" },
  { code: "allowance_phone_bill", label: "Phone/Internet Bill Payment", group: "Allowances / Recurring Monthly" },
  { code: "allowance_phone_fixed", label: "Phone Allowance (Fixed)", group: "Allowances / Recurring Monthly" },
  { code: "wages_bonus_annual", label: "Annual Bonus", group: "Remuneration" },
  { code: "wages_bonus_non_annual", label: "Non-Annual Bonus", group: "Remuneration" },
  { code: "wages_commission", label: "Commission", group: "Remuneration" },
  { code: "wages_incentive", label: "Incentive", group: "Remuneration" },
  { code: "wages_arrears", label: "Arrears of Wages", group: "Remuneration" },
  { code: "wages_overtime", label: "Overtime", group: "Remuneration" },
  { code: "wages_service_charge", label: "Service Charge", group: "Remuneration" },
  { code: "wages_leave_pay", label: "Unutilized Leave Pay", group: "Remuneration" },
  { code: "wages_gratuity", label: "Gratuity", group: "Remuneration" },
  { code: "wages_compensation_loss_employment", label: "Compensation for Loss of Employment", group: "Remuneration" },
  { code: "wages_ex_gratia", label: "Ex-gratia", group: "Remuneration" },
  { code: "wages_tax_borne_by_employer", label: "Tax Borne by Employer (perquisite)", group: "Remuneration" },
  { code: "wages_director_fee", label: "Director Fee", group: "Remuneration" },
  { code: "wages_expense_claim", label: "Expense Claim", group: "Remuneration" },
  { code: "bik_car", label: "Car/Petrol BIK", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_medical", label: "Medical/Dental Benefit", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_award", label: "Awards/Rewards", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_living_accommodation", label: "Living Accommodation", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_share_scheme", label: "Share Scheme", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_subsidised_loan", label: "Subsidised Loan Interest", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_phone_pda_gift", label: "Gift of Phone / PDA (1 unit/category/year)", group: "Benefits-in-kind / Perquisites" },
  { code: "bik_other_exempt", label: "Other Tax Exempt Benefit", group: "Benefits-in-kind / Perquisites" },
];

export const DEDUCTION_CATEGORIES: AdjustmentCategory[] = [
  { code: "deduct_unpaid_leave", label: "Unpaid Leave deduction", group: "Deductions" },
  { code: "deduct_salary_adjustment", label: "Salary Adjustment", group: "Deductions" },
  { code: "deduct_advance", label: "Advance Deduction", group: "Deductions" },
  { code: "deduct_miscellaneous", label: "Miscellaneous / Other Deduction", group: "Deductions" },
  { code: "deduct_loan_repayment", label: "Loan Repayment", group: "Deductions" },
  { code: "deduct_cp38", label: "CP38 Arrears (LHDN Order)", group: "Deductions" },
  { code: "deduct_additional_pcb", label: "Additional PCB", group: "Deductions" },
  { code: "deduct_zakat", label: "Zakat — via salary deduction (PZB)", group: "Deductions" },
  { code: "deduct_zakat_tp1", label: "Zakat — self-paid (TP1)", group: "Deductions" },
  // TP1 relief claims. Lower PCB, take nothing from pay — as in v1, a claim
  // the employee makes every month (life insurance, PRS) can sit on the
  // profile instead of being re-entered on each run.
  // In Borang TP1 (1/2026) order, so an admin can key a paper form top to
  // bottom. Labels match the server's catalogue.
  { code: "deduct_tp1_parents_medical", label: "TP1 · Parents — Medical, Special Needs & Carer", group: "Deductions" },
  { code: "deduct_tp1_parents_dental", label: "TP1 · Parents — Dental Treatment", group: "Deductions" },
  { code: "deduct_tp1_parents_medical_exam", label: "TP1 · Parents — Full Medical Exam & Vaccination", group: "Deductions" },
  { code: "deduct_tp1_supporting_equipment", label: "TP1 · Basic Supporting Equipment (Disabled)", group: "Deductions" },
  { code: "deduct_tp1_education_fees", label: "TP1 · Education Fees (Self)", group: "Deductions" },
  { code: "deduct_tp1_upskilling", label: "TP1 · Up-skilling / Self-enhancement Course", group: "Deductions" },
  { code: "deduct_tp1_serious_disease_medical", label: "TP1 · Serious Disease / Fertility / Medical", group: "Deductions" },
  { code: "deduct_tp1_vaccination", label: "TP1 · Vaccination (Self / Spouse / Child)", group: "Deductions" },
  { code: "deduct_tp1_dental", label: "TP1 · Dental Exam & Treatment (Self / Spouse / Child)", group: "Deductions" },
  { code: "deduct_tp1_medical_exam", label: "TP1 · Full Medical Exam / Mental Health / Test Kits", group: "Deductions" },
  { code: "deduct_tp1_learning_disability", label: "TP1 · Learning Disability Diagnosis & Intervention (Child)", group: "Deductions" },
  { code: "deduct_tp1_lifestyle", label: "TP1 · Lifestyle (Books / PC / Internet)", group: "Deductions" },
  { code: "deduct_tp1_sports_equipment", label: "TP1 · Sports Equipment / Gym", group: "Deductions" },
  { code: "deduct_tp1_breastfeeding", label: "TP1 · Breastfeeding Equipment", group: "Deductions" },
  { code: "deduct_tp1_childcare_fees", label: "TP1 · Childcare / Kindergarten / After-school Centre Fees", group: "Deductions" },
  { code: "deduct_tp1_sspn", label: "TP1 · SSPN Net Savings", group: "Deductions" },
  { code: "deduct_tp1_alimony", label: "TP1 · Alimony to Former Wife", group: "Deductions" },
  { code: "deduct_tp1_voluntary_epf", label: "TP1 · Voluntary EPF (paid outside payroll)", group: "Deductions" },
  { code: "deduct_tp1_life_insurance", label: "TP1 · Life Insurance", group: "Deductions" },
  { code: "deduct_tp1_prs", label: "TP1 · Private Retirement Scheme (PRS)", group: "Deductions" },
  { code: "deduct_tp1_medical_insurance", label: "TP1 · Medical / Education Insurance", group: "Deductions" },
  { code: "deduct_tp1_ev_charging", label: "TP1 · EV Charging / Food Waste Machine / Home CCTV", group: "Deductions" },
  { code: "deduct_tp1_housing_loan_500k", label: "TP1 · First Home Loan Interest (Home up to RM500k)", group: "Deductions" },
  { code: "deduct_tp1_housing_loan_750k", label: "TP1 · First Home Loan Interest (Home RM500k–750k)", group: "Deductions" },
  { code: "deduct_tp1_tourism", label: "TP1 · Tourist Attraction & Cultural Programme Fees", group: "Deductions" },
  { code: "deduct_tp1_other", label: "TP1 · Other (Admin-Trusted)", group: "Deductions" },
  { code: "deduct_departure_levy_tp1", label: "Departure Levy — Umrah / Religious Travel (TP1)", group: "Deductions" },
  { code: "deduct_tp1", label: "TP1/TP3 Deduction (legacy)", group: "Deductions" },
];

export const categoriesFor = (kind: AdjustmentKind) =>
  kind === "DEDUCTION" ? DEDUCTION_CATEGORIES : ALLOWANCE_CATEGORIES;

/** The kind is encoded in the code — that is how the previous system tells them apart. */
export const kindOf = (code: string): AdjustmentKind =>
  code.startsWith("deduct_") ? "DEDUCTION" : "ALLOWANCE";

export const DEFAULT_CATEGORY: Record<AdjustmentKind, string> = {
  ALLOWANCE: "allowance_standard",
  DEDUCTION: "deduct_salary_adjustment",
};

export function labelForCategory(code: string) {
  const all = [...ALLOWANCE_CATEGORIES, ...DEDUCTION_CATEGORIES];
  return all.find((c) => c.code === code)?.label ?? code;
}
