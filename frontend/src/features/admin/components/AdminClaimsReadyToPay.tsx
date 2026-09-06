import { useEffect, useMemo, useState } from "react";
import {
  ArrowRight,
  BanknoteArrowUp,
  ChevronDown,
  CircleAlert,
  CircleCheck,
  CloudUpload,
  Info,
  LoaderCircle,
  Lock,
  Wallet,
} from "lucide-react";
import {
  bulkSyncClaimsToXero,
  syncClaimToXero,
  type Claim,
  type ClaimsBulkResult,
  type XeroBillStage,
} from "@/features/claims/api";
import { getXeroStatus } from "@/features/settings/api";
import { formatCurrency } from "@/features/claims/lib/claim-formatters";
import {
  isReadyToPay,
  isSettledCompanySpend,
  settledByEmployee,
  type PayeeGroup,
} from "@/features/claims/lib/claim-insights";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
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

// The two halves of a settled claim, which Xero records differently:
// PERSONAL becomes a bill the org owes, COMPANY a spend that already left an
// account. Same page, different obligations.
type PaymentSide = "PERSONAL" | "COMPANY";

const SIDE_LABELS: Record<PaymentSide, string> = {
  PERSONAL: "Owed to employees",
  COMPANY: "Company spend",
};

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
  // Personal and company claims become genuinely different records in Xero — a
  // bill you owe versus money that already left the account — so they are two
  // lists, not one list with a column.
  const [side, setSide] = useState<PaymentSide>("PERSONAL");

  const personal = useMemo(() => settledByEmployee(claims, isReadyToPay), [claims]);
  const company = useMemo(() => settledByEmployee(claims, isSettledCompanySpend), [claims]);
  const payees = side === "PERSONAL" ? personal : company;

  const [xeroConnected, setXeroConnected] = useState<boolean | null>(null);
  const [syncingId, setSyncingId] = useState<string | null>(null);
  const [syncError, setSyncError] = useState<string | null>(null);
  // Which state a pushed bill lands in. Defaults to a live payable: the claim
  // has cleared its chain, so the org genuinely owes the money.
  const [stage, setStage] = useState<XeroBillStage>("AwaitingPayment");
  // The "not live yet" explanations are collapsed — they are the same every
  // visit, and an admin who has read them once should not have to scroll past
  // them to reach the people they owe.
  const [notesOpen, setNotesOpen] = useState(false);
  // Selection is by payee, matching how the list is grouped — an admin picks
  // people to pay, then expands to their claims underneath.
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkResult, setBulkResult] = useState<ClaimsBulkResult | null>(null);

  useEffect(() => {
    // Unknown until asked; a failed lookup is treated as not connected so the
    // UI never offers a button that can only fail.
    getXeroStatus()
      .then((status) => setXeroConnected(status.connected))
      .catch(() => setXeroConnected(false));
  }, []);

  // Only payees with something left to push can be selected; the rest are
  // already in Xero and would just be noise in the count.
  const syncable = useMemo(
    () => payees.filter((payee) => payee.unsyncedClaimIds.length > 0),
    [payees],
  );
  const selectedPayees = useMemo(
    () => syncable.filter((payee) => selected.has(payee.employeeId)),
    [syncable, selected],
  );
  const selectedClaimIds = selectedPayees.flatMap((payee) => payee.unsyncedClaimIds);
  const selectedTotal = selectedPayees.reduce((sum, payee) => sum + payee.amount, 0);

  // A payee who becomes fully synced drops out of the selection rather than
  // lingering in the count.
  useEffect(() => {
    setSelected((current) => {
      const available = new Set(syncable.map((payee) => payee.employeeId));
      const next = new Set([...current].filter((id) => available.has(id)));
      return next.size === current.size ? current : next;
    });
  }, [syncable]);

  useEffect(() => {
    setSelected(new Set());
    setBulkResult(null);
  }, [side]);

  const allSelected = syncable.length > 0 && selectedPayees.length === syncable.length;
  const toggleAll = () =>
    setSelected(allSelected ? new Set() : new Set(syncable.map((p) => p.employeeId)));

  function togglePayee(employeeId: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(employeeId)) next.delete(employeeId);
      else next.add(employeeId);
      return next;
    });
  }

  async function syncSelected() {
    if (selectedClaimIds.length === 0) return;

    setBulkBusy(true);
    setSyncError(null);
    setBulkResult(null);
    try {
      setBulkResult(await bulkSyncClaimsToXero(selectedClaimIds, stage));
      setSelected(new Set());
    } catch (e) {
      setSyncError(e instanceof Error ? e.message : "Could not push to Xero.");
    } finally {
      setBulkBusy(false);
      onSynced();
    }
  }

  async function syncPayee(employeeId: string, claimIds: string[]) {
    setSyncingId(employeeId);
    setSyncError(null);
    try {
      // One bill per claim, in order. Sequential rather than parallel: these
      // are writes into an external ledger, and a burst is how you trip Xero's
      // rate limit and half-bill someone.
      for (const id of claimIds) {
        await syncClaimToXero(id, stage);
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


  // Gathered rather than rendered inline so the toggle can count them and the
  // buttons can still point at one by id.
  const notices = [
    !PAYROLL_READY
      ? {
          id: "payroll-pending",
          title: "Adding to payroll is not live yet.",
          body: "The Payroll module has no draft runs to attach these to, and a claim carries no record of being paid — so nothing would survive a refresh. Until then, export the run and pay it outside the system.",
        }
      : null,
    xeroConnected === false
      ? {
          id: "xero-pending",
          title: "Xero isn't connected.",
          body: "Connect it in System Settings to push these as bills.",
        }
      : null,
  ].filter((note) => note !== null);

  const name = (employeeId: string) => {
    const email = employeeEmails.get(employeeId);
    return email ? buildName(email) : employeeId;
  };


  const isPersonal = side === "PERSONAL";

  return (
    <div className="space-y-6">
      {/* OverflowTabList directly, not StatusFilterTabs: that one prepends an
          "All" tab, and there is no "all" here — a claim is either owed back
          or already spent, and the two become different records in Xero. */}
      <OverflowTabList<PaymentSide>
        // No badges: the segmented variant stacks them under the label, and the
        // count is already spelled out in the panel directly below.
        items={[
          { id: "PERSONAL", label: SIDE_LABELS.PERSONAL },
          { id: "COMPANY", label: SIDE_LABELS.COMPANY },
        ]}
        value={side}
        onChange={setSide}
        variant="segmented"
        ariaLabel="Which claims to settle"
      />

      {payees.length === 0 ? (
        <section className={CARD}>
          <CardHead
            title={isPersonal ? "Nothing waiting on payment" : "No company spend to record"}
          />
          <EmptyState
            text={
              isPersonal
                ? "Every approved out-of-pocket claim has been settled. Nothing is owed."
                : "No approved claims were paid from a company account."
            }
          />
        </section>
      ) : (
      <>
      {/* The amount, and — the part that actually needs a decision — how long
          the longest-waiting person has been carrying it. */}
      <section className={CARD}>
        <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-start gap-3">
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
              <Wallet className="h-5 w-5" />
            </span>
            <div>
              <p className={EYEBROW}>
                {isPersonal ? "Owed to employees" : "Paid from company accounts"}
              </p>
              <p className="mt-1 text-3xl font-black leading-none tabular-nums text-foreground">
                {formatCurrency(owed)}
              </p>
              <p className="mt-1.5 text-xs text-muted-foreground">
                {claimCount} claim{claimCount === 1 ? "" : "s"} · {payees.length}{" "}
                {payees.length === 1 ? "person" : "people"} ·{" "}
                {isPersonal ? "approved and unpaid" : "already spent — record it in Xero"}
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
            {/* Payroll only means anything for money still owed — company
                spend has already left the account. */}
            {isPersonal ? (
              <AddToPayrollButton
                label="Add all to payroll"
                claimIds={payees.flatMap((payee) => payee.claimIds)}
                variant="primary"
              />
            ) : null}
          </div>
        </div>

        {notices.length > 0 ? (
          <div className="mt-4">
            <button
              type="button"
              aria-expanded={notesOpen}
              aria-controls="payment-run-notices"
              onClick={() => setNotesOpen((open) => !open)}
              className="inline-flex items-center gap-1.5 text-xs font-semibold text-muted-foreground transition hover:text-foreground"
            >
              <Info className="h-3.5 w-3.5" />
              {notices.length === 1
                ? "1 thing isn't set up yet"
                : `${notices.length} things aren't set up yet`}
              <ChevronDown
                className={`h-3.5 w-3.5 transition-transform ${notesOpen ? "rotate-180" : ""}`}
              />
            </button>

            <div id="payment-run-notices" hidden={!notesOpen} className="mt-3 space-y-2">
              {notices.map((note) => (
                <p
                  key={note.title}
                  id={note.id}
                  className="rounded-2xl border border-border/60 bg-surface-low px-4 py-3 text-xs text-muted-foreground"
                >
                  <span className="font-semibold text-foreground">{note.title}</span> {note.body}
                </p>
              ))}
            </div>
          </div>
        ) : null}

        {syncError ? (
          <p className="mt-3 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-xs font-medium text-destructive">
            {syncError}
          </p>
        ) : null}

        {isPersonal && overdue.length > 0 ? (
          <p className="mt-4 rounded-2xl border border-tertiary/25 bg-tertiary/5 px-4 py-3 text-xs font-semibold text-tertiary">
            {overdue.length} {overdue.length === 1 ? "person has" : "people have"} been waiting more
            than {OVERDUE_DAYS} days since their claim was approved — the decision is made, so this
            is the company holding their money.
          </p>
        ) : null}
      </section>

      <section className={CARD}>
        <div className="flex flex-wrap items-center justify-between gap-3 pb-3">
          <div className="flex items-center gap-3">
            {syncable.length > 0 ? (
              <input
                type="checkbox"
                aria-label="Select everyone with claims still to push"
                checked={allSelected}
                onChange={toggleAll}
                className="h-4 w-4 cursor-pointer accent-primary"
              />
            ) : null}
            <h3 className="text-base font-black text-foreground">Who to pay</h3>
          </div>

          <div className="flex items-center gap-2">
            <span className={EYEBROW}>Push to Xero as</span>
            {/* One choice for the whole run: an admin pushing five people's
                claims means the same thing by all of them. */}
            <select
              value={stage}
              onChange={(event) => setStage(event.target.value as XeroBillStage)}
              disabled={xeroConnected !== true}
              className="h-9 rounded-full border border-border/60 bg-card px-3 text-xs font-bold text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50"
            >
              <option value="AwaitingPayment">Awaiting payment</option>
              <option value="Draft">Draft</option>
            </select>
          </div>
        </div>
        {selectedPayees.length > 0 ? (
          <div className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-primary/30 bg-primary/5 px-4 py-3">
            <div className="min-w-0">
              <p className="text-sm font-bold text-foreground">
                {selectedPayees.length} {selectedPayees.length === 1 ? "person" : "people"} ·{" "}
                {selectedClaimIds.length} claim{selectedClaimIds.length === 1 ? "" : "s"}
              </p>
              {/* The money, not just a count — this is a push into the ledger. */}
              <p className="text-xs text-muted-foreground">
                {formatCurrency(selectedTotal)} as{" "}
                {stage === "Draft" ? "drafts" : "awaiting payment"}
              </p>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <button
                type="button"
                disabled={bulkBusy}
                onClick={() => setSelected(new Set())}
                className="rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
              >
                Clear
              </button>
              <button
                type="button"
                disabled={bulkBusy || xeroConnected !== true}
                title={xeroConnected === false ? "Connect Xero in System Settings first" : undefined}
                onClick={syncSelected}
                className="inline-flex items-center gap-2 rounded-full bg-primary px-5 py-2 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {bulkBusy ? (
                  <LoaderCircle className="h-4 w-4 animate-spin" />
                ) : (
                  <CloudUpload className="h-4 w-4" />
                )}
                Sync {selectedClaimIds.length} to Xero
              </button>
            </div>
          </div>
        ) : null}

        {bulkResult ? (
          <div className="mb-3 rounded-2xl border border-border/60 bg-surface-low px-4 py-3">
            <div className="flex items-start justify-between gap-3">
              <p className="text-sm font-bold text-foreground">
                {bulkResult.succeeded} pushed to Xero
                {bulkResult.failed > 0 ? ` · ${bulkResult.failed} not pushed` : ""}
              </p>
              <button
                type="button"
                onClick={() => setBulkResult(null)}
                aria-label="Dismiss Xero result"
                className="text-muted-foreground transition hover:text-foreground"
              >
                ×
              </button>
            </div>
            {bulkResult.items.filter((i) => !i.ok).length > 0 ? (
              <ul className="nice-scrollbar mt-2 max-h-32 space-y-1.5 overflow-y-auto">
                {bulkResult.items
                  .filter((item) => !item.ok)
                  .map((item, index) => (
                    <li
                      key={`${item.id}-${index}`}
                      className="rounded-xl bg-destructive/5 px-3 py-2 text-xs text-destructive"
                    >
                      {item.error ?? "Could not be pushed."}
                    </li>
                  ))}
              </ul>
            ) : null}
          </div>
        ) : null}

        <div className="space-y-3">
          {payees.map((payee) => {
            const late = payee.waitingDays >= OVERDUE_DAYS;
            return (
              <div
                key={payee.employeeId}
                className={`group flex flex-wrap items-center justify-between gap-3 transition ${TILE} hover:border-primary/40`}
              >
                {payee.unsyncedClaimIds.length > 0 ? (
                  <input
                    type="checkbox"
                    aria-label={`Select ${name(payee.employeeId)}`}
                    checked={selected.has(payee.employeeId)}
                    onChange={() => togglePayee(payee.employeeId)}
                    className="h-4 w-4 shrink-0 cursor-pointer accent-primary"
                  />
                ) : null}

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
                  {isPersonal ? (
                    <AddToPayrollButton label="Add to payroll" claimIds={payee.claimIds} />
                  ) : null}
                </div>
              </div>
            );
          })}
        </div>
      </section>

      </>
      )}
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

