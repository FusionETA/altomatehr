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
  tenantName: string | null;
};

export const getXeroStatus = () => apiGet<XeroStatus>("/xero/status");
