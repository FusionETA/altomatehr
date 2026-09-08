import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

export type Employee = {
  id: string;
  email: string;
  // The person's real name, stored on the global User. The app derived names
  // from email addresses for a long time because this was never read.
  name: string;
  avatarUrl: string | null;
  role: string;
  employeeNumber: string | null;
  jobTitle: string | null;
  /** First day in THIS org. Drives pro-rated leave accrual. */
  joinDate: string | null;
  otTimeBalanceMin: number;
  supervisorId: string | null;
  supervisorEmail: string | null;
  policyId: string | null;
  shiftId: string | null;
  /** Per-admin module grant. null = full access. */
  modules: string[] | null;
};

// Name, employee number, job title and join date PATCH: omit to leave
// unchanged, send an empty string to clear (the join date can only be
// corrected, not blanked). Role is always required.
//
// shiftId and modules do NOT patch, and can't: null is a real value for both
// — "fall back to the project's default shift" and "full access" — so there is
// no spare value left to mean "leave this alone". They are written on every
// update, which is why a caller must build the payload with toUpdateEmployee
// rather than by hand.
export type UpdateEmployee = {
  role: string;
  name?: string;
  employeeNumber?: string;
  jobTitle?: string;
  joinDate?: string | null;
  supervisorId?: string | null;
  policyId?: string | null;
  shiftId?: string | null;
  modules?: string[] | null;
};

/**
 * The employee's current state as an update payload, ready to be edited.
 *
 * The patch fields are omitted where the employee has no value: under the
 * backend's semantics an empty string CLEARS, so sending "" for a field that
 * was already empty would be a pointless write, and sending null is not
 * expressible. shiftId and modules are always carried, because they don't
 * patch — see UpdateEmployee.
 */
export const toUpdateEmployee = (employee: Employee): UpdateEmployee => ({
  role: employee.role,
  name: employee.name,
  employeeNumber: employee.employeeNumber ?? undefined,
  jobTitle: employee.jobTitle ?? undefined,
  joinDate: employee.joinDate,
  supervisorId: employee.supervisorId,
  policyId: employee.policyId,
  shiftId: employee.shiftId,
  modules: employee.modules,
});

export const ROLES = ["Employee", "Supervisor", "Admin", "Owner"] as const;

// The roles Manage Employee deals in. Admin and Owner are administrative
// access rather than employment: they hold no approval-chain place and no
// payroll profile, so they are neither listed nor assignable there.
export const STAFF_ROLES = ["Employee", "Supervisor"] as const;

export const getEmployees = () => apiGet<Employee[]>("/employees");
export const updateEmployee = (id: string, body: UpdateEmployee) =>
  apiPut<Employee>(`/employees/${id}`, body);

export type CreateEmployee = {
  email: string;
  // Only needed for a brand-new account; ignored if the email already exists (multi-org reuse).
  password?: string;
  // Required for a new account; ignored when reusing an existing identity,
  // since that person keeps the name they already have.
  name?: string;
  employeeNumber?: string;
  jobTitle?: string;
  joinDate?: string | null;
  role: string;
  supervisorId?: string | null;
  policyId?: string | null;
};

export const createEmployee = (body: CreateEmployee) => apiPost<Employee>("/employees", body);

// ---- Full HR profile ----
//
// The backend has carried this since the module landed and nothing reached it:
// GET/PUT /employees/{id}/profile, 67 properties across twelve sections. Typed
// here in the slices the UI actually edits, so a field can't be silently
// dropped on save — see saveEmployeeProfile.

export type Gender = "MALE" | "FEMALE";
export type IdType = "NRIC" | "PASSPORT" | "ARMY_NO" | "POLICE_NO";
export type MaritalStatus = "SINGLE" | "MARRIED" | "DIVORCED" | "WIDOWED";
export type SocsoScheme = "EMPLOYMENT_INJURY_INVALIDITY" | "EMPLOYMENT_INJURY_ONLY";
export type PaymentMethod = "BANK_TRANSFER" | "CASH" | "CHEQUE";
export type SalaryType = "HOURLY" | "MONTHLY";

