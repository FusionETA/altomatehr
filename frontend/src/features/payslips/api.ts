import { apiGet, apiGetFile } from "@/shared/lib/api-client";
import type { Payslip } from "@/features/payroll/api";

// The employee's OWN payslips. Every read here is scoped to the caller's
// profile by the server — there is deliberately no "whose" parameter, because
// reading someone else's pay happens through the role-gated admin surfaces
// under /payroll/runs.
//
// Only SUBMITTED runs come back. A draft is still being edited, and showing
// someone a figure that may still move is worse than showing them nothing.

export type PayslipSummary = {
  id: string;
  periodYear: number;
  periodMonth: number;
  periodLabel: string;
  grossPay: number;
  netPay: number;
  /** What the employee themselves paid — the figures they check first. */
  epfEmployee: number;
  socsoEmployee: number;
  eisEmployee: number;
  pcb: number;
  /** When the run went live: the date this payslip became real. */
  submittedAt: string | null;
};

export const getMyPayslips = () => apiGet<PayslipSummary[]>("/payslips");

// The full breakdown, same shape the admin run detail uses.
export const getMyPayslip = (id: string) => apiGet<Payslip>(`/payslips/${id}`);

export const downloadMyPayslipPdf = (id: string, label: string) =>
  apiGetFile(`/payslips/${id}/pdf`, `payslip-${label}.pdf`);

// The employee's own Borang PCB/TP1 for that month — the reliefs claimed,
// this month and year to date. LHDN requires the employee can print and save it.
export const downloadMyTp1Form = (id: string, label: string) =>
  apiGetFile(`/payslips/${id}/tp1`, `TP1-${label}.pdf`);

// A year the employee was paid in, for their own Form EA. The form covers
// their whole year with the company, so it is downloadable only once every
// month of it is approved — until then `available` is false.
export type EaFormYear = {
  year: number;
  available: boolean;
  /** January through this month must be approved: 12, or earlier for a leaver. */
  requiredMonths: number;
  /** How many of those are approved so far. */
  approvedMonths: number;
};

export const getMyEaForms = () => apiGet<EaFormYear[]>("/payslips/ea-forms");

export const downloadMyEaForm = (year: number) =>
  apiGetFile(`/payslips/ea-forms/${year}`, `Form_EA_${year}.pdf`);
