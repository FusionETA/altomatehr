/**
 * How many calendar days after `from` the `to` instant lands, as a short
 * suffix ("+1d"), or "" when they share a day.
 *
 * A night shift ends on the next calendar date, and a column that prints the
 * time alone then reads as clocking out before clocking in — 04:33 PM followed
 * by 10:47 AM. Showing the offset rather than the whole date keeps the column
 * narrow enough to stay in a table.
 *
 * Compared on LOCAL date parts because the times beside it are rendered with
 * `toLocaleTimeString`; comparing UTC days would disagree with what is on
 * screen for any shift crossing midnight in the viewer's zone but not in UTC.
 */
export function dayOffsetLabel(from: string | null, to: string | null): string {
  if (!from || !to) return "";
  const a = new Date(from);
  const b = new Date(to);
  if (Number.isNaN(a.getTime()) || Number.isNaN(b.getTime())) return "";
  const days = Math.round(
    (Date.UTC(b.getFullYear(), b.getMonth(), b.getDate()) -
      Date.UTC(a.getFullYear(), a.getMonth(), a.getDate())) /
      86_400_000,
  );
  return days > 0 ? `+${days}d` : "";
}
