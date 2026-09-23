import {
  apiGet,
  apiGetBlob,
  apiGetFile,
  apiPost,
  apiPostForm,
  saveFile,
} from "@/shared/lib/api-client";

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
// One clock-in/clock-out stint. The record is the DAY and reports first-start,
// last-end and summed minutes; these are what explain that total.
export type AttendanceSession = {
  id: string;
  startedAt: string;
  endedAt: string | null;
  durationMin: number | null;
  lateByMin: number | null;
  status: AttendanceStatus;
  clockInDistanceMeters: number | null;
  clockOutDistanceMeters: number | null;
  clockInPhotoUrl: string | null;
  clockOutPhotoUrl: string | null;
  /** Where THIS shift's two ends happened; the record only carries the day's
   *  first clock-in and last clock-out. */
  clockInLat: number | null;
  clockInLng: number | null;
  clockOutLat: number | null;
  clockOutLng: number | null;
};

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
  /** The day's clock-in/out stints, oldest first. A day can have several. */
  /** What the IP check saw on the first clock-in of the day. `ipAllowed` is
   *  tri-state: true matched the project's allowlist, false means the check
   *  ran and it didn't (the employee clocked in via the remark-and-photo
   *  override), null means no check ran. */
  clockInIpAddress?: string | null;
  clockInIpAllowed?: boolean | null;
  sessions?: AttendanceSession[];
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

// The still-running session, whatever day it started on — normally the same
// record as getTodayAttendance(), and a different (earlier) one only when a
// clock-out was forgotten. Clocking in is refused while one of those is open,
// so the UI reads this on load to offer "clock out" instead of a button the
// server would reject.
export const getOpenSession = async () =>
  (await apiGet<AttendanceRecord | undefined>("/attendance/open-session")) ?? null;

// One of the caller's direct reports, with today's record — null when they
// haven't clocked in, which is a state worth showing rather than hiding.
export type TeamAttendanceMember = {
  employeeId: string;
  employeeEmail?: string | null;
  /** The project of the team they're in — what the supervisor switches between. */
  projectId: string;
  projectName?: string | null;
  teamId: string;
  teamName: string;
  record: AttendanceRecord | null;
};

// Presence for the caller's team today. Not the approval queue — that's
// getTeamAttendanceApprovals.
export const getTeamToday = () =>
  apiGet<TeamAttendanceMember[]>("/attendance/team/today");

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
// Exported so callers can use the SAME string as a cache key — a hand-built
// key that drifts from the real path is a cache that never hits.
export const hoursSummaryPath = (from: string, to: string) =>
  `/attendance/hours-summary/me?from=${from}&to=${to}`;
export const getMyHoursSummary = (from: string, to: string) =>
  apiGet<HoursBuckets>(hoursSummaryPath(from, to));

// ---- Admin reports ----
//
// All org-wide, all Admin/Owner. The three filters narrow every one of them the
// same way, resolved server-side so the tabs cannot disagree about who a filter
// means. Dates are inclusive plain YYYY-MM-DD days.

export type AdminAttendanceFilter = {
  projectId?: string;
  teamId?: string;
  q?: string;
};

function reportQuery(from: string, to: string, filter: AdminAttendanceFilter = {}) {
  const params = new URLSearchParams({ from, to });
  if (filter.projectId) params.set("projectId", filter.projectId);
  if (filter.teamId) params.set("teamId", filter.teamId);
  if (filter.q?.trim()) params.set("q", filter.q.trim());
  return params.toString();
}

export type EmployeeHoursRow = { employeeId: string; email: string | null; buckets: HoursBuckets };
export type OrgHoursSummary = { totals: HoursBuckets; employees: EmployeeHoursRow[] };

// Takes the whole filter, not just a team: the Analytics table and its totals
// row are both built from this one response, so narrowing has to happen
// server-side — filtering the rows on the client would leave the totals
// describing a different population than the table under them.
export const getOrgHoursSummary = (
  from: string,
  to: string,
  filter: AdminAttendanceFilter = {},
) => apiGet<OrgHoursSummary>(`/attendance/hours-summary/org?${reportQuery(from, to, filter)}`);

export type SupervisorPerformance = {
  reviewerId: string;
  reviewerName: string;
  totalDecisions: number;
  approvedCount: number;
  rejectedCount: number;
  // Decisions that took longer than the org's SLA.
  slowDecisionCount: number;
  avgDelayMinutes: number | null;
  // Beside the average because an average of 40 minutes hides the one request
  // that waited three days.
  maxDelayMinutes: number | null;
};

export const getSupervisorPerformance = (
  from: string,
  to: string,
  filter: AdminAttendanceFilter = {},
) =>
  apiGet<SupervisorPerformance[]>(
    `/attendance/supervisor-performance?${reportQuery(from, to, filter)}`,
  );

export type ApprovalAuditEntry = {
  id: string;
  employeeId: string;
  employeeName: string;
  kind: string;
  status: string;
  eventAt: string;
  submittedAt: string;
  decidedAt: string | null;
  reviewerId: string | null;
  reviewerName: string | null;
  reviewNotes: string | null;
  // Measured to the decision, or to now while still pending — so a stale
  // pending row reads on the same scale as a slow decided one.
  delayMinutes: number | null;
};