export const GENDERS: Gender[] = ["MALE", "FEMALE"];
export const ID_TYPES: IdType[] = ["NRIC", "PASSPORT", "ARMY_NO", "POLICE_NO"];
export const MARITAL_STATUSES: MaritalStatus[] = ["SINGLE", "MARRIED", "DIVORCED", "WIDOWED"];

export const SOCSO_SCHEMES: SocsoScheme[] = [
  "EMPLOYMENT_INJURY_INVALIDITY",
  "EMPLOYMENT_INJURY_ONLY",
];
export const PAYMENT_METHODS: PaymentMethod[] = ["BANK_TRANSFER", "CASH", "CHEQUE"];
export const SALARY_TYPES: SalaryType[] = ["MONTHLY", "HOURLY"];

export const SOCSO_SCHEME_LABELS: Record<SocsoScheme, string> = {
  EMPLOYMENT_INJURY_INVALIDITY: "Injury + invalidity (First Category)",
  EMPLOYMENT_INJURY_ONLY: "Injury only (Second Category)",
};
export const PAYMENT_METHOD_LABELS: Record<PaymentMethod, string> = {
  BANK_TRANSFER: "Bank transfer",
  CASH: "Cash",
  CHEQUE: "Cheque",
};

export const ID_TYPE_LABELS: Record<IdType, string> = {
  NRIC: "NRIC",
  PASSPORT: "Passport",
  ARMY_NO: "Army number",
  POLICE_NO: "Police number",
};

// Only the fields this screen edits are named. The rest of the profile —
// statutory, payroll, tax relief — rides along untouched as `unknown` so a save
// from here can't wipe what another screen owns.
export type EmployeeProfile = {
  // Read-only context, server-set.
  id: string;
  email: string;
  name: string;

  // Personal / demographic
  phone: string | null;
  alternateEmail: string | null;
  gender: Gender | null;
  dateOfBirth: string | null;          // ISO
  nationality: string | null;
  race: string | null;
  hasPr: boolean;
  idType: IdType | null;
  idNumber: string | null;
  maritalStatus: MaritalStatus | null;
  isResident: boolean;
  isOku: boolean;
  city: string | null;
  postcode: string | null;
  state: string | null;
  emergencyContactName: string | null;
  emergencyContactPhone: string | null;
  emergencyContactRelation: string | null;

  // Employment placement
  joinDate: string | null;
  leaveDate: string | null;
  department: string | null;
  location: string | null;
  workSchedule: string | null;

  // Spouse / tax relief
  spouseWorking: boolean | null;
  spouseDisabled: boolean | null;
  spousePcbNumber: string | null;
  spouseIdNumber: string | null;

  // Prior-employment YTD, for a mid-year joiner's PCB
  prevEmploymentYear: number | null;
  prevRemuneration: number | null;
  prevEpf: number | null;
  prevAllowableDeductions: number | null;
  prevPcb: number | null;
  prevZakat: number | null;
  prevIncludesPriorThisOrgPeriod: boolean;

  // EPF. Rates are FRACTIONS on the wire (0.11), shown as percentages.
  contributeToEpf: boolean;
  epfNumber: string | null;
  epfEmployeeRate: number;
  epfEmployeeVoluntary: number;
  epfEmployerVoluntary: number;

  // SOCSO / EIS / SKBBK
  socsoNumber: string | null;
  socsoScheme: SocsoScheme | null;
  contributeToEis: boolean;
  contributeToSkbbk: boolean;

  // Income tax
  incomeTaxNumber: string | null;
  pcbBorneByEmployer: boolean;
  ssfwNumber: string | null;
  reportedToLhdn: boolean;

  // Bank / payment
  paymentMethod: PaymentMethod;
  bankName: string | null;
  bankAccountHolderName: string | null;
  bankAccountNumber: string | null;

  // Salary
  salaryType: SalaryType;
  monthlySalary: number | null;
  hourlyRate: number | null;
  /** JSON list of FixedAllowance — read it with parseFixedAllowances. */
  fixedAllowancesJson: string | null;

  // Payroll config
  payrollPolicy: string | null;
  payrollCycle: string | null;

  /** JSON list of ChildRelief — read it with parseChildRelief. */
  childReliefJson: string | null;

  // Lifecycle
  isArchived: boolean;
  archivedAt: string | null;
  archiveReason: string | null;
  temporaryReviewDate: string | null;
} & Record<string, unknown>;


