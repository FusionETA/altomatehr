import {
  ApiError,
  apiDelete,
  apiGet,
  apiGetFile,
  apiPost,
  apiPostForm,
  apiPut,
} from "@/shared/lib/api-client";

// Mirrors the backend Payroll module (later: generate this from the OpenAPI
// spec). Money arrives as a number; the backend computes in decimal and
// rounds before serialising, so nothing here should do arithmetic on it
// beyond display — see `lib/payroll-format`.

// ─── Settings ─────────────────────────────────────────────────────────

// The ÷ divisor for the ordinary rate of pay (EA s.60I). It does NOT govern
// incomplete-month proration — s.18A overrides that with calendar days.
export type WorkingDaysRule = "CALENDAR" | "TWENTY_SIX";

export const workingDaysRuleLabels: Record<WorkingDaysRule, string> = {
  CALENDAR: "Calendar days in the month",
  TWENTY_SIX: "26 days (Malaysian convention)",
};

export type PayrollSettings = {
  workingDaysRule: WorkingDaysRule;
  defaultEpfEmployeeRate: number;
  defaultEpfEmployerRate: number;
  hrdfEnabled: boolean;
  hrdfRate: number | null;
  autoApplySocsoEisRelief: boolean;
  syncClaimsToXeroOnSubmit: boolean;
  syncPayrollToXeroOnSubmit: boolean;
  xeroMappingJson: string | null;
  payrollBankName: string | null;
  payorAccountHolderName: string | null;
  payorOrganisationCode: string | null;
  ecpPayorAccountNo: string | null;
  ecpPayorBic: string | null;
  // False until the org saves for the first time. The GET returns the
  // statutory defaults rather than 404, so the form always has something to
  // render — this is what tells it whether those are real settings.
  isConfigured: boolean;
  updatedAt: string | null;
};

export type SavePayrollSettings = Omit<
  PayrollSettings,
  "isConfigured" | "updatedAt"
>;

export const getPayrollSettings = () =>
  apiGet<PayrollSettings>("/payroll/settings");

export const savePayrollSettings = (settings: SavePayrollSettings) =>
  apiPut<PayrollSettings>("/payroll/settings", settings);

// ─── Company info ─────────────────────────────────────────────────────

export type IdType = "NRIC" | "PASSPORT" | "ARMY" | "POLICE";

// The employer's filing identity. Every statutory body issues its own
// registration, which is why these are separate fields rather than one
// "company number".
export type PayrollCompanyInfo = {
  employerName: string | null;
  employerTin: string | null;
  registrationNo: string | null;
  perkesoEmployerCode: string | null;
  epfEmployerNo: string | null;
  hrdfEmployerNo: string | null;
  zakatNumber: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  postcode: string | null;
  city: string | null;
  state: string | null;
  country: string | null;
  phone: string | null;
  handphone: string | null;
  email: string | null;
  taxAgentName: string | null;
  taxAgentTin: string | null;
  taxAgentLicenceNo: string | null;
  taxAgentPhone: string | null;
  taxAgentEmail: string | null;
  declarantName: string | null;
  declarantIdType: IdType | null;
  declarantIdNumber: string | null;
  declarantPosition: string | null;
  isConfigured: boolean;
  updatedAt: string | null;
};

export type SavePayrollCompanyInfo = Omit<
  PayrollCompanyInfo,
  "isConfigured" | "updatedAt"
>;

export const getPayrollCompanyInfo = () =>
  apiGet<PayrollCompanyInfo>("/payroll/company-info");

export const savePayrollCompanyInfo = (info: SavePayrollCompanyInfo) =>
  apiPut<PayrollCompanyInfo>("/payroll/company-info", info);

// ─── Runs ─────────────────────────────────────────────────────────────

export type PayrollRunStatus = "DRAFT" | "PENDING_APPROVAL" | "SUBMITTED";

// COMPUTED came out of the calc engine. IMPORTED was seeded from a YTD
// migration and its figures were taken as typed, never recomputed.
export type PayrollRunSource = "COMPUTED" | "IMPORTED";

export type XeroSyncStatus = "NOT_SYNCED" | "SYNCED" | "ERROR";