export const getApprovalAudit = (from: string, to: string, filter: AdminAttendanceFilter = {}) =>
  apiGet<ApprovalAuditEntry[]>(`/attendance/approval-audit?${reportQuery(from, to, filter)}`);

export type SelfieStorage = {
  photoCount: number;
  totalBytes: number;
  // Photos a record points at that are no longer on disk.
  missingCount: number;
  oldestPhotoAt: string | null;
};

export const getSelfieStorage = () => apiGet<SelfieStorage>("/attendance/selfie-storage");

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
// The RECORD-level decisions still waiting on a day — clock-ins, clock-outs and
// the corrections filed against them.
//
// Breaks are deliberately excluded even though they sit in `record.approvals`
// too. /attendance/team attaches every request on the record regardless of
// kind, while /attendance/team/breaks returns those same break rows again, by
// the same ids. Every caller here pairs this helper with that second list, so
// counting breaks in both places counted each one twice: a day with three clock
// events and two breaks read "7 events pending" over five cards, and the
// sidebar badge said 7 as well.
//
// Corrections stay in: a correction's kind is still CLOCK_IN or CLOCK_OUT, it
// renders as its own card, and it is decided from the same button.
export function pendingApprovalIds(record: AttendanceRecord): string[] {
  return (record.approvals ?? [])
    .filter(
      (a) =>
        a.approvalStatus === "PENDING" &&
        (a.kind === "CLOCK_IN" || a.kind === "CLOCK_OUT"),
    )
    .map((a) => a.id);
}

export const OFF_SITE_CODE = "OFF_SITE_ACTION_REQUIRED";

// Returned when a clock-in carries no GPS fix at all. Separate from the
// off-site code because it isn't about a geofence: a project with no geofenced
// site has nothing to be outside of, and the coordinates are the only record
// of where the shift started. Takes the same remark+photo override, so the
// client answers it with the same proof dialog.
export const LOCATION_REQUIRED_CODE = "LOCATION_REQUIRED";

// Returned when a clock-in is refused because an earlier shift was never closed.
// The rule: one open session at a time. Close the old one first, then correct
// its time with an adjustment request — a second session would leave the first
// stranded with no hours ever counted against it.
export const OPEN_SESSION_CODE = "OPEN_SESSION_REQUIRES_CLOCK_OUT";

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

// --- Exports ---

// Opens the generated file in a new tab, the way leave and claims do.
// Saved, not opened in a tab.
//
// window.open on an object URL hands the browser a blob: URL with no name, so
// the file landed in Downloads called 73372216-d2a8-….pdf — the object URL's
// own GUID. saveFile reads the server's Content-Disposition and puts that on
// an <a download>, which is what the header was being sent for.
async function saveAttendanceFile(path: string, fallbackName: string) {
  saveFile(await apiGetFile(path, fallbackName));
}

// The caller's OWN report. Same endpoint and same narrowing as the admin one
// below — the server resolves whose record it is from the token, so there is
// no id to pass and nothing to get wrong by passing someone else's.
export const exportMyAttendancePdf = (from: string, to: string) =>
  saveAttendanceFile(
    `/attendance/export/summary?mine=true&from=${from}&to=${to}&format=pdf`,
    `attendance-${from}-to-${to}.pdf`,
  );

// The caller's whole team in one file: a supervisor gets their reports and
// themselves, an admin gets the org. Same endpoint again — the server resolves
// who reports to whom, so no ids cross the wire to be tampered with.
export const exportTeamAttendancePdf = (from: string, to: string) =>
  saveAttendanceFile(
    `/attendance/export/summary?team=true&from=${from}&to=${to}&format=pdf`,
    `attendance-team-${from}-to-${to}.pdf`,
  );

// One person's worked-hours report over a range. Same endpoint as the org
// export, narrowed server-side, so the figures cannot drift from the org one.
export const exportEmployeeAttendancePdf = (employeeId: string, from: string, to: string) =>
  saveAttendanceFile(
    `/attendance/export/summary?employeeId=${employeeId}&from=${from}&to=${to}&format=pdf`,
    `attendance-${from}-to-${to}.pdf`,
  );

// Declared here rather than imported from the claims feature: features don't
// import from each other (see CLAUDE.md), and leave/api.ts already spells the
// same union out locally. Worth hoisting into shared/ if a fourth turns up.
export type ExportFormat = "csv" | "xlsx" | "pdf";

// The approval trail as a file. Goes through the same reportQuery as the
// on-screen report, so the export mirrors exactly what the admin is looking at
// rather than everything.
export const exportApprovalAudit = (
  format: ExportFormat,
  from: string,
  to: string,
  filter: AdminAttendanceFilter = {},
) =>
  apiGetFile(
    `/attendance/export/approval-audit?${reportQuery(from, to, filter)}&format=${format}`,
    `approval-trail.${format}`,
  );
