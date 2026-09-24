import { useCallback, useEffect } from "react";
import { getTeamAttendanceApprovals, getTeamBreakApprovals, pendingApprovalIds } from "@/features/attendance/api";
import { getTeamClaims } from "@/features/claims/api";
import { getTeamLeave } from "@/features/leave/api";
import { getTeamOvertime } from "@/features/overtime/api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useRealtimeEvent } from "@/shared/lib/use-realtime";

export type ApprovalCounts = {
  claims: number;
  leave: number;
  /** Clock and break decisions plus overtime — one number behind one tab. */
  attendance: number;
  refresh: () => void;
};

// How much is waiting for THIS supervisor, per queue — the nav badges and the
// dashboard's review card read the same numbers from here, so they cannot
// disagree.
//
// Kept live three ways, because a badge that still says "3" after the queue
// was cleared is worse than no badge:
//   1. A decision made anywhere in the portal invalidates its module's reads
//      (api-cache), and these queries re-fetch on that (refetchOnInvalidate).
//   2. A teammate submitting something reaches the approver as a realtime
//      nudge, which re-reads.
//   3. Coming back to the tab re-reads — overtime publishes no realtime event,
//      and a laptop that slept missed whatever was sent meanwhile.
//
// The queries share their cache keys with the approval screens themselves, so
// while one is open the two read one request, not two.
export function useApprovalCounts(isSupervisor: boolean): ApprovalCounts {
  const live = { enabled: isSupervisor, refetchOnInvalidate: true };

  // Failures read as zero rather than an error: a badge has nowhere to show
  // one, and the queue screen itself will say what went wrong.
  const claims = useCachedQuery("/claims/team", () => getTeamClaims().catch(() => []), live);
  const leave = useCachedQuery("/leave/team", () => getTeamLeave().catch(() => []), live);
  const days = useCachedQuery(
    "/attendance/team",
    () => getTeamAttendanceApprovals().catch(() => []),
    live,
  );
  const breaks = useCachedQuery(
    "/attendance/team/breaks",
    () => getTeamBreakApprovals().catch(() => []),
    live,
  );
  const overtime = useCachedQuery("/overtime/team", () => getTeamOvertime().catch(() => []), live);

  const refreshClaims = claims.refresh;
  const refreshLeave = leave.refresh;
  const refreshDays = days.refresh;
  const refreshBreaks = breaks.refresh;
  const refreshOvertime = overtime.refresh;

  const refresh = useCallback(() => {
    if (!isSupervisor) return;
    refreshClaims();
    refreshLeave();
    refreshDays();
    refreshBreaks();
    refreshOvertime();
  }, [isSupervisor, refreshClaims, refreshLeave, refreshDays, refreshBreaks, refreshOvertime]);

  useRealtimeEvent(["CLAIMS", "LEAVE", "ATTENDANCE"], (event) => {
    if (!isSupervisor) return;
    if (event.scope === "CLAIMS") refreshClaims();
    else if (event.scope === "LEAVE") refreshLeave();
    else {
      refreshDays();
      refreshBreaks();
    }
  });

  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === "visible") refresh();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [refresh]);

  if (!isSupervisor) return { claims: 0, leave: 0, attendance: 0, refresh };

  return {
    // canAct, not status: the team view includes the whole team's claims, so
    // counting every pending one would advertise another step's work.
    claims: (claims.data ?? []).filter((c) => c.canAct).length,
    leave: (leave.data ?? []).filter((l) => l.status === "PENDING").length,
    attendance:
      // Decisions, not days: one shift can have a clock-in AND a clock-out
      // waiting, which is two things to review.
      (days.data ?? []).reduce((n, day) => n + pendingApprovalIds(day).length, 0) +
      (breaks.data ?? []).filter((b) => b.approvalStatus === "PENDING").length +
      // /overtime/team carries decided rows too — the queue shows history.
      (overtime.data ?? []).filter((o) => o.status === "PENDING").length,
    refresh,
  };
}
