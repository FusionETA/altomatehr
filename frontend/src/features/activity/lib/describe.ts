// Turns the activity vocabulary — page ids, data-track ids, method + route
// templates — into words for the Support tab. Generic on purpose: a new screen
// or endpoint reads sensibly the day it ships, with no list to keep in step.
// The raw name is always shown beside it, so nothing is lost to a bad guess.

// "manage-employee" → "Manage employee", "payslipPdf" → "Payslip pdf"
export function humanize(id: string): string {
  const words = id
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[-_]+/g, " ")
    .trim()
    .toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

// "payroll/runs" → "Payroll › Runs"
export function describePage(name: string): string {
  return name.split("/").filter(Boolean).map(humanize).join(" › ");
}

// "payslips.export" → "Payslips: Export"
export function describeClick(name: string): string {
  const [area, ...rest] = name.split(".");
  return rest.length === 0 ? humanize(area) : `${humanize(area)}: ${rest.map(humanize).join(" ")}`;
}

// Last segments that name a thing done rather than a collection — so
// "POST auth/change-password" reads "Change password", while "POST claims"
// reads "Create" claims.
const ACTION_WORDS = new Set([
  "approve", "reject", "submit", "generate", "revert", "cancel", "send", "sync", "export", "import",
  "upload", "connect", "disconnect", "enter", "exit", "confirm", "archive", "restore", "settle",
  "withdraw", "resend", "reset", "verify", "revoke", "pause", "resume", "skip", "replan", "deactivate",
  "reactivate", "unblock", "unlock", "publish", "recalculate", "finalize", "finalise", "lock", "close",
  "reopen", "clock-in", "clock-out", "redeem", "rotate", "preview", "download", "email", "test",
]);

// "POST claims/{id}/approve" → { verb: "Approve", where: "Claims" }
// "DELETE leave/{id}"        → { verb: "Delete",  where: "Leave" }
export function describeAction(name: string): { verb: string; where: string } {
  const space = name.indexOf(" ");
  const method = (space > 0 ? name.slice(0, space) : name).toUpperCase();
  const route = space > 0 ? name.slice(space + 1) : "";
  const segments = route.split("/").filter(Boolean);
  const literals = segments.filter((s) => !s.startsWith("{"));
  const last = segments.at(-1) ?? "";
  const lastIsLiteral = last !== "" && !last.startsWith("{");

  // A literal after an id ("claims/{id}/approve"), or a known action word /
  // hyphenated phrase ("auth/change-password"), is the action itself.
  const hasParam = segments.some((s) => s.startsWith("{"));
  const lastIsAction =
    lastIsLiteral &&
    literals.length > 1 &&
    (hasParam || ACTION_WORDS.has(last.toLowerCase()) || last.includes("-"));

  if (lastIsAction) {
    return { verb: humanize(last), where: literals.slice(0, -1).map(humanize).join(" › ") };
  }

  const verb =
    method === "POST" ? "Create" : method === "DELETE" ? "Delete" : method === "GET" ? "Load" : "Update";
  return { verb, where: literals.map(humanize).join(" › ") };
}

// 40_000 → "40 s", 180_000 → "3 min", 3_900_000 → "1 h 5 min"
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return "";
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${Math.max(seconds, 1)} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}

// Read as UTC even if the server leaves off the trailing Z.
export function utc(iso: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`);
}

export function formatTime(iso: string): string {
  return utc(iso).toLocaleTimeString("en-MY", { hour: "numeric", minute: "2-digit", second: "2-digit" });
}

export function formatDateTime(iso: string): string {
  return utc(iso).toLocaleString("en-MY", {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "numeric",
    minute: "2-digit",
  });
}

// How long the sign-in has lasted: to sign-out, or to the last sign of life.
export function sessionLength(startedAt: string, lastSeenAt: string, endedAt: string | null): number {
  return Math.max(0, utc(endedAt ?? lastSeenAt).getTime() - utc(startedAt).getTime());
}

// "1 page · 3 clicks · 0 actions"
export function counts(pages: number, clicks: number, actions: number): string {
  const n = (count: number, word: string) => `${count} ${word}${count === 1 ? "" : "s"}`;
  return [n(pages, "page"), n(clicks, "click"), n(actions, "action")].join(" · ");
}

export const VIA_OPTIONS = [
  { value: "password", label: "Password" },
  { value: "Altomate SSO", label: "Altomate SSO" },
  { value: "resumed", label: "Resumed (signed in before tracking)" },
] as const;

export function viaLabel(via: string): string {
  return via === "resumed" ? "resumed session" : via;
}
