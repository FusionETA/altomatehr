export function parseYmd(ymd: string) {
  const [y, m, d] = ymd.split("-").map(Number);
  return new Date(y, (m ?? 1) - 1, d ?? 1);
}

export function toYmd(date: Date) {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

// Inclusive list of yyyy-MM-dd dates from `start` to `end`, capped so a very
// long leave (e.g. maternity) doesn't fire dozens of requests.
export function eachDateInRange(start: string, end: string, maxDays = 31) {
  const dates: string[] = [];
  const cursor = parseYmd(start);
  const last = parseYmd(end);
  while (cursor <= last && dates.length < maxDays) {
    dates.push(toYmd(cursor));
    cursor.setDate(cursor.getDate() + 1);
  }
  return dates;
}

export function formatDate(ymd: string) {
  return new Intl.DateTimeFormat("en-MY", { day: "2-digit", month: "short", year: "numeric" }).format(
    parseYmd(ymd),
  );
}

export function formatDateRange(start: string, end: string) {
  return start === end ? formatDate(start) : `${formatDate(start)} – ${formatDate(end)}`;
}

function startOfToday() {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

// Whole days between today and `ymd` — negative if `ymd` is in the past.
export function daysFromToday(ymd: string) {
  return Math.round((parseYmd(ymd).getTime() - startOfToday().getTime()) / 86_400_000);
}

// "Submitted 3d ago" / "Submitted today" phrasing for an ISO timestamp.
export function relativeDaysAgo(isoTimestamp: string) {
  const days = Math.floor((Date.now() - new Date(isoTimestamp).getTime()) / 86_400_000);
  if (days <= 0) return "today";
  if (days === 1) return "1 day ago";
  return `${days} days ago`;
}

// Urgency label for a pending request's start date — null once it's more than
// 3 days out (not worth flagging yet).
export function urgencyLabel(startDateYmd: string): string | null {
  const days = daysFromToday(startDateYmd);
  if (days < 0) return "In progress";
  if (days === 0) return "Starts today";
  if (days === 1) return "Starts tomorrow";
  if (days <= 3) return `Starts in ${days}d`;
  return null;
}