export type PayrollRun = {
  id: string;
  periodYear: number;
  periodMonth: number;
  periodLabel: string;
  status: PayrollRunStatus;
  source: PayrollRunSource;
  employeeCount: number;
  totalGross: number;
  totalNet: number;
  totalEmployeeEpf: number;
  totalEmployerEpf: number;
  totalEmployeeSocso: number;
  totalEmployerSocso: number;
  totalEmployeeEis: number;
  totalEmployerEis: number;
  totalEmployeeSkbbk: number;
  totalPcb: number;
  totalCp38: number;
  totalZakat: number;
  totalHrdf: number;
  employeesSubjectToHrdf: number;
  totalWagesSubjectToHrdf: number;
  totalCostToEmployer: number;
  generatedAt: string | null;
  submittedForApprovalAt: string | null;
  submittedForApprovalById: string | null;
  submittedAt: string | null;
  submittedById: string | null;
  approvalRejectionReason: string | null;
  // The run's inputs have changed since its payslips were generated, so what
  // is on screen is behind what it would produce. Blocks submission.
  isStale: boolean;
  xeroManualJournalId?: string | null;
  xeroJournalNumber?: string | null;
  xeroSyncStatus?: XeroSyncStatus;
  xeroSyncError?: string | null;
  xeroSyncedAt?: string | null;
  createdAt: string;
  updatedAt: string;
};

export type SalaryType = "MONTHLY" | "HOURLY";

export type PayslipLineKind = "ALLOWANCE" | "DEDUCTION" | "REIMBURSEMENT";

export type PayslipLineItem = {
  id: string;
  kind: PayslipLineKind;
  label: string;
  amount: number;
  category: string | null;
  // What of this line actually feeds the PCB base — an allowance under its
  // annual exemption ceiling carries less than its full amount.
  pcbTaxableAmount: number | null;
  claimId: string | null;
  subjectToEpf: boolean;
  subjectToSocso: boolean;
  subjectToEis: boolean;
  subjectToPcb: boolean;
};

export type Payslip = {
  id: string;
  employeeProfileId: string;
  userId: string;
  // Snapshots: what was true at generation. A later salary edit must not
  // rewrite what a filed month paid.
  snapshotName: string;
  snapshotEmployeeNumber: string | null;
  snapshotPosition: string | null;
  snapshotNationality: string | null;
  snapshotIsResident: boolean;
  snapshotSalaryType: SalaryType;
  snapshotMonthlySalary: number | null;
  snapshotHourlyRate: number | null;
  totalWorkingDays: number;
  proratedDays: number;
  prorationDaysInPeriod: number;
  proratedFactor: number;
  workedHours: number | null;
  expectedHours: number | null;
  unpaidLeaveDays: number | null;
  basicPay: number;
  proratedPay: number;
  otNormalHours: number;
  otRestHours: number;
  otPublicHours: number;
  otPay: number;
  totalAllowances: number;
  totalReimbursements: number;
  totalDeductions: number;
  totalBenefitsInKind: number;
  epfEmployee: number;
  epfEmployer: number;
  socsoEmployee: number;
  socsoEmployer: number;
  eisEmployee: number;
  eisEmployer: number;
  skbbkEmployee: number;
  skbbkWage: number;
  pcb: number;
  pcbNormal: number;
  pcbAdditional: number;
  pcbCalculationJson: string | null;
  cp38: number;
  zakat: number;
  hrdf: number;
  hrdfWage: number;
  grossPay: number;
  netPay: number;
  totalCostToEmployer: number;
  // Missing TIN / EPF / SOCSO numbers, recorded at generation. The
  // readiness guard reads the same facts before a submission is accepted.
  statutoryWarnings: string[];
  lineItems: PayslipLineItem[];
};

export type PayrollRunDetail = {
  run: PayrollRun;
  payslips: Payslip[];
};

export type SkippedEmployee = {
  employeeProfileId: string;
  name: string;
  reason: string;
};

export type GenerateResult = {
  detail: PayrollRunDetail;
  payslipCount: number;
  // Who was left out and why — an archived profile, no salary on file, a
  // leave date before the period. Shown rather than silently dropped.
  skippedEmployees: SkippedEmployee[];
};

export const getPayrollRuns = () => apiGet<PayrollRun[]>("/payroll/runs");

export const getPayrollRun = (id: string) =>
  apiGet<PayrollRunDetail>(`/payroll/runs/${id}`);

export const createPayrollRun = (periodYear: number, periodMonth: number) =>
  apiPost<PayrollRun>("/payroll/runs", { periodYear, periodMonth });

export const generatePayrollRun = (id: string) =>
  apiPost<GenerateResult>(`/payroll/runs/${id}/generate`);

export const deletePayrollRun = (id: string) =>
  apiDelete<void>(`/payroll/runs/${id}`);

// ─── The status machine ───────────────────────────────────────────────

