import { useEffect, useMemo, useState } from "react";
import { Plus } from "lucide-react";
import {
  approveLeave,
  cancelLeave,
  getLeaveBalances,
  getLeaveTypes,
  getMyLeave,
  getTeamLeave,
  rejectLeave,
  type LeaveApplication,
  type LeaveBalance,
  type LeaveType,
} from "../api";
import { LeaveStatusBadge } from "./LeaveStatusBadge";
import { ApplyLeaveModal } from "./ApplyLeaveModal";
import { EmptyModule } from "@/features/employee-portal/components/EmptyModule";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm";

function fmtDate(ymd: string) {
  const [y, m, d] = ymd.split("-").map(Number);
  return new Intl.DateTimeFormat("en-MY", { day: "2-digit", month: "short", year: "numeric" }).format(
    new Date(y, (m ?? 1) - 1, d ?? 1),
  );
}

function fmtRange(start: string, end: string) {
  return start === end ? fmtDate(start) : `${fmtDate(start)} – ${fmtDate(end)}`;
}

export function LeaveView({ sub, role }: { sub: string; role: string }) {
  const canApprove = role !== "Employee";

  const [types, setTypes] = useState<LeaveType[]>([]);
  const [balances, setBalances] = useState<LeaveBalance[]>([]);
  const [mine, setMine] = useState<LeaveApplication[]>([]);
  const [team, setTeam] = useState<LeaveApplication[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [applyOpen, setApplyOpen] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      getLeaveTypes(),
      getLeaveBalances(),
      getMyLeave(),
      canApprove ? getTeamLeave() : Promise.resolve<LeaveApplication[]>([]),
    ])
      .then(([t, b, m, tm]) => {
        setTypes(t);
        setBalances(b);
        setMine(m);
        setTeam(tm);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [canApprove]);

  const activeTypes = useMemo(() => types.filter((t) => !t.isArchived), [types]);
  const typeName = (id: string) => types.find((t) => t.id === id)?.name ?? "Leave";
  const quotaBalances = balances.filter((b) => b.entitlementDays > 0);

  // Approve / reject a team request: preserve the applicant email the response omits.
  async function decide(id: string, fn: (id: string) => Promise<LeaveApplication>) {
    setBusyId(id);
    setError(null);
    try {
      const updated = await fn(id);
      setTeam((cur) =>
        cur.map((a) => (a.id === updated.id ? { ...updated, employeeEmail: a.employeeEmail } : a)),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not update the application.");
    } finally {
      setBusyId(null);
    }
  }

  async function cancelMine(id: string) {
    setBusyId(id);
    setError(null);
    try {
      const updated = await cancelLeave(id);
      setMine((cur) => cur.map((a) => (a.id === updated.id ? updated : a)));
      setBalances(await getLeaveBalances());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not cancel.");
    } finally {
      setBusyId(null);
    }
  }

  function row(a: LeaveApplication, action: React.ReactNode) {
    return (
      <li key={a.id} className="flex flex-wrap items-center justify-between gap-3 px-5 py-4 sm:px-6">
        <div className="min-w-0">
          <p className="font-semibold text-foreground">
            {typeName(a.leaveTypeId)}{" "}
            <span className="font-normal text-muted-foreground">
              · {a.totalDays} day{a.totalDays === 1 ? "" : "s"}
            </span>
          </p>
          <p className="text-xs text-muted-foreground">
            {a.employeeEmail ? `${a.employeeEmail} · ` : ""}
            {fmtRange(a.startDate, a.endDate)}
          </p>
          {a.reason ? <p className="mt-1 text-xs text-muted-foreground">“{a.reason}”</p> : null}
          {a.reviewNotes ? (
            <p className="mt-1 text-xs text-muted-foreground">Note: {a.reviewNotes}</p>
          ) : null}
        </div>
        <div className="flex items-center gap-2">
          <LeaveStatusBadge status={a.status} />
          {action}
        </div>
      </li>
    );
  }

  return (
    <div className="space-y-5 sm:space-y-6">
      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {/* ── My Leave: balances + my applications ─────────────────────── */}
      {sub === "leave-mine" ? (
        <>
          {quotaBalances.length > 0 ? (
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {quotaBalances.map((b) => (
                <div key={b.leaveTypeId} className={`${CARD} p-5`}>
                  <p className="text-sm font-semibold text-foreground">{b.name}</p>
                  <p className="mt-2 text-3xl font-black tabular-nums text-foreground">
                    {b.remainingDays}
                    <span className="text-base font-semibold text-muted-foreground"> / {b.entitlementDays}</span>
                  </p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {b.takenDays} taken{b.pendingDays > 0 ? ` · ${b.pendingDays} pending` : ""}
                  </p>
                </div>
              ))}
            </div>
          ) : null}

          <div className="flex items-center justify-between gap-4">
            <h2 className="text-lg font-black text-foreground">My leave</h2>
            <button
              type="button"
              onClick={() => setApplyOpen(true)}
              disabled={activeTypes.length === 0}
              className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground shadow-panel transition hover:opacity-90 disabled:opacity-50"
            >
              <Plus className="h-4 w-4" />
              Apply
            </button>
          </div>
          <section className={CARD}>
            {loading ? (
              <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">Loading…</p>
            ) : mine.length === 0 ? (
              <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">No leave applications yet.</p>
            ) : (
              <ul className="divide-y divide-border/60">
                {mine.map((a) =>
                  row(
                    a,
                    a.status === "PENDING" ? (
                      <button
                        type="button"
                        disabled={busyId === a.id}
                        onClick={() => cancelMine(a.id)}
                        className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
                      >
                        Cancel
                      </button>
                    ) : null,
                  ),
                )}
              </ul>
            )}
          </section>
        </>
      ) : null}

      {/* ── Approvals: my team's leave requests ──────────────────────── */}
      {sub === "leave-approvals" && canApprove ? (
        <>
          <h2 className="text-lg font-black text-foreground">Team approvals</h2>
          <section className={CARD}>
            {loading ? (
              <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">Loading…</p>
            ) : team.length === 0 ? (
              <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">
                No leave from your team yet.
              </p>
            ) : (
              <ul className="divide-y divide-border/60">
                {team.map((a) =>
                  row(
                    a,
                    a.status === "PENDING" ? (
                      <>
                        <button
                          type="button"
                          disabled={busyId === a.id}
                          onClick={() => decide(a.id, approveLeave)}
                          className="rounded-full bg-secondary px-3 py-1.5 text-xs font-semibold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
                        >
                          Approve
                        </button>
                        <button
                          type="button"
                          disabled={busyId === a.id}
                          onClick={() => decide(a.id, (id) => rejectLeave(id))}
                          className="rounded-full bg-destructive/10 px-3 py-1.5 text-xs font-semibold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
                        >
                          Reject
                        </button>
                      </>
                    ) : null,
                  ),
                )}
              </ul>
            )}
          </section>
        </>
      ) : null}

      {/* ── Team Balances: not yet rebuilt ───────────────────────────── */}
      {sub === "leave-team" ? (
        <EmptyModule
          title="Team Balances"
          body="An org-wide view of every team member's leave balances will live here."
        />
      ) : null}

      {applyOpen ? (
        <ApplyLeaveModal
          types={activeTypes}
          onClose={() => setApplyOpen(false)}
          onCreated={async (app) => {
            setMine((cur) => [app, ...cur]);
            setBalances(await getLeaveBalances());
          }}
        />
      ) : null}
    </div>
  );
}
