export type RangeKey = "24h" | "7d" | "30d";

export const RANGES: { key: RangeKey; label: string; hours: number }[] = [
  { key: "24h", label: "24 hours", hours: 24 },
  { key: "7d", label: "7 days", hours: 24 * 7 },
  // The whole retention window — older rows are already gone.
  { key: "30d", label: "30 days", hours: 24 * 30 },
];

export function rangeBounds(key: RangeKey, now = new Date()) {
  const hours = RANGES.find((r) => r.key === key)?.hours ?? 24;
  return {
    from: new Date(now.getTime() - hours * 3_600_000).toISOString(),
    to: now.toISOString(),
  };
}

export const count = (n: number) => n.toLocaleString("en-MY");

export function ms(n: number) {
  return n >= 1000 ? `${(n / 1000).toFixed(n >= 10_000 ? 0 : 1)} s` : `${n} ms`;
}

export function percent(rate: number) {
  if (rate === 0) return "0%";
  const value = rate * 100;
  return value < 0.1 ? "<0.1%" : `${value.toFixed(value < 10 ? 1 : 0)}%`;
}

// The backend stores UTC but sends it without a zone ("2026-10-04T03:12:45"),
// which the browser would read as local time — 8 hours early in Malaysia.
export function when(iso: string) {
  const utc = /(Z|[+-]\d{2}:\d{2})$/.test(iso) ? iso : `${iso}Z`;
  return new Date(utc).toLocaleString("en-MY", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}
