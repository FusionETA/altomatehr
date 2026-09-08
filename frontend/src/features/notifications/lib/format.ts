const rtf = new Intl.RelativeTimeFormat("en", { numeric: "auto" });

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ["year", 60 * 60 * 24 * 365],
  ["month", 60 * 60 * 24 * 30],
  ["week", 60 * 60 * 24 * 7],
  ["day", 60 * 60 * 24],
  ["hour", 60 * 60],
  ["minute", 60],
];

// "3m ago", "2h ago", "just now" — the backend sends a UTC ISO timestamp
// with no "Z" suffix (System.Text.Json's default DateTime format), so this
// treats a bare timestamp as UTC rather than letting the browser assume local.
export function timeAgo(iso: string): string {
  const utcIso = iso.endsWith("Z") ? iso : `${iso}Z`;
  const seconds = (Date.now() - new Date(utcIso).getTime()) / 1000;
  if (seconds < 30) return "just now";

  for (const [unit, secondsInUnit] of UNITS) {
    const value = Math.floor(seconds / secondsInUnit);
    if (value >= 1) return rtf.format(-value, unit);
  }
  return "just now";
}
