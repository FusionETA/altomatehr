import {
  apiGet,
  apiGetBlob,
  apiGetFile,
  apiPost,
  apiPostForm,
  apiPut,
  type ApiFile,
} from "@/shared/lib/api-client";

// Mirrors the backend Claim (later: generate this from the OpenAPI spec).
export type Claim = {
  id: string;
  claimNumber: string;
  title: string;
  description: string;
  category: string;
  amount: number;
  currency: string;
  status: string;
  // Xero bill state. NOT_SYNCED and ERROR are distinct on purpose: "never
  // pushed" and "pushed and failed" need different actions.
  xeroSyncStatus: "NOT_SYNCED" | "SYNCED" | "ERROR";
  xeroBillId?: string | null;
  xeroBillRef?: string | null;
  xeroSyncError?: string | null;
  xeroSyncedAt?: string | null;
  // How this claim gets paid out. XERO_BILL pushes it to Xero; PAYROLL takes it
  // out of Xero entirely and into the payroll reimbursement run.
  settlement: ClaimSettlement;
  // Position in the approval chain. A REJECTED claim with currentStep > 0 got
  // past its first-line approver before a later layer turned it down — that is
  // how the dashboard spots an overturned approval.
  currentStep: number;
  updatedAt: string;
  claimType: string;
  paymentType: string;
  payViaAccountId?: string | null;
  spendingWith?: string | null;
  spendingAt?: string | null;
  employeeId: string;
  employeeEmail?: string | null; // populated for team/approver views
  // Team view only: whether the caller can decide this claim right now. A
  // settled claim, or one sitting with another step's approver, is visible but
  // not actionable.
  canAct?: boolean;
  // Team view: who the claim is waiting on, when it is not the caller. Empty
  // when it is their turn, or when the claim is already settled.
  awaitingApprovers?: string[];
  spentAt: string;
  submittedAt: string;
  projectId?: string | null;
  chartOfAccountId?: string | null;
  exceedsLimit: boolean;
  distance?: number | null;
  mileageOriginAddress?: string | null;
  mileageDestinationAddress?: string | null;
  mileageRateUsed?: number | null;
  mileageUnitUsed?: "KM" | "MILE" | null;
  receiptUrl?: string | null;
  supportingDocumentUrls?: string[] | null;
  reviewNotes?: string | null;
};

export type CreateClaimRequest = {
  title: string;
  description: string;
  category: string;
  amount?: number;
  // No currency: the server stamps the org's default. See CreateClaimDto.
  spentAt: string;
  claimType: string;
  paymentType: string;
  payViaAccountId?: string;
  spendingWith?: string;
  spendingAt?: string;
  projectId?: string;
  chartOfAccountId?: string;
  distance?: number;
  mileageOriginAddress?: string;
  mileageDestinationAddress?: string;
  receiptUrl?: string;
  supportingDocumentUrls?: string[];
};

export type UploadReceiptResponse = {
  receiptUrl: string;
};

export const getMyClaims = () => apiGet<Claim[]>("/claims");
export const getTeamClaims = () => apiGet<Claim[]>("/claims/team");

// Every claim in the org — the admin dashboard's source of truth. Admin/Owner only.
export const getAllClaims = () => apiGet<Claim[]>("/claims/all");
export const createClaim = (body: CreateClaimRequest) => apiPost<Claim>("/claims", body);
export const updateClaim = (id: string, body: CreateClaimRequest) => apiPut<Claim>(`/claims/${id}`, body);
export const approveClaim = (id: string) => apiPost<Claim>(`/claims/${id}/approve`);

// Per-id success/failure, so the response is a report rather than one
// pass/fail for the batch. Over-limit claims come back as failures on purpose:
// they have to be opened and read individually.
export type ClaimsBulkResultItem = { id: string; ok: boolean; error?: string | null };
export type ClaimsBulkResult = {
  succeeded: number;
  failed: number;
  items: ClaimsBulkResultItem[];
};

export const bulkApproveClaims = (ids: string[]) =>
  apiPost<ClaimsBulkResult>("/claims/bulk/approve", { ids });

// Push one approved claim to Xero as a bill. Idempotent server-side, so a
// double press cannot create a second bill.
export type ClaimXeroSyncResponse = { alreadySynced: boolean; claim: Claim };

// AwaitingPayment is Xero's "Awaiting payment" — a live payable. Draft is the
// reviewable version that sits in the accountant's queue.
export type XeroBillStage = "AwaitingPayment" | "Draft";

// Omit `status` to use the org's configured stage (the normal case). Passing one
// overrides the setting for this push only.
export const syncClaimToXero = (id: string, status?: XeroBillStage) =>
  apiPost<ClaimXeroSyncResponse>(`/claims/${id}/xero-sync`, status ? { status } : {});

