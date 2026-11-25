import { apiGet, apiPost, apiPostKeepalive } from "@/shared/lib/api-client";

// User activity — sign-ins and what people did in them. Fusioneta-only: any
// signed-in person's browser SENDS (the tracker in lib/tracker.ts); only
// superadmins can READ it back (the Support page's "User activity" tab).

// ---- Sending (the tracker) ----

export type ActivityEventIn = {
  kind: "page" | "click";
  // A page id ("payroll/runs") or a data-track id ("payslips.export").
  name: string;
  at: string;
};

const EVENTS_PATH = "/activity/events";

// An empty list is a heartbeat: "this tab is still open and visible".
export const sendActivity = (events: ActivityEventIn[]) => apiPost<void>(EVENTS_PATH, { events });

// For a tab on its way out — survives the page closing.
export const sendActivityOnExit = (events: ActivityEventIn[]) => apiPostKeepalive(EVENTS_PATH, { events });

// ---- Reading (superadmins) ----

export type SessionStatus = "online" | "inactive" | "signed-out";

export type ActivitySession = {
  id: string;
  userId: string;
  name: string;
  email: string;
  organizationId: string;
  companyName: string;
  role: string;
  // "password" | "Altomate SSO" | "resumed"
  via: string;
  ipAddress: string | null;
  device: string | null;
  isStaff: boolean;
  startedAt: string;
  lastSeenAt: string;
  endedAt: string | null;
  endReason: string | null;
  status: SessionStatus;
  pageViews: number;
  clicks: number;
  actions: number;
  failedActions: number;
};

export type SessionPage = { sessions: ActivitySession[]; total: number };

export type SessionQuery = {
  from?: string;
  to?: string;
  organizationId?: string;
  search?: string;
  via?: string;
  onlineOnly?: boolean;
  includeStaff?: boolean;
  page?: number;
  limit?: number;
};

export type TimelineItem = {
  at: string;
  kind: "page" | "click" | "action" | "error";
  name: string;
  targetId: string | null;
  statusCode: number | null;
  durationMs: number | null;
  message: string | null;
  isSupport: boolean;
  // Only when it differs from the session's company (they switched).
  companyName: string | null;
};

export type SessionDetail = {
  session: ActivitySession;
  items: TimelineItem[];
  truncated: boolean;
};

const PLATFORM = "/platform/activity";

export function getSessions(query: SessionQuery = {}) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== "" && value !== false) params.set(key, String(value));
  }
  const qs = params.toString();
  return apiGet<SessionPage>(`${PLATFORM}/sessions${qs ? `?${qs}` : ""}`);
}

export const getSession = (id: string) =>
  apiGet<SessionDetail>(`${PLATFORM}/sessions/${encodeURIComponent(id)}`);
