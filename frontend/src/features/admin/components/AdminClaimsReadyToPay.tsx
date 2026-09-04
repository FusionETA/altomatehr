import { useEffect, useMemo, useState } from "react";
import { ArrowRight, BanknoteArrowUp, CircleAlert, CircleCheck, LoaderCircle, Lock, Wallet } from "lucide-react";
import { syncClaimToXero, type Claim } from "@/features/claims/api";
import { getXeroStatus } from "@/features/settings/api";
import { formatCurrency } from "@/features/claims/lib/claim-formatters";
import {
  isSettledCompanySpend,
  readyToPayByEmployee,
  sumAmount,
  type PayeeGroup,
} from "@/features/claims/lib/claim-insights";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { CARD, EYEBROW, TILE } from "../lib/dashboard-styles";
import { claimIdsDrilldown, readyToPayDrilldown, type ClaimDrilldown } from "../lib/claims-drilldown";
import { CardHead, EmptyState } from "./DashboardCard";

// What the org owes its own people, once approval is done and arguing is over.
//
// This is the one surface where a total IS the action — it is the payment about
// to be made. But it is grouped BY PERSON, because a reimbursement is one
// transfer to one employee, not one per claim, and ordered by who has been out
// of pocket longest: with the decision already made, waiting is just the
// company holding someone else's money.

// An approved claim nobody has paid after this long is its own problem.
const OVERDUE_DAYS = 7;

// ─── Add to payroll: the slot, not the wiring ────────────────────────────────
//
// The Payroll module is not built. There is no draft run to attach a claim to,
// and Claim carries no payrollRunId and no paid marker — so nothing about
// "added to payroll" could survive a refresh.
//
// The button is therefore rendered DISABLED, not wired to a no-op. An admin who
// clicks "Add to payroll" and sees nothing happen would reasonably assume it
// worked, walk away, and never pay the person. A visibly locked button is the
// honest state.
//
// TO WIRE IT, once payroll runs + a claim→run link exist:
//   1. flip PAYROLL_READY to true
//   2. implement addToPayroll below (POST the claimIds to the chosen draft run)
//   3. call onAdded() so the page reloads — paid claims then drop off this list
//      on their own, because isReadyToPay stops matching them
// Everything else here already works off claimIds and needs no change.
const PAYROLL_READY = false;

const PAYROLL_PENDING_HINT = "Available once the Payroll module is built";

