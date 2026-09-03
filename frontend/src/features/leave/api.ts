import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

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
