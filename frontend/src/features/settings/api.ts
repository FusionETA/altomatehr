import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

export type Organization = {
  id: string;
  name: string;
  defaultCurrency: string;
  defaultMileageRate: number;
  mileageUnit: "KM" | "MILE";
  geofenceRadiusMeters: number;
  // The org's default schedule. An employee's assigned Shift overrides these;
  // the backend falls back to them when nobody has assigned one.
  workingHoursStart: string | null;   // "HH:mm", 24h
  workingHoursEnd: string | null;     // "HH:mm", 24h
  workingDays: string | null;         // CSV ISO weekdays, "1,2,3,4,5"; null = Mon-Fri
  plan: string;
  tier: string | null;
  addons: string[];
  enabledModules: string[];
};
export type UpdateOrganization = {
  name: string;
  defaultCurrency: string;
  defaultMileageRate: number;
  mileageUnit: "KM" | "MILE";
  geofenceRadiusMeters: number;
};

export type Project = {
  id: string;
  name: string;
  /** Street address of the site. Free text, shown to people — never parsed. */
  location: string | null;
  latitude: number | null;
  longitude: number | null;
  isArchived: boolean;
  createdAt: string;
};
export type SaveProject = {
  name: string;
  latitude?: number | null;
  longitude?: number | null;
};

export type ChartOfAccount = {
  id: string;
  code: string;
  name: string;
  type: string;
  isSelectable: boolean;
  limitAmount: number | null;
  allowMileageClaim: boolean;
  mileageRate: number | null;
  isArchived: boolean;
  // Set when the account came from Xero. Its code, name and type are Xero's;
  // everything else on the row is this app's.
  xeroAccountId?: string | null;
};
export type SaveAccount = {
  code: string;
  name: string;
  type: string;
  isSelectable: boolean;
  limitAmount?: number | null;
  allowMileageClaim: boolean;
  mileageRate?: number | null;
};

// --- Organization ---
export const getOrganization = () => apiGet<Organization>("/organizations/current");
export const updateOrganization = (body: UpdateOrganization) =>
  apiPut<Organization>("/organizations/current", body);

// --- Projects ---
export const getProjects = () => apiGet<Project[]>("/projects");

// Only the projects the caller is on, via their team memberships. Use this for
// the clock-in picker — clocking into a project you're not on is refused, so
// offering the whole org list only invites the rejection.
export const getMyProjects = () => apiGet<Project[]>("/projects/mine");
export const createProject = (body: SaveProject) => apiPost<Project>("/projects", body);
export const updateProject = (id: string, body: SaveProject) =>
  apiPut<Project>(`/projects/${id}`, body);
export const archiveProject = (id: string) => apiPost<Project>(`/projects/${id}/archive`);
export const restoreProject = (id: string) => apiPost<Project>(`/projects/${id}/restore`);

// --- Chart of Accounts ---
export const getAccounts = () => apiGet<ChartOfAccount[]>("/accounts");
export const createAccount = (body: SaveAccount) => apiPost<ChartOfAccount>("/accounts", body);
export const updateAccount = (id: string, body: SaveAccount) =>
  apiPut<ChartOfAccount>(`/accounts/${id}`, body);
export const archiveAccount = (id: string) => apiPost<ChartOfAccount>(`/accounts/${id}/archive`);
export const restoreAccount = (id: string) => apiPost<ChartOfAccount>(`/accounts/${id}/restore`);


// ---- Xero ----

// Whether the org can push bills at all. The claims dashboard asks so it can
// say "connect Xero" instead of offering a sync button that can only fail.
export type XeroStatus = {
  connected: boolean;
  /** The Xero organisation, e.g. "AltomateHR-V2". Null until a connection exists. */
  tenantName: string | null;
  tenantId: string | null;
  connectedAt: string | null;
  /**
   * Still connected on paper, but the stored tokens are unusable and only a
   * fresh consent fixes it. Set so the card can prompt straight away instead of
   * looking healthy until the next sync fails with a 409.
   */
  needsReconnect?: boolean;
};

export const getXeroStatus = () => apiGet<XeroStatus>("/xero/status");

// The currencies the connected Xero org is subscribed to. Empty when Xero is
// not connected — which the settings form reads as "we cannot constrain the
// choice", not "no currency is allowed".
export type XeroCurrency = { code: string; description: string };

export const getXeroCurrencies = () => apiGet<XeroCurrency[]>("/xero/currencies");

// Starts the OAuth handshake. The backend records the state and hands back the
// Xero URL to send the admin to; Xero returns them to /xero/callback, which
// redirects back into the app.
export const getXeroConnectUrl = (returnUrl?: string) =>
  apiPost<{ url: string }>(
    `/xero/connect-url${returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : ""}`,
    {},
  );

export const disconnectXero = () => apiPost<void>("/xero/disconnect", {});

// Pulls Xero's chart of accounts in. While Xero is connected this is the only
// way accounts get created — the backend refuses hand-made ones, because an
// account with no Xero counterpart cannot carry a valid code onto a bill.
export type XeroSyncAccountsResult = { imported: number; updated: number; skipped: number };

export const syncXeroAccounts = () =>
  apiPost<XeroSyncAccountsResult>("/xero/sync-accounts");