// The server sequences these — Xero rate-limits per tenant, so firing them
// from the browser in parallel is how half a run lands and the rest 429s.
export const bulkSyncClaimsToXero = (ids: string[], status?: XeroBillStage) =>
  apiPost<ClaimsBulkResult>("/claims/bulk/xero-sync", status ? { ids, status } : { ids });
// ---- Settlement route ----

export type ClaimSettlement = "XERO_BILL" | "PAYROLL";

export const claimSettlementLabels: Record<ClaimSettlement, string> = {
  XERO_BILL: "Sync to Xero as a bill",
  PAYROLL: "Add to payroll",
};

export const claimSettlementHints: Record<ClaimSettlement, string> = {
  XERO_BILL:
    "Approved claims are pushed to Xero — a bill for out-of-pocket claims, a spend-money transaction for company-paid ones.",
  PAYROLL:
    "Approved out-of-pocket claims are reimbursed through the employee's pay and collected in the payroll export instead of going to Xero.",
};

// The payroll reimbursement run: approved out-of-pocket claims routed to
// payroll, one row per employee. Omit `month` for the run currently open.
export function exportPayrollReimbursements(
  format: ExportFormat,
  month?: string,
): Promise<ApiFile> {
  const query = new URLSearchParams({ format });
  if (month) query.set("month", month);
  return apiGetFile(
    `/claims/export/payroll?${query.toString()}`,
    `payroll-reimbursements.${format}`,
  );
}

export const rejectClaim = (id: string, reviewNotes: string) =>
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

// ---- Import / export (admin month-end) ----

// Spreadsheet formats the summary export speaks. PDF is write-only — you file
// it or send it, you don't upload it back.
export type ExportFormat = "csv" | "xlsx" | "pdf";
export type ImportFormat = Extract<ExportFormat, "csv" | "xlsx">;

// Mirrors ClaimsExportQueryDto. Everything optional — an empty filter exports
// every claim in the org.
export type ClaimsExportFilters = {
  from?: string;
  to?: string;
  // Finance reconciles on when the money was spent, payroll on when it was filed.
  dateField?: "spent" | "submitted";
  status?: string;
  // PERSONAL = owed back to the employee; COMPANY = already paid from a company
  // account. A reimbursement run is only ever the PERSONAL half.
  paymentType?: "PERSONAL" | "COMPANY";
  employeeId?: string;
  projectId?: string;
};

// Mirrors TabularImportResult. Skipped is not a failure: the importer is
// append-only and idempotent, so re-uploading a corrected file reports the rows
// that already landed as skipped rather than duplicating them.
export type ClaimsImportResult = {
  imported: number;
  skipped: number;
  failed: number;
  errors: { row: number; message: string }[];
};

function toQuery(params: Record<string, string | undefined>) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const query = search.toString();
  return query ? `?${query}` : "";
}

export function exportClaimsSummary(
  format: ExportFormat,
  filters: ClaimsExportFilters = {},
): Promise<ApiFile> {
  const query = toQuery({ ...filters, format });
  return apiGetFile(`/claims/export/summary${query}`, `claims-summary.${format}`);
}

export function downloadClaimsImportTemplate(format: ImportFormat): Promise<ApiFile> {
  return apiGetFile(`/claims/import/template?format=${format}`, `claims-import-template.${format}`);
}

export function importClaims(file: File) {
  const formData = new FormData();
  formData.append("file", file);
  return apiPostForm<ClaimsImportResult>("/claims/import", formData);
}

// ---- Claim settings ----

export type ClaimSettings = {
  // Day of month that closes the claims run (1-28).
  claimRunCutoffDay: number;
  // How approved claims are paid out, org-wide. Each claim is stamped with this
  // when it is created, so changing it never re-routes a claim already billed.
  settlementRoute: ClaimSettlement;
  // Which stage a bill lands at in Xero. Only applies on the XERO_BILL route.
  xeroBillStage: XeroBillStage;
};

export const xeroBillStageLabels: Record<XeroBillStage, string> = {
  AwaitingPayment: "Awaiting payment",
  Draft: "Draft",
};

export const xeroBillStageHints: Record<XeroBillStage, string> = {
  AwaitingPayment:
    "The bill is a live payable the moment it arrives — the claim already cleared approval, so the money is genuinely owed.",
  Draft:
    "The bill parks in your accountant's queue in Xero to be reviewed there before it counts.",
};

export const getClaimSettings = () => apiGet<ClaimSettings>("/claims/settings");

export const updateClaimSettings = (body: ClaimSettings) =>
  apiPut<ClaimSettings>("/claims/settings", body);
