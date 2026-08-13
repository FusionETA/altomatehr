import { useEffect, useState } from "react";
import { approveClaim, getTeamClaims, rejectClaim, type Claim } from "../api";
import { ClaimStatusBadge } from "./ClaimStatusBadge";
import { OverLimitBadge } from "./OverLimitBadge";
import { formatCurrency, formatShortDate } from "../lib/claim-formatters";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm";

export function ClaimsApprovals() {
  const [claims, setClaims] = useState<Claim[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  useEffect(() => {
    getTeamClaims()
      .then(setClaims)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  // Preserve the applicant email the transition response omits.
  async function decide(id: string, fn: (id: string) => Promise<Claim>) {
    setBusyId(id);
    setError(null);
    try {
      const updated = await fn(id);
      setClaims((cur) =>
        cur.map((c) => (c.id === updated.id ? { ...updated, employeeEmail: c.employeeEmail } : c)),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not update the claim.");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-black text-foreground">Team approvals</h2>
      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
      <section className={CARD}>
        {loading ? (
          <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">Loading…</p>
        ) : claims.length === 0 ? (
          <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">No claims from your team yet.</p>
        ) : (
          <ul className="divide-y divide-border/60">
            {claims.map((c) => (
              <li
                key={c.id}
                className="flex flex-wrap items-center justify-between gap-3 px-5 py-4 sm:px-6"
              >
                <div className="min-w-0">
                  <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                    {c.claimNumber}
                  </p>
                  <p className="mt-0.5 font-bold text-foreground">{c.title}</p>
                  <p className="text-xs text-muted-foreground">
                    {c.employeeEmail ? `${c.employeeEmail} · ` : ""}
                    {c.category} · {formatShortDate(c.spentAt)}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-bold tabular-nums text-foreground">
                    {formatCurrency(c.amount, c.currency)}
                  </span>
                  {c.exceedsLimit ? <OverLimitBadge /> : null}
                  <ClaimStatusBadge status={c.status} />
                  {c.status === "PENDING" ? (
                    <>
                      <button
                        type="button"
                        disabled={busyId === c.id}
                        onClick={() => decide(c.id, approveClaim)}
                        className="rounded-full bg-secondary px-3 py-1.5 text-xs font-semibold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
                      >
                        Approve
                      </button>
                      <button
                        type="button"
                        disabled={busyId === c.id}
                        onClick={() => decide(c.id, (id) => rejectClaim(id))}
                        className="rounded-full bg-destructive/10 px-3 py-1.5 text-xs font-semibold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
                      >
                        Reject
                      </button>
                    </>
                  ) : null}
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
