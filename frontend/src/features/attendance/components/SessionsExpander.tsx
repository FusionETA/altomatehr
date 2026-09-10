import { useState } from "react";
import { ChevronDown, ChevronUp } from "lucide-react";

import { formatMinutes } from "@/features/admin/lib/attendance-format";
import type { AttendanceSession } from "../api";
import { dayOffsetLabel } from "../lib/attendance-time";
import { AttendancePhotoButton } from "./AttendancePhotoButton";

function timeOnly(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });
}

/**
 * A day's clock-in/out stints, one row each.
 *
 * Multi-shift means the employee finished a morning shift — clocked in AND
 * out — then clocked in again later. Each of those stints is a session, and
 * they all hang off one `AttendanceRecord` for the day, whose TimeIn/TimeOut
 * are the first start and last end while DurationMin is the SUM of the stints.
 * A row showing only the roll-up therefore reads as one unbroken stretch, with
 * a duration that does not match the window it spans.
 *
 * Stateless: the caller owns the open/closed state, so a table can put the
 * control in its own column and the panel in a full-width row beneath.
 */
export function SessionList({ sessions }: { sessions: AttendanceSession[] }) {
  return (
    <ul className="divide-y divide-border/40 overflow-hidden rounded-xl border border-border/60 bg-card">
      {sessions.map((session, i) => (
        <li key={session.id} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 px-3 py-2 text-xs">
          <span className="w-14 shrink-0 text-[10px] font-bold uppercase tracking-[0.1em] text-muted-foreground">
            Shift {i + 1}
          </span>
          <span className="whitespace-nowrap tabular-nums text-foreground">
            {timeOnly(session.startedAt)} –{" "}
            {session.endedAt ? timeOnly(session.endedAt) : "still in"}
            {dayOffsetLabel(session.startedAt, session.endedAt) ? (
              <span className="ml-1 text-[10px] font-bold text-tertiary">
                {dayOffsetLabel(session.startedAt, session.endedAt)}
              </span>
            ) : null}
          </span>
          {/* This stint's own selfies. The day roll-up only keeps the first
              clock-in's and the last clock-out's, so a middle shift's photo is
              reachable nowhere else. */}
          {session.clockInPhotoUrl || session.clockOutPhotoUrl ? (
            <span className="flex items-center gap-1.5">
              <AttendancePhotoButton
                url={session.clockInPhotoUrl}
                label={`Clock-in photo, shift ${i + 1}`}
              />
              <AttendancePhotoButton
                url={session.clockOutPhotoUrl}
                label={`Clock-out photo, shift ${i + 1}`}
              />
            </span>
          ) : null}
          {/* `!= null`, not truthy: a 0m stint is a real answer, and the
              truthy check rendered it as nothing at all. */}
          {session.durationMin != null ? (
            <span className="tabular-nums text-muted-foreground">
              {formatMinutes(session.durationMin)}
            </span>
          ) : null}
          {/* Lateness only on the first stint. `ClockInAsync` measures every
              clock-in against the one scheduled start, so a 14:30 return from
              an afternoon off is stamped "330 minutes late" — a number with no
              meaning, since there is no second shift start to be late for.
              Showing it would put a five-hour lateness against someone who
              worked their whole day. */}
          {i === 0 && (session.lateByMin ?? 0) > 0 ? (
            <span className="tabular-nums text-tertiary">
              Late {formatMinutes(session.lateByMin)}
            </span>
          ) : null}
        </li>
      ))}
    </ul>
  );
}

/**
 * The arrow that opens a day's shifts. Controlled, so a table row and its
 * full-width detail row can share one piece of state.
 */
export function SessionsToggle({
  count,
  open,
  onToggle,
  label,
  variant = "icon",
}: {
  count: number;
  open: boolean;
  onToggle: () => void;
  label: string;
  /**
   * `icon` for a table with a column of its own to put the arrow in; `pill`
   * for one where the control has to sit inside a cell. The pill renders the
   * same whether open or closed, so toggling never changes its column's width.
   */
  variant?: "icon" | "pill";
}) {
  const chevron = open ? (
    <ChevronUp className="h-3 w-3" aria-hidden />
  ) : (
    <ChevronDown className="h-3 w-3" aria-hidden />
  );

  return (
    <button
      type="button"
      onClick={onToggle}
      aria-expanded={open}
      aria-label={`${open ? "Hide" : "Show"} the ${count} shifts for ${label}`}
      className={
        variant === "pill"
          ? "mt-1 inline-flex items-center gap-1 rounded-full border border-border/70 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.1em] text-muted-foreground transition-colors duration-150 hover:bg-surface-low hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          : "inline-flex h-6 w-6 items-center justify-center rounded-full border border-border/70 text-muted-foreground transition-colors duration-150 hover:bg-surface-low hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      }
    >
      {variant === "pill" ? `${count} shifts` : null}
      {chevron}
    </button>
  );
}

/**
 * Self-contained toggle + panel, for the list views that have no column to put
 * a control in.
 */
export function SessionsExpander({ sessions }: { sessions: AttendanceSession[] }) {
  const [open, setOpen] = useState(false);
  if (sessions.length < 2) return null;

  return (
    <div className="mt-1">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="inline-flex items-center gap-1 rounded-full border border-border/70 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.1em] text-muted-foreground transition-colors duration-150 hover:bg-surface-low hover:text-foreground"
      >
        {sessions.length} shifts
        {open ? <ChevronUp className="h-3 w-3" aria-hidden /> : <ChevronDown className="h-3 w-3" aria-hidden />}
      </button>
      {open ? (
        <div className="mt-2">
          <SessionList sessions={sessions} />
        </div>
      ) : null}
    </div>
  );
}
