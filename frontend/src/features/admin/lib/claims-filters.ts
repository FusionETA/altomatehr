import type { Claim, ClaimsExportFilters } from "@/features/claims/api";
import { claimDateKey, type ClaimDateBasis } from "@/features/claims/lib/claim-insights";
import { claimMatchesStatus, type ClaimStatusFilter } from "@/features/claims/lib/claim-status";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";

// The claims table's filter set, kept in one place so the rows on screen and
// the rows in the export are derived from the same values. Every field here
// except `search` maps to a real query parameter on /claims/export/summary —
// that is the rule for what earns a control: a filter the export cannot
// express would quietly hand payroll more than the admin was looking at.

export const ALL = "ALL";

export type ClaimsFilters = {
  search: string;
  status: ClaimStatusFilter;
  projectId: string;
  employeeId: string;
  paymentType: string;
  // Inclusive, both optional, as yyyy-MM-dd.
  from: string;
  to: string;
  dateBasis: ClaimDateBasis;
};

export const EMPTY_FILTERS: ClaimsFilters = {
  search: "",
  status: "ALL",
  projectId: ALL,
  employeeId: ALL,
  paymentType: ALL,
  from: "",
  to: "",
  // Spend date by default, matching the backend export's own default.
  dateBasis: "spent",
};

// dateBasis is excluded on purpose: it changes which date is filtered, not
// whether anything is filtered, so on its own it should not light up "Clear".
export function hasAnyFilter(filters: ClaimsFilters) {
  return (
    filters.search.trim().length > 0 ||
    filters.status !== "ALL" ||
    filters.projectId !== ALL ||
    filters.employeeId !== ALL ||
    filters.paymentType !== ALL ||
    filters.from.length > 0 ||
    filters.to.length > 0
  );
}

export function matchesFilters(
  claim: Claim,
  filters: ClaimsFilters,
  labels: { projectName: (claim: Claim) => string; employeeEmail: (claim: Claim) => string },
) {
  if (!claimMatchesStatus(claim, filters.status)) return false;
  if (filters.projectId !== ALL && (claim.projectId ?? "") !== filters.projectId) return false;
  if (filters.employeeId !== ALL && claim.employeeId !== filters.employeeId) return false;
  if (filters.paymentType !== ALL && claim.paymentType !== filters.paymentType) return false;

  if (filters.from || filters.to) {
    const key = claimDateKey(claim, filters.dateBasis);
    if (!key) return false;
    // String compare on yyyy-MM-dd is a date compare, and inclusive at both ends.
    if (filters.from && key < filters.from) return false;
    if (filters.to && key > filters.to) return false;
  }

  const query = filters.search.trim().toLowerCase();
  if (query.length === 0) return true;

  const email = labels.employeeEmail(claim);
  return [
    claim.claimNumber,
    claim.title,
    email ? buildName(email) : claim.employeeId,
    email,
    claim.category,
    labels.projectName(claim),
  ]
    .filter(Boolean)
    .join(" ")
    .toLowerCase()
    .includes(query);
}

// The API-expressible half of the filters. `search` has no server equivalent,
// which is why the UI says so rather than pretending the export is narrower
// than it is.
export function toExportFilters(filters: ClaimsFilters): ClaimsExportFilters {
  return {
    // A "Pending" filter also covers SUBMITTED in the UI; the export takes a
    // single status, so it exports the PENDING ones.
    status: filters.status === "ALL" ? undefined : filters.status,
    projectId: filters.projectId === ALL ? undefined : filters.projectId,
    employeeId: filters.employeeId === ALL ? undefined : filters.employeeId,
    paymentType:
      filters.paymentType === ALL ? undefined : (filters.paymentType as "PERSONAL" | "COMPANY"),
    from: filters.from || undefined,
    to: filters.to || undefined,
    dateField: filters.dateBasis,
  };
}

// One sentence describing what an export would actually contain.
export function describeFilters(
  filters: ClaimsFilters,
  projectNames: Map<string, string>,
  employeeEmails: Map<string, string>,
) {
  const parts: string[] = [];

  if (filters.status !== "ALL") parts.push(`${filters.status.toLowerCase()} claims`);
  if (filters.projectId !== ALL) parts.push(projectNames.get(filters.projectId) ?? "one project");
  if (filters.employeeId !== ALL) {
    const email = employeeEmails.get(filters.employeeId);
    parts.push(email ? buildName(email) : "one employee");
  }
  if (filters.paymentType !== ALL) {
    parts.push(filters.paymentType === "PERSONAL" ? "paid personally" : "paid by the company");
  }
  if (filters.from && filters.to) parts.push(`${filters.from} to ${filters.to}`);
  else if (filters.from) parts.push(`from ${filters.from}`);
  else if (filters.to) parts.push(`up to ${filters.to}`);

  const base = parts.length > 0 ? `Filtered to ${parts.join(" · ")}` : "Every claim in the org";

  // The search box narrows the table but has no server equivalent, so an export
  // taken while searching is wider than what is on screen. Say so.
  return filters.search.trim().length > 0
    ? `${base} — the search term isn't part of the export`
    : base;
}
