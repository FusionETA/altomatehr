import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { Users, X } from "lucide-react";
import { getOnLeaveToday, type LeaveApplication, type OnLeaveToday } from "../api";
import { eachDateInRange, formatDate, formatDateRange, relativeDaysAgo, urgencyLabel } from "../lib/leave-formatters";
import { LeaveStatusBadge } from "./LeaveStatusBadge";

// Fetches who else has approved leave overlapping this application's dates,
// one day at a time (the API only exposes a single-day snapshot) and dedupes
// by employee — each entry already carries that person's own full leave range.
function useOverlappingLeave(application: LeaveApplication, enabled: boolean) {
  const [loading, setLoading] = useState(enabled);
  const [people, setPeople] = useState<OnLeaveToday[]>([]);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    setLoading(true);

    const dates = eachDateInRange(application.startDate, application.endDate);
    Promise.allSettled(dates.map((date) => getOnLeaveToday(date))).then((results) => {
      if (cancelled) return;
      const byEmployee = new Map<string, OnLeaveToday>();
      for (const result of results) {
        if (result.status !== "fulfilled") continue;
        for (const entry of result.value) {
          if (entry.employeeId === application.employeeId) continue;
          byEmployee.set(entry.employeeId, entry);
        }
      }
      setPeople([...byEmployee.values()]);
      setLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [enabled, application.employeeId, application.startDate, application.endDate]);

  return { loading, people };
}

export function LeaveDetailsModal({
  application,
  typeName,
  employeeLabel,
  showWhoElseIsOff = false,
  onClose,
  footer,
}: {
  application: LeaveApplication;
  typeName: string;
  employeeLabel?: string;
  showWhoElseIsOff?: boolean;
  onClose: () => void;
  footer?: ReactNode;
}) {
  const { loading: loadingOverlap, people: overlapping } = useOverlappingLeave(
    application,
    showWhoElseIsOff,
  );
  const urgency = application.status === "PENDING" ? urgencyLabel(application.startDate) : null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="nice-scrollbar max-h-[90vh] w-full max-w-[640px] overflow-y-auto rounded-[28px] border border-white/40 bg-card/95 p-6 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl sm:p-8">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              {employeeLabel ?? "Leave request"}
            </p>
            <h2 className="mt-1 truncate text-2xl font-black text-foreground">{typeName}</h2>
          </div>
          <button
            type="button"
            aria-label="Close leave details"
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <section className="mt-5 rounded-[22px] border border-border/70 bg-surface-low/60 p-5">
          <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                Duration
              </p>
              <p className="mt-2 text-3xl font-black leading-none text-foreground">
                {application.totalDays}
                <span className="text-base font-semibold text-muted-foreground">
                  {" "}
                  day{application.totalDays === 1 ? "" : "s"}
                </span>
              </p>
              <div className="mt-4 flex flex-wrap items-center gap-2">
                <LeaveStatusBadge status={application.status} />
                {urgency ? (
                  <span className="inline-flex items-center rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-amber-800">
                    {urgency}
                  </span>
                ) : null}
              </div>
            </div>
            <div className="grid grid-cols-2 gap-x-6 gap-y-3 sm:min-w-[260px]">
              <Fact label="Dates" value={formatDateRange(application.startDate, application.endDate)} />
              <Fact label="Submitted" value={relativeDaysAgo(application.createdAt)} />
              {application.decidedAt ? (
                <Fact label="Decided" value={formatDate(application.decidedAt.slice(0, 10))} />
              ) : null}
            </div>
          </div>
        </section>

        {application.reason ? (
          <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Reason</p>
            <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">{application.reason}</p>
          </section>
        ) : null}

        {application.reviewNotes ? (
          <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Reviewer note
            </p>
            <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">
              {application.reviewNotes}
            </p>
          </section>
        ) : null}

        {showWhoElseIsOff ? (
          <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
            <div className="flex items-center gap-2 text-primary">
              <Users className="h-4 w-4" />
              <p className="text-xs font-semibold uppercase tracking-[0.18em]">Also off this period</p>
            </div>
            {loadingOverlap ? (
              <p className="mt-3 text-sm text-muted-foreground">Checking the team calendar…</p>
            ) : overlapping.length === 0 ? (
              <p className="mt-3 text-sm text-muted-foreground">No one else has approved leave in this period.</p>
            ) : (
              <ul className="mt-3 space-y-2">
                {overlapping.map((person) => (
                  <li
                    key={person.employeeId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-2xl bg-surface-low/70 px-4 py-2.5"
                  >
                    <span className="text-sm font-semibold text-foreground">{person.email ?? "Teammate"}</span>
                    <span className="text-xs text-muted-foreground">
                      {person.leaveTypeName} · {formatDateRange(person.startDate.slice(0, 10), person.endDate.slice(0, 10))}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        ) : null}

        {footer ? <div className="mt-5 flex flex-col gap-4 border-t border-border/60 pt-5 sm:flex-row sm:items-center sm:justify-end">{footer}</div> : null}
      </div>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className="mt-1 break-words text-sm font-bold text-foreground">{value}</p>
    </div>
  );
}
