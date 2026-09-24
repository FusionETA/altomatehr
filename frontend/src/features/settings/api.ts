import { apiDelete, apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

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
  lunchBreakMinutes: number;
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
  // Org-wide default work schedule (a project's own schedule overrides it).
  workingHoursStart: string;   // "HH:mm"
  workingHoursEnd: string;     // "HH:mm"
  workingDays: string | null;  // CSV ISO weekdays; null/blank = Mon-Fri
  lunchBreakMinutes: number;
};

export type Project = {
  id: string;
  name: string;
  /** Street address of the site. Free text, shown to people — never parsed. */
  location: string | null;
  latitude: number | null;
  longitude: number | null;
  /** Comma-separated IPs employees must clock in from when their policy has
   *  "Require IP allowlist" on. Null / empty means the check is skipped. */
  allowedIps: string | null;
  /** The geofenced sites. Order is behaviour, not presentation: the check
   *  walks them in order and the first one inside the radius wins. Empty
   *  means the single latitude/longitude above is used instead. */
  geofencePoints: GeofencePoint[];
  /** Labelled allowlist entries, each an IPv4 address or CIDR range. Order
   *  carries no meaning — matching asks whether ANY entry covers the address.
   *  Empty means the legacy allowedIps string above is used instead. */
  allowedIpEntries: AllowedIpEntry[];
  /** Counts for the settings grid. The list endpoint returns these instead of
   *  the rows above, which it leaves empty. */
  geofenceSiteCount: number;
  allowedIpCount: number;
  /** Origin markers when the project was synced from Xero (read-only). One or
   *  the other is set, never both: xeroProjectId for Xero's Projects product,
   *  xeroTrackingOptionId for an option on a tracking category. */
  xeroProjectId: string | null;
  xeroTrackingOptionId: string | null;
  /** Synced from a Xero tracking category that no longer holds projects. Kept
   *  (past claims and shifts point at it) but not offered where a project is
   *  picked. */
  hiddenByTrackingCategory: boolean;
  xeroStatus: string | null;
  xeroSyncedAt: string | null;
  /** Work schedule. Times are local "HH:mm"; workingDays is a CSV of ISO
   *  weekday numbers (1 = Monday … 7 = Sunday), e.g. "1,2,3,4,5". */
  workingHoursStart: string | null;
  workingHoursEnd: string | null;
  workingDays: string | null;
  lunchBreakMinutes: number;
  isArchived: boolean;
  createdAt: string;
};
export type GeofencePoint = {
  id: string;
  label: string;
  latitude: number;
  longitude: number;
};

export type AllowedIpEntry = {
  id: string;
  label: string;
  /** An IPv4 address (treated as /32) or an IPv4 CIDR range. */
  cidr: string;
};

