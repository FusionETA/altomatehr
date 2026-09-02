import { apiGet, apiGetBlob, apiPost, apiPostForm } from "@/shared/lib/api-client";

export type OvertimeStatus = "PENDING" | "APPROVED" | "REJECTED" | "CANCELLED";

export type OvertimeRequest = {
  id: string;
  employeeId: string;
  employeeEmail?: string | null;
  projectId?: string | null;
  workDate: string;
  startAt: string;
  endAt: string;
  requestedMinutes: number;
  reason: string;
  beforePhotoUrl: string;
  afterPhotoUrl?: string | null;
  status: OvertimeStatus;
  currentStep: number;
  reviewNotes?: string | null;
  submittedAt: string;
  decidedAt?: string | null;
  createdAt: string;
  updatedAt: string;
};

export type CreateOvertimeRequest = {
  projectId?: string;
  workDate: string;
  startAt: string;
  endAt: string;
  reason: string;
  beforePhotoUrl: string;
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
export const createOvertime = (body: CreateOvertimeRequest) => apiPost<OvertimeRequest>("/overtime", body);
export const attachOvertimeAfterPhoto = (id: string, afterPhotoUrl: string) =>
  apiPost<OvertimeRequest>(`/overtime/${id}/after-photo`, { afterPhotoUrl });
export const approveOvertime = (id: string) => apiPost<OvertimeRequest>(`/overtime/${id}/approve`);
export const rejectOvertime = (id: string, reviewNotes: string) =>
  apiPost<OvertimeRequest>(`/overtime/${id}/reject`, { reviewNotes });

export function uploadOvertimePhoto(file: File) {
  const formData = new FormData();
  formData.append("photo", file);
  return apiPostForm<UploadOvertimePhotoResponse>("/overtime/photo", formData);
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