export const submitPayrollRun = (id: string) =>
  apiPost<PayrollRun>(`/payroll/runs/${id}/submit`);

export const approvePayrollRun = (id: string) =>
  apiPost<PayrollRun>(`/payroll/runs/${id}/approve`);

export const rejectPayrollRun = (id: string, reason: string | null) =>
  apiPost<PayrollRun>(`/payroll/runs/${id}/reject`, { reason });

export const revertPayrollRun = (id: string) =>
  apiPost<PayrollRun>(`/payroll/runs/${id}/revert`);

// Reverting a month also reverts every later submitted month in the same
// year, because their year-to-date figures were computed off it. This names
// them BEFORE the admin commits.
export type RevertImpact = {
  // The period labels of the later months this revert will also take back.
  alsoReverted: string[];
};

export const getRevertImpact = (id: string) =>
  apiGet<RevertImpact>(`/payroll/runs/${id}/revert-impact`);

// ─── Readiness ────────────────────────────────────────────────────────

// What the statutory files will need that the org does not yet have.
// Enforced before a submission is accepted, so it is shown on the run page
// rather than sprung at the end.
export type PayrollReadiness = {
  ok: boolean;
  totalMissingCount: number;
  orgIssues: string[];
  employeeIssues: { name: string; employeeCode: string; missing: string[] }[];
};

export const getPayrollReadiness = (id: string) =>
  apiGet<PayrollReadiness>(`/payroll/runs/${id}/readiness`);

// ─── Mid-cycle salary changes ─────────────────────────────────────────

export type SalaryChangeScenario =
  | "OVERPAID"
  | "UNDERPAID"
  | "MATCHED"
  | "UNKNOWN";

// The engine pays ONE salary for the whole month, so a raise effective
// mid-period leaves the payslip out by the prorated delta. This is advisory:
// it computes the correction and suggests the line, the admin decides.
export type SalaryChangeHint = {
  payslipId: string;
  employeeProfileId: string;
  employeeName: string;
  salaryChangeId: string;
  effectiveDate: string;
  previousMonthlySalary: number;
  newMonthlySalary: number;
  reasonLabel: string;
  payslipSnapshotMonthlySalary: number;
  outcome: SalaryChangeScenario;
  prorationRule: WorkingDaysRule;
  totalDaysInPeriod: number;
  daysAtOldRate: number;
  daysAtNewRate: number;
  // Always non-negative — the direction is in `outcome`.
  delta: number;
  suggestedLineItem: {
    kind: PayslipLineKind;
    category: string;
    label: string;
    amount: number;
  } | null;
  alreadyApplied: boolean;
};

export const getSalaryChangeHints = (id: string) =>
  apiGet<SalaryChangeHint[]>(`/payroll/runs/${id}/salary-change-hints`);

// ─── Xero ─────────────────────────────────────────────────────────────

export type XeroPreviewLine = {
  accountCode: string;
  description: string;
  // Positive debits, negative credits — Xero's own convention.
  amount: number;
  trackingOption: string | null;
};

export type XeroPreview = {
  ok: boolean;
  error: string | null;
  narration: string;
  date: string;
  lines: XeroPreviewLine[];
  xeroManualJournalId: string | null;
  status: XeroSyncStatus;
  // Must be zero. Xero rejects an unbalanced journal, and one that posted
  // would misstate the books.
  balance: number;
  totalDebits: number;
  totalCredits: number;
};

export const getXeroPreview = (id: string) =>
  apiGet<XeroPreview>(`/payroll/runs/${id}/xero/preview`);

export type XeroSyncResult = {
  found: boolean;
  ok: boolean;
  manualJournalId: string | null;
  lineCount: number;
  alreadyPosted: boolean;
  error: string | null;
};

export const syncPayrollToXero = (id: string) =>
  apiPost<XeroSyncResult>(`/payroll/runs/${id}/xero/sync`);

// ─── Documents and statutory files ────────────────────────────────────

// Every download is a file rather than JSON, so they share one helper. The
// backend answers 409 with a specific reason when the org is missing a field
// the file needs — api-client surfaces that as the thrown message.
const download = (path: string, fallbackName: string) =>
  apiGetFile(path, fallbackName);

export const downloadPayslipPdf = (runId: string, employeeProfileId: string) =>
  download(
    `/payroll/runs/${runId}/documents/payslip/${employeeProfileId}`,
    "payslip.pdf",
  );

export const downloadAllPayslips = (runId: string) =>
  download(`/payroll/runs/${runId}/documents/payslips`, "payslips.zip");

