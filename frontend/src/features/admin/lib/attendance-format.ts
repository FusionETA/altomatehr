// Formatters and constants shared by the attendance admin tabs.
//
// Kept out of AttendanceFilterBar so that file only exports components: mixing
// components and plain values in one module breaks Fast Refresh, which turns
// every edit into a full reload.

export const ALL_FILTER = "__all__";

// "1h 30m" rather than 90 — nobody reads a working day in minutes.
export function formatMinutes(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) return "—";
  const total = Math.round(minutes);
  if (total < 60) return `${total}m`;
  const hours = Math.floor(total / 60);
  const rest = total % 60;
  return rest === 0 ? `${hours}h` : `${hours}h ${rest}m`;
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

// "1,2,3,4,5" -> "Mon-Fri", "1,3,5" -> "Mon, Wed, Fri".
//
// null means the shift never had days set, which the backend treats as Mon-Fri;
// saying so beats an em dash the reader has to go and look up.
export function formatWorkingDays(csv: string | null): string {
  const days = (csv ?? "1,2,3,4,5")
    .split(",")
    .map((part) => Number(part.trim()))
    .filter((n) => n >= 1 && n <= 7)
    .sort((a, b) => a - b);

  if (days.length === 0) return "—";
  if (days.length === 7) return "Every day";

  // Collapse to a range only when the run is unbroken, so "Mon, Wed, Fri"
  // never renders as the "Mon-Fri" it is not.
  const unbroken = days.every((day, i) => i === 0 || day === days[i - 1] + 1);
  if (unbroken && days.length > 2) {
    return `${WEEKDAYS[days[0] - 1]}-${WEEKDAYS[days[days.length - 1] - 1]}`;
  }
  return days.map((day) => WEEKDAYS[day - 1]).join(", ");
}
