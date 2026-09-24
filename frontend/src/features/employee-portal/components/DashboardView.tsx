import { useEffect, useState } from "react";
import {
  ArrowRight,
  CalendarDays,
  CircleDollarSign,
  ClipboardCheck,
  Clock3,
  FileCheck2,
  Fingerprint,
  TriangleAlert,
  LoaderCircle,
  LogOut,
  Plus,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import { BreakControl } from "@/features/attendance/components/BreakControl";
import {
  ClockOutDialog,
  type ClockOutChoice,
} from "@/features/attendance/components/ClockOutDialog";
import {
  OffSiteClockDialog,
  type OffSiteProof,
  type OffSiteReason,
} from "@/features/attendance/components/OffSiteClockDialog";
import {
  clockIn,
  clockOut,
  submitTimeAdjustment,
  getTodayAttendance,
  getOpenSession,
  OFF_SITE_CODE,
  LOCATION_REQUIRED_CODE,
  OPEN_SESSION_CODE,
  type AttendanceRecord,
} from "@/features/attendance/api";
import { getMyClaims } from "@/features/claims/api";
import { formatCurrency } from "@/features/claims/lib/claim-formatters";
import { NewClaimModal } from "@/features/claims/components/NewClaimModal";
import {
  getLeaveBalances,
  getLeaveTypes,
  type LeaveBalance,
  type LeaveType,
} from "@/features/leave/api";
import { ApplyLeaveModal } from "@/features/leave/components/ApplyLeaveModal";
import { getMyProjects, type Project } from "@/features/settings/api";
import { ApiError } from "@/shared/lib/api-client";
import { requestGeolocation, type Coords } from "@/shared/lib/geolocation";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import type { SignedInUser } from "@/shared/types/session";
import { summariseClaims } from "../lib/employee-formatters";
import type { EmployeeView } from "../lib/types";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { getMyPayslips } from "@/features/payslips/api";
import { rmWithUnit, shortDate } from "@/features/payroll/lib/payroll-format";
import { useApprovalCounts } from "../lib/use-approval-counts";
import { Skeleton } from "@/shared/components/Skeleton";

// The still-open session only matters here when it started on an EARLIER day —
// today's open record is just a normal shift in progress.
function staleFrom(open: AttendanceRecord | null): AttendanceRecord | null {
  if (!open?.timeIn || open.timeOut) return null;
  const started = new Date(open.date);
  const now = new Date();
  const sameDay =
    started.getFullYear() === now.getFullYear() &&
    started.getMonth() === now.getMonth() &&
    started.getDate() === now.getDate();
  return sameDay ? null : open;
}


function fmtDay(iso: string) {
  return new Date(iso).toLocaleDateString("en-US", {
    weekday: "short",
    day: "numeric",
    month: "short",
  });
}

function fmtDuration(totalMin: number) {
  const hours = Math.floor(totalMin / 60);
  const minutes = totalMin % 60;
  return hours > 0 ? `${hours}h ${String(minutes).padStart(2, "0")}m` : `${minutes}m`;
}

function fmtClock(iso?: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" });
}

// A shift left open overnight ends on a later date, and "03:45 PM – 04:26 PM"
// next to "24h 42m" reads as a bug — the two times look 41 minutes apart.
// Naming the day is what reconciles them.
function fmtShiftEnd(startIso: string, endIso?: string | null) {
  if (!endIso) return "now";
  const end = fmtClock(endIso);
  const sameDay = new Date(startIso).toDateString() === new Date(endIso).toDateString();
  if (sameDay) return end;
  return `${end} (${new Date(endIso).toLocaleDateString("en-US", {
    day: "numeric",
    month: "short",
  })})`;
}

export function DashboardView({
  user,
  onNavigate,
}: {
  user: SignedInUser;
  onNavigate: (view: EmployeeView) => void;
}) {
  const isSupervisor = user.role === "Supervisor";


  const [projects, setProjects] = useState<Project[]>([]);
  const [projectId, setProjectId] = useState("");
  const [now, setNow] = useState(() => new Date());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [leaveTypes, setLeaveTypes] = useState<LeaveType[]>([]);
  const [leaveBalances, setLeaveBalances] = useState<LeaveBalance[]>([]);
  const [newClaimOpen, setNewClaimOpen] = useState(false);
  const [applyLeaveOpen, setApplyLeaveOpen] = useState(false);
  const [clockOutOpen, setClockOutOpen] = useState(false);
  // Set when the server refuses a clock for being off-site; holds the distance
  // it reported so the dialog can show how far out we are.
  // `choice` rides along because an off-site clock-out is a RETRY, and the
  // correction the employee asked for on the first attempt has to survive it.
  // Without it the retry called runClock(undefined, proof): the clock-out
  // landed, the adjustment was silently dropped, and nothing said so.
  const [offSite, setOffSite] = useState<
    {
      action: "in" | "out";
      distance?: number;
      choice?: ClockOutChoice;
      reason: OffSiteReason;
    } | null
  >(null);
  // A shift from an earlier day that was never clocked out. While one exists the
  // server refuses a new clock-in, so the card stops offering one and asks for
  // that shift to be closed instead.


  // Live clock — the "RIGHT NOW" readout ticks every second.
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(id);
  }, []);

  // Today's attendance and any open session are cached like everything else.
  //
  // They used to be read fresh on every mount, for fear that a remembered
  // answer to "am I clocked in?" was worse than a moment's wait. It was worse
  // than that: with no loading state, both started null, so the card painted a
  // purple "Tap to Clock In" on every single visit and flipped to a red "Tap
  // to Clock Out" a round trip later. The wrong answer, stated confidently,
  // beat the wait to the screen.
  //
  // Caching is safe here for the same reason it is on the attendance screen:
  // the server is what refuses a second clock-in while a session is open, and
  // a clock-in or clock-out invalidates /attendance*, which drops both keys.
  const todayQuery = useCachedQuery("/attendance/today", getTodayAttendance);
  const openSessionQuery = useCachedQuery("/attendance/open-session", getOpenSession);
  const projectsQuery = useCachedQuery("/projects/mine", getMyProjects);
  // Newest first from the server, so the head of the list is the latest.
  const payslipsQuery = useCachedQuery("/payslips", getMyPayslips);
  const leaveTypesQuery = useCachedQuery("/leave-types", getLeaveTypes);
  // Applying from the dashboard needs the same balances the Leave screen shows,
  // or the quick action is the one place that asks people to guess.
  const leaveBalancesQuery = useCachedQuery("/leave/balances", getLeaveBalances);

  // These four figures were hardcoded — RM 4,820.00 / 3 / 8 / 12 — so every
  // employee saw the same invented numbers, including anyone who had never
  // filed a claim.
  const myClaimsQuery = useCachedQuery("/claims", getMyClaims);
  const claimSummary = summariseClaims(myClaimsQuery.data);

  const latestPayslip = (payslipsQuery.data ?? [])[0] ?? null;
  const today = todayQuery.data ?? null;
  const stale = staleFrom(openSessionQuery.data ?? null);

  // Whether this person is on the clock is not yet known — nothing cached and
  // the first read still in flight. The card renders a skeleton rather than
  // guessing, because both guesses are a button that does the wrong thing.
  const statusKnown = !todayQuery.loading && !openSessionQuery.loading;

  useEffect(() => {
    setProjects(projectsQuery.data ?? []);
  }, [projectsQuery.data]);
  useEffect(() => {
    setLeaveTypes((leaveTypesQuery.data ?? []).filter((x) => !x.isArchived));
  }, [leaveTypesQuery.data]);
  useEffect(() => {
    setLeaveBalances(leaveBalancesQuery.data ?? []);
  }, [leaveBalancesQuery.data]);

  // Follows whichever record the card is acting on. Reading only `today` left
  // the picker empty while a stale shift was open — today has no record yet, so
  // the project the employee is actually still clocked in against went missing.
  // Written out rather than using `clockTarget`, which is declared below.
  // Falls back to the first project rather than an empty picker: almost
  // everyone is on exactly one, and "Select a project..." made that person
  // choose the only answer there was — or clock in untagged by forgetting to.
  // `current` is kept ahead of the fallback so a deliberate pick survives the
  // project list arriving; the record still wins, since it's what the shift
  // was actually filed against.
  useEffect(() => {
    const onRecord = (stale ?? today)?.projectId;
    setProjectId((current) => onRecord ?? (current || projects[0]?.id) ?? "");
  }, [stale, today, projects]);

  // Supervisor review counters — the same live numbers as the nav badges
  // (useApprovalCounts), so the card and the sidebar cannot disagree. This
  // used to be its own one-off fetch on mount: stale after the first decision,
  // and counting every pending claim where the badge counted only the ones
  // this person can act on.
  const approvals = useApprovalCounts(isSupervisor);
  const claimCount = approvals.claims;
  const leaveCount = approvals.leave;
  const attendanceCount = approvals.attendance;

  // Two states, because a finished shift no longer ends the day: a day holds
  // several sessions, so clocking out at noon and back in at one is normal.
  // "OUT" therefore means "not on the clock right now", not "done".
  const state: "IN" | "OUT" = today?.timeIn && !today.timeOut ? "IN" : "OUT";

  // The day's stints. The record only reports first-start/last-end, so without
  // these a second clock-in makes the morning look like it never happened.
  const shifts = today?.sessions ?? [];
  const doneShifts = shifts.filter((s) => s.endedAt);
  // Which stint is running: "Shift 2" while the second one is open.
  const shiftNumber = state === "IN" ? shifts.length : doneShifts.length + 1;

  // The record the button acts on, and which way it acts. A stale shift takes
  // over the card: today's clock-in can't happen until it's closed.
  const clockTarget = stale ?? today;
  const clockingOut = stale !== null || state === "IN";
  const timeLabel = now.toLocaleTimeString("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });
  const greeting =
    now.getHours() < 12 ? "Good morning" : now.getHours() < 18 ? "Good afternoon" : "Good evening";
  // The first word of their real name. With no name on file the greeting says
  // the email rather than a first name invented from it.
  const firstName = user.name?.trim().split(/\s+/)[0] || user.email;

  // Ending a break changes today's totals, so the card re-reads the record.
  // Through the query, which writes the cache every other reader shares.
  const refreshToday = todayQuery.refresh;

  function handleClock() {
    // Clocking in is unambiguous; clocking out is the one that may need
    // correcting, so it goes through the dialog. A stale shift always goes
    // through it — its recorded end time is wrong by definition, so the
    // adjustment tab is the whole point.
    if (clockingOut) {
      setError(null);
      setClockOutOpen(true);
      return;
    }
    void runClock();
  }

  async function runClock(choice?: ClockOutChoice, proof?: OffSiteProof) {
    setBusy(true);
    setError(null);
    let coords: Coords | undefined;
    try {
      coords = await requestGeolocation();
    } catch {
      coords = undefined;
    }
    try {
      if (!clockingOut) {
        await clockIn({
          projectId: projectId || undefined,
          lat: coords?.lat,
          lng: coords?.lng,
          remark: proof?.remark,
          photoUrl: proof?.photoUrl,
        });
      } else {
        await clockOut({
          lat: coords?.lat,
          lng: coords?.lng,
          remark: proof?.remark,
          photoUrl: proof?.photoUrl,
        });
      }
      setOffSite(null);
      // Read back rather than trusting the response: the clock POST already
      // invalidated /attendance*, and these refresh the cache both queries
      // above are reading, so the card updates without a second render path.
      const [refreshed, open] = await Promise.all([
        getTodayAttendance().catch(() => null),
        getOpenSession().catch(() => null),
      ]);
      void open;

      // The clock-out landed first, deliberately: if this fails the day still
      // has a real clock-out, and the employee can ask again from the
      // Attendance screen.
      // The record just closed — the stale one when a forgotten shift was being
      // cleared, today's otherwise. Reading it off `refreshed` would file the
      // correction against the wrong day, or against nothing at all.
      const adjusted = stale?.id ?? refreshed?.id;
      if (choice?.adjustment) {
        // Caught separately from the clock-out above, which has already
        // succeeded by this point. Letting this fall into the outer handler
        // reported "Could not update your clock. Try again." for a clock that
        // had updated perfectly well — so the correction looked like it had
        // been swallowed, and a retry would have double-clocked.
        try {
          if (!adjusted) throw new Error("The shift this correction belongs to could not be found.");
          await submitTimeAdjustment({
            recordId: adjusted,
            requestedTimeOut: choice.adjustment.requestedTimeOut,
            reason: choice.adjustment.reason,
          });
          void todayQuery.refresh();
        } catch (adjustErr) {
          setError(
            `You're clocked out, but the correction wasn't filed: ${
              adjustErr instanceof Error ? adjustErr.message : "please try again"
            } You can ask for it again from the Attendance screen.`,
          );
          return;
        }
      }
      setClockOutOpen(false);
    } catch (e) {
      // Off-site clocks need a remark + photo — that flow lives on the full
      // Attendance screen, so hand the user off there to finish.
      // Off-site clocks need a remark and a photo. This used to navigate to the
      // Attendance screen "to finish there" — but that screen has no clock form,
      // so the hand-off was a dead end and the tap looked like it did nothing.
      // Collect the proof here and retry instead.
      if (e instanceof ApiError && e.code === OFF_SITE_CODE) {
        setClockOutOpen(false);
        setOffSite({
          action: clockingOut ? "out" : "in",
          distance: e.distanceMeters,
          choice,
          reason: "geofence",
        });
        return;
      }

      // No GPS fix reached the server. On a project with no geofenced site
      // nothing else records where the shift started or ended, so the clock
      // is refused until there's either a location or a remark and a photo
      // standing in for one — at either end of the shift. Same dialog,
      // different wording — the first thing to try here is granting the
      // browser permission, not explaining why you're away from the site.
      // `choice` rides along so a correction asked for on clock-out survives
      // the retry, exactly as on the off-site path above.
      if (e instanceof ApiError && e.code === LOCATION_REQUIRED_CODE) {
        setClockOutOpen(false);
        setOffSite({ action: clockingOut ? "out" : "in", choice, reason: "no-location" });
        return;
      }

      // A shift was left open on an earlier day. The refusal proves the
      // cached open-session read was stale, so re-read it — that switches the
      // card over to closing that shift rather than leaving an error the
      // employee can't act on.
      if (e instanceof ApiError && e.code === OPEN_SESSION_CODE) {
        void openSessionQuery.refresh();
      }
      setError(e instanceof Error ? e.message : "Could not update your clock. Try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4 sm:space-y-6">
      <section className="rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6">
        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
          {greeting}
        </p>
        <h2 className="mt-1 text-2xl font-black tracking-tight text-foreground">
          {firstName}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Here's your work snapshot for today.
        </p>
      </section>

      {/* ── Today's attendance ─────────────────────────────────────── */}
      <section>
        <div className="rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Right now
              </p>
              <p className="mt-1 text-4xl font-black tracking-tight text-foreground sm:text-5xl">
                {timeLabel}
              </p>
            </div>
            <span
              className={`rounded-full px-3 py-1.5 text-xs font-bold ${
                // muted, not secondary: --secondary is hue 166, the same green
                // family as --success, so an "Off shift" pill in it read as
                // on-shift at a glance. --muted is the actual neutral.
                stale
                  ? "bg-destructive/15 text-destructive"
                  : state === "IN"
                    ? "bg-success/15 text-success"
                    : "bg-muted text-muted-foreground"
              }`}
            >
              {stale
                ? "Shift still open"
                : state === "IN"
                  ? shifts.length > 1
                    ? `On shift ${shiftNumber}`
                    : "On shift"
                  : "Off shift"}
            </span>
          </div>

          {stale ? (
            <div className="mt-4 flex gap-3 rounded-[20px] border border-destructive/30 bg-destructive/10 px-4 py-3">
              <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
              <div className="min-w-0 text-xs">
                <p className="font-bold text-destructive">
                  You never clocked out on {fmtDay(stale.date)}
                </p>
                <p className="mt-0.5 text-muted-foreground">
                  Clocked in at {fmtClock(stale.timeIn)}. Close that shift before starting
                  today&rsquo;s &mdash; you can correct its end time on the way out, and your
                  supervisor approves the correction.
                </p>
              </div>
            </div>
          ) : null}

          <div className="mt-4">
            <p className="mb-1.5 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
              Project
            </p>
            <Select value={projectId} onValueChange={setProjectId} disabled={state !== "OUT" || stale !== null}>
              <SelectTrigger>
                <SelectValue
                  placeholder={projects.length === 0 ? "No project assigned" : "Select a project..."}
                />
              </SelectTrigger>
              <SelectContent>
                {projects.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="mt-4 rounded-[24px] border border-border/60 bg-surface-low/50 p-5 sm:p-6">
            {/* Shaped like the real thing — circle, label, sub-label — so
                nothing moves when the answer lands. A guessed button would be
                worse than a wait: half the time it says the opposite of what
                tapping it does. */}
            {!statusKnown ? (
              <div className="flex flex-col items-center gap-3">
                <Skeleton className="h-20 w-20 rounded-full sm:h-24 sm:w-24" />
                <Skeleton className="h-5 w-40" />
                <Skeleton className="h-3 w-56" />
              </div>
            ) : (
            <div className="flex flex-col items-center gap-3">
              <button
                type="button"
                onClick={handleClock}
                disabled={busy}
                // Red for clocking out, purple for in. The two taps sit in the
                // same spot and look identical otherwise, and only one of them
                // ends the shift — colour is what stops a glance from mistaking
                // which one this is.
                className={`grid h-20 w-20 place-items-center rounded-full shadow-panel transition hover:opacity-90 disabled:opacity-60 sm:h-24 sm:w-24 ${
                  clockingOut
                    ? "bg-destructive text-destructive-foreground"
                    : "bg-primary text-primary-foreground"
                }`}
                aria-label={clockingOut ? "Clock out" : "Clock in"}
              >
                {busy ? (
                  <LoaderCircle className="h-8 w-8 animate-spin sm:h-9 sm:w-9" />
                ) : clockingOut ? (
                  <LogOut className="h-8 w-8 sm:h-9 sm:w-9" />
                ) : (
                  <Fingerprint className="h-8 w-8 sm:h-9 sm:w-9" />
                )}
              </button>
              <p className="text-base font-bold text-primary">
                {stale
                  ? `Close ${fmtDay(stale.date)}'s shift`
                  : state === "OUT"
                    ? doneShifts.length > 0
                      ? "Start another shift"
                      : "Tap to Clock In"
                    : "Tap to Clock Out"}
              </p>
              <p className="text-center text-xs text-muted-foreground">
                {stale
                  ? "Then you can clock in for today"
                  : state === "IN" && shifts.length > 1
                    ? `Shift ${shiftNumber} today, started ${fmtClock(shifts[shifts.length - 1].startedAt)}`
                    : "Pending supervisor approval after tap"}
              </p>
            </div>
            )}
          </div>

          {doneShifts.length > 0 ? (
            <div className="mt-3 rounded-[24px] border border-border/60 bg-surface-low/50 p-4">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Today&rsquo;s shifts
              </p>
              <ul className="mt-2 space-y-1">
                {shifts.map((shift, index) => (
                  <li key={shift.id} className="flex items-baseline justify-between gap-3 text-xs">
                    <span className="text-muted-foreground">
                      <span className="font-semibold text-foreground">{index + 1}.</span>{" "}
                      {fmtClock(shift.startedAt)} &ndash;{" "}
                      {fmtShiftEnd(shift.startedAt, shift.endedAt)}
                    </span>
                    <span className="font-semibold tabular-nums text-foreground">
                      {shift.durationMin != null ? fmtDuration(shift.durationMin) : "running"}
                    </span>
                  </li>
                ))}
              </ul>
              {/* The sum, so it's clear the gap between shifts isn't counted. */}
              <p className="mt-2 border-t border-border/50 pt-2 text-[11px] text-muted-foreground">
                {fmtDuration(doneShifts.reduce((n, s) => n + (s.durationMin ?? 0), 0))} worked across{" "}
                {shifts.length} shift{shifts.length === 1 ? "" : "s"}
              </p>
            </div>
          ) : null}

          <BreakControl
            recordId={stale ? null : today?.id ?? null}
            clockedIn={!stale && state === "IN"}
            onChange={refreshToday}
          />

          {offSite ? (
            <OffSiteClockDialog
              action={offSite.action}
              distanceMeters={offSite.distance}
              reason={offSite.reason}
              busy={busy}
              error={error}
              onSubmit={(proof) => void runClock(offSite.choice, proof)}
              onClose={() => setOffSite(null)}
            />
          ) : null}

          {clockOutOpen && clockTarget ? (
            <ClockOutDialog
              today={clockTarget}
              busy={busy}
              error={error}
              onConfirm={(choice) => void runClock(choice)}
              onClose={() => setClockOutOpen(false)}
            />
          ) : null}

          {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}
        </div>
      </section>

      {/* ── Supervisor review queues ───────────────────────────────── */}
      {isSupervisor ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <QuickAction
            icon={ClipboardCheck}
            label="Attendance approvals"
            count={attendanceCount}
            onClick={() => onNavigate("attendance")}
          />
          <QuickAction
            icon={CircleDollarSign}
            label="Claims queue"
            count={claimCount}
            onClick={() => onNavigate("claims")}
          />
          <QuickAction
            icon={CalendarDays}
            label="Leave approvals"
            count={leaveCount}
            onClick={() => onNavigate("leave")}
          />
        </div>
      ) : null}

      {/* ── Latest payslip ─────────────────────────────────────────── */}
      <button
        type="button"
        onClick={() => onNavigate("payslips")}
        className="flex w-full items-center gap-3 rounded-[24px] border border-border/70 bg-card/90 p-4 text-left shadow-ambient backdrop-blur-sm transition hover:border-primary/40"
      >
        <div className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
          <Wallet className="h-5 w-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            Latest payslip
          </p>
          {/* Was hardcoded to "No payslips yet" — it said that to everyone,
              including anyone who had been paid for months. The list comes
              back newest first, so the newest is the head of it. */}
          {latestPayslip ? (
            <>
              <p className="text-sm font-bold text-foreground">
                {rmWithUnit(latestPayslip.netPay)}
              </p>
              <p className="text-xs text-muted-foreground">
                {latestPayslip.periodLabel}
                {latestPayslip.submittedAt ? ` · paid ${shortDate(latestPayslip.submittedAt)}` : ""}
              </p>
            </>
          ) : (
            <>
              <p className="text-sm font-bold text-foreground">No payslips yet</p>
              <p className="text-xs text-muted-foreground">
                They&rsquo;ll appear here once payroll finalises your first run.
              </p>
            </>
          )}
        </div>
        <ArrowRight className="h-4 w-4 shrink-0 text-muted-foreground" />
      </button>

      {/* ── Quick actions + claim metrics ──────────────────────────── */}
      <div className="grid gap-4 sm:gap-6 xl:grid-cols-[1.25fr_0.75fr] xl:items-start">
        <section className="overflow-hidden rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm">
          <div className="p-5 sm:p-8 xl:p-6">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground sm:text-xs sm:tracking-[0.18em]">
              Quick actions
            </p>
            <p className="mt-1 text-sm text-muted-foreground">
              Start the common tasks from here.
            </p>
          </div>
          <div className="grid gap-3 p-5 pt-0 sm:grid-cols-2 sm:gap-4 sm:p-8 sm:pt-0 xl:p-6 xl:pt-0">
            {/* Hidden on the smallest screens — the quick actions matter more
                there than the figure does. */}
            <div className="hidden rounded-[24px] border border-border/70 bg-surface-low p-5 sm:block xl:p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Total reimbursed
              </p>
              <p className="mt-3 text-4xl font-black tracking-tight text-foreground xl:text-[2.5rem]">
                {claimSummary
                  ? formatCurrency(claimSummary.approvedTotalYtd, claimSummary.currency)
                  : "—"}
              </p>
              <p className="mt-2 text-sm text-muted-foreground xl:text-[0.95rem]">
                {/* Approved only. There is no paid state on a claim, so saying
                    "and paid" would describe something the data cannot know. */}
                This year, across approved claims
              </p>
            </div>
            <div className="flex flex-col gap-3 sm:gap-4 xl:content-start">
              <button
                type="button"
                onClick={() => setNewClaimOpen(true)}
                className="flex h-12 items-center justify-between rounded-2xl bg-primary px-5 text-sm font-semibold text-primary-foreground shadow-panel transition hover:opacity-90"
              >
                Submit new claim
                <Plus className="h-4 w-4" />
              </button>
              {leaveTypes.length > 0 ? (
                <button
                  type="button"
                  onClick={() => setApplyLeaveOpen(true)}
                  className="flex h-12 items-center justify-between rounded-2xl border border-border/70 bg-card px-5 text-sm font-semibold text-foreground transition hover:border-primary/40"
                >
                  Request leave
                  <ArrowRight className="h-4 w-4" />
                </button>
              ) : null}
            </div>
          </div>
        </section>

        {/* Claim counts, desktop only.
            Deliberately NOT year-filtered, unlike the figure above: these are
            the current state of this person's claims, and a claim stuck in
            review since December is exactly the one they need to chase. */}
        <div className="hidden gap-4 xl:grid xl:grid-cols-1">
          <MetricCard
            title="Awaiting review"
            value={claimSummary ? String(claimSummary.awaiting) : "—"}
            icon={Clock3}
            detail="Open queue"
          />
          <MetricCard
            title="Approved"
            value={claimSummary ? String(claimSummary.approved) : "—"}
            icon={FileCheck2}
            detail="Ready for payout"
          />
          {/* Was "Paid", which no claim ever is — the entity has no paid state.
              Rejected is the one outcome an employee has to act on. */}
          <MetricCard
            title="Rejected"
            value={claimSummary ? String(claimSummary.rejected) : "—"}
            icon={CircleDollarSign}
            detail="Not approved"
          />
        </div>
      </div>

      {newClaimOpen ? (
        <NewClaimModal
          onClose={() => setNewClaimOpen(false)}
          onCreated={() => setNewClaimOpen(false)}
        />
      ) : null}
      {applyLeaveOpen ? (
        <ApplyLeaveModal
          types={leaveTypes}
          balances={leaveBalances}
          onClose={() => setApplyLeaveOpen(false)}
          onCreated={() => setApplyLeaveOpen(false)}
        />
      ) : null}
    </div>
  );
}

function MetricCard({
  title,
  value,
  icon: Icon,
  detail,
}: {
  title: string;
  value: string;
  icon: LucideIcon;
  detail?: string;
}) {
  return (
    <div className="rounded-2xl border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm">
      <div className="mb-4 flex items-center justify-between">
        <div className="rounded-2xl bg-primary/10 p-3 text-primary">
          <Icon className="h-5 w-5" />
        </div>
        {detail ? (
          <span className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
            {detail}
          </span>
        ) : null}
      </div>
      <p className="text-sm font-medium text-muted-foreground">{title}</p>
      <p className="mt-1 text-3xl font-black tracking-tight text-foreground">{value}</p>
    </div>
  );
}

function QuickAction({
  icon: Icon,
  label,
  count,
  onClick,
}: {
  icon: LucideIcon;
  label: string;
  count: number;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex items-center gap-3 rounded-[24px] border border-border/70 bg-card/90 p-4 text-left shadow-ambient backdrop-blur-sm transition hover:border-primary/40"
    >
      <div className="relative flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground">
        <Icon className="h-5 w-5" />
        {count > 0 ? (
          <span className="absolute -right-1 -top-1 flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-bold text-destructive-foreground">
            {count > 99 ? "99+" : count}
          </span>
        ) : null}
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-bold text-foreground">{label}</p>
        <p className="text-xs text-muted-foreground">
          {count === 0 ? "All caught up" : `${count} waiting for your review`}
        </p>
      </div>
      <ArrowRight className="h-4 w-4 shrink-0 text-muted-foreground" />
    </button>
  );
}
