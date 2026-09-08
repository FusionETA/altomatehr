import { apiGet } from "@/shared/lib/api-client";

export type AuditEntry = {
  id: string;
  // Per-org, 1-based, and the paging cursor. Also what a chain break is
  // reported against, so it is worth showing.
  seq: number;
  actorUserId: string | null;
  actorRole: string | null;
  actorEmail: string;
  actorName: string;
  // The raw "module.verb" code, kept greppable.
  action: string;
  // The same action as a sentence, resolved server-side.
  label: string;
  status: "SUCCESS" | "FAILED";
  summary: string;
  errorReason: string | null;
  targetType: string | null;
  targetId: string | null;
  ipAddress: string | null;
  // Raw JSON, free-form per action — the detail behind the summary. Rendered,
  // never interpreted, so a new action can carry whatever shape it needs.
  metadata: string | null;
  createdAt: string;
};

export type AuditPage = {
  entries: AuditEntry[];
  // How many rows match the filters in total, so the pager can say "1-10 of 47".
  total: number;
};

export type AuditQuery = {
  action?: string;
  status?: "SUCCESS" | "FAILED";
  from?: string;
  to?: string;
  limit?: number;
  // 1-based.
  page?: number;
};

export function getAuditLog(query: AuditQuery = {}) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== "" && value !== null) params.set(key, String(value));
  }
  const qs = params.toString();
  return apiGet<AuditPage>(`/audit${qs ? `?${qs}` : ""}`);
}

// GET /audit/verify still exists server-side — it recomputes the whole hash
// chain and reports the first break. No client calls it: the button was removed
// from the page, so the check is a deliberate out-of-band action now (curl, or
// whatever runs it on a schedule) rather than something an admin can press.