export function AdminClaimsReadyToPay({
  claims,
  employeeEmails,
  onDrill,
  onSynced,
}: {
  claims: Claim[];
  employeeEmails: Map<string, string>;
  onDrill: (drilldown: ClaimDrilldown) => void;
  // A synced claim changes state on the server, so the page re-reads rather
  // than this component patching rows it does not own.
  onSynced: () => void;
}) {
  const payees = useMemo(() => readyToPayByEmployee(claims), [claims]);

  const [xeroConnected, setXeroConnected] = useState<boolean | null>(null);
  const [syncingId, setSyncingId] = useState<string | null>(null);
  const [syncError, setSyncError] = useState<string | null>(null);

  useEffect(() => {
    // Unknown until asked; a failed lookup is treated as not connected so the
    // UI never offers a button that can only fail.
    getXeroStatus()
      .then((status) => setXeroConnected(status.connected))
      .catch(() => setXeroConnected(false));
  }, []);

  async function syncPayee(employeeId: string, claimIds: string[]) {
    setSyncingId(employeeId);
    setSyncError(null);
    try {
      // One bill per claim, in order. Sequential rather than parallel: these
      // are writes into an external ledger, and a burst is how you trip Xero's
      // rate limit and half-bill someone.
      for (const id of claimIds) {
        await syncClaimToXero(id);
      }
      onSynced();
    } catch (e) {
      setSyncError(e instanceof Error ? e.message : "Could not push to Xero.");
      // Some may have landed before the failure, so re-read either way.
      onSynced();
    } finally {
      setSyncingId(null);
    }
  }

  const owed = useMemo(() => payees.reduce((total, payee) => total + payee.amount, 0), [payees]);
  const claimCount = useMemo(
    () => payees.reduce((total, payee) => total + payee.claimIds.length, 0),
    [payees],
  );
  const overdue = useMemo(() => payees.filter((p) => p.waitingDays >= OVERDUE_DAYS), [payees]);

  // Named so an admin isn't left wondering where the company-card claims went.
  const companySpend = useMemo(() => claims.filter(isSettledCompanySpend), [claims]);

  const name = (employeeId: string) => {
    const email = employeeEmails.get(employeeId);
    return email ? buildName(email) : employeeId;
  };

  if (payees.length === 0) {
    return (
      <div className="space-y-6">
        <section className={CARD}>
          <CardHead title="Nothing waiting on payment" />
          <EmptyState text="Every approved out-of-pocket claim has been settled. Nothing is owed." />
        </section>
        <CompanySpendNote claims={companySpend} />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* The amount owed, and — the part that actually needs a decision — how
          long the longest-waiting person has been carrying it. */}
      <section className={CARD}>
        <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-start gap-3">
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
              <Wallet className="h-5 w-5" />
            </span>
            <div>
              <p className={EYEBROW}>Owed to employees</p>
              <p className="mt-1 text-3xl font-black leading-none tabular-nums text-foreground">
                {formatCurrency(owed)}
              </p>
              <p className="mt-1.5 text-xs text-muted-foreground">
                {claimCount} claim{claimCount === 1 ? "" : "s"} · {payees.length}{" "}
                {payees.length === 1 ? "person" : "people"} · approved and unpaid
              </p>
            </div>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => onDrill(readyToPayDrilldown())}
              className="inline-flex h-11 items-center gap-2 rounded-full border border-border/60 bg-card px-5 text-sm font-bold text-muted-foreground shadow-sm transition hover:border-primary/40 hover:text-primary"
            >
              <BanknoteArrowUp className="h-4 w-4" />
              Review the run
            </button>
            <AddToPayrollButton
              label={`Add all to payroll`}
              claimIds={payees.flatMap((payee) => payee.claimIds)}
              variant="primary"
            />
          </div>
        </div>

        {!PAYROLL_READY ? (
          <p
            id="payroll-pending"
            className="mt-4 rounded-2xl border border-border/60 bg-surface-low px-4 py-3 text-xs text-muted-foreground"
          >
            <span className="font-semibold text-foreground">Adding to payroll is not live yet.</span>{" "}
            The Payroll module has no draft runs to attach these to, and a claim carries no record
            of being paid — so nothing would survive a refresh. Until then, export the run and pay
            it outside the system.
          </p>
        ) : null}

        {xeroConnected === false ? (
          <p className="mt-3 rounded-2xl border border-border/60 bg-surface-low px-4 py-3 text-xs text-muted-foreground">
            <span className="font-semibold text-foreground">Xero isn't connected.</span> Connect it
            in System Settings to push these as bills.
          </p>
        ) : null}

        {syncError ? (
          <p className="mt-3 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-xs font-medium text-destructive">
            {syncError}
          </p>
        ) : null}

        {overdue.length > 0 ? (
          <p className="mt-4 rounded-2xl border border-tertiary/25 bg-tertiary/5 px-4 py-3 text-xs font-semibold text-tertiary">
            {overdue.length} {overdue.length === 1 ? "person has" : "people have"} been waiting more
            than {OVERDUE_DAYS} days since their claim was approved — the decision is made, so this
            is the company holding their money.
          </p>
        ) : null}
      </section>

      <section className={CARD}>
        <CardHead title="Who to pay" meta="Longest waiting first" />
        <div className="space-y-3">
          {payees.map((payee) => {
            const late = payee.waitingDays >= OVERDUE_DAYS;
            return (
              <div
                key={payee.employeeId}
                className={`group flex flex-wrap items-center justify-between gap-3 transition ${TILE} hover:border-primary/40`}
              >
                {/* The drill is its own control so the row can also carry an
                    action — a button inside a button is invalid markup. */}
                <button
                  type="button"
                  onClick={() =>
                    onDrill(
                      claimIdsDrilldown(
                        payee.claimIds,
                        "Ready to pay",
                        name(payee.employeeId),
                      ),
                    )
                  }
                  className="flex min-w-0 flex-1 items-center justify-between gap-3 text-left"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-bold text-foreground">
                      {name(payee.employeeId)}
                    </p>
                    <p className="truncate text-xs text-muted-foreground">
                      {employeeEmails.get(payee.employeeId) ?? payee.employeeId}
                    </p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {payee.claimIds.length} claim{payee.claimIds.length === 1 ? "" : "s"} ·{" "}
                      <span className={late ? "font-semibold text-tertiary" : ""}>
                        waiting {payee.waitingDays}d
                      </span>
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2 text-right">
                    <div>
                      <p className="text-base font-black tabular-nums text-foreground">
                        {formatCurrency(payee.amount)}
                      </p>
                      <p className={EYEBROW}>to pay</p>
                    </div>
                    <ArrowRight className="h-4 w-4 text-primary opacity-0 transition group-hover:opacity-100" />
                  </div>
                </button>

                <div className="flex shrink-0 items-center gap-2">
                  <XeroState payee={payee} />
                  <button
                    type="button"
                    disabled={
                      xeroConnected !== true ||
                      syncingId !== null ||
                      payee.unsyncedClaimIds.length === 0
                    }
                    title={
                      xeroConnected === false
                        ? "Connect Xero in System Settings first"
                        : payee.unsyncedClaimIds.length === 0
                          ? "Every claim for this person is already billed"
                          : undefined
                    }
                    onClick={() => syncPayee(payee.employeeId, payee.unsyncedClaimIds)}
                    className="inline-flex h-11 shrink-0 items-center gap-2 rounded-full border border-border/60 bg-card px-4 text-sm font-bold text-muted-foreground shadow-sm transition hover:border-primary/40 hover:text-primary disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {syncingId === payee.employeeId ? (
                      <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                    ) : null}
                    {payee.failedCount > 0 ? "Retry Xero" : "Sync to Xero"}
                  </button>
                  <AddToPayrollButton label="Add to payroll" claimIds={payee.claimIds} />
                </div>
              </div>
            );
          })}
        </div>
      </section>

      <CompanySpendNote claims={companySpend} />
    </div>
  );
}

