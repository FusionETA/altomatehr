import {
  Building2,
  CalendarClock,
  Clock,
  ShieldAlert,
  TimerReset,
  TrendingUp,
} from "lucide-react";
import type {
  AdminOverview,
  AttendanceHealth,
  OverturnedSupervisor,
  ProjectClaimSpend,
  SlowOtApprover,
  StalePendingClaim,
  UpcomingClaimRun,
} from "../api";
import { CARD, EYEBROW, TILE } from "../lib/dashboard-styles";
import { CardHead, EmptyState, Stat } from "./DashboardCard";

// Ported from the monolith's ExecutiveOverview — six analytics cards. Cards are wired to
// real data one by one; the rest render their empty states until their backend lands.

const money = (n: number) =>
  new Intl.NumberFormat("en-MY", { style: "currency", currency: "MYR" }).format(n);
const shortDate = (iso: string) =>
  new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });

export function ExecutiveOverview({ data }: { data: AdminOverview }) {
  // Only show analytics for modules the org actually has — no Claims module, no claims
  // cards, etc. Cards reflow to fill the 2-column grid. Approval health (overturned
  // approvers) is cross-module, so it always shows.
  const has = (m: string) => data.enabledModules.includes(m);
  return (
    <div className="grid gap-6 lg:grid-cols-2">
      {has("claims") ? <ProjectClaimsCard projects={data.projectSpend} /> : null}
      {has("attendance") ? <AttendanceHealthCard projects={data.attendanceHealth} /> : null}
      {has("overtime") ? <SlowOtApproversCard approvers={data.slowOtApprovers} /> : null}
      {has("claims") ? <StalePendingClaimsCard claims={data.stalePendingClaims} /> : null}
      {has("claims") ? <UpcomingClaimRunCard run={data.upcomingClaimRun} /> : null}
      <OverturnedSupervisorsCard
        total={data.overturnedSupervisors.total}
        samples={data.overturnedSupervisors.samples}
      />
    </div>
  );
}

// ─── Card 1: Project claims breakdown ────────────────────────────────────────

function ProjectClaimsCard({ projects }: { projects: ProjectClaimSpend[] }) {
  const total = projects.reduce((sum, p) => sum + p.totalAmount, 0);

  return (
    <section className={CARD}>
      <CardHead icon={TrendingUp} title="Project claims" meta="This month" />
      <div className="space-y-3">
        {projects.length === 0 ? (
          <EmptyState text="No claims submitted this month yet." />
        ) : (
          <>
            {projects.map((p) => {
              const pct = total > 0 ? Math.round((p.totalAmount / total) * 100) : 0;
              return (
                <div key={p.project} className={TILE}>
                  <div className="flex items-baseline justify-between gap-3">
                    <p className="truncate text-sm font-bold text-foreground">{p.project}</p>
                    <p className="text-base font-black tabular-nums text-foreground">
                      {money(p.totalAmount)}
                    </p>
                  </div>
                  <div className="mt-2 flex items-center justify-between gap-3">
                    <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-border/60">
                      <div className="h-full rounded-full bg-primary" style={{ width: `${pct}%` }} />
                    </div>
                    <p className="text-xs text-muted-foreground">
                      {p.claimCount} claim{p.claimCount === 1 ? "" : "s"} · {pct}%
                    </p>
                  </div>
                </div>
              );
            })}
            <p className="px-1 pt-1 text-xs text-muted-foreground">
              Total this month:{" "}
              <span className="font-semibold text-foreground">{money(total)}</span>
            </p>
          </>
        )}
      </div>
    </section>
  );
}

// ─── Card 2: Attendance health by project ────────────────────────────────────

function AttendanceHealthCard({ projects }: { projects: AttendanceHealth[] }) {
  return (
    <section className={CARD}>
      <CardHead icon={Building2} title="Attendance health" meta="Last 30 days" />
      <div className="space-y-3">
        {projects.length === 0 ? (
          <EmptyState text="No attendance recorded in the last 30 days." />
        ) : (
          projects.map((p) => {
            const onTimeRate = p.total > 0 ? Math.round((p.onTime / p.total) * 100) : 0;
            return (
              <div key={p.project} className={TILE}>
                <div className="flex items-baseline justify-between gap-3">
                  <p className="truncate text-sm font-bold text-foreground">{p.project}</p>
                  <p className="text-base font-black tabular-nums text-foreground">{onTimeRate}%</p>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {p.onTime} on time · {p.late} late · {p.missing} missing · {p.onLeave} on leave
                </p>
              </div>
            );
          })
        )}
      </div>
    </section>
  );
}

// ─── Card 3: Slow OT approvers ───────────────────────────────────────────────