// ---- The two JSON columns the UI owns ----
//
// Both are stored as a JSON *string* on the profile, with the shape the
// previous system settled on — kept byte-compatible so payroll reads what it
// already expects rather than a second dialect of the same data.

/** One dependent child, for the PCB child relief (QC) calculation. */
export type ChildRelief = {
  age: number | null;
  abilityStatus: "NORMAL" | "DISABLED";
  currentlyStudying: "PRESCHOOL" | "PRIMARY" | "SECONDARY" | "HIGHER_ED" | "NONE";
  /** How much of the relief this employee claims — split with the spouse. */
  pcbDeduction: "FULL" | "HALF" | "NONE";
};

export const CHILD_ABILITY: ChildRelief["abilityStatus"][] = ["NORMAL", "DISABLED"];
export const CHILD_ABILITY_LABELS: Record<ChildRelief["abilityStatus"], string> = {
  NORMAL: "No disability",
  DISABLED: "Disabled (OKU)",
};

export const CHILD_STUDYING: ChildRelief["currentlyStudying"][] = [
  "NONE",
  "PRESCHOOL",
  "PRIMARY",
  "SECONDARY",
  "HIGHER_ED",
];
export const CHILD_STUDYING_LABELS: Record<ChildRelief["currentlyStudying"], string> = {
  NONE: "Not studying",
  PRESCHOOL: "Preschool",
  PRIMARY: "Primary school",
  SECONDARY: "Secondary school",
  HIGHER_ED: "Higher education",
};

export const CHILD_DEDUCTION: ChildRelief["pcbDeduction"][] = ["FULL", "HALF", "NONE"];
export const CHILD_DEDUCTION_LABELS: Record<ChildRelief["pcbDeduction"], string> = {
  FULL: "Full relief",
  HALF: "Half (shared with spouse)",
  NONE: "None",
};

/** A fixed adjustment applied on every payroll run. */
export type FixedAllowance = {
  category: string;
  name: string;
  amount: number | null;
};

// Parsing is deliberately forgiving: these columns are written by payroll too,
// and a shape we don't recognise must not blank the form or throw on render.
function parseList<T>(json: string | null | undefined): T[] {
  if (!json) return [];
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? (parsed as T[]) : [];
  } catch {
    return [];
  }
}

export const parseChildRelief = (json: string | null | undefined) =>
  parseList<ChildRelief>(json);
export const parseFixedAllowances = (json: string | null | undefined) =>
  parseList<FixedAllowance>(json);

/** An empty list is stored as null, not "[]" — nothing there means nothing there. */
export const serializeList = (rows: unknown[]) =>
  rows.length === 0 ? null : JSON.stringify(rows);

export const getEmployeeProfile = (id: string) =>
  apiGet<EmployeeProfile>(`/employees/${id}/profile`);

// PUT replaces the whole profile, so the previously-fetched object must be
// spread underneath the edits. Sending only the edited fields would null every
// statutory and payroll value the payroll screens own.
export const saveEmployeeProfile = (id: string, profile: EmployeeProfile) =>
  apiPut<EmployeeProfile>(`/employees/${id}/profile`, profile);
