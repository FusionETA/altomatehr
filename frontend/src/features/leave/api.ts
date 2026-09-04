import { apiGet, apiGetBlob, apiPost, apiPostForm, apiPut } from "@/shared/lib/api-client";

export type LeaveStatus = "PENDING" | "APPROVED" | "REJECTED" | "CANCELLED";

export type LeaveType = {
  id: string;
  code: string;
  name: string;
  paid: boolean;
  defaultDays: number;
  isArchived: boolean;
};
export type SaveLeaveType = {
  code: string;
  name: string;
  paid: boolean;
  defaultDays: number;
};

export type LeaveApplication = {
  id: string;
  employeeId: string;
  employeeEmail: string | null; // populated for team/approver views
  leaveTypeId: string;
  startDate: string; // yyyy-MM-dd
  endDate: string;
  totalDays: number;
  reason: string | null;
  status: LeaveStatus;
  reviewNotes: string | null;
  decidedAt: string | null;
  createdAt: string;
};
export type CreateLeaveApplication = {
  leaveTypeId: string;
  startDate: string;
  endDate: string;
  reason?: string;
};

export type LeaveBalance = {
  leaveTypeId: string;
  code: string;
  name: string;
  paid: boolean;
  entitlementDays: number;
  accruedDays: number;
  carriedDays: number;
  accrualMethod: string; // "LUMP_SUM" | "PRO_RATED"
  year: number;
  isOpened: boolean;
  takenDays: number;
  pendingDays: number;
  remainingDays: number;
};

export type OnLeaveToday = {
  employeeId: string;
  email: string | null;
  leaveTypeId: string;
  leaveTypeCode: string;
  leaveTypeName: string;
  startDate: string;
  endDate: string;
  totalDays: number;
};

// --- Admin: overview & org-wide balances ---
export type LeaveOverview = {
  year: number;
  totals: { pending: number; approved: number; rejected: number; cancelled: number };
  daysUsedByType: { leaveTypeId: string; code: string; name: string; paid: boolean; daysUsed: number }[];
  onLeaveToday: OnLeaveToday[];
  recentApplications: LeaveApplication[];
};

export type EmployeeLeaveBalances = {
  userId: string;
  email: string;
  role: string;
  balances: LeaveBalance[];
};

// --- Admin: entitlements ---
export type LeaveAccrualMethod = "LUMP_SUM" | "PRO_RATED";
export type SetEntitlement = {
  entitledDays: number;
  accrualMethod?: LeaveAccrualMethod | null;
};

// --- Reports & audit ---
export type LeaveMonthlyRow = {
  leaveTypeName: string;
  entitledDays: number;
  carriedDays: number;
  monthly: (number | null)[]; // Jan-Dec
  total: number;
  balance: number;
};
export type LeaveDetailRow = {
  from: string;
  to: string;
  leaveTypeName: string;
  days: number;
  reason: string | null;
  attachmentName: string | null;
};
export type LeaveSummaryReport = {
  organizationName: string;
  employeeLabel: string;
  year: number;
  reportDate: string;
  monthlyRows: LeaveMonthlyRow[];
  detailRows: LeaveDetailRow[];
};

export type LeaveApprovalEntry = {
  step: number;
  approverId: string;
  decision: string; // "APPROVED" | "REJECTED" | "ADMIN_APPLIED" | "IMPORTED"
  decidedAt: string;
  notes: string | null;
};

// --- Import ---
export type TabularImportResult = {
  imported: number;
  skipped: number;
  failed: number;
  errors: { row: number; message: string }[];
};

// --- Leave types (admin-managed) ---
export const getLeaveTypes = () => apiGet<LeaveType[]>("/leave-types");
export const createLeaveType = (body: SaveLeaveType) => apiPost<LeaveType>("/leave-types", body);
export const updateLeaveType = (id: string, body: SaveLeaveType) =>
  apiPut<LeaveType>(`/leave-types/${id}`, body);
export const archiveLeaveType = (id: string) => apiPost<LeaveType>(`/leave-types/${id}/archive`);
export const restoreLeaveType = (id: string) => apiPost<LeaveType>(`/leave-types/${id}/restore`);

// --- Applications + balances ---
export const getMyLeave = () => apiGet<LeaveApplication[]>("/leave");
export const getTeamLeave = () => apiGet<LeaveApplication[]>("/leave/team");
export const getLeaveBalances = () => apiGet<LeaveBalance[]>("/leave/balances");
export const applyLeave = (body: CreateLeaveApplication) =>
  apiPost<LeaveApplication>("/leave", body);
