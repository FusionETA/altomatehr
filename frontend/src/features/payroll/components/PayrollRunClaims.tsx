import { useCallback, useEffect, useState } from "react";
import { LoaderCircle, Paperclip, X } from "lucide-react";
import {
  attachClaim,
  detachClaim,
  getAttachableClaims,
  getRunClaims,
  type AttachableClaim,
  type PayrollRunClaim,
} from "../api";
import { rm, shortDate } from "../lib/payroll-format";
import {
  BUTTON_GHOST,
  CARD,
  ERROR_PANEL,
  HINT,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
} from "../lib/ui";
import { TableSkeleton } from "./TableSkeleton";

// Approved out-of-pocket claims paid through this month's payroll.
//
// Attaching snapshots the label and amount, so editing the claim afterwards
// cannot move a figure on a run that has already been generated.
export function PayrollRunClaims({
  runId,
  editable,
  onChanged,
}: {
  runId: string;
  editable: boolean;
  onChanged: () => void;
}) {
  const [attached, setAttached] = useState<PayrollRunClaim[]>([]);
  const [available, setAvailable] = useState<AttachableClaim[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    return Promise.all([getRunClaims(runId), getAttachableClaims(runId)])
      .then(([nextAttached, nextAvailable]) => {
        setAttached(nextAttached);
        setAvailable(nextAvailable);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [runId]);

  useEffect(() => {
    void load();
  }, [load]);

  async function act(key: string, action: () => Promise<unknown>) {
    setBusy(key);
    setError(null);
    try {
      await action();
      await load();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "That did not go through.");
    } finally {
      setBusy(null);
    }
  }

  const total = attached.reduce((sum, claim) => sum + claim.amount, 0);

  // Rows already on another run come back flagged rather than omitted, so the
  // picker can say WHERE they are instead of silently not offering them.
  const free = available.filter(
    (claim) => claim.attachedToRunId === null && claim.blockedReason === null,
  );
  const elsewhere = available.filter(
    (claim) => claim.attachedToRunId !== null && claim.attachedToRunId !== runId,
  );
  const blocked = available.filter((claim) => claim.blockedReason !== null);

  // Nothing attached and nothing attachable — an org settling claims through
  // Xero will never have either, so the card would be a permanent explainer
  // for a feature it does not use.
  //
  // Silent while loading too: rendering a skeleton and then removing it is a
  // flash of a card that was never going to stay.
  if (loading || (attached.length === 0 && available.length === 0)) return null;

  return (
    <section className={CARD}>
      <header className="mb-4">
        <h2 className="text-base font-semibold text-foreground">Reimbursed claims</h2>
        <p className={HINT}>
          Approved claims the employee paid out of pocket, settled through this
          month's pay. Attaching one fixes its label and amount here, so editing the
          claim later cannot move a figure on a generated run.
        </p>
      </header>

      {error ? <p className={`${ERROR_PANEL} mb-4`}>Error: {error}</p> : null}

      {loading ? (
        <TableSkeleton columns={4} label="Loading attached claims" />
      ) : (
        <div className="space-y-5">
          {attached.length > 0 ? (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[620px] border-collapse">
                <thead>
                  <tr className="border-b border-border/70">
                    <th className={TH}>Claim</th>
                    <th className={TH}>Employee</th>
                    <th className={TH_NUM}>Amount</th>
                    <th className={TH} aria-label="Remove" />
                  </tr>
                </thead>
                <tbody>
                  {attached.map((claim) => (
                    <tr key={claim.id} className="border-b border-border/40 last:border-0">
                      <td className={TD}>
                        <div className="font-medium text-foreground">{claim.label}</div>
                        <div className="text-xs text-muted-foreground">
                          {claim.claimNumber}
                        </div>
                      </td>
                      <td className={`${TD} text-muted-foreground`}>{claim.employeeName}</td>
                      <td className={`${TD_NUM} font-semibold`}>{rm(claim.amount)}</td>
                      <td className={`${TD} text-right`}>
                        {editable ? (
                          <button
                            type="button"
                            aria-label={`Remove ${claim.label} from this run`}
                            className="rounded-full p-2 text-muted-foreground transition hover:bg-destructive/10 hover:text-destructive focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
                            disabled={busy !== null}
                            onClick={() =>
                              void act(`detach-${claim.claimId}`, () =>
                                detachClaim(runId, claim.claimId),
                              )
                            }
                          >
                            {busy === `detach-${claim.claimId}` ? (
                              <LoaderCircle className="size-4 animate-spin" aria-hidden />
                            ) : (
                              <X className="size-4" aria-hidden />
                            )}
                          </button>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr className="border-t border-border/70 bg-muted/40 font-semibold">
                    <td className={TD} colSpan={2}>
                      {attached.length} claim(s) on this run
                    </td>
                    <td className={TD_NUM}>{rm(total)}</td>
                    <td />
                  </tr>
                </tfoot>
              </table>
            </div>
          ) : (
            <p className={HINT}>No claims attached to this run.</p>
          )}

          {editable && free.length > 0 ? (
            <div>
              <h3 className="mb-2 text-sm font-semibold text-foreground">
                Waiting to be paid
              </h3>
              <ul className="space-y-2">
                {free.map((claim) => (
                  <li
                    key={claim.claimId}
                    className="flex flex-wrap items-center gap-3 rounded-2xl border border-border/60 bg-card p-3"
                  >
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-foreground">{claim.title}</p>
                      <p className="text-xs text-muted-foreground">
                        {claim.employeeName} · {claim.claimNumber} · spent{" "}
                        {shortDate(claim.spentAt)}
                      </p>
                    </div>
                    <span className="tabular-nums text-sm font-semibold text-foreground">
                      {rm(claim.amount)}
                    </span>
                    <button
                      type="button"
                      className={`${BUTTON_GHOST} h-9 rounded-xl px-3 text-xs`}
                      disabled={busy !== null}
                      onClick={() =>
                        void act(`attach-${claim.claimId}`, () =>
                          attachClaim(runId, claim.claimId),
                        )
                      }
                    >
                      {busy === `attach-${claim.claimId}` ? (
                        <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
                      ) : (
                        <Paperclip className="size-3.5" aria-hidden />
                      )}
                      Add to this run
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {/* Named rather than hidden: an admin hunting for a claim that is
              "missing" needs to be told it is on another month. */}
          {elsewhere.length > 0 ? (
            <p className={HINT}>
              {elsewhere.length} more approved claim(s) are already on another run:{" "}
              {[...new Set(elsewhere.map((claim) => claim.attachedToRunPeriod))]
                .filter(Boolean)
                .join(", ")}
              .
            </p>
          ) : null}

          {blocked.length > 0 ? (
            <ul className={HINT}>
              {blocked.map((claim) => (
                <li key={claim.claimId}>
                  <strong>{claim.claimNumber}</strong> cannot be attached —{" "}
                  {claim.blockedReason}
                </li>
              ))}
            </ul>
          ) : null}

          {/* The commonest reason this list is empty, and the only one an
              admin can do nothing about from here. */}
          {editable && free.length === 0 && attached.length === 0 ? (
            <p className={HINT}>
              Nothing is waiting. A claim only reaches payroll when it is approved,
              paid personally, and the org's claim settlement route is set to Payroll
              rather than Xero — that switch is under Claims → Settings, and it only
              affects claims submitted after it is changed.
            </p>
          ) : null}
        </div>
      )}
    </section>
  );
}
