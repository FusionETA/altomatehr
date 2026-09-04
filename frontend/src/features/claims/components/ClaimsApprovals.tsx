import { useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import { CheckCheck, LoaderCircle, TriangleAlert, X } from "lucide-react";
import {
  approveClaim,
  bulkApproveClaims,
  getTeamClaims,
  rejectClaim,
  type Claim,
  type ClaimsBulkResult,
} from "../api";
import {
  claimMatchesStatus,
  type ClaimStatusFilter,
} from "../lib/claim-status";
import { formatCurrency, formatShortDate } from "../lib/claim-formatters";
import { ClaimStatusBadge } from "./ClaimStatusBadge";
import { ClaimDetailsModal } from "./ClaimDetailsModal";
import { ClaimStatusTabs } from "./ClaimStatusTabs";
import { OverLimitBadge } from "./OverLimitBadge";
import { CLAIMS_PAGE_SIZE, PaginationControls } from "./PaginationControls";
import { getAccounts } from "@/features/settings/api";
import { buildName, displayPerson } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// onDecided lets the shell refresh its sidebar badge — the count lives up
// there and has no other way to learn a claim just left the queue.
export function ClaimsApprovals({ onDecided }: { onDecided?: () => void } = {}) {
  const [claims, setClaims] = useState<Claim[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<ClaimStatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [page, setPage] = useState(1);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [selectedClaim, setSelectedClaim] = useState<Claim | null>(null);
  const [rejectingClaim, setRejectingClaim] = useState<Claim | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);
  const [accountLabels, setAccountLabels] = useState<Map<string, string>>(new Map());
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkResult, setBulkResult] = useState<ClaimsBulkResult | null>(null);

  useEffect(() => {
    getTeamClaims()
      .then(setClaims)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    getAccounts()
      .then((accounts) => setAccountLabels(new Map(accounts.map((a) => [a.id, `${a.code} · ${a.name}`]))))
      .catch(() => {
        /* labels stay empty */
      });
  }, []);

  const employeeName = (c: Claim) => (c.employeeEmail ? buildName(c.employeeEmail) : "—");
  const accountLabel = (c: Claim) =>
    c.chartOfAccountId ? accountLabels.get(c.chartOfAccountId) ?? "Not assigned" : "Not assigned";

  const filteredClaims = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    return claims.filter((claim) => {
      const matchesStatus = claimMatchesStatus(claim, status);
      const acc = claim.chartOfAccountId ? accountLabels.get(claim.chartOfAccountId) : "";
      const matchesQuery =
        query.length === 0
          ? true
          : [claim.claimNumber, claim.title, employeeName(claim), claim.employeeEmail, claim.category, acc]
              .filter(Boolean)
              .join(" ")
              .toLowerCase()
              .includes(query);
      return matchesStatus && matchesQuery;
    });
  }, [claims, searchTerm, status, accountLabels]);

  useEffect(() => {
    setPage(1);
  }, [searchTerm, status]);

  const totalPages = Math.max(1, Math.ceil(filteredClaims.length / CLAIMS_PAGE_SIZE));
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const paginatedClaims = useMemo(() => {
    const start = (page - 1) * CLAIMS_PAGE_SIZE;
    return filteredClaims.slice(start, start + CLAIMS_PAGE_SIZE);
  }, [filteredClaims, page]);

  const hasActiveFilters = status !== "ALL" || searchTerm.trim().length > 0;

  // Bulk approval is only offered on claims that are actually decidable AND
  // safe to wave through in a batch. An over-limit claim blew past the
  // account's spend limit — that is precisely the one somebody should open and
  // read, so it is never selectable here.
  const isBulkable = (claim: Claim) => !!claim.canAct && !claim.exceedsLimit;

  const bulkable = useMemo(() => filteredClaims.filter(isBulkable), [filteredClaims]);
  const selectedClaims = useMemo(
    () => bulkable.filter((claim) => selected.has(claim.id)),
    [bulkable, selected],
  );
  const selectedTotal = selectedClaims.reduce((sum, claim) => sum + claim.amount, 0);
  const excludedOverLimit = filteredClaims.filter(
    (claim) => claim.canAct && claim.exceedsLimit,
  ).length;

  // Dropping a claim out of the filtered view should drop it out of the
  // selection too, or an approver could submit rows they can no longer see.
  useEffect(() => {
    setSelected((current) => {
      const visible = new Set(bulkable.map((claim) => claim.id));
      const next = new Set([...current].filter((id) => visible.has(id)));
      return next.size === current.size ? current : next;
    });
  }, [bulkable]);

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const allSelected = bulkable.length > 0 && selectedClaims.length === bulkable.length;
  const toggleAll = () =>
    setSelected(allSelected ? new Set() : new Set(bulkable.map((claim) => claim.id)));

  async function confirmBulkApprove() {
    if (selectedClaims.length === 0) return;

    setBulkBusy(true);
    setError(null);
    try {
      const result = await bulkApproveClaims(selectedClaims.map((claim) => claim.id));
      setBulkResult(result);
      setSelected(new Set());
      // Re-read rather than patching each row: a claim on a multi-step chain
      // stays PENDING and moves to the next approver, so it may leave this
      // queue entirely.
      setClaims(await getTeamClaims());
      onDecided?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not approve those claims.");
    } finally {
      setBulkBusy(false);
    }
  }

  async function decide(id: string, fn: (id: string) => Promise<Claim>) {
    setBusyId(id);
    setError(null);
    try {
      const updated = await fn(id);

      // Re-read rather than patching the row in place. /claims/team returns
      // only what is still awaiting THIS approver, so a decided claim has left
      // the queue — either finished, or moved to the next step's approver.
      // Patching it kept a settled claim on screen with a stale status.
      setClaims(await getTeamClaims());
      setSelectedClaim((cur) =>
        cur?.id === updated.id
          ? { ...updated, employeeEmail: cur.employeeEmail }
          : cur,
      );
      onDecided?.();
      return true;
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not update the claim.");
      return false;
    } finally {
      setBusyId(null);
    }
  }

  function openRejectDialog(claim: Claim) {
    setRejectingClaim(claim);
    setRejectNotes("");
    setRejectError(null);
  }

  function closeRejectDialog() {
    if (busyId === rejectingClaim?.id) return;
    setRejectingClaim(null);
    setRejectNotes("");
    setRejectError(null);
  }

  async function confirmReject() {
    if (!rejectingClaim) return;

    const notes = rejectNotes.trim();
    if (!notes) {
      setRejectError("Remark is required when rejecting a claim.");
      return;
    }

    const ok = await decide(rejectingClaim.id, (id) => rejectClaim(id, notes));
    if (ok) closeRejectDialog();
  }

  function actions(claim: Claim, variant: "compact" | "detail" = "compact") {
    // CanAct, not status. The team view now includes settled claims and ones
    // waiting on a different step's approver — offering buttons on those would
    // just produce a 404.
    if (!claim.canAct) return null;
    const isDetail = variant === "detail";

    return (
      <div className={isDetail ? "grid w-full grid-cols-2 gap-3 sm:flex sm:w-auto sm:items-center" : "flex items-center gap-2"}>
        <button
          type="button"
          disabled={busyId === claim.id}
          onClick={(event) => {
            event.stopPropagation();
            decide(claim.id, approveClaim);
          }}
          className={
            isDetail
              ? "inline-flex h-12 items-center justify-center gap-2 rounded-[18px] bg-secondary px-6 text-sm font-bold text-secondary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
              : "inline-flex items-center gap-1.5 rounded-full bg-secondary px-3 py-1.5 text-xs font-semibold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
          }
        >
          {busyId === claim.id ? (
            <LoaderCircle className={isDetail ? "h-4 w-4 animate-spin" : "h-3 w-3 animate-spin"} />
          ) : null}
          Approve
        </button>
        <button
          type="button"
          disabled={busyId === claim.id}
          onClick={(event) => {
            event.stopPropagation();
            openRejectDialog(claim);
          }}
          className={
            isDetail
              ? "inline-flex h-12 items-center justify-center rounded-[18px] bg-destructive/10 px-6 text-sm font-bold text-destructive shadow-sm transition hover:bg-destructive/20 disabled:opacity-50"
              : "rounded-full bg-destructive/10 px-3 py-1.5 text-xs font-semibold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
          }
        >
          Reject
        </button>
      </div>
    );
  }

  function handleClaimKeyDown(event: KeyboardEvent, claim: Claim) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setSelectedClaim(claim);
    }
  }

  return (
    <>
      <div className="mb-4 space-y-3 md:hidden">
        <ClaimStatusTabs value={status} onChange={setStatus} />
        <SearchInput
          value={searchTerm}
          onChange={setSearchTerm}
          placeholder="Search by claim, employee, or account"
          inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
        />
      </div>

      <div className="space-y-4 sm:space-y-6">
        <section className={`hidden md:block ${CARD}`}>
          <div className="space-y-4 px-5 pb-5 pt-3 sm:space-y-5 sm:p-6">
            <SearchInput
              value={searchTerm}
              onChange={setSearchTerm}
              placeholder="Search by claim, employee, or account"
              className="max-w-sm"
              inputClassName="h-12"
            />

            <ClaimStatusTabs value={status} onChange={setStatus} />

            <div className="flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
              <p>
                Showing <span className="font-semibold text-foreground">{filteredClaims.length}</span> of{" "}
                <span className="font-semibold text-foreground">{claims.length}</span> claims
              </p>
              {hasActiveFilters ? (
                <button
                  type="button"
                  className="w-fit rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                  onClick={() => {
                    setStatus("ALL");
                    setSearchTerm("");
                  }}
                >
                  Clear filters
                </button>
              ) : null}
            </div>
          </div>
        </section>

        <div className="text-sm text-muted-foreground md:hidden">
          <p>
            Showing <span className="font-semibold text-foreground">{filteredClaims.length}</span> of{" "}
            <span className="font-semibold text-foreground">{claims.length}</span> claims
          </p>
        </div>

        {bulkResult ? (
          <BulkResultPanel result={bulkResult} onDismiss={() => setBulkResult(null)} />
        ) : null}

        {selectedClaims.length > 0 ? (
          <div className="sticky top-20 z-20 flex flex-wrap items-center justify-between gap-3 rounded-[24px] border border-primary/30 bg-primary/5 px-5 py-4 backdrop-blur-sm">
            <div className="min-w-0">
              <p className="text-sm font-bold text-foreground">
                {selectedClaims.length} claim{selectedClaims.length === 1 ? "" : "s"} selected
              </p>
              {/* The amount, not just the count: a number hides what is being
                  signed off, a total does not. */}
              <p className="text-xs text-muted-foreground">
                Approving {formatCurrency(selectedTotal)} in one go
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
                disabled={bulkBusy}
                onClick={confirmBulkApprove}
                className="inline-flex items-center gap-2 rounded-full bg-secondary px-5 py-2 text-sm font-bold text-secondary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
              >
                {bulkBusy ? (
                  <LoaderCircle className="h-4 w-4 animate-spin" />
                ) : (
                  <CheckCheck className="h-4 w-4" />
                )}
                Approve {selectedClaims.length}
              </button>
            </div>
          </div>
        ) : null}

        {excludedOverLimit > 0 ? (
          <p className="flex items-start gap-2 px-1 text-xs text-muted-foreground">
            <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-600" />
            {excludedOverLimit} over-limit claim{excludedOverLimit === 1 ? " is" : "s are"} not
            selectable — open {excludedOverLimit === 1 ? "it" : "them"} and approve individually.
          </p>
        ) : null}

        {loading ? (
          <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading claims…</section>
        ) : null}

        {error ? (
          <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : null}

        {!loading && !error && filteredClaims.length === 0 ? (
          <section className={`${CARD} p-8 text-center`}>
            <p className="text-lg font-bold text-foreground">No claims match this filter.</p>
            <p className="mt-2 text-sm text-muted-foreground">
              Try a different status or clear the search term.
            </p>
          </section>
        ) : null}

        {/* Mobile cards */}
        {!loading && !error && filteredClaims.length > 0 ? (
          <div className="grid gap-3 sm:gap-4 md:hidden">
            {paginatedClaims.map((claim) => (
              <article
                key={claim.id}
                role="button"
                tabIndex={0}
                onClick={() => setSelectedClaim(claim)}
                onKeyDown={(event) => handleClaimKeyDown(event, claim)}
                className={`${CARD} cursor-pointer space-y-4 p-4 transition hover:border-primary/40 focus-visible:border-primary/50 focus-visible:outline-none sm:p-5`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="flex min-w-0 items-start gap-3">
                    {isBulkable(claim) ? (
                      <input
                        type="checkbox"
                        aria-label={`Select ${claim.claimNumber}`}
                        checked={selected.has(claim.id)}
                        onClick={(e) => e.stopPropagation()}
                        onChange={() => toggle(claim.id)}
                        className="mt-1 h-4 w-4 shrink-0 cursor-pointer accent-primary"
                      />
                    ) : null}
                    <div className="min-w-0">
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      {claim.claimNumber}
                    </p>
                    <p className="mt-1 text-base font-black">{claim.title}</p>
                    <p className="text-sm text-muted-foreground">{employeeName(claim)}</p>
                    </div>
                  </div>
                  <div className="flex shrink-0 flex-col items-end gap-1.5">
                    <ClaimStatusBadge status={claim.status} />
                    {claim.exceedsLimit ? <OverLimitBadge /> : null}
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Account</p>
                    <p className="mt-1 text-sm font-semibold">{accountLabel(claim)}</p>
                  </div>
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Amount</p>
                    <p className="mt-1 text-sm font-semibold">{formatCurrency(claim.amount, claim.currency)}</p>
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3 rounded-2xl bg-surface-low p-4">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Submitted</p>
                    <p className="mt-1 text-sm font-semibold">
                      {formatShortDate(claim.submittedAt || claim.spentAt)}
                    </p>
                  </div>
                  {actions(claim)}
                </div>
              </article>
            ))}
          </div>
        ) : null}

        {/* Desktop table */}
        {!loading && !error && filteredClaims.length > 0 ? (
          <section className={`hidden md:block ${CARD}`}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[1020px] caption-bottom text-sm">
                <thead>
                  <tr className="border-b border-border/60">
                    <th className="h-12 w-12 pl-6 text-left">
                      <input
                        type="checkbox"
                        aria-label="Select every claim that can be bulk-approved"
                        checked={allSelected}
                        disabled={bulkable.length === 0}
                        onChange={toggleAll}
                        className="h-4 w-4 cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-40"
                      />
                    </th>
                    {["Employee", "Claim", "Account", "Submitted", "Amount", "Status", "Action"].map((h) => (
                      <th
                        key={h}
                        className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground last:pr-6"
                      >
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {paginatedClaims.map((claim) => (
                    <tr
                      key={claim.id}
                      tabIndex={0}
                      onClick={() => setSelectedClaim(claim)}
                      onKeyDown={(event) => handleClaimKeyDown(event, claim)}
                      className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                    >
                      <td className="w-12 p-4 pl-6 align-middle" onClick={(e) => e.stopPropagation()}>
                        <input
                          type="checkbox"
                          aria-label={`Select ${claim.claimNumber}`}
                          checked={selected.has(claim.id)}
                          disabled={!isBulkable(claim)}
                          onChange={() => toggle(claim.id)}
                          title={
                            claim.exceedsLimit
                              ? "Over the spend limit — approve this one on its own"
                              : undefined
                          }
                          className="h-4 w-4 cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-40"
                        />
                      </td>
                      <td className="p-4 align-middle">
                        <p className="font-bold text-foreground">{employeeName(claim)}</p>
                        <p className="text-xs text-muted-foreground">{claim.employeeEmail ?? ""}</p>
                      </td>
                      <td className="p-4 align-middle">
                        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          {claim.claimNumber}
                        </p>
                        <p className="mt-1 font-bold">{claim.title}</p>
                      </td>
                      <td className="p-4 align-middle text-muted-foreground">{accountLabel(claim)}</td>
                      <td className="p-4 align-middle">{formatShortDate(claim.submittedAt || claim.spentAt)}</td>
                      <td className="p-4 align-middle">{formatCurrency(claim.amount, claim.currency)}</td>
                      <td className="p-4 align-middle">
                        <div className="flex flex-col items-start gap-1.5">
                          <ClaimStatusBadge status={claim.status} />
                          {claim.exceedsLimit ? <OverLimitBadge /> : null}
                        </div>
                      </td>
                      <td className="p-4 pr-6 align-middle">
                        {actions(claim) ?? <WhyNoAction claim={claim} />}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <PaginationControls
              className="flex flex-col gap-3 px-5 pb-5 sm:flex-row sm:items-center sm:justify-between sm:px-6 sm:pb-6"
              currentPage={page}
              totalItems={filteredClaims.length}
              onPageChange={setPage}
            />
          </section>
        ) : null}

        {!loading && !error && filteredClaims.length > 0 ? (
          <div className="md:hidden">
            <PaginationControls
              className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
              currentPage={page}
              totalItems={filteredClaims.length}
              onPageChange={setPage}
            />
          </div>
        ) : null}
      </div>

      {selectedClaim ? (
        <ClaimDetailsModal
          claim={selectedClaim}
          accountLabel={accountLabel(selectedClaim)}
          employeeLabel={employeeName(selectedClaim)}
          onClose={() => setSelectedClaim(null)}
          footer={actions(selectedClaim, "detail")}
        />
      ) : null}

      {rejectingClaim ? (
        <RejectRemarkDialog
          claim={rejectingClaim}
          value={rejectNotes}
          error={rejectError}
          busy={busyId === rejectingClaim.id}
          onChange={(value) => {
            setRejectNotes(value);
            if (rejectError && value.trim()) setRejectError(null);
          }}
          onClose={closeRejectDialog}
          onConfirm={confirmReject}
        />
      ) : null}
    </>
  );
}

// What happened to each claim in the batch. Failures are the point: an
// over-limit claim, or one someone else already decided, comes back with a
// reason rather than vanishing from the count.
function BulkResultPanel({
  result,
  onDismiss,
}: {
  result: ClaimsBulkResult;
  onDismiss: () => void;
}) {
  const failures = result.items.filter((item) => !item.ok);

  return (
    <section className={`${CARD} p-5`}>
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm font-bold text-foreground">
          {result.succeeded} approved
          {result.failed > 0 ? ` · ${result.failed} not approved` : ""}
        </p>
        <button
          type="button"
          onClick={onDismiss}
          aria-label="Dismiss approval result"
          className="flex h-7 w-7 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>

      {failures.length > 0 ? (
        <ul className="nice-scrollbar mt-3 max-h-40 space-y-1.5 overflow-y-auto">
          {failures.map((item, index) => (
            <li
              key={`${item.id}-${index}`}
              className="rounded-xl bg-warning/15 px-3 py-2 text-xs text-foreground"
            >
              {item.error ?? "Could not be approved."}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

// A dash reads the same for "you already approved this", "it is with the next
// layer" and "it is settled" — three different answers to "why can't I click
// anything?". This says which.
function WhyNoAction({ claim }: { claim: Claim }) {
  if (claim.status !== "PENDING") {
    return <span className="text-xs text-muted-foreground">No action needed</span>;
  }

  const waiting = claim.awaitingApprovers ?? [];
  return (
    <span className="text-xs text-muted-foreground">
      {waiting.length > 0
        ? `You approved · now with ${waiting.map(displayPerson).join(", ")}`
        : "Waiting on another approver"}
    </span>
  );
}

function RejectRemarkDialog({
  claim,
  value,
  error,
  busy,
  onChange,
  onClose,
  onConfirm,
}: {
  claim: Claim;
  value: string;
  error: string | null;
  busy: boolean;
  onChange: (value: string) => void;
  onClose: () => void;
  onConfirm: () => void;
}) {
  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm">
      <section className="w-full max-w-[520px] rounded-[26px] border border-white/40 bg-card p-6 shadow-[0_18px_48px_rgba(76,26,134,0.16)]">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Reject claim
            </p>
            <h3 className="mt-1 truncate text-xl font-black text-foreground">{claim.title}</h3>
            <p className="mt-1 text-sm text-muted-foreground">{claim.claimNumber}</p>
          </div>
          <button
            type="button"
            aria-label="Close reject remark"
            disabled={busy}
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:opacity-50"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <label className="mt-5 block space-y-3">
          <span className="text-sm font-bold text-foreground">Remark</span>
          <textarea
            value={value}
            disabled={busy}
            onChange={(event) => onChange(event.target.value)}
            placeholder="Explain why this claim is rejected."
            className="min-h-32 w-full resize-none rounded-[18px] border border-border bg-card px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-60"
          />
        </label>
        {error ? <p className="mt-2 text-sm font-semibold text-destructive">{error}</p> : null}

        <div className="mt-5 grid grid-cols-2 gap-3">
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="h-12 rounded-[18px] border border-border/70 bg-card text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            className="inline-flex h-12 items-center justify-center gap-2 rounded-[18px] bg-destructive/10 text-sm font-bold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Reject
          </button>
        </div>
      </section>
    </div>
  );
}
