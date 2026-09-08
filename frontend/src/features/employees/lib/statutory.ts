import type { SocsoScheme } from "../api";

/**
 * Whole-years age from an ISO date string. Returns 0 when missing/invalid,
 * so an unknown date of birth defaults to the most lenient EPF branch
 * (under 60) rather than blocking on a field the admin hasn't filled in yet.
 */
export function calculateAge(dateOfBirth: string | null | undefined): number {
  if (!dateOfBirth) return 0;
  const dob = new Date(dateOfBirth);
  if (Number.isNaN(dob.getTime())) return 0;
  const today = new Date();
  let age = today.getUTCFullYear() - dob.getUTCFullYear();
  const monthDelta = today.getUTCMonth() - dob.getUTCMonth();
  if (monthDelta < 0 || (monthDelta === 0 && today.getUTCDate() < dob.getUTCDate())) {
    age -= 1;
  }
  return Math.max(0, age);
}

/** Accepts "Malaysian" / "Malaysia" / "MY" / "MYS" variants, case-insensitively. */
export function isMalaysianNationality(nationality: string | null | undefined): boolean {
  const v = (nationality ?? "").toLowerCase().trim();
  if (v === "") return false;
  return v === "malaysian" || v === "malaysia" || v === "my" || v === "mys";
}

/**
 * Recommend a SOCSO scheme per PERKESO's age rules.
 *
 * Malaysian citizens (or unknown nationality — the conservative default):
 *   - Age < 55  → Scheme 1 (Injury + Invalidity)
 *   - Age ≥ 60  → Scheme 2 (Employment Injury only)
 *   - Age 55-59 → null, ambiguous (depends on first-time-registrant status,
 *                 which can't be reliably detected — see
 *                 socsoSchemeNeedsManualChoice)
 *
 * Foreign workers (non-Malaysian) are always new registrants under the
 * post-2025 PERKESO expansion, so the 55-59 ambiguity doesn't apply —
 * unambiguous Scheme 1 right through to age 60.
 */
export function recommendSocsoScheme(input: {
  dateOfBirth: string | null | undefined;
  isMalaysianCitizen: boolean | null;
}): SocsoScheme | null {
  if (!input.dateOfBirth) return null;
  const age = calculateAge(input.dateOfBirth);
  if (age >= 60) return "EMPLOYMENT_INJURY_ONLY";
  if (age >= 55 && input.isMalaysianCitizen !== false) return null;
  return "EMPLOYMENT_INJURY_INVALIDITY";
}

/**
 * True in the age 55-59 window where `recommendSocsoScheme` returns null —
 * i.e. a Malaysian (or unknown nationality) employee where the
 * first-time-registrant ambiguity applies. Foreign workers in that age
 * range auto-fill cleanly, so this is false for them.
 */
export function socsoSchemeNeedsManualChoice(input: {
  dateOfBirth: string | null | undefined;
  isMalaysianCitizen: boolean | null;
}): boolean {
  if (!input.dateOfBirth) return false;
  const age = calculateAge(input.dateOfBirth);
  if (age < 55 || age >= 60) return false;
  return input.isMalaysianCitizen !== false;
}

export type EpfBranch =
  | "MALAYSIAN_UNDER_60"
  | "MALAYSIAN_CITIZEN_60_PLUS"
  | "PR_OR_PRE1998_60_PLUS"
  | "POST_1998_NON_MALAYSIAN";

/**
 * Which KWSP Third Schedule branch applies to this employee. `contributeToEpf`
 * isn't part of the decision — the caller gates the whole EPF card on it, the
 * same way the branch is still meaningful for display even when opted out.
 */
export function pickEpfBranch(input: {
  isMalaysianCitizen: boolean;
  hasPr: boolean;
  epfMemberBefore1998: boolean;
  age: number;
}): EpfBranch {
  const isPartAEligible =
    input.isMalaysianCitizen ||
    input.hasPr ||
    (!input.isMalaysianCitizen && !input.hasPr && input.epfMemberBefore1998);

  if (!isPartAEligible) return "POST_1998_NON_MALAYSIAN";
  if (input.age < 60) return "MALAYSIAN_UNDER_60";
  if (input.isMalaysianCitizen) return "MALAYSIAN_CITIZEN_60_PLUS";
  return "PR_OR_PRE1998_60_PLUS";
}

/** Formats an EPF rate fraction (0.055) as a percentage string ("5.5%"). */
export function formatEpfRate(rate: number): string {
  const pct = Math.round(rate * 1000) / 10; // one decimal place, avoids float noise
  return `${pct}%`;
}

export type EpfBranchInfo = {
  branchLabel: string;
  /** Fraction, e.g. 0.11 for 11% — matches the wire format of epfEmployeeRate. */
  employeeRate: number;
  employeeNote: string;
  employerText: string;
};

/**
 * KWSP Third Schedule rates for a branch. `monthlySalary` resolves the
 * RM 5,000 employer-rate cliff on Parts A/C; pass null when it's not known
 * (e.g. an hourly-paid employee) to show both sides of the cliff.
 */
export function epfBranchInfo(branch: EpfBranch, monthlySalary: number | null): EpfBranchInfo {
  const wage = monthlySalary ?? 0;
  const atOrBelowCliff = wage > 0 && wage <= 5000;
  const aboveCliff = wage > 5000;
  const standardNote = "Statutory rate for this employee's branch.";

  switch (branch) {
    case "POST_1998_NON_MALAYSIAN":
      return {
        branchLabel: "Foreign worker (post-1 Aug 1998) — Part F",
        employeeRate: 0.02,
        employeeNote: standardNote,
        employerText: "2% (effective Oct 2025 salary)",
      };
    case "MALAYSIAN_CITIZEN_60_PLUS":
      return {
        branchLabel: "Malaysian citizen, age 60+ — Part E",
        employeeRate: 0,
        employeeNote: standardNote,
        employerText: "4%",
      };
    case "PR_OR_PRE1998_60_PLUS":
      return {
        branchLabel: "PR or pre-1998 Non-Malaysian, age 60+ — Part C",
        employeeRate: 0.055,
        employeeNote: standardNote,
        employerText: atOrBelowCliff
          ? "6.5% (salary ≤ RM 5,000)"
          : aboveCliff
            ? "6% (salary > RM 5,000)"
            : "6.5% (≤ RM 5,000) or 6% (> RM 5,000)",
      };
    case "MALAYSIAN_UNDER_60":
      return {
        branchLabel: "Standard (under age 60) — Part A",
        employeeRate: 0.11,
        employeeNote:
          'Statutory minimum 11%. The COVID-era 9% election has ended; use "Employee voluntary" below to capture above-statutory contributions (KWSP 17A i-TOPUP).',
        employerText: atOrBelowCliff
          ? "13% (salary ≤ RM 5,000)"
          : aboveCliff
            ? "12% (salary > RM 5,000)"
            : "13% (≤ RM 5,000) or 12% (> RM 5,000)",
      };
  }
}
