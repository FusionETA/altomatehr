import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

export type SalaryType = "HOURLY" | "MONTHLY";
export type OtMethod = "CASH" | "TIME_BANK";

export type PolicyLeaveEntitlement = { leaveTypeId: string; defaultDays: number };

export type Policy = {
  id: string;
  name: string;
  description: string | null;
  isDefault: boolean;
  isArchived: boolean;
  // May the signed-in admin edit it? False for an admin limited to some
  // policies when it also covers the company's admins or owner.
  canEdit?: boolean;
  canAccessAttendance: boolean;
  canAccessClaims: boolean;
  canAccessLeave: boolean;
  requireGeofence: boolean;
  requireSelfie: boolean;
  requireClockOutSelfie: boolean;
  salaryType: SalaryType;
  otEnabled: boolean;
  otDailyThresholdMinutes: number;
  otMethod: OtMethod;
  temporary: boolean;
  requireIpWhitelist: boolean;
  geolocationEnabled: boolean;
  captureLocationOnClockIn: boolean;
  captureLocationOnClockOut: boolean;
  captureLocationOnBreakStart: boolean;
  captureLocationOnBreakEnd: boolean;
  autoClockOutEnabled: boolean;
  autoClockOutAfterMinutes: number | null;
  // Null = the company's rate for that built-in overtime type.
  otRateNormalDay: number | null;
  otRatePublicHoliday: number | null;
  otRateRestDay: number | null;
  // Only the company's own types this policy changes; any other is on at the
  // company's rate.
  overtimeRates: PolicyOvertimeRate[];
  otSalaryThreshold: number | null;
  leaveEntitlements: PolicyLeaveEntitlement[];
};

export type SavePolicy = {
  name: string;
  description?: string | null;
  canAccessAttendance: boolean;
  canAccessClaims: boolean;
  canAccessLeave: boolean;
  requireGeofence: boolean;
  requireSelfie: boolean;
  requireClockOutSelfie: boolean;
  salaryType: SalaryType;
  otEnabled: boolean;
  otDailyThresholdMinutes: number;
  otMethod: OtMethod;
  temporary: boolean;
  requireIpWhitelist: boolean;
  geolocationEnabled: boolean;
  captureLocationOnClockIn: boolean;
  captureLocationOnClockOut: boolean;
  captureLocationOnBreakStart: boolean;
  captureLocationOnBreakEnd: boolean;
  autoClockOutEnabled: boolean;
  autoClockOutAfterMinutes: number | null;
  // Null = the company's rate for that built-in overtime type.
  otRateNormalDay: number | null;
  otRatePublicHoliday: number | null;
  otRateRestDay: number | null;
  // Only the company's own types this policy changes; any other is on at the
  // company's rate.
  overtimeRates: PolicyOvertimeRate[];
  otSalaryThreshold: number | null;
  leaveEntitlements: PolicyLeaveEntitlement[];
};

// A policy's take on one of the company's own overtime types.
export type PolicyOvertimeRate = {
  overtimeTypeId: string;
  multiplier: number | null; // null = the company's
  enabled: boolean;
};

export const getPolicies = () => apiGet<Policy[]>("/policies");
export const createPolicy = (body: SavePolicy) => apiPost<Policy>("/policies", body);
export const updatePolicy = (id: string, body: SavePolicy) => apiPut<Policy>(`/policies/${id}`, body);
export const setDefaultPolicy = (id: string) => apiPost<Policy>(`/policies/${id}/default`);
export const archivePolicy = (id: string) => apiPost<Policy>(`/policies/${id}/archive`);
export const restorePolicy = (id: string) => apiPost<Policy>(`/policies/${id}/restore`);

// Which modules the SIGNED-IN employee's own policy includes.
//
// Advisory: the backend gates each endpoint regardless. This is so the shell
// doesn't offer a section that would refuse them.
export type MyModuleAccess = {
  attendance: boolean;
  claims: boolean;
  leave: boolean;
};

export const getMyModuleAccess = () =>
  apiGet<MyModuleAccess>("/policies/mine/modules");

// ---- The company's overtime types ----
// Three are built in (filled by the calendar; renamable, never removed);
// the rest are the company's own, picked on an overtime request.

export type OvertimeTypeKind = "NORMAL_DAY" | "REST_DAY" | "PUBLIC_HOLIDAY" | "CUSTOM";

export type OvertimeType = {
  id: string;
  name: string;
  kind: OvertimeTypeKind;
  multiplier: number;
  isArchived: boolean;
};

export type SaveOvertimeType = { name: string; multiplier: number };

export const OVERTIME_TYPES_PATH = "/overtime-types";
export const getOvertimeTypes = () => apiGet<OvertimeType[]>(OVERTIME_TYPES_PATH);
export const createOvertimeType = (body: SaveOvertimeType) => apiPost<OvertimeType>(OVERTIME_TYPES_PATH, body);
export const updateOvertimeType = (id: string, body: SaveOvertimeType) =>
  apiPut<OvertimeType>(`${OVERTIME_TYPES_PATH}/${id}`, body);
export const archiveOvertimeType = (id: string) => apiPost<OvertimeType>(`${OVERTIME_TYPES_PATH}/${id}/archive`);