// The one place the Payroll module plugs in.
//
// Locked until PAYROLL_READY. `claimIds` is already the exact set this button
// would submit, so wiring it is a matter of implementing the call — the
// selection logic above needs no change.
function AddToPayrollButton({
  label,
  claimIds,
  variant = "secondary",
}: {
  label: string;
  claimIds: string[];
  variant?: "primary" | "secondary";
}) {
  const disabled = !PAYROLL_READY || claimIds.length === 0;

  const base =
    "inline-flex h-11 shrink-0 items-center gap-2 rounded-full px-5 text-sm font-bold shadow-sm transition disabled:cursor-not-allowed disabled:opacity-60";
  const tone =
    variant === "primary"
      ? "bg-primary text-primary-foreground hover:opacity-90"
      : "border border-border/60 bg-card text-muted-foreground hover:border-primary/40 hover:text-primary";

  return (
    <button
      type="button"
      disabled={disabled}
      title={PAYROLL_READY ? undefined : PAYROLL_PENDING_HINT}
      aria-describedby={PAYROLL_READY ? undefined : "payroll-pending"}
      className={`${base} ${tone}`}
      onClick={() => {
        // Deliberately unreachable while PAYROLL_READY is false.
        // POST the claimIds to the chosen draft run here, then refresh.
      }}
    >
      {PAYROLL_READY ? null : <Lock className="h-3.5 w-3.5" />}
      {label}
    </button>
  );
}

// What Xero knows about this person's claims. Silent when nothing has been
// pushed — an untouched row needs no chip, and a page of "not synced" badges
// says nothing an admin can act on.
function XeroState({ payee }: { payee: PayeeGroup }) {
  if (payee.failedCount > 0) {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-destructive/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-destructive">
        <CircleAlert className="h-3 w-3" />
        {payee.failedCount} failed
      </span>
    );
  }

  if (payee.syncedCount === 0) return null;

  const all = payee.unsyncedClaimIds.length === 0;
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] ${
        all ? "bg-secondary text-secondary-foreground" : "bg-surface-low text-muted-foreground"
      }`}
    >
      <CircleCheck className="h-3 w-3" />
      {all ? "In Xero" : `${payee.syncedCount}/${payee.claimIds.length} in Xero`}
    </span>
  );
}

// Company-paid claims are approved spend, but nobody is owed anything — the
// money already left a company account. Stated rather than silently dropped,
// so the figure above reads as complete.
function CompanySpendNote({ claims }: { claims: Claim[] }) {
  if (claims.length === 0) return null;

  return (
    <p className="px-1 text-xs text-muted-foreground">
      Not counted above: {claims.length} approved claim{claims.length === 1 ? "" : "s"} worth{" "}
      <span className="font-semibold text-foreground">{formatCurrency(sumAmount(claims))}</span>{" "}
      {claims.length === 1 ? "was" : "were"} paid on a company account, so {claims.length === 1 ? "it needs" : "they need"} no reimbursement.
    </p>
  );
}
