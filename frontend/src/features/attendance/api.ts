import { apiGet, apiGetBlob, apiPost, apiPostForm } from "@/shared/lib/api-client";

// Mirrors the backend AttendanceStatus enum.
export type AttendanceStatus =
  | "ON_TIME"
  | "LATE"
  | "MISSING"
  | "CLOCKED_IN"
  | "CLOCKED_OUT"
  | "ON_LEAVE";

export type AttendanceApprovalStatus = "PENDING" | "APPROVED" | "REJECTED";

// Mirrors the backend AttendanceRecordDto. `date` is a local-day key (yyyy-MM-dd);
// timeIn/timeOut/createdAt/updatedAt are ISO-8601 UTC strings.
export type AttendanceRecord = {
  id: string;
  employeeId: string;
  date: string;
  timeIn: string | null;
  timeOut: string | null;
  durationMin: number | null;
  lateByMin: number | null;
  location: string | null;
  projectId: string | null;
  clockInLat: number | null;
  clockInLng: number | null;
  clockInDistanceMeters: number | null;
  clockOutLat: number | null;
  clockOutLng: number | null;
  clockOutDistanceMeters: number | null;
  clockInPhotoUrl: string | null;
  clockOutPhotoUrl: string | null;
  status: AttendanceStatus;
  approvalStatus: AttendanceApprovalStatus;
  currentStep: number;
  employeeEmail?: string | null;
  notes: string | null;
  remark: string | null;
  reviewNotes: string | null;
  submittedAt: string | null;
  decidedAt: string | null;
  createdAt: string;
  updatedAt: string;
};

export type ClockInRequest = {
  projectId?: string;
  location?: string;
  remark?: string;
  photoUrl?: string;
  lat?: number;
  lng?: number;
};
export type ClockOutRequest = {
  remark?: string;
  photoUrl?: string;
  lat?: number;
  lng?: number;
};

// /attendance/today returns 204 (→ undefined) when there's no record yet today.
export const getTodayAttendance = async () =>
  (await apiGet<AttendanceRecord | undefined>("/attendance/today")) ?? null;

// Admins get the whole org (roll call); employees get their own records.
export const getAttendanceHistory = () => apiGet<AttendanceRecord[]>("/attendance");
export const getTeamAttendanceApprovals = () => apiGet<AttendanceRecord[]>("/attendance/team");

export const clockIn = (body: ClockInRequest = {}) =>
  apiPost<AttendanceRecord>("/attendance/clock-in", body);
export const clockOut = (body: ClockOutRequest = {}) =>
  apiPost<AttendanceRecord>("/attendance/clock-out", body);
export const approveAttendance = (id: string) => apiPost<AttendanceRecord>(`/attendance/${id}/approve`);
export const rejectAttendance = (id: string, reviewNotes: string) =>
  apiPost<AttendanceRecord>(`/attendance/${id}/reject`, { reviewNotes });

// Server code returned when a clock is refused for being off-site.
export const OFF_SITE_CODE = "OFF_SITE_ACTION_REQUIRED";

// Upload an off-site proof photo; returns the URL to attach to the clock request.
export function uploadAttendancePhoto(file: File) {
  const form = new FormData();
  form.append("photo", file);
  return apiPostForm<{ photoUrl: string }>("/attendance/photo", form);
}

export async function openAttendancePhoto(photoUrl: string) {
  const path = getApiPath(photoUrl);
  const blob = await apiGetBlob(path);
  const objectUrl = URL.createObjectURL(blob);
  window.open(objectUrl, "_blank", "noopener,noreferrer");
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
}

function getApiPath(photoUrl: string) {
  if (photoUrl.startsWith("/attendance/photos/")) return photoUrl;

  if (photoUrl.startsWith("http://") || photoUrl.startsWith("https://")) {
    return new URL(photoUrl).pathname;
  }

  return photoUrl;
}