export const downloadPayrollSummary = (runId: string) =>
  download(`/payroll/runs/${runId}/documents/summary`, "payroll-summary.pdf");

export const downloadPaymentSchedule = (runId: string) =>
  download(
    `/payroll/runs/${runId}/documents/payment-schedule`,
    "payment-schedule.pdf",
  );

export const downloadPcbDetails = (runId: string) =>
  download(
    `/payroll/runs/${runId}/documents/pcb-details`,
    "pcb-calculation-details.pdf",
  );

export const downloadBankFile = (runId: string, paymentDate?: string) =>
  download(
    `/payroll/runs/${runId}/documents/bank-file${
      paymentDate ? `?paymentDate=${paymentDate}` : ""
    }`,
    "bank-file.xlsx",
  );

export const downloadEpfCsv = (runId: string) =>
  download(`/payroll/runs/${runId}/files/epf`, "epf.csv");

export const downloadPerkesoTxt = (runId: string) =>
  download(`/payroll/runs/${runId}/files/socso-eis`, "socso-eis.txt");

export const downloadPcbTxt = (runId: string) =>
  download(`/payroll/runs/${runId}/files/pcb`, "pcb.txt");

// ─── The payroll roster ───────────────────────────────────────────────

export type PaymentMethod = "BANK_TRANSFER" | "CASH" | "CHEQUE";

// Keyed by employeeProfileId, NOT the user id that `/employees` returns —
// payslips, loans and salary changes all reference the profile, so this is
// the id anything payroll attaches to a person needs.
export type PayrollEmployee = {
  employeeProfileId: string;
  userId: string;
  name: string;
  email: string;
  employeeNumber: string | null;
  department: string | null;
  jobTitle: string | null;

  salaryType: SalaryType;
  monthlySalary: number | null;
  hourlyRate: number | null;

  contributeToEpf: boolean;
  epfNumber: string | null;
  epfEmployeeRate: number;
  socsoNumber: string | null;
  contributeToEis: boolean;
  incomeTaxNumber: string | null;

  paymentMethod: PaymentMethod;
  bankName: string | null;
  bankAccountNumber: string | null;

  idNumber: string | null;
  idType: IdType | null;
  nationality: string | null;
  hasPr: boolean;
  isResident: boolean;

  joinDate: string | null;
  leaveDate: string | null;
  isArchived: boolean;

  // Why a run would leave them out. Null when they are payable.
  notPayableReason: string | null;

  // The same gaps the run readiness check reports, computed off the live
  // profile — so they can be cleared before a run exists.
  missing: string[];
};

export const getPayrollEmployees = (includeArchived = false) =>
  apiGet<PayrollEmployee[]>(
    `/payroll/employees${includeArchived ? "?includeArchived=true" : ""}`,
  );

// ─── Bulk import / export ─────────────────────────────────────────────

export type TabularFormat = "Csv" | "Xlsx";

// Per-row, so the answer is a report rather than one pass/fail for the file.
// Skipped is not a failure: the importers are idempotent, so re-uploading a
// corrected file reports the untouched rows as skipped instead of duplicating
// them.
export type TabularImportResult = {
  imported: number;
  skipped: number;
  failed: number;
  errors: { row: number; message: string }[];
};

export const downloadPayrollEmployeeTemplate = (format: TabularFormat) =>
  download(
    `/payroll/employees/template?format=${format}`,
    `payroll-employees-template.${format === "Csv" ? "csv" : "xlsx"}`,
  );

export const downloadPayrollEmployeeExport = (format: TabularFormat) =>
  download(
    `/payroll/employees/export?format=${format}`,
    `payroll-employees.${format === "Csv" ? "csv" : "xlsx"}`,
  );

export function importPayrollEmployees(file: File) {
  const form = new FormData();
  form.append("file", file);
  return apiPostForm<TabularImportResult>("/payroll/employees/import", form);
}

// ─── Loans ────────────────────────────────────────────────────────────

// FIXED divides the principal over a number of months; CUSTOM fixes the
// monthly amount and derives the count. Either way the LAST installment
// absorbs the rounding, so the schedule sums to the principal exactly.
export type LoanRepaymentMode = "FIXED" | "CUSTOM";

export type LoanStatus = "ACTIVE" | "COMPLETED" | "CANCELLED";

export type LoanInstallment = {
  index: number;
  year: number;
  month: number;
  periodLabel: string;
  // Paid is derived from whether that period has a SUBMITTED run — never
  // stored. Reverting a month therefore un-pays its installment for free.
  paid: boolean;
  amount: number;
};