export const approveLeave = (id: string) => apiPost<LeaveApplication>(`/leave/${id}/approve`);
export const rejectLeave = (id: string, reviewNotes?: string) =>
  apiPost<LeaveApplication>(`/leave/${id}/reject`, { reviewNotes });
export const cancelLeave = (id: string) => apiPost<LeaveApplication>(`/leave/${id}/cancel`);

// `date` is a yyyy-MM-dd string; omit for today.
export const getOnLeaveToday = (date?: string) =>
  apiGet<OnLeaveToday[]>(`/leave/on-leave-today${date ? `?date=${date}` : ""}`);

// --- Admin: overview & org-wide balances ---
export const getLeaveOverview = (year?: number) =>
  apiGet<LeaveOverview>(`/leave/overview${year ? `?year=${year}` : ""}`);
export const getAllLeaveBalances = (year?: number) =>
  apiGet<{ data: EmployeeLeaveBalances[]; total: number; year: number }>(
    `/leave/balances/all${year ? `?year=${year}` : ""}`,
  );
export const getEmployeeLeaveBalances = (employeeId: string, year?: number) =>
  apiGet<{ data: LeaveBalance[]; total: number; year: number }>(
    `/leave/balances/${employeeId}${year ? `?year=${year}` : ""}`,
  );

// --- Admin: entitlements ---
export const setLeaveEntitlement = (
  employeeId: string,
  leaveTypeId: string,
  body: SetEntitlement,
  year?: number,
) =>
  apiPut<LeaveBalance>(
    `/leave/entitlements/${employeeId}/${leaveTypeId}${year ? `?year=${year}` : ""}`,
    body,
  );
export const resetLeaveEntitlement = (employeeId: string, leaveTypeId: string, year?: number) =>
  apiPost<LeaveBalance>(
    `/leave/entitlements/${employeeId}/${leaveTypeId}/reset${year ? `?year=${year}` : ""}`,
  );
export const seedLeaveEntitlements = (employeeId: string, year?: number) =>
  apiPost<{ created: number }>(`/leave/entitlements/${employeeId}/seed${year ? `?year=${year}` : ""}`);

// --- Admin: apply on behalf (lands APPROVED immediately, bypasses the chain) ---
export const applyLeaveOnBehalf = (employeeId: string, body: CreateLeaveApplication) =>
  apiPost<LeaveApplication>(`/leave/on-behalf/${employeeId}`, body);

// --- Reports & audit ---
export const getLeaveSummaryReport = (employeeId: string, year?: number) =>
  apiGet<LeaveSummaryReport>(
    `/leave/summary-report?employeeId=${employeeId}${year ? `&year=${year}` : ""}`,
  );
export const getLeaveAudit = (id: string) => apiGet<LeaveApprovalEntry[]>(`/leave/${id}/audit`);

// Opens a downloaded file in a new tab — mirrors `openClaimReceipt` in claims/api.ts.
async function openLeaveFile(path: string) {
  const blob = await apiGetBlob(path);
  const objectUrl = URL.createObjectURL(blob);
  window.open(objectUrl, "_blank", "noopener,noreferrer");
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
}

// --- Exports ---
export const exportEmployeeLeaveSummaryPdf = (employeeId: string, year?: number) =>
  openLeaveFile(`/leave/export/summary.pdf?employeeId=${employeeId}${year ? `&year=${year}` : ""}`);
export const exportEmployeeLeaveSummary = (
  employeeId: string,
  format: "csv" | "xlsx",
  year?: number,
) =>
  openLeaveFile(
    `/leave/export/summary?employeeId=${employeeId}&format=${format}${year ? `&year=${year}` : ""}`,
  );
export const exportAllLeaveSummary = (format: "csv" | "xlsx", year?: number) =>
  openLeaveFile(`/leave/export/summary/all?format=${format}${year ? `&year=${year}` : ""}`);
export const exportLeaveBulkZip = (year?: number) =>
  openLeaveFile(`/leave/export/summary-bulk.zip${year ? `?year=${year}` : ""}`);

// --- Import ---
export const getLeaveImportTemplate = (format: "csv" | "xlsx") =>
  openLeaveFile(`/leave/import/template?format=${format}`);
export function importLeave(file: File) {
  const form = new FormData();
  form.append("file", file);
  return apiPostForm<TabularImportResult>("/leave/import", form);
}

// --- Maintenance (manual triggers for the two background jobs) ---
export const runLeaveYearRollover = (year?: number) =>
  apiPost<unknown>(`/leave/cron/year-rollover${year ? `?year=${year}` : ""}`);
export const runLeaveMonthlyAccrual = () => apiPost<unknown>("/leave/cron/monthly-accrual");
