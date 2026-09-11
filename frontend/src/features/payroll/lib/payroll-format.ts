import type { LoanStatus, PayrollRunStatus, PayrollRunSource } from "../api";

// How payroll money and status are spelled on screen.
//
// One place, because these figures appear on the runs list, the run detail,
// the payslip drawer and the Xero preview — and an admin reconciling a
// payslip against a bank statement should not have to notice that two
// screens rounded differently.

// Always both decimals, always grouped. An amount reaching someone's bank
// account is never "RM 4,300" — the sen matter, and a missing one reads as
// a different figure.
export function rm(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";

  return value.toLocaleString("en-MY", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

// With the currency, for headline figures where the unit is not obvious from
// a neighbouring column.
export const rmWithUnit = (value: number | null | undefined): string =>
  value === null || value === undefined ? "—" : `RM ${rm(value)}`;

export function shortDate(value: string | null | undefined): string {
  if (!value) return "—";

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : date.toLocaleDateString("en-MY", {
        day: "numeric",
        month: "short",
        year: "numeric",
      });
}

export function dateTime(value: string | null | undefined): string {
  if (!value) return "—";

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : date.toLocaleString("en-MY", {
        day: "numeric",
        month: "short",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
}

export const statusLabels: Record<PayrollRunStatus, string> = {
  DRAFT: "Draft",
  PENDING_APPROVAL: "Awaiting approval",
  SUBMITTED: "Submitted",
};

// Submitted is the only status that counts towards year-to-date and the only
// one whose figures are final, so it is the only one styled as settled.
export const statusTone: Record<PayrollRunStatus, string> = {
  DRAFT: "border-border bg-muted/60 text-muted-foreground",
  PENDING_APPROVAL: "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400",
  SUBMITTED: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
};

export const sourceLabels: Record<PayrollRunSource, string> = {
  COMPUTED: "Computed",
  IMPORTED: "Imported",
};

export const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
] as const;

export const monthName = (month: number): string =>
  MONTHS[month - 1] ?? `Month ${month}`;

// The backend records a payslip's statutory gaps as codes, because they are
// matched on rather than read. They are not sentences, so they never reach
// the screen raw.
const WARNING_LABELS: Record<string, string> = {
  MISSING_INCOME_TAX_NUMBER: "No income tax number",
  MISSING_EPF_NUMBER: "No EPF number",
  MISSING_SOCSO_NUMBER: "No SOCSO number",
  MISSING_ID_NUMBER: "No IC or passport number",
  MISSING_EMPLOYEE_NUMBER: "No employee number",
  MISSING_BANK_ACCOUNT: "No bank account",
};

export const warningLabel = (code: string): string =>
  WARNING_LABELS[code] ??
  // An unmapped code still has to read as English rather than as a constant.
  code
    .replace(/^MISSING_/, "No ")
    .toLowerCase()
    .replace(/_/g, " ");

export const warningLabels = (codes: string[]): string =>
  codes.map(warningLabel).join(" · ");

// ─── Loans ────────────────────────────────────────────────────────────

// COMPLETED is derived rather than set: the last installment's period has a
// submitted run. CANCELLED stops future deductions but leaves the ones
// already taken on their payslips, which is why it is not a delete.
export const loanStatusLabels: Record<LoanStatus, string> = {
  ACTIVE: "Active",
  COMPLETED: "Repaid",
  CANCELLED: "Cancelled",
};

export const loanStatusTone: Record<LoanStatus, string> = {
  ACTIVE: "border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-400",
  COMPLETED: "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
  CANCELLED: "border-border bg-muted/60 text-muted-foreground",
};

// "September 2026" from the two numbers a period is stored as.
export const periodLabel = (year: number, month: number): string =>
  `${monthName(month)} ${year}`;
