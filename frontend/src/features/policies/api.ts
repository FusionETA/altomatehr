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
  otRateNormalDay: number;
  otRatePublicHoliday: number;
  otRateRestDay: number;
  otRatePublicHolidayInShift: number;
  otRateRestDayInShift: number;
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
  otRateNormalDay: number;
  otRatePublicHoliday: number;
  otRateRestDay: number;
  otRatePublicHolidayInShift: number;
  otRateRestDayInShift: number;
  otSalaryThreshold: number | null;
  leaveEntitlements: PolicyLeaveEntitlement[];
};

export const getPolicies = () => apiGet<Policy[]>("/policies");
export const createPolicy = (body: SavePolicy) => apiPost<Policy>("/policies", body);
export const updatePolicy = (id: string, body: SavePolicy) => apiPut<Policy>(`/policies/${id}`, body);
export const setDefaultPolicy = (id: string) => apiPost<Policy>(`/policies/${id}/default`);
export const archivePolicy = (id: string) => apiPost<Policy>(`/policies/${id}/archive`);
export const restorePolicy = (id: string) => apiPost<Policy>(`/policies/${id}/restore`);