export type EmployeeLoan = {
  id: string;
  employeeProfileId: string;
  employeeName: string;
  principalAmount: number;
  mode: LoanRepaymentMode;
  installmentAmount: number;
  startYear: number;
  startMonth: number;
  installmentCount: number;
  status: LoanStatus;
  notes: string | null;
  schedule: LoanInstallment[];
  paidInstallments: number;
  paidAmount: number;
  remainingAmount: number;
  endYear: number;
  endMonth: number;
  fullyRepaid: boolean;
  // Editing the terms of a loan already repaying would restate months
  // already filed, so the form locks on this.
  hasStarted: boolean;
};

export type SaveEmployeeLoan = {
  employeeProfileId: string;
  principalAmount: number;
  mode: LoanRepaymentMode;
  installmentCount: number | null;
  installmentAmount: number | null;
  startYear: number;
  startMonth: number;
  notes: string | null;
  // A hand-varied schedule — a lighter month, a lump sum at bonus time.
  // Must add up to the principal. Omitted means an equal split.
  schedule?: number[] | null;
};

export const getEmployeeLoans = (employeeProfileId?: string) =>
  apiGet<EmployeeLoan[]>(
    `/payroll/loans${employeeProfileId ? `?employeeProfileId=${employeeProfileId}` : ""}`,
  );

export const createEmployeeLoan = (body: SaveEmployeeLoan) =>
  apiPost<EmployeeLoan>("/payroll/loans", body);

export const updateEmployeeLoan = (id: string, body: SaveEmployeeLoan) =>
  apiPut<EmployeeLoan>(`/payroll/loans/${id}`, body);

// Cancelling stops future deductions while leaving the ones already taken
// explained — which is why a started loan cannot be deleted.
export const cancelEmployeeLoan = (id: string) =>
  apiPost<EmployeeLoan>(`/payroll/loans/${id}/cancel`);

export const reactivateEmployeeLoan = (id: string) =>
  apiPost<EmployeeLoan>(`/payroll/loans/${id}/reactivate`);

export const deleteEmployeeLoan = (id: string) =>
  apiDelete<void>(`/payroll/loans/${id}`);

// ─── Annual filings ───────────────────────────────────────────────────

export type PayrollAnnualReportKind =
  | "FORM_EA_BULK_PDF"
  | "FORM_E_CP8D_PDF"
  | "CP8D_EMPLOYER_TXT"
  | "CP8D_EMPLOYEE_TXT";

export type PayrollAnnualReportMeta = {
  kind: PayrollAnnualReportKind;
  // "FORMS" for what the employer keeps or hands out, "LHDN_TXT" for the
  // e-CP8D upload pair.
  group: string;
  title: string;
  description: string;
  portal: string | null;
  extension: string;
  mimeType: string;
};

// One employee's whole year, summed across the SUBMITTED runs only. A draft
// is not remuneration that was paid, and a return built on one would be false.
export type AnnualEmployeeRow = {
  employeeProfileId: string;
  employeeName: string;
  employeeCode: string;
  jobTitle: string | null;
  idNumber: string | null;
  idType: IdType | null;
  epfNumber: string | null;
  socsoNumber: string | null;
  incomeTaxNumber: string | null;
  qualifyingChildren: number;
  annualChildRelief: number;
  pcbBorneByEmployer: boolean;
  grossSalary: number;
  // Bonus, commission, fees, arrears, director fee — reported apart from
  // salary on both Form EA and CP8D.
  bonusAndCommission: number;
  // Benefits in kind. Never part of gross or net, but still part of the
  // employee's reportable income.
  totalBik: number;
  totalPcb: number;
  totalCp38: number;
  totalZakat: number;
  totalEpfEmployee: number;
  totalSocsoEmployee: number;
  totalEisEmployee: number;
  totalIncome: number;
};

export type PayrollAnnualPayload = {
  year: number;
  organizationName: string;
  // The LHDN E-number with its letter and punctuation stripped. Empty when
  // unset, which the TXT renderers treat as a refusal.
  employerNo: string;
  employees: AnnualEmployeeRow[];
};

export const getAnnualReportKinds = () =>
  apiGet<PayrollAnnualReportMeta[]>("/payroll/annual/reports");

export const getPayrollAnnual = (year: number) =>
  apiGet<PayrollAnnualPayload>(`/payroll/annual/${year}`);

export const downloadAnnualReport = (
  year: number,
  kind: PayrollAnnualReportKind,
  extension: string,
) => download(`/payroll/annual/${year}/reports/${kind}`, `${kind}-${year}.${extension}`);

