import { apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

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
