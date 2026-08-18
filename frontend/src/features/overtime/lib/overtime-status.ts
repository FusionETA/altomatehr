import type { OvertimeRequest, OvertimeStatus } from "../api";

export type OvertimeStatusFilter = "ALL" | OvertimeStatus;

export const visibleOvertimeStatuses: OvertimeStatus[] = [
  "PENDING",
  "APPROVED",
  "REJECTED",
  "CANCELLED",
];

export const overtimeStatusLabels: Record<OvertimeStatus, string> = {
  PENDING: "Pending",
  APPROVED: "Approved",
  REJECTED: "Rejected",
  CANCELLED: "Cancelled",
};

export function overtimeMatchesStatus(request: OvertimeRequest, status: OvertimeStatusFilter) {
  return status === "ALL" ? true : request.status === status;
}