// ─── Year-to-date import ──────────────────────────────────────────────

export type YtdImportPreviewRow = {
  employeeName: string;
  employeeProfileId: string;
  months: number[];
  totalGross: number;
  totalPcb: number;
};

export type YtdImportPreview = {
  ok: boolean;
  errors: string[];
  // Columns and months that will be skipped or replaced. Not errors — the
  // import can proceed — but the admin should see them first.
  warnings: string[];
  employees: YtdImportPreviewRow[];
  // Names in the sheet that matched nobody, surfaced so the admin can fix
  // the spelling rather than wonder who was dropped.
  unmatchedNames: string[];
};

export type YtdImportResult = {
  ok: boolean;
  errors: string[];
  monthsImported: number;
  payslipsImported: number;
  unmatchedNames: string[];
  // Months left alone because this system already computed them.
  skippedMonths: string[];
};

export const downloadYtdTemplate = (year: number, format: TabularFormat) =>
  download(
    `/payroll/ytd-import/template/${year}?format=${format}`,
    `ytd-${year}-template.${format === "Csv" ? "csv" : "xlsx"}`,
  );

// A rejected file comes back as 400 carrying the SAME payload as a good one,
// with `ok: false` and the reasons filled in. That is the endpoint's contract
// rather than a failure, so both are unwrapped here and the caller only ever
// sees the report.
async function ytdPost<T extends { ok: boolean }>(path: string, file: File): Promise<T> {
  const form = new FormData();
  form.append("file", file);

  try {
    return await apiPostForm<T>(path, form);
  } catch (err) {
    if (err instanceof ApiError && err.status === 400 && isReport<T>(err.body)) {
      return err.body;
    }
    throw err;
  }
}

const isReport = <T>(body: unknown): body is T =>
  typeof body === "object" && body !== null && "ok" in body && "errors" in body;

export const previewYtdImport = (year: number, file: File) =>
  ytdPost<YtdImportPreview>(`/payroll/ytd-import/${year}/preview`, file);

export const runYtdImport = (year: number, file: File) =>
  ytdPost<YtdImportResult>(`/payroll/ytd-import/${year}`, file);

// ─── Adjustment categories ────────────────────────────────────────────

export type AdjustmentCategoryGroup =
  | "ALLOWANCE"
  | "REMUNERATION"
  | "BENEFIT_IN_KIND"
  | "DEDUCTION";

// Served by the backend rather than duplicated here: the calculator
// dispatches on these codes, and a second copy that drifted would describe a
// row's statutory treatment wrongly or offer one generation silently skips.
export type AdjustmentCategory = {
  code: string;
  label: string;
  kind: PayslipLineKind;
  subjectToEpf: boolean;
  subjectToSocso: boolean;
  subjectToEis: boolean;
  subjectToPcb: boolean;
  subjectToHrdf: boolean;
  // Annual ringgit ceiling under which the row is PCB-exempt — or, on a TP1
  // deduction, the per-item yearly cap.
  taxExemptLimit: number | null;
  reducesBase: boolean;
  reducesGross: boolean;
  cashNeutral: boolean;
  feedsLp1Relief: boolean;
  addsToCp38Field: boolean;
  isAdditionalRemuneration: boolean;
  offsetsPcb: boolean;
  // A benefit in kind: never reaches cash, still taxable income on Form EA.
  nonCash: boolean;
  group: AdjustmentCategoryGroup;
};

export const getAdjustmentCategories = () =>
  apiGet<AdjustmentCategory[]>("/payroll/adjustment-categories");

// ─── Per-run adjustments ──────────────────────────────────────────────

export type ManualLineItem = {
  kind: PayslipLineKind;
  category: string;
  label: string | null;
  amount: number;
  // Only meaningful on an additional-remuneration category: routes the amount
  // through the smoothed monthly PCB path instead of LHDN's one-shot formula.
  treatAsRecurring: boolean;
};

export type FixedAllowanceOverride = {
  // Null keeps the profile's amount.
  amount: number | null;
  // Zero this row out for this run only.
  skip: boolean;
};

export type PayrollRunAdjustment = {
  id: string;
  payrollRunId: string;
  employeeProfileId: string;
  otNormalHours: number;
  otRestHours: number;
  otPublicHours: number;
  manualLineItems: ManualLineItem[];
  // Keyed by the profile allowance's array index, as a string.
  fixedAllowanceOverrides: Record<string, FixedAllowanceOverride>;
  workedHours: number | null;
  expectedHours: number | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
};