function SlowOtApproversCard({ approvers }: { approvers: SlowOtApprover[] }) {
  return (
    <section className={CARD}>
      <CardHead
        icon={TimerReset}
        title="Slow OT approvers"
        meta="> 24h average"
        tone="text-tertiary"
        toneBg="bg-tertiary/10"
      />
      <div className="space-y-3">
        {approvers.length === 0 ? (
          <EmptyState text="All supervisors are reviewing OT requests within 24 hours." />
        ) : (
          approvers.map((a) => (
            <div key={a.reviewerId} className={`flex items-center justify-between gap-3 ${TILE}`}>
              <div className="min-w-0">
                <p className="truncate text-sm font-bold text-foreground">{a.reviewerName}</p>
                <p className="text-xs text-muted-foreground">
                  {a.reviewedCount} reviewed · {a.pendingCount} pending
                </p>
              </div>
              <div className="text-right">
                <p className="text-base font-black tabular-nums text-tertiary">
                  {a.averageHours.toFixed(1)}h
                </p>
                <p className={EYEBROW}>avg</p>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

// ─── Card 4: Stale pending claims ────────────────────────────────────────────

function StalePendingClaimsCard({ claims }: { claims: StalePendingClaim[] }) {
  return (
    <section className={CARD}>
      <CardHead
        icon={Clock}
        title="Stale pending claims"
        meta="> 7 days"
        tone="text-tertiary"
        toneBg="bg-tertiary/10"
      />
      <div className="space-y-3">
        {claims.length === 0 ? (
          <EmptyState text="No claims have been pending for more than 7 days." />
        ) : (
          claims.map((c) => (
            <div key={c.id} className={`flex items-center justify-between gap-3 ${TILE}`}>
              <div className="min-w-0">
                <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                  {c.claimNumber}
                </p>
                <p className="truncate text-sm font-bold text-foreground">{c.title}</p>
                <p className="text-xs text-muted-foreground">{c.employeeName}</p>
              </div>
              <div className="text-right">
                <p className="text-base font-black tabular-nums text-foreground">{money(c.amount)}</p>
                <p className="text-[11px] font-semibold text-tertiary">{c.daysPending}d pending</p>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

// ─── Card 5: Upcoming claim run ──────────────────────────────────────────────

function UpcomingClaimRunCard({ run }: { run: UpcomingClaimRun | null }) {
  return (
    <section className={CARD}>
      <CardHead
        icon={CalendarClock}
        title="Upcoming claim run"
        meta={
          run
            ? run.daysUntilCutoff === 0
              ? "Cutoff today"
              : `${run.daysUntilCutoff} day${run.daysUntilCutoff === 1 ? "" : "s"} left`
            : undefined
        }
      />
      <div>
        {!run ? (
          <EmptyState text="Configure a claim cutoff in Settings to see this." />
        ) : (
          <div className="space-y-4">
            <div className={TILE}>
              <p className={EYEBROW}>Cuts off</p>
              <p className="mt-1 text-2xl font-black text-foreground">{shortDate(run.cutoffDate)}</p>
              <p className="mt-0.5 text-xs text-muted-foreground">Day {run.cutoffDay} of the month</p>
            </div>
            <div className="grid grid-cols-3 gap-3">
              <Stat label="Claims" value={String(run.claimsInRun)} />
              <Stat label="Pending" value={String(run.pendingInRun)} tone="text-tertiary" />
              <Stat label="Queued value" value={money(run.totalAmountInRun)} />
            </div>
          </div>
        )}
      </div>
    </section>
  );
}

// ─── Card 6: Most-overturned layer-1 supervisors ─────────────────────────────

function OverturnedSupervisorsCard({
  total,
  samples,
}: {
  total: number;
  samples: OverturnedSupervisor[];
}) {
  return (
    <section className={CARD}>
      <CardHead
        icon={ShieldAlert}
        title="Most-overturned approvers"
        meta="Last 90 days"
        tone="text-destructive"
        toneBg="bg-destructive/10"
      />
      <div className="space-y-3">
        {samples.length === 0 ? (
          <EmptyState text="No layer-1 approvals have been overturned recently." />
        ) : (
          <>
            <p className="px-1 text-xs text-muted-foreground">
              Layer-1 supervisors whose approvals were rejected by a higher layer.
            </p>
            {samples.map((s, idx) => (
              <div
                key={s.supervisorId}
                className="flex items-center gap-3 rounded-2xl border border-destructive/20 bg-destructive/5 p-4"
              >
                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-destructive/15 text-sm font-black text-destructive">
                  {idx + 1}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-bold text-foreground">{s.supervisorName}</p>
                  <p className="text-xs text-muted-foreground">
                    {s.affectedEmployees} {s.affectedEmployees === 1 ? "employee" : "employees"}{" "}
                    affected
                  </p>
                </div>
                <div className="text-right">
                  <p className="text-2xl font-black tabular-nums text-destructive">
                    {s.overturnedCount}
                  </p>
                  <p className={EYEBROW}>overturned</p>
                </div>
              </div>
            ))}
            {total > 0 ? (
              <p className="px-1 pt-1 text-xs text-muted-foreground">
                {total} total layer-1 approval{total === 1 ? "" : "s"} overturned in the last 90
                days.
              </p>
            ) : null}
          </>
        )}
      </div>
    </section>
  );
}
