// Prefers a real name when one is known — "Evan Employee" reads as EE, where
// the email local part would give EM.
export function buildInitials(email: string, name?: string | null) {
  const source = name?.trim() ? name.trim() : email.split("@")[0];
  return (
    source
      .split(/[\s._-]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join("") || "EH"
  );
}

// How a person is labelled everywhere: their real name from the directory,
// else their email, else a neutral word.
//
// This replaced `buildName`, which invented a name from the email address —
// "aisha.rahman@…" became "Aisha Rahman", "admin@…" became a person called
// "Admin", "oscar123@…" became "Oscar123". It looked like data and wasn't:
// the real name was on the user record the whole time and never read. The
// email fallback is shown as-is, because a raw address is at least true.
export function personName(
  name: string | null | undefined,
  email: string | null | undefined,
  fallback = "Employee",
) {
  return name?.trim() || email?.trim() || fallback;
}

// The claim figures on the employee dashboard.
//
// Null until the first read lands, so the cards show "—" rather than a
// confident zero: "0 awaiting review" and "we haven't looked yet" are
// different answers, and only one of them is safe to act on.
export type ClaimSummary = {
  awaiting: number;
  approved: number;
  rejected: number;
  // Approved claims submitted this calendar year. Reckoned on submittedAt to
  // match the admin overview and the payroll reimbursement run — an employee's
  // "this year" agreeing with the admin's is worth more than picking the
  // theoretically nicer date.
  approvedTotalYtd: number;
  // Claims are always entered in the org's own currency, so the first one
  // speaks for all of them. Falls back to MYR when there are none to read.
  currency: string;
};

export function summariseClaims(
  claims: { status: string; amount: number; currency: string; submittedAt: string }[] | undefined,
): ClaimSummary | null {
  if (!claims) return null;

  const year = new Date().getFullYear();
  const summary: ClaimSummary = {
    awaiting: 0,
    approved: 0,
    rejected: 0,
    approvedTotalYtd: 0,
    currency: claims[0]?.currency ?? "MYR",
  };

  for (const claim of claims) {
    switch (claim.status) {
      // SUBMITTED and PENDING both mean nobody has decided yet — they are one
      // queue to the person waiting, whatever the distinction is internally.
      case "SUBMITTED":
      case "PENDING":
        summary.awaiting += 1;
        break;
      case "APPROVED": {
        summary.approved += 1;
        const submitted = new Date(claim.submittedAt);
        if (!Number.isNaN(submitted.getTime()) && submitted.getFullYear() === year) {
          summary.approvedTotalYtd += claim.amount;
        }
        break;
      }
      case "REJECTED":
        summary.rejected += 1;
        break;
    }
  }

  return summary;
}