// A save REPLACES the row. A partial patch would make "I cleared the
// overtime" indistinguishable from "I did not mention the overtime".
export type SavePayrollRunAdjustment = {
  otNormalHours: number;
  otRestHours: number;
  otPublicHours: number;
  manualLineItems: {
    category: string;
    label: string | null;
    amount: number;
    treatAsRecurring: boolean;
  }[];
  fixedAllowanceOverrides: Record<string, FixedAllowanceOverride>;
  workedHours: number | null;
  expectedHours: number | null;
  notes: string | null;
};

export type FixedAllowanceRow = {
  // The array position in the profile's JSON — what the override map keys on.
  index: number;
  category: string;
  name: string | null;
  amount: number;
  treatAsRecurring: boolean;
};

export type LoanInstallmentPreview = {
  loanId: string;
  label: string;
  amount: number;
};

// Everything the editor needs for one employee, in one read.
export type PayrollAdjustmentContext = {
  employeeProfileId: string;
  employeeName: string;
  salaryType: SalaryType;
  adjustment: PayrollRunAdjustment | null;
  fixedAllowances: FixedAllowanceRow[];
  // What attendance derived — the value an empty override falls back to.
  autoWorkedHours: number | null;
  autoExpectedHours: number | null;
  // False when the policy keeps this employee off attendance; the figures
  // above are then absent rather than zero.
  attendanceApplies: boolean;
  // False when the policy banks overtime as time off or disables it, in which
  // case typed hours are IGNORED by generation.
  cashOvertime: boolean;
  overtimeDisabledReason: string | null;
  loanInstallments: LoanInstallmentPreview[];
  editable: boolean;
};

export const getRunAdjustments = (runId: string) =>
  apiGet<PayrollRunAdjustment[]>(`/payroll/runs/${runId}/adjustments`);

export const getAdjustmentContext = (runId: string, employeeProfileId: string) =>
  apiGet<PayrollAdjustmentContext>(
    `/payroll/runs/${runId}/adjustments/${employeeProfileId}/context`,
  );

export const saveAdjustment = (
  runId: string,
  employeeProfileId: string,
  body: SavePayrollRunAdjustment,
) =>
  apiPut<PayrollRunAdjustment>(
    `/payroll/runs/${runId}/adjustments/${employeeProfileId}`,
    body,
  );

export const clearAdjustment = (runId: string, employeeProfileId: string) =>
  apiDelete<void>(`/payroll/runs/${runId}/adjustments/${employeeProfileId}`);

// ─── Attached claims ──────────────────────────────────────────────────

export type ClaimCategory =
  | "TRAVEL" | "TRANSPORT" | "MEAL" | "MEDICAL"
  | "WELLNESS" | "HARDWARE" | "OFFICE" | "OTHER";

export type PayrollRunClaim = {
  id: string;
  payrollRunId: string;
  claimId: string;
  employeeProfileId: string;
  // Snapshotted at attach time, not the claim's current values — editing the
  // claim afterwards must not move a figure on a generated run.
  label: string;
  amount: number;
  employeeName: string;
  employeeNumber: string | null;
  claimNumber: string;
  createdAt: string;
};

export type AttachableClaim = {
  claimId: string;
  claimNumber: string;
  title: string;
  category: ClaimCategory;
  claimType: string;
  amount: number;
  spentAt: string;
  userId: string;
  employeeProfileId: string | null;
  employeeName: string;
  employeeNumber: string | null;
  // Null = free to attach. Otherwise the run already holding it.
  attachedToRunId: string | null;
  attachedToRunPeriod: string | null;
  // Why it cannot be attached right now. Commonest case: the submitter has no
  // employee profile, so there is nobody on the payroll to pay it to.
  blockedReason: string | null;
};

export const getRunClaims = (runId: string) =>
  apiGet<PayrollRunClaim[]>(`/payroll/runs/${runId}/claims`);

export const getAttachableClaims = (runId: string) =>
  apiGet<AttachableClaim[]>(`/payroll/runs/${runId}/claims/attachable`);

export const attachClaim = (runId: string, claimId: string) =>
  apiPost<PayrollRunClaim>(`/payroll/runs/${runId}/claims`, { claimId });

export const detachClaim = (runId: string, claimId: string) =>
  apiDelete<void>(`/payroll/runs/${runId}/claims/${claimId}`);

// ─── Statutory portal credentials ─────────────────────────────────────

export type PortalKind = "KWSP" | "PERKESO" | "LHDN";

