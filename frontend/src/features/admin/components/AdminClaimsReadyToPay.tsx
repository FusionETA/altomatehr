import { useEffect, useMemo, useState } from "react";
import {
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
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { formatCurrency, formatShortDate } from "@/features/claims/lib/claim-formatters";
import {
  approvedAgeDays,
  isReadyToPay,
  isSettledCompanySpend,
  sumAmount,
} from "@/features/claims/lib/claim-insights";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { CARD, EYEBROW, TILE } from "../lib/dashboard-styles";
import { claimIdsDrilldown, readyToPayDrilldown, type ClaimDrilldown } from "../lib/claims-drilldown";
import { CardHead, EmptyState } from "./DashboardCard";

// What is settled and still has to reach the ledger.
//
// Listed CLAIM BY CLAIM rather than grouped by person, because a claim is the
// unit Xero actually records — one bill or one spend transaction each. Grouping
// by payee hid which of a person's claims failed and offered no way to retry
// just that one.

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
// and Claim carries no payroll link and no paid marker — so nothing about
// "added to payroll" could survive a refresh.
//
// The button is therefore rendered DISABLED, not wired to a no-op. An admin who
// clicks "Add to payroll" and sees nothing happen would reasonably assume it
// worked, walk away, and never pay the person.
//
// TO WIRE IT: flip PAYROLL_READY, POST the claimIds to the chosen draft run,
// then call onChanged so paid claims drop off this list on their own.
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
  onSynced: () => void;
}) {
  const [side, setSide] = useState<PaymentSide>("PERSONAL");
  const [xeroConnected, setXeroConnected] = useState<boolean | null>(null);
  const [syncingId, setSyncingId] = useState<string | null>(null);
  const [syncError, setSyncError] = useState<string | null>(null);
  const [stage, setStage] = useState<XeroBillStage>("AwaitingPayment");
  const [notesOpen, setNotesOpen] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkResult, setBulkResult] = useState<ClaimsBulkResult | null>(null);
  const [page, setPage] = useState(1);

  const matches = side === "PERSONAL" ? isReadyToPay : isSettledCompanySpend;

  // Longest-waiting first: with the decision already made, waiting is the
  // company holding somebody else's money.
  const rows = useMemo(
    () =>
      claims
        .filter(matches)
        .sort((a, b) => approvedAgeDays(b) - approvedAgeDays(a)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [claims, side],
  );

  const totalPages = Math.max(1, Math.ceil(rows.length / CLAIMS_PAGE_SIZE));
  const paged = useMemo(
    () => rows.slice((page - 1) * CLAIMS_PAGE_SIZE, page * CLAIMS_PAGE_SIZE),
    [rows, page],
  );

  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const people = new Set(rows.map((claim) => claim.employeeId)).size;
  const owed = sumAmount(rows);
  const overdue = rows.filter((claim) => approvedAgeDays(claim) >= OVERDUE_DAYS);

  useEffect(() => {
    getXeroStatus()
      .then((status) => setXeroConnected(status.connected))
      .catch(() => setXeroConnected(false));
  }, []);

  // Ids belong to the side that produced them, so a switch clears them.
  useEffect(() => {
    setSelected(new Set());
    setBulkResult(null);
    setPage(1);
  }, [side]);

  const unsynced = useMemo(
    () => paged.filter((claim) => claim.xeroSyncStatus !== "SYNCED"),
    [paged],
  );
  const selectedRows = unsynced.filter((claim) => selected.has(claim.id));

  // A claim that becomes synced drops out of the selection rather than
  // lingering in the count.
  useEffect(() => {
    setSelected((current) => {
      const available = new Set(unsynced.map((claim) => claim.id));
      const next = new Set([...current].filter((id) => available.has(id)));
      return next.size === current.size ? current : next;
    });
  }, [unsynced]);

  const allSelected = unsynced.length > 0 && selectedRows.length === unsynced.length;
  const toggleAll = () =>
    setSelected(allSelected ? new Set() : new Set(unsynced.map((claim) => claim.id)));

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function syncOne(claim: Claim) {
    setSyncingId(claim.id);
    setSyncError(null);
    try {
      await syncClaimToXero(claim.id, stage);
    } catch (e) {
      setSyncError(e instanceof Error ? e.message : "Could not push to Xero.");
    } finally {
      setSyncingId(null);
      onSynced();
    }
  }

  async function syncSelected() {
    if (selectedRows.length === 0) return;

    setBulkBusy(true);
    setSyncError(null);
    setBulkResult(null);
    try {
      setBulkResult(await bulkSyncClaimsToXero(selectedRows.map((c) => c.id), stage));
      setSelected(new Set());
    } catch (e) {
      setSyncError(e instanceof Error ? e.message : "Could not push to Xero.");
    } finally {
      setBulkBusy(false);
      onSynced();
    }
  }

  const name = (employeeId: string) => {
    const email = employeeEmails.get(employeeId);
    return email ? buildName(email) : employeeId;
  };

  const notices = [
    !PAYROLL_READY && side === "PERSONAL"
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

  const isPersonal = side === "PERSONAL";

  return (
    <div className="space-y-6">
      <OverflowTabList<PaymentSide>
        items={[
          { id: "PERSONAL", label: SIDE_LABELS.PERSONAL },
          { id: "COMPANY", label: SIDE_LABELS.COMPANY },
        ]}
        value={side}
        onChange={setSide}
        variant="segmented"
        ariaLabel="Which claims to settle"
      />

      {rows.length === 0 ? (
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
                    {rows.length} claim{rows.length === 1 ? "" : "s"} · {people}{" "}
                    {people === 1 ? "person" : "people"} ·{" "}
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
                {isPersonal ? (
                  <AddToPayrollButton
                    label="Add all to payroll"
                    claimIds={rows.map((claim) => claim.id)}
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
                      <span className="font-semibold text-foreground">{note.title}</span>{" "}
                      {note.body}
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
                {overdue.length} claim{overdue.length === 1 ? " has" : "s have"} been waiting more
                than {OVERDUE_DAYS} days since approval — the decision is made, so this is the
                company holding their money.
              </p>
            ) : null}
          </section>

          <section className={CARD}>
            <div className="flex flex-wrap items-center justify-between gap-3 pb-3">
              <div className="flex items-center gap-3">
                {unsynced.length > 0 ? (
                  <input
                    type="checkbox"
                    aria-label="Select every claim on this page still to push"
                    checked={allSelected}
                    onChange={toggleAll}
                    className="h-4 w-4 cursor-pointer accent-primary"
                  />
                ) : null}
                <h3 className="text-base font-black text-foreground">
                  {isPersonal ? "Claims to pay" : "Claims to record"}
                </h3>
              </div>

              <div className="flex items-center gap-2">
                <span className={EYEBROW}>Push to Xero as</span>
                {/* The shared Select, not a native one: a bare <select> drops
                    to the OS menu — system font, blue highlight — beside
                    controls that are all themed. */}
                <Select
                  value={stage}
                  onValueChange={(value) => setStage(value as XeroBillStage)}
                  disabled={xeroConnected !== true || !isPersonal}
                >
                  <SelectTrigger
                    className="h-9 w-[168px] rounded-full bg-card text-xs font-bold"
                    title={
                      isPersonal
                        ? undefined
                        : "Company spend has already left the account, so Xero records it as authorised either way."
                    }
                  >
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="AwaitingPayment">Awaiting payment</SelectItem>
                    <SelectItem value="Draft">Draft</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            {selectedRows.length > 0 ? (
              <div className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-primary/30 bg-primary/5 px-4 py-3">
                <div className="min-w-0">
                  <p className="text-sm font-bold text-foreground">
                    {selectedRows.length} claim{selectedRows.length === 1 ? "" : "s"} selected
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {formatCurrency(sumAmount(selectedRows))}
                    {isPersonal ? ` as ${stage === "Draft" ? "drafts" : "awaiting payment"}` : ""}
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
                    title={
                      xeroConnected === false ? "Connect Xero in System Settings first" : undefined
                    }
                    onClick={syncSelected}
                    className="inline-flex items-center gap-2 rounded-full bg-primary px-5 py-2 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {bulkBusy ? (
                      <LoaderCircle className="h-4 w-4 animate-spin" />
                    ) : (
                      <CloudUpload className="h-4 w-4" />
                    )}
                    Sync {selectedRows.length} to Xero
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
                {bulkResult.items.filter((item) => !item.ok).length > 0 ? (
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
              {paged.map((claim) => {
                const synced = claim.xeroSyncStatus === "SYNCED";
                const failed = claim.xeroSyncStatus === "ERROR";
                const waiting = approvedAgeDays(claim);

                return (
                  <div
                    key={claim.id}
                    className={`group flex flex-wrap items-center justify-between gap-3 transition ${TILE} hover:border-primary/40`}
                  >
                    {!synced ? (
                      <input
                        type="checkbox"
                        aria-label={`Select ${claim.claimNumber}`}
                        checked={selected.has(claim.id)}
                        onChange={() => toggle(claim.id)}
                        className="h-4 w-4 shrink-0 cursor-pointer accent-primary"
                      />
                    ) : (
                      <span className="h-4 w-4 shrink-0" />
                    )}

                    <button
                      type="button"
                      onClick={() =>
                        onDrill(claimIdsDrilldown([claim.id], claim.claimNumber, claim.title))
                      }
                      className="flex min-w-0 flex-1 items-center justify-between gap-3 text-left"
                    >
                      <div className="min-w-0">
                        <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                          {claim.claimNumber}
                        </p>
                        <p className="truncate text-sm font-bold text-foreground">{claim.title}</p>
                        <p className="mt-0.5 text-xs text-muted-foreground">
                          {name(claim.employeeId)} · {formatShortDate(claim.spentAt)}
                          {isPersonal ? (
                            <>
                              {" · "}
                              <span className={waiting >= OVERDUE_DAYS ? "font-semibold text-tertiary" : ""}>
                                waiting {waiting}d
                              </span>
                            </>
                          ) : null}
                        </p>
                      </div>
                      <div className="shrink-0 text-right">
                        <p className="text-base font-black tabular-nums text-foreground">
                          {formatCurrency(claim.amount, claim.currency)}
                        </p>
                        <p className={EYEBROW}>{isPersonal ? "to pay" : "spent"}</p>
                      </div>
                    </button>

                    <div className="flex shrink-0 items-center gap-2">
                      <XeroState claim={claim} />

                      {synced ? null : (
                        <button
                          type="button"
                          disabled={xeroConnected !== true || syncingId !== null || bulkBusy}
                          title={
                            xeroConnected === false
                              ? "Connect Xero in System Settings first"
                              : undefined
                          }
                          onClick={() => syncOne(claim)}
                          className="inline-flex h-10 shrink-0 items-center gap-2 rounded-full border border-border/60 bg-card px-4 text-xs font-bold text-muted-foreground shadow-sm transition hover:border-primary/40 hover:text-primary disabled:cursor-not-allowed disabled:opacity-50"
                        >
                          {syncingId === claim.id ? (
                            <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                          ) : null}
                          {failed ? "Retry" : "Sync"}
                        </button>
                      )}

                      {isPersonal ? (
                        <AddToPayrollButton label="Payroll" claimIds={[claim.id]} />
                      ) : null}
                    </div>
                  </div>
                );
              })}
            </div>

            <PaginationControls
              className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
              currentPage={page}
              totalItems={rows.length}
              onPageChange={setPage}
            />
          </section>
        </>
      )}
    </div>
  );
}

// Per claim, because a claim is one Xero record. ERROR carries the reason Xero
// or the pre-flight check gave, which is the whole point of storing it.
function XeroState({ claim }: { claim: Claim }) {
  if (claim.xeroSyncStatus === "SYNCED") {
    return (
      <span
        title={claim.xeroBillRef ?? undefined}
        className="inline-flex items-center gap-1.5 rounded-full bg-secondary px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-secondary-foreground"
      >
        <CircleCheck className="h-3 w-3" />
        In Xero
      </span>
    );
  }

  if (claim.xeroSyncStatus === "ERROR") {
    return (
      <span
        title={claim.xeroSyncError ?? undefined}
        className="inline-flex max-w-[14rem] items-center gap-1.5 rounded-full bg-destructive/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-destructive"
      >
        <CircleAlert className="h-3 w-3 shrink-0" />
        <span className="truncate normal-case tracking-normal">
          {claim.xeroSyncError ?? "Failed"}
        </span>
      </span>
    );
  }

  // Never pushed needs no badge — the Sync button already says so.
  return null;
}

// The one place the Payroll module plugs in. Locked until PAYROLL_READY;
// claimIds is already the exact set it would submit.
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
    "inline-flex h-10 shrink-0 items-center gap-2 rounded-full px-4 text-xs font-bold shadow-sm transition disabled:cursor-not-allowed disabled:opacity-60";
  const tone =
    variant === "primary"
      ? "h-11 bg-primary px-5 text-sm text-primary-foreground hover:opacity-90"
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
      }}
    >
      {PAYROLL_READY ? null : <Lock className="h-3.5 w-3.5" />}
      {label}
    </button>
  );
}
