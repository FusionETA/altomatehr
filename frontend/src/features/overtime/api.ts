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

export type UploadOvertimePhotoResponse = {
  photoUrl: string;
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
