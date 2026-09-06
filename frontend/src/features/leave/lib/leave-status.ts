import type { LeaveApplication, LeaveStatus } from "../api";

export type LeaveStatusFilter = "ALL" | LeaveStatus;

export const visibleLeaveStatuses: LeaveStatus[] = ["PENDING", "APPROVED", "REJECTED", "CANCELLED"];

export const leaveStatusLabels: Record<LeaveStatus, string> = {
  PENDING: "Pending",
  APPROVED: "Approved",
  REJECTED: "Rejected",
  CANCELLED: "Cancelled",
};

export function leaveMatchesStatus(application: LeaveApplication, status: LeaveStatusFilter) {
  if (status === "ALL") return true;
  return application.status === status;
}
