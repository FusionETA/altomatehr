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

export type SectionId = "personal" | "employment" | "statutory" | "company" | "documents";

/** null / undefined / "" / "   " are blank; 0 and false are real answers. */
function present(value: unknown): boolean {
  if (value === null || value === undefined) return false;
  return typeof value === "string" ? value.trim().length > 0 : true;
}

// One rule per required field: when it applies, and whether it is filled.
// `label` is the field's label on the form — the same words the "Still needed
// here" banner prints, and what `Field` matches itself against to mark the box.
type Rule = {
  label: string;
  applies: (p: EmployeeProfile) => boolean;
  filled: (p: EmployeeProfile) => boolean;
};

const always = () => true;

const RULES: Record<SectionId, Rule[]> = {
  personal: [
    { label: "Gender", applies: always, filled: (p) => present(p.gender) },
    { label: "Date of birth", applies: always, filled: (p) => present(p.dateOfBirth) },
    { label: "Nationality", applies: always, filled: (p) => present(p.nationality) },
    { label: "ID type", applies: always, filled: (p) => present(p.idType) },
    { label: "ID number", applies: always, filled: (p) => present(p.idNumber) },
    { label: "Marital status", applies: always, filled: (p) => present(p.maritalStatus) },
    // Drives the PCB spouse-relief branch, so it only matters once married.
    {
      label: "Spouse working",
      applies: (p) => p.maritalStatus === "MARRIED",
      filled: (p) => p.spouseWorking !== null,
    },
  ],
  employment: [
    { label: "Join date", applies: always, filled: (p) => present(p.joinDate) },
    {
      label: "Monthly salary",
      applies: (p) => p.salaryType === "MONTHLY",
      filled: (p) => present(p.monthlySalary),
    },
    {
      label: "Hourly rate",
      applies: (p) => p.salaryType === "HOURLY",
      filled: (p) => present(p.hourlyRate),
    },
  ],
  // Only gated when they actually contribute to the scheme.
  statutory: [
    { label: "EPF number", applies: (p) => p.contributeToEpf, filled: (p) => present(p.epfNumber) },
    {
      label: "SOCSO number",
      applies: (p) => present(p.socsoScheme),
      filled: (p) => present(p.socsoNumber),
    },
  ],
  // "company" is never gated: role and policy always hold a value, and where
  // someone sits in the org is not what stops payroll from running.
  company: [],
  documents: [],
};

/** Which fields this section requires right now, given the rest of the profile. */
export function requiredFields(profile: EmployeeProfile, section: SectionId): string[] {
  return RULES[section].filter((r) => r.applies(profile)).map((r) => r.label);
}

/** What each section is still missing, in the admin's words. */
export function missingFields(profile: EmployeeProfile, section: SectionId): string[] {
  return RULES[section]
    .filter((r) => r.applies(profile) && !r.filled(profile))
    .map((r) => r.label);
}

export function isSectionComplete(profile: EmployeeProfile, section: SectionId) {
  return missingFields(profile, section).length === 0;
}

/** Every section satisfied — the same bar payroll uses to include someone in a run. */
export function isReadyForPayroll(profile: EmployeeProfile) {
  const sections: SectionId[] = ["personal", "employment", "statutory", "company"];
  return sections.every((s) => isSectionComplete(profile, s));
}
