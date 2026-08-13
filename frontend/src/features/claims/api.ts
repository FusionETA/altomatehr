import { apiGet, apiGetBlob, apiPost, apiPostForm } from "@/shared/lib/api-client";

// Mirrors the backend Claim (later: generate this from the OpenAPI spec).
export type Claim = {
  id: string;
  claimNumber: string;
  title: string;
  category: string;
  amount: number;
  currency: string;
  status: string;
  claimType: string;
  employeeId: string;
  employeeEmail?: string | null; // populated for team/approver views
  spentAt: string;
  submittedAt: string;
  projectId?: string | null;
  chartOfAccountId?: string | null;
  exceedsLimit: boolean;
  receiptUrl?: string | null;
  reviewNotes?: string | null;
};

export type CreateClaimRequest = {
  title: string;
  description: string;
  category: string;
  amount: number;
  currency: string;
  spentAt: string;
  claimType: string;
  paymentType: string;
  projectId?: string;
  chartOfAccountId?: string;
  receiptUrl?: string;
};

export type UploadReceiptResponse = {
  receiptUrl: string;
};

export const getMyClaims = () => apiGet<Claim[]>("/claims");
export const getTeamClaims = () => apiGet<Claim[]>("/claims/team");
export const createClaim = (body: CreateClaimRequest) => apiPost<Claim>("/claims", body);
export const approveClaim = (id: string) => apiPost<Claim>(`/claims/${id}/approve`);
export const rejectClaim = (id: string, reviewNotes?: string) =>
  apiPost<Claim>(`/claims/${id}/reject`, { reviewNotes });

export function uploadClaimReceipt(file: File) {
  const formData = new FormData();
  formData.append("receiptFile", file);
  return apiPostForm<UploadReceiptResponse>("/claims/receipts", formData);
}

export async function openClaimReceipt(receiptUrl: string) {
  const path = getApiPath(receiptUrl);
  const blob = await apiGetBlob(path);
  const objectUrl = URL.createObjectURL(blob);
  window.open(objectUrl, "_blank", "noopener,noreferrer");
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
}

function getApiPath(receiptUrl: string) {
  if (receiptUrl.startsWith("/claims/receipts/")) return receiptUrl;

  if (receiptUrl.startsWith("http://") || receiptUrl.startsWith("https://")) {
    return new URL(receiptUrl).pathname;
  }

  return receiptUrl;
}