export const portalLabels: Record<PortalKind, string> = {
  KWSP: "KWSP i-Akaun",
  PERKESO: "PERKESO ASSIST",
  LHDN: "LHDN e-PCB",
};

export const PORTALS: PortalKind[] = ["KWSP", "PERKESO", "LHDN"];

export type PortalCredential = {
  portal: PortalKind;
  portalLabel: string;
  loginId: string | null;
  // Null on the list. Populated only by an explicit reveal — a page showing
  // every password at once is one shoulder-surf from losing all three.
  password: string | null;
  hasPassword: boolean;
  image: string | null;
  secretCode: string | null;
  securityPhrase: string | null;
  passwordReminder: string | null;
  notes: string | null;
  isConfigured: boolean;
  updatedAt: string | null;
};

export type SavePortalCredential = {
  loginId: string | null;
  // Omit to leave the stored password untouched — so a typo in the login id
  // can be corrected without retyping it. Empty string clears it.
  password?: string | null;
  image: string | null;
  secretCode: string | null;
  securityPhrase: string | null;
  passwordReminder: string | null;
  notes: string | null;
};

export const getPortalCredentials = () =>
  apiGet<PortalCredential[]>("/payroll/portal-credentials");

export const revealPortalCredential = (portal: PortalKind) =>
  apiGet<PortalCredential>(`/payroll/portal-credentials/${portal}/reveal`);

export const savePortalCredential = (portal: PortalKind, body: SavePortalCredential) =>
  apiPut<PortalCredential>(`/payroll/portal-credentials/${portal}`, body);

export const deletePortalCredential = (portal: PortalKind) =>
  apiDelete<void>(`/payroll/portal-credentials/${portal}`);

// ─── Xero journal mapping ─────────────────────────────────────────────

// PER_EMPLOYEE puts one line per person per category on the journal;
// SUM_BY_PROJECT rolls them up by project. Accruals are always summed either
// way — they are one liability per agency, not one per person.
export type XeroAggregationMode = "PER_EMPLOYEE" | "SUM_BY_PROJECT";

// UNIFIED posts every allowance to one account; PER_CATEGORY gives each its
// own, and the sync then refuses rather than guessing at an unmapped one.
export type XeroLineGroupingMode = "UNIFIED" | "PER_CATEGORY";

// Stored as JSON on PayrollSettings.xeroMappingJson.
export type PayrollXeroMapping = {
  v: number;
  aggregationMode: XeroAggregationMode;
  trackingCategoryId: string | null;
  // Xero account ids keyed by the slot names below.
  accounts: Record<string, string | null>;
  allowanceMode: XeroLineGroupingMode;
  allowanceAccounts: Record<string, string | null>;
  deductionMode: XeroLineGroupingMode;
  deductionAccounts: Record<string, string | null>;
};

export const emptyXeroMapping = (): PayrollXeroMapping => ({
  v: 1,
  aggregationMode: "PER_EMPLOYEE",
  trackingCategoryId: null,
  accounts: {},
  allowanceMode: "UNIFIED",
  allowanceAccounts: {},
  deductionMode: "UNIFIED",
  deductionAccounts: {},
});

// The slot names are JSON keys on the stored blob, so they are spelled here
// exactly as `PayrollXeroAccounts` spells them. Renaming one would orphan an
// admin's saved configuration.
export const XERO_EXPENSE_SLOTS = [
  { key: "salary", label: "Salary" },
  { key: "allowance", label: "Allowances" },
  { key: "deduction", label: "Deductions" },
  { key: "epfEmployer", label: "EPF — employer contribution" },
  { key: "socsoEmployer", label: "SOCSO — employer contribution" },
  { key: "eisEmployer", label: "EIS — employer contribution" },
  { key: "hrdfEmployer", label: "HRD Corp levy" },
] as const;

export const XERO_ACCRUAL_SLOTS = [
  { key: "accrualSalary", label: "Net pay payable" },
  { key: "accrualEpf", label: "EPF payable" },
  { key: "accrualSocso", label: "SOCSO payable" },
  { key: "accrualEis", label: "EIS payable" },
  { key: "accrualPcb", label: "PCB payable" },
  { key: "accrualHrdf", label: "HRD Corp levy payable" },
] as const;

export type XeroTrackingCategory = {
  trackingCategoryId: string;
  name: string;
  options: string[];
};

export const getXeroTrackingCategories = () =>
  apiGet<XeroTrackingCategory[]>("/payroll/runs/xero/tracking-categories");
