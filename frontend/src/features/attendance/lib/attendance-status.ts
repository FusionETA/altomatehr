import type { AttendanceRecord, AttendanceStatus } from "../api";

/**
 * The status a finished day should actually badge.
 *
 * NOT `record.status`. That column is a roll-up: `RecomputeRollupAsync`
 * overwrites it with `CLOCKED_OUT` the moment the day's last session closes,
 * which throws away whether the employee was on time or late. Every completed
 * day therefore badges "Clocked out" — a four-hour-late day included. The
 * production app carries the same warning on its own roll-up and derives the
 * badge from the sessions instead.
 *
 * We cannot copy production's version of the fix. It reads
 * `sessions.some(s => s.status === "LATE")`, but our `AttendanceService` sets
 * `session.Status = CLOCKED_OUT` on clock-out too, so our sessions lose the
 * outcome exactly like the record does. What survives on both is `lateByMin`,
 * so that is what this reads.
 *
 * Deliberately the day's FIRST arrival rather than production's "any session
 * that started late". `RecomputeRollupAsync` sets `LateByMin` from the first
 * session on purpose — "an afternoon session starting at 14:00 is not five
 * hours late against a 09:00 shift" — and honouring that keeps this badge
 * agreeing with the activity heatmap and the Analytics late count, which both
 * already read `lateByMin`.
 */
export function displayStatus(
  record: Pick<AttendanceRecord, "status" | "timeIn" | "lateByMin">,
): AttendanceStatus {
  if (record.status === "ON_LEAVE") return "ON_LEAVE";
  // Still on the clock, where "clocked in" is the answer the reader wants.
  if (record.status === "CLOCKED_IN") return "CLOCKED_IN";
  if (!record.timeIn) return "MISSING";
  return (record.lateByMin ?? 0) > 0 ? "LATE" : "ON_TIME";
}
