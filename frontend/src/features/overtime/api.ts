import type { BulkResult } from "@/shared/lib/use-bulk-selection";
import { apiDelete, apiGet, apiGetBlob, apiPost, apiPostForm } from "@/shared/lib/api-client";

export type OvertimeStatus = "PENDING" | "APPROVED" | "REJECTED" | "CANCELLED";

export type OvertimeRequest = {
  id: string;
  employeeId: string;
  employeeEmail?: string | null;
  /** Real name from the directory; null when none is set. */
  employeeName?: string | null;
  projectId?: string | null;
  workDate: string;
  startAt: string;
  endAt: string;
  requestedMinutes: number;
  reason: string;
  // The first before/after file — kept for older rows. Read the lists below.
  beforePhotoUrl: string;
  afterPhotoUrl?: string | null;
  beforeAttachments: OvertimeAttachment[];
  afterAttachments: OvertimeAttachment[];
  status: OvertimeStatus;
  currentStep: number;
  reviewNotes?: string | null;
  // Who last decided. Null where nobody did — auto-approved at submit for an
  // employee with no approver, or resolved by the unreachable-approval sweep.
  reviewerId?: string | null;
  reviewerEmail?: string | null;
  reviewerName?: string | null;
  submittedAt: string;
  decidedAt?: string | null;
  createdAt: string;
  updatedAt: string;
};

// A before/after-work file on a request: a photo, or a PDF such as a signed
// job sheet. Up to OVERTIME_MAX_FILES a side.
export type OvertimeAttachment = {
  id: string;
  url: string;
  fileName: string;
  addedAt: string;
};

export type OvertimeAttachmentInput = { url: string; fileName: string };

// Mirrors OvertimeRequest.MaxAttachmentsPerSide on the server.
export const OVERTIME_MAX_FILES = 10;

// What the file pickers accept — the same set the server stores.
export const OVERTIME_FILE_ACCEPT = "image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf";

export type CreateOvertimeRequest = {
  projectId?: string;
  workDate: string;
  startAt: string;
  endAt: string;
  reason: string;
  beforeAttachments: OvertimeAttachmentInput[];
};

export type OtDayType = "NORMAL_DAY" | "REST_DAY" | "PUBLIC_HOLIDAY";

// Which OT multiplier applies on a given date, and why. Derived server-side from
// the employee's shift working days and the holiday calendar — never chosen by
// the employee, so it can't disagree with what payroll will use.
//
// Both multipliers are null when no cash rate applies at all: OT switched off on
// the policy, or banked as time off instead of paid. `reason` explains which.
export type OtRateResolution = {
  dayType: OtDayType;
  outOfShiftMultiplier: number | null;
  inShiftMultiplier: number | null;
  reason: string;
};

export type UploadOvertimePhotoResponse = {
  photoUrl: string;
};

// date is a plain YYYY-MM-DD day; the server resolves the day type from it.
export const getOvertimeRate = (date: string, projectId?: string) => {
  const query = new URLSearchParams({ date });
  if (projectId) query.set("projectId", projectId);
  return apiGet<OtRateResolution>(`/overtime/rate?${query}`);
};

export const getMyOvertime = () => apiGet<OvertimeRequest[]>("/overtime");
export const getTeamOvertime = () => apiGet<OvertimeRequest[]>("/overtime/team");

// Org-wide, for the admin attendance view. Distinct from getTeamOvertime,
// which is approver-scoped and returns nothing to an admin — admins are never
// in an approval chain.
export const getAllOvertime = () => apiGet<OvertimeRequest[]>("/overtime/all");
export const createOvertime = (body: CreateOvertimeRequest) => apiPost<OvertimeRequest>("/overtime", body);
// ADDS after-work files to a pending request; what's already there stays.
export const attachOvertimeAfterFiles = (id: string, attachments: OvertimeAttachmentInput[]) =>
  apiPost<OvertimeRequest>(`/overtime/${id}/after-photo`, { attachments });

// One after-work file off a pending request. Before-work files can't be removed.
export const removeOvertimeAttachment = (id: string, attachmentId: string) =>
  apiDelete<OvertimeRequest>(`/overtime/${id}/attachments/${attachmentId}`);
export const approveOvertime = (id: string) => apiPost<OvertimeRequest>(`/overtime/${id}/approve`);

// Per-id success/failure, so the response is a report rather than one pass/fail
// for the batch. Requests with no after-work photo come back as failures WITH
// their reason: approval gates on that photo, so saying so is what tells the
// approver to chase it.
export type OvertimeBulkResult = BulkResult;

// Approve-only on purpose: rejection needs a remark about THAT request, so it
// stays one at a time. See BulkApproveOvertimeDto on the server.
export const bulkApproveOvertime = (ids: string[]) =>
  apiPost<OvertimeBulkResult>("/overtime/bulk/approve", { ids });
export const rejectOvertime = (id: string, reviewNotes: string) =>
  apiPost<OvertimeRequest>(`/overtime/${id}/reject`, { reviewNotes });

export function uploadOvertimePhoto(file: File) {
  const formData = new FormData();
  formData.append("photo", file);
  return apiPostForm<UploadOvertimePhotoResponse>("/overtime/photo", formData);
}

// Uploads each file (one request apiece, in parallel), then hands back what a
// submit or attach call takes. All-or-nothing: if any upload fails the caller
// sends nothing, rather than a request missing one of the chosen files.
export async function uploadOvertimeFiles(files: File[]): Promise<OvertimeAttachmentInput[]> {
  const uploads = await Promise.all(files.map((file) => uploadOvertimePhoto(file)));
  return uploads.map((upload, i) => ({ url: upload.photoUrl, fileName: files[i].name }));
}

export async function openOvertimePhoto(photoUrl: string) {
  const path = getApiPath(photoUrl);
  const blob = await apiGetBlob(path);
  const objectUrl = URL.createObjectURL(blob);
  window.open(objectUrl, "_blank", "noopener,noreferrer");
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
}

function getApiPath(photoUrl: string) {
  if (photoUrl.startsWith("/overtime/photos/")) return photoUrl;

  if (photoUrl.startsWith("http://") || photoUrl.startsWith("https://")) {
    return new URL(photoUrl).pathname;
  }

  return photoUrl;
}
