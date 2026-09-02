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

// One approvable event on a record. A time adjustment arrives as one of these
// with `originalEventAt` set: `eventAt` is the corrected time the employee is
// asking for, `originalEventAt` is what the clock actually recorded. Approving
// applies the corrected time; rejecting leaves the record as it was.
export type AttendanceApprovalRequest = {
  id: string;
  employeeId: string;
  employeeEmail?: string | null;
  kind: "CLOCK_IN" | "CLOCK_OUT" | "BREAK_START" | "BREAK_END";
  eventAt: string;
  originalEventAt?: string | null;
  reason?: string | null;
  approvalStatus: AttendanceApprovalStatus;
  currentStep: number;
  reviewNotes?: string | null;
  reviewerId?: string | null;
  submittedAt: string;
  decidedAt?: string | null;
  attendanceRecordId?: string | null;
  attendanceSessionId?: string | null;
  attendanceBreakId?: string | null;
};

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
  approvals?: AttendanceApprovalRequest[];
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
// Server code returned when a clock is refused for being off-site.
// Worked-minutes totals for a date range, computed server-side. The rule lives
// in AttendanceHoursMath: a working day counts at most the shift length, real
// breaks come off the clock time, and rest days / holidays sit in their own
// buckets. Deliberately not recomputed on the client — the numbers here are the
// same ones payroll would read.
export type HoursBuckets = {
  normalMin: number;         // capped at the shift length
  restDayMin: number;        // uncapped — outside the schedule entirely
  publicHolidayMin: number;
  beyondShiftMin: number;    // past the shift; needs an approved OT submission
  breakMin: number;          // deducted from the raw clock time
  totalMin: number;
  otApprovedMin: number;
  otPendingMin: number;
  otRejectedMin: number;
  expectedMin: number;       // scheduled days x the employee's standard day
};

// from/to are inclusive plain YYYY-MM-DD days.
export const getMyHoursSummary = (from: string, to: string) =>
  apiGet<HoursBuckets>(`/attendance/hours-summary/me?from=${from}&to=${to}`);

// A break within today's session. `endedAt` null means it's still running.
// Breaks go through the same approval chain as clock events, so they carry the
// same approval rollup.
export type AttendanceBreak = {
  id: string;
  attendanceSessionId: string;
  attendanceRecordId: string;
  startedAt: string;
  endedAt?: string | null;
  durationMin?: number | null;
  startLat?: number | null;
  startLng?: number | null;
  endLat?: number | null;
  endLng?: number | null;
  remark?: string | null;
  approvalStatus: "PENDING" | "APPROVED" | "REJECTED";
  currentStep: number;
  reviewNotes?: string | null;
  submittedAt?: string | null;
  decidedAt?: string | null;
};

export type BreakLocation = { lat?: number; lng?: number; remark?: string };

export const getBreaks = (recordId: string) =>
  apiGet<AttendanceBreak[]>(`/attendance/${recordId}/breaks`);

export const startBreak = (body: BreakLocation = {}) =>
  apiPost<AttendanceBreak>("/attendance/break/start", body);

export const endBreak = (body: BreakLocation = {}) =>
  apiPost<AttendanceBreak>("/attendance/break/end", body);

// A correction the employee asks for on their own record. At least one of the
// two times must be set. The clock-out itself already happened at the real
// time — this files a pending request on top of it, so a rejection costs
// nothing.
export type SubmitTimeAdjustment = {
  recordId: string;
  requestedTimeIn?: string;    // ISO-8601
  requestedTimeOut?: string;   // ISO-8601
  reason: string;
};

export const submitTimeAdjustment = (body: SubmitTimeAdjustment) =>
  apiPost<AttendanceApprovalRequest[]>("/attendance/adjustments", body);

// Independent per-id outcomes: one bad id doesn't fail the batch, so the caller
// has to read `items` rather than assume all-or-nothing.
export type AttendanceBulkResult = {
  succeeded: number;
  failed: number;
  items: { id: string; ok: boolean; error?: string | null }[];
};

// NOTE: every id below is an AttendanceApprovalRequest id (record.approvals[].id),
// never a record id. The two are separate GUIDs and the server resolves only the
// former, so passing a record id silently finds nothing.
export const bulkApproveAttendance = (ids: string[]) =>
  apiPost<AttendanceBulkResult>("/attendance/bulk/approve", { ids });

export const bulkRejectAttendance = (ids: string[], reviewNotes?: string) =>
  apiPost<AttendanceBulkResult>("/attendance/bulk/reject", { ids, reviewNotes });

// Break approvals awaiting the caller as current-step approver.
export const getTeamBreakApprovals = () =>
  apiGet<AttendanceApprovalRequest[]>("/attendance/team/breaks");

// The pending approval-request ids on a record — what the decision endpoints
// actually take.
export function pendingApprovalIds(record: AttendanceRecord): string[] {
  return (record.approvals ?? [])
    .filter((a) => a.approvalStatus === "PENDING")
    .map((a) => a.id);
}

export const OFF_SITE_CODE = "OFF_SITE_ACTION_REQUIRED";

// Upload an off-site proof photo; returns the URL to attach to the clock request.
// Unused so far: the off-site clock flow that needs a photo isn't built yet, so
// nothing posts to /attendance/photo. Kept because it's that flow's uploader,
// not leftovers.
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
