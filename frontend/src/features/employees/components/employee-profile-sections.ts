import type { EmployeeProfile } from "../api";

// What "ready" means for one employee, section by section.
//
// These are the REQUIRED fields — the ones payroll genuinely can't run
// without — not every field on the form. That distinction is the whole point:
// counting all 67 fields made a profile look half-done because nobody had
// typed an alternate email, while a missing NRIC (which actually blocks the
// LHDN filing) got the same weight.
//
// The rules mirror the previous system's readiness checks, so the badge here
// and payroll's own view of the same employee can't disagree:
//   - identity fields LHDN e-filing and the SOCSO/EIS file need
//   - a salary figure for whichever basis the person is on, and a join date
//     (first/last month proration)
//   - the statutory numbers, but only for the schemes they actually contribute to
//
// Deliberately NOT required, matching the previous system: address (foreign
// workers and new joiners often have none captured yet), income tax number
// (LHDN issues the TIN later), and bank details (needed to disburse, not to
// calculate).

export type SectionId = "personal" | "employment" | "statutory" | "company";

/** null / undefined / "" / "   " are blank; 0 and false are real answers. */
function present(value: unknown): boolean {
  if (value === null || value === undefined) return false;
  return typeof value === "string" ? value.trim().length > 0 : true;
}

/** What each section is still missing, in the admin's words. */
export function missingFields(profile: EmployeeProfile, section: SectionId): string[] {
  const gaps: string[] = [];

  if (section === "personal") {
    if (!present(profile.gender)) gaps.push("Gender");
    if (!present(profile.dateOfBirth)) gaps.push("Date of birth");
    if (!present(profile.nationality)) gaps.push("Nationality");
    if (!present(profile.idType)) gaps.push("ID type");
    if (!present(profile.idNumber)) gaps.push("ID number");
    if (!present(profile.maritalStatus)) gaps.push("Marital status");
    // Drives the PCB spouse-relief branch, so it only matters once married.
    if (profile.maritalStatus === "MARRIED" && profile.spouseWorking === null)
      gaps.push("Spouse working");
  }

  if (section === "employment") {
    if (!present(profile.joinDate)) gaps.push("Join date");
    const basis = profile.salaryType;
    if (basis === "MONTHLY" && !present(profile.monthlySalary)) gaps.push("Monthly salary");
    if (basis === "HOURLY" && !present(profile.hourlyRate)) gaps.push("Hourly rate");
  }

  if (section === "statutory") {
    // Only gated when they actually contribute to the scheme.
    if (profile.contributeToEpf && !present(profile.epfNumber)) gaps.push("EPF number");
    if (present(profile.socsoScheme) && !present(profile.socsoNumber)) gaps.push("SOCSO number");
  }

  // "company" is never gated: role and policy always hold a value, and where
  // someone sits in the org is not what stops payroll from running.

  return gaps;
}

export function isSectionComplete(profile: EmployeeProfile, section: SectionId) {
  return missingFields(profile, section).length === 0;
}

/** Every section satisfied — the same bar payroll uses to include someone in a run. */
export function isReadyForPayroll(profile: EmployeeProfile) {
  const sections: SectionId[] = ["personal", "employment", "statutory", "company"];
  return sections.every((s) => isSectionComplete(profile, s));
}
