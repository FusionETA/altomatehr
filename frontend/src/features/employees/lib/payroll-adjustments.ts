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
  { code: "deduct_additional_pcb", label: "Additional PCB", group: "Deductions" },
  { code: "deduct_zakat", label: "Zakat — via salary deduction (PZB)", group: "Deductions" },
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
