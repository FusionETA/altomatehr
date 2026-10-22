import type { SectionId } from "../components/employee-profile-sections";
import { NAV_PARAM } from "@/shared/lib/use-url-nav";

// A link straight to one field of one person's record — the payroll run's
// "fields the statutory files need" list uses it, so an admin goes from
// "qwer: EPF number" to that EPF number box instead of hunting for it.
//
// A URL rather than in-app state so it opens in a new tab: the run stays open
// behind it, ready for "Re-run payroll" once the field is filled.

const EMPLOYEE = "employee";
const SECTION = "section";
const FIELD = "field";

const SECTIONS: readonly SectionId[] = ["personal", "employment", "statutory", "company", "documents"];

// Where each payroll readiness gap (backend PayrollRunReadiness) is filled.
const GAP_FIELDS: Record<string, { section: SectionId; field: string }> = {
  "Employee number": { section: "company", field: "Employee number" },
  "IC number": { section: "personal", field: "ID number" },
  "Passport number": { section: "personal", field: "ID number" },
  "EPF number (digits only)": { section: "statutory", field: "EPF number" },
};

export type EmployeeLink = { userId: string; section: SectionId | null; field: string | null };

/** The URL that opens `userId`'s record at the field a payroll gap names (or just the record). */
export function employeeFieldUrl(userId: string, gap?: string): string {
  const target = gap ? GAP_FIELDS[gap] : undefined;
  const params = new URLSearchParams({ [NAV_PARAM]: "company/manage-employee", [EMPLOYEE]: userId });
  if (target) {
    params.set(SECTION, target.section);
    params.set(FIELD, target.field);
  }
  return `${window.location.pathname}?${params.toString().replace(/%2F/g, "/")}`;
}

/** The record (and field) the URL asks to open, if any. */
export function readEmployeeLink(): EmployeeLink | null {
  const params = new URLSearchParams(window.location.search);
  const userId = params.get(EMPLOYEE);
  if (!userId) return null;
  const section = params.get(SECTION) as SectionId | null;
  return {
    userId,
    section: section && SECTIONS.includes(section) ? section : null,
    field: params.get(FIELD),
  };
}

/** Drop the link from the address bar once it has been followed, so Back to the list stays on the list. */
export function clearEmployeeLink(): void {
  const params = new URLSearchParams(window.location.search);
  params.delete(EMPLOYEE);
  params.delete(SECTION);
  params.delete(FIELD);
  const query = params.toString().replace(/%2F/g, "/");
  window.history.replaceState(
    null,
    "",
    `${window.location.pathname}${query ? `?${query}` : ""}${window.location.hash}`,
  );
}
