import { apiDelete, apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

// Fusioneta support — superadmins only (SUPERADMIN_EMAILS on the server).
// Entering a company is `enterSupport` in features/auth/api.ts: it re-mints
// the session, so it lives with the rest of auth.

export type SupportOrganization = {
  id: string;
  name: string;
  plan: string;
  tier: string | null;
  addons: string[];
  ownerName: string | null;
  ownerEmail: string | null;
  employeeCount: number;
  createdAt: string;
};

export const SUPPORT_ORGS_PATH = "/support/organizations";

// Every company. Searched in the browser: the list is small and a keystroke
// should not be a round trip.
export const getSupportOrganizations = () => apiGet<SupportOrganization[]>(SUPPORT_ORGS_PATH);

export type CreateSupportCompany = {
  orgName: string;
  ownerName: string;
  ownerEmail: string;
  // Only for an email with no account yet; an existing account keeps its own.
  password?: string;
  plan: Plan;
  tier: Tier | null;
  claims: boolean;
  attendance: boolean;
};

export type CreateSupportCompanyResult = {
  organizationId: string;
  organizationName: string;
  // False when the email already had an account — its own password applies.
  ownerCreated: boolean;
};

export const createSupportCompany = (body: CreateSupportCompany) =>
  apiPost<CreateSupportCompanyResult>(SUPPORT_ORGS_PATH, body);

export type Plan = "DIY" | "EXPERT";
export type Tier = "FREE" | "PAID";

// The paid add-ons and the modules they unlock (OrgModules.AddonToModules).
export const ADDONS = [
  { key: "expense_claim", label: "Claims" },
  { key: "clock", label: "Attendance" },
] as const;

// A company's package: plan, tier (DIY only) and add-ons. DIY Free never
// unlocks an add-on, whatever is ticked.
export const updateOrganizationPlan = (
  organizationId: string,
  body: { plan: Plan; tier: Tier | null; addons: string[] },
) => apiPut<unknown>(`/organizations/${organizationId}/plan`, body);

// ---- Xero apps (superadmin) ----
// Which Xero app each owner's companies connect through. A secret is sent in
// and never comes back; a client id only by its last 4 characters.

export type XeroAppChoice = "Shared" | "Original" | "Standalone";

export type XeroAppCompany = {
  id: string;
  name: string;
  owners: { userId: string; name: string }[];
  connectedTenantName: string | null;
  /** "original" or an app id. Null when not connected. */
  connectedVia: string | null;
  needsReconnect: boolean;
  reconnectToMoveApp: boolean;
};

export type XeroOwnerGroup = {
  userId: string;
  name: string;
  email: string;
  choice: XeroAppChoice;
  xeroAppId: string | null;
  companies: XeroAppCompany[];
};

export type XeroAppSummary = {
  id: string;
  name: string;
  clientIdEnding: string;
  isShared: boolean;
  owners: number;
  connections: number;
  updatedAt: string;
};

export type XeroAppsOverview = {
  redirectUri: string;
  originalApp: { configured: boolean; clientIdEnding: string | null; isShared: boolean; connections: number };
  apps: XeroAppSummary[];
  owners: XeroOwnerGroup[];
  companiesWithoutOwner: XeroAppCompany[];
};

/** On an edit, a blank client id or secret keeps the saved one. */
export type SaveXeroApp = { name: string; clientId?: string; clientSecret?: string; isShared: boolean };

export const XERO_APPS_PATH = "/support/xero";
export const getXeroAppsOverview = () => apiGet<XeroAppsOverview>(XERO_APPS_PATH);
export const addXeroApp = (body: SaveXeroApp) => apiPost<XeroAppSummary>(`${XERO_APPS_PATH}/apps`, body);
export const updateXeroApp = (id: string, body: SaveXeroApp) =>
  apiPut<XeroAppSummary>(`${XERO_APPS_PATH}/apps/${id}`, body);
export const deleteXeroApp = (id: string) => apiDelete<unknown>(`${XERO_APPS_PATH}/apps/${id}`);
export const setXeroOwnerChoice = (userId: string, body: { choice: XeroAppChoice; xeroAppId: string | null }) =>
  apiPut<unknown>(`${XERO_APPS_PATH}/owners/${userId}`, body);
export const setXeroCompanyOwner = (organizationId: string, ownerUserId: string) =>
  apiPut<unknown>(`${XERO_APPS_PATH}/companies/${organizationId}/owner`, { ownerUserId });
