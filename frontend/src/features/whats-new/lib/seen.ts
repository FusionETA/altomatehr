import { whatsNewFor, type WhatsNewAudience } from "./entries";

// Which release a person has already read, so the menu can show a dot for the
// rest. Kept in the browser per signed-in email: it's a convenience, not a
// record — a cleared browser just shows the dot once more.
//
// The latest release differs by audience (an admins-only day isn't one an
// employee sees), so each reader compares against their own.

const KEY_PREFIX = "whats-new-seen:";

export function latestReleaseDate(audience: WhatsNewAudience): string | null {
  return whatsNewFor(audience)[0]?.date ?? null;
}

export function readSeen(email: string): string | null {
  try {
    return window.localStorage.getItem(KEY_PREFIX + email.toLowerCase());
  } catch {
    return null;
  }
}

// Never moves backwards: an owner who also opens the employee view mustn't
// lose the admin release they've already read.
export function markSeen(email: string, audience: WhatsNewAudience): void {
  const latest = latestReleaseDate(audience);
  if (!latest) return;
  const seen = readSeen(email);
  if (seen != null && seen >= latest) return;
  try {
    window.localStorage.setItem(KEY_PREFIX + email.toLowerCase(), latest);
  } catch {
    // Private window or blocked storage: the dot just comes back next visit.
  }
}

/** yyyy-MM-dd compares correctly as a string. */
export function hasUnseen(seen: string | null, audience: WhatsNewAudience): boolean {
  const latest = latestReleaseDate(audience);
  return latest != null && (seen == null || seen < latest);
}