export type SaveProject = {
  name: string;
  location?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  allowedIps?: string | null;
  workingHoursStart?: string | null;
  workingHoursEnd?: string | null;
  workingDays?: string | null;
  lunchBreakMinutes?: number;
  /** Replace-all, both of them: what is sent IS the list afterwards, and an
   *  empty array clears it. */
  geofencePoints?: { label: string; latitude: number; longitude: number }[];
  allowedIpEntries?: { label: string; cidr: string }[];
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

// The org's fields are edited across several screens (currency on Claims,
// mileage on Accounts, geofence on Projects, schedule on Work Schedule), but the
// endpoint is a full replace. So every screen loads the whole org and writes it
// back with only its slice changed — this turns the loaded org into that payload
// so a save can't reset a field another screen owns.
export const orgToUpdate = (
  org: Organization,
  overrides: Partial<UpdateOrganization> = {},
): UpdateOrganization => ({
  name: org.name,
  defaultCurrency: org.defaultCurrency,
  defaultMileageRate: org.defaultMileageRate,
  mileageUnit: org.mileageUnit,
  geofenceRadiusMeters: org.geofenceRadiusMeters,
  workingHoursStart: org.workingHoursStart ?? "09:00",
  workingHoursEnd: org.workingHoursEnd ?? "18:00",
  workingDays: org.workingDays,
  lunchBreakMinutes: org.lunchBreakMinutes,
  ...overrides,
});

// Create a new company; the caller becomes its Owner. Admins and Owners can.
export const createOrganization = (name: string) =>
  apiPost<Organization>("/organizations", { name });

// --- Admin access control (Owner only) ---
export type AdminAccess = {
  userId: string;
  name: string;
  email: string;
  role: string;
  // null = full access (everything the plan enables); a list narrows the admin
  // to those modules; an empty list locks them out.
  modules: string[] | null;
};
export const getAdmins = () => apiGet<AdminAccess[]>("/organizations/admins");
export const setAdminAccess = (userId: string, modules: string[] | null) =>
  apiPut<AdminAccess>(`/organizations/admins/${userId}/access`, { modules });

// Every grantable module key, plus the caller's own effective enabled set.
export type ModuleAccess = { all: string[]; enabled: string[] };
export const getModuleAccess = () => apiGet<ModuleAccess>("/organizations/modules");

// --- Public holidays ---
//
// Days that don't count as working days for leave/attendance. A holiday with a
// projectId is observed only by that project; projectId === null is org-wide.
// The Work Schedule settings screen manages the org-wide ones.
export type Holiday = {
  id: string;
  projectId: string | null;
  date: string; // yyyy-MM-dd
  name: string;
};
export const getHolidays = () => apiGet<Holiday[]>("/holidays");
export const createHoliday = (body: { date: string; name: string; projectId?: string | null }) =>
  apiPost<Holiday>("/holidays", body);
export const deleteHoliday = (id: string) => apiDelete<void>(`/holidays/${id}`);

// Pull a country's calendar for one year. `skipped` counts dates the org
// already had — a re-run after a calendar revision leaves those alone rather
// than overwriting a name an admin edited.
export type HolidayImportResult = {
  imported: number;
  skipped: number;
  source: string | null;
};

export const importHolidays = (year: number, countryCode: string) =>
  apiPost<HolidayImportResult>("/holidays/import", { year, countryCode });

// --- Projects ---
export const getProjects = () => apiGet<Project[]>("/projects");

// One project, WITH its geofence sites and allowlist entries — the list above
// omits both, since the grid shows names and would otherwise pay a query per
// project for rows it never draws.
export const getProject = (id: string) => apiGet<Project>(`/projects/${id}`);

// Only the projects the caller is on, via their team memberships. Use this for
// the clock-in picker — clocking into a project you're not on is refused, so
// offering the whole org list only invites the rejection.
export const getMyProjects = () => apiGet<Project[]>("/projects/mine");
export const createProject = (body: SaveProject) => apiPost<Project>("/projects", body);
export const updateProject = (id: string, body: SaveProject) =>
  apiPut<Project>(`/projects/${id}`, body);
// The IP the server currently sees for this admin — for the "Use my IP" button
// on the allowlist editor. Same value the clock-in IP check compares against.
export const getMyIp = () => apiGet<{ ip: string | null }>("/projects/my-ip");
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

// Pulls projects in from the connected Xero org (its projects / tracking
// options). Creates new rows and updates existing ones by their Xero id.
export type XeroSyncProjectsResult = {
  imported: number;
  updated: number;
  skipped: number;
  /** Which tracking category the projects came from, when they came from one. */
  trackingCategoryName: string | null;
  /** Xero has more than one tracking category and nobody has said which holds
   *  the projects, so the sync deliberately imported nothing. */
  needsTrackingCategoryChoice: boolean;
};

export const syncXeroProjects = () =>
  apiPost<XeroSyncProjectsResult>("/xero/sync-projects");

// Most Xero orgs model projects as options on a tracking category rather than
// with Xero's Projects product. This says which category to read.
export type XeroTrackingCategory = { id: string; name: string; optionCount: number };
export type XeroProjectTracking = {
  categories: XeroTrackingCategory[];
  selectedCategoryId: string | null;
};

export const getXeroProjectTracking = () =>
  apiGet<XeroProjectTracking>("/xero/project-tracking");

// Choosing or switching the category syncs it straight away and returns what
// the sync did; clearing it returns nothing.
export const setXeroProjectTrackingCategory = (categoryId: string | null) =>
  apiPut<XeroSyncProjectsResult | undefined>("/xero/project-tracking", { categoryId });
