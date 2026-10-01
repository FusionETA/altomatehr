import { WHATS_NEW } from "./entries";

// Which release a person has already read, so the menu can show a dot for the
// rest. Kept in the browser per signed-in email: it's a convenience, not a
// record — a cleared browser just shows the dot once more.

const KEY_PREFIX = "whats-new-seen:";

export const latestReleaseDate: string | null = WHATS_NEW[0]?.date ?? null;

export function readSeen(email: string): string | null {
  try {
    return window.localStorage.getItem(KEY_PREFIX + email.toLowerCase());
  } catch {
    return null;
  }
}

export function markSeen(email: string): void {
  if (!latestReleaseDate) return;
  try {
    window.localStorage.setItem(KEY_PREFIX + email.toLowerCase(), latestReleaseDate);
  } catch {
    // Private window or blocked storage: the dot just comes back next visit.
  }
}

/** yyyy-MM-dd compares correctly as a string. */
export function hasUnseen(seen: string | null): boolean {
  return latestReleaseDate != null && (seen == null || seen < latestReleaseDate);
}
