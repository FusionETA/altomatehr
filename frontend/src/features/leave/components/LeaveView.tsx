import { useEffect, useMemo, useState } from "react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonCards } from "@/shared/components/Skeleton";
import { createPortal } from "react-dom";
import type { KeyboardEvent } from "react";
import { LoaderCircle, Plus } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { FloatingActionButton } from "@/shared/components/FloatingActionButton";
import {
  cancelLeave,
  getLeaveBalances,
  getLeaveTypes,
  getMyLeave,
  isCancellationUnderReview,
  requestLeaveCancellation,
  withdrawLeaveCancellation,
  type LeaveApplication,
  type LeaveBalance,
  type LeaveType,
} from "../api";
import { businessToday } from "@/shared/lib/business-day";
import { leaveMatchesStatus, type LeaveStatusFilter } from "../lib/leave-status";
import { formatDateRange, relativeDaysAgo } from "../lib/leave-formatters";
import { LeaveCancellationBadge, LeaveStatusBadge } from "./LeaveStatusBadge";
import { LeaveStatusTabs } from "./LeaveStatusTabs";
import { LeaveDetailsModal } from "./LeaveDetailsModal";
import { ApplyLeaveModal } from "./ApplyLeaveModal";
import { LEAVE_PAGE_SIZE, PaginationControls } from "./PaginationControls";
import { SearchInput } from "@/shared/components/SearchInput";
import { OverflowTabList } from "@/shared/components/OverflowTabList";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

type MyLeaveTab = "balances" | "history";
const MY_LEAVE_TABS = [
  { id: "balances" as const, label: "My Balances" },
  { id: "history" as const, label: "History" },
];

// Newest first. Declared once so the seed and the refresh cannot sort
// differently.
function newestFirst(rows: LeaveApplication[] | undefined) {
  return [...(rows ?? [])].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

export function LeaveView() {
  const [tab, setTab] = useState<MyLeaveTab>("balances");
  const [error, setError] = useState<string | null>(null);
  const [applyOpen, setApplyOpen] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [status, setStatus] = useState<LeaveStatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<LeaveApplication | null>(null);
  // The application awaiting a "yes, cancel it" — cancelling is not undoable
  // (re-applying is a new request that has to be approved again), so the button
  // opens this instead of acting.
  const [confirming, setConfirming] = useState<LeaveApplication | null>(null);
  // Approved leave the employee is asking to cancel — goes to their approver.
  const [requesting, setRequesting] = useState<LeaveApplication | null>(null);

  // Three independent reads rather than one Promise.all: leave types and
  // balances rarely change and stay cached across visits, so only the
  // applications list is usually worth refetching.
  const typesQuery = useCachedQuery("/leave-types", getLeaveTypes);
  const balancesQuery = useCachedQuery("/leave/balances", getLeaveBalances);
  const mineQuery = useCachedQuery("/leave", getMyLeave);
  const loading = typesQuery.loading || balancesQuery.loading || mineQuery.loading;
  // Seeded from the cache in the initializer, not left empty for an effect to
  // fill after the paint — that one frame, built from [], is the flash of an
  // empty list on a revisit. The effects below still run, and are what keep
  // these in step with a background refresh.
  const [types, setTypes] = useState<LeaveType[]>(() => typesQuery.data ?? []);
  const [balances, setBalances] = useState<LeaveBalance[]>(() => balancesQuery.data ?? []);
  // Sorted in the seed exactly as the effect below sorts it — a seed that
  // skipped it would paint the list in the wrong order and then re-order.
  const [mine, setMine] = useState<LeaveApplication[]>(() => newestFirst(mineQuery.data));


  useEffect(() => {
    if (typesQuery.data) setTypes(typesQuery.data);
  }, [typesQuery.data]);
  useEffect(() => {
    if (balancesQuery.data) setBalances(balancesQuery.data);
  }, [balancesQuery.data]);
  useEffect(() => {
    if (mineQuery.data) setMine(newestFirst(mineQuery.data));
  }, [mineQuery.data]);
  useEffect(() => {
    const first = typesQuery.error ?? balancesQuery.error ?? mineQuery.error;
    if (first) setError(first);
  }, [typesQuery.error, balancesQuery.error, mineQuery.error]);

  const activeTypes = useMemo(() => types.filter((t) => !t.isArchived), [types]);
  const typeName = (id: string) => types.find((t) => t.id === id)?.name ?? "Leave";
  const quotaBalances = balances.filter((b) => b.entitlementDays > 0);
  const lastRequest = mine[0];

  const filtered = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    return mine.filter((a) => {
      const matchesStatus = leaveMatchesStatus(a, status);
      const matchesQuery =
        query.length === 0
          ? true
          : [typeName(a.leaveTypeId), a.status, a.reason, a.startDate, a.endDate]
              .filter(Boolean)
              .join(" ")
              .toLowerCase()
              .includes(query);
      return matchesStatus && matchesQuery;
    });
  }, [mine, searchTerm, status, types]);

  useEffect(() => setPage(1), [searchTerm, status]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / LEAVE_PAGE_SIZE));
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);
  const paginated = useMemo(() => {
    const start = (page - 1) * LEAVE_PAGE_SIZE;
    return filtered.slice(start, start + LEAVE_PAGE_SIZE);
  }, [filtered, page]);

  const hasActiveFilters = status !== "ALL" || searchTerm.trim().length > 0;

  async function cancelMine(id: string) {
    setBusyId(id);
    setError(null);
    try {
      const updated = await cancelLeave(id);
      setMine((cur) => cur.map((a) => (a.id === updated.id ? updated : a)));
      setSelected((cur) => (cur?.id === updated.id ? updated : cur));
      setBalances(await getLeaveBalances());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not cancel.");
    } finally {
      setBusyId(null);
      // Closed either way: on failure the message belongs on the page behind,
      // not under a dialog the user has to dismiss to read it.
      setConfirming(null);
    }
  }

  function replace(updated: LeaveApplication) {
    setMine((cur) => cur.map((a) => (a.id === updated.id ? updated : a)));
    setSelected((cur) => (cur?.id === updated.id ? updated : cur));
  }

  async function withdrawRequest(id: string) {
    setBusyId(id);
    setError(null);
    try {
      replace(await withdrawLeaveCancellation(id));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not withdraw the request.");
    } finally {
      setBusyId(null);
    }
  }

  // The one action a row offers, by where the leave stands:
  //   pending               → Cancel (it's withdrawn straight away)
  //   approved, not started → Request cancellation (goes to the approver)
  //   cancellation pending  → Withdraw request
  function cancelButton(application: LeaveApplication, variant: "compact" | "detail" = "compact") {
    const isDetail = variant === "detail";
    const className = isDetail
      ? "inline-flex h-12 items-center justify-center rounded-[18px] border border-border/70 bg-card px-6 text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
      : "rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50";

    let label: string;
    let onClick: () => void;
    if (application.status === "PENDING") {
      label = "Cancel";
      onClick = () => setConfirming(application);
    } else if (isCancellationUnderReview(application)) {
      label = "Withdraw request";
      onClick = () => void withdrawRequest(application.id);
    } else if (application.status === "APPROVED" && application.startDate > businessToday()) {
      label = "Request cancellation";
      onClick = () => setRequesting(application);
    } else {
      return null;
    }

    return (
      <button
        type="button"
        disabled={busyId === application.id}
        onClick={(event) => {
          event.stopPropagation();
          onClick();
        }}
        className={className}
      >
        {busyId === application.id && !isDetail ? "…" : label}
      </button>
    );
  }

  function handleRowKeyDown(event: KeyboardEvent, application: LeaveApplication) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setSelected(application);
    }
  }

  return (
    <>
      {error ? <p className="mb-4 text-sm font-medium text-destructive">{error}</p> : null}

      <OverflowTabList
        items={MY_LEAVE_TABS}
        value={tab}
        onChange={setTab}
        variant="segmented"
        className="mb-4 sm:mb-6"
        ariaLabel="My leave sections"
      />

      {tab === "balances" ? (
        <>
          {/* Guarded: without this, "No leave balances yet." was rendered on
              every cold load and replaced by the cards a round trip later —
              telling someone they have no entitlement when they have fourteen
              days. */}
          {loading ? (
            <SkeletonCards />
          ) : quotaBalances.length > 0 ? (
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
          ) : (
            <section className={`${CARD} p-8 text-center`}>
              <p className="text-lg font-bold text-foreground">No leave balances yet.</p>
            </section>
          )}

          {lastRequest ? (
            <button
              type="button"
              onClick={() => setSelected(lastRequest)}
              className={`mt-4 w-full ${CARD} p-5 text-left transition hover:border-primary/40 sm:mt-6`}
            >
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                    Your last request
                  </p>
                  <p className="mt-1 truncate text-base font-black text-foreground">
                    {typeName(lastRequest.leaveTypeId)} · {formatDateRange(lastRequest.startDate, lastRequest.endDate)}
                  </p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {lastRequest.status === "PENDING"
                      ? `Submitted ${relativeDaysAgo(lastRequest.createdAt)} — awaiting approval`
                      : lastRequest.decidedAt
                        ? `Decided ${relativeDaysAgo(lastRequest.decidedAt)}`
                        : `Submitted ${relativeDaysAgo(lastRequest.createdAt)}`}
                  </p>
                </div>
                <LeaveStatusBadge status={lastRequest.status} />
              </div>
            </button>
          ) : null}
        </>
      ) : null}

      {tab === "history" ? (
        <>
          <div className="mb-4 space-y-3 md:hidden">
            <LeaveStatusTabs value={status} onChange={setStatus} />
            <SearchInput
              value={searchTerm}
              onChange={setSearchTerm}
              placeholder="Search your leave history"
              inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
            />
          </div>

          <div className="space-y-4 sm:space-y-6">
        <section className={`hidden md:block ${CARD}`}>
          <div className="space-y-4 px-5 pb-5 pt-3 sm:space-y-5 sm:p-6">
            <div className="flex items-center justify-between gap-4">
              <h2 className="text-lg font-black text-foreground">My leave</h2>
            </div>
            <SearchInput
              value={searchTerm}
              onChange={setSearchTerm}
              placeholder="Search your leave history"
              className="max-w-sm"
              inputClassName="h-12 focus-visible:ring-primary"
            />
            <LeaveStatusTabs value={status} onChange={setStatus} />
            <div className="flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
              <p>
                Showing <span className="font-semibold text-foreground">{filtered.length}</span> of{" "}
                <span className="font-semibold text-foreground">{mine.length}</span> requests
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
            Showing <span className="font-semibold text-foreground">{filtered.length}</span> of{" "}
            <span className="font-semibold text-foreground">{mine.length}</span> requests
          </p>
        </div>

        {loading ? <SkeletonCards /> : null}

        {!loading && filtered.length === 0 ? (
          <section className={`${CARD} p-8 text-center`}>
            <p className="text-lg font-bold text-foreground">No leave applications match this filter.</p>
            <p className="mt-2 text-sm text-muted-foreground">Try a different status, or apply for leave below.</p>
          </section>
        ) : null}

        {!loading && filtered.length > 0 ? (
          <div className="grid gap-3 sm:gap-4 md:hidden">
            {paginated.map((a) => (
              <article
                key={a.id}
                role="button"
                tabIndex={0}
                onClick={() => setSelected(a)}
                onKeyDown={(event) => handleRowKeyDown(event, a)}
                className={`${CARD} cursor-pointer space-y-3 p-4 transition hover:border-primary/40 focus-visible:border-primary/50 focus-visible:outline-none sm:p-5`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="min-w-0">
                    <p className="text-base font-black">{typeName(a.leaveTypeId)}</p>
                    <p className="text-sm text-muted-foreground">{formatDateRange(a.startDate, a.endDate)}</p>
                  </div>
                  <div className="flex flex-col items-end gap-1">
                    <LeaveStatusBadge status={a.status} />
                    <LeaveCancellationBadge application={a} />
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3">
                  <p className="text-xs text-muted-foreground">
                    {a.totalDays} day{a.totalDays === 1 ? "" : "s"} · {relativeDaysAgo(a.createdAt)}
                  </p>
                  {cancelButton(a)}
                </div>
              </article>
            ))}
          </div>
        ) : null}

        {!loading && filtered.length > 0 ? (
          <section className={`hidden md:block ${CARD}`}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] caption-bottom text-sm">
                <thead>
                  <tr className="border-b border-border/60">
                    {["Type", "Dates", "Days", "Submitted", "Status", ""].map((h) => (
                      <th
                        key={h}
                        className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground first:pl-6 last:pr-6"
                      >
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {paginated.map((a) => (
                    <tr
                      key={a.id}
                      tabIndex={0}
                      onClick={() => setSelected(a)}
                      onKeyDown={(event) => handleRowKeyDown(event, a)}
                      className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                    >
                      <td className="p-4 pl-6 align-middle font-bold">{typeName(a.leaveTypeId)}</td>
                      <td className="p-4 align-middle">{formatDateRange(a.startDate, a.endDate)}</td>
                      <td className="p-4 align-middle">{a.totalDays}</td>
                      <td className="p-4 align-middle">{relativeDaysAgo(a.createdAt)}</td>
                      <td className="p-4 align-middle">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <LeaveStatusBadge status={a.status} />
                          <LeaveCancellationBadge application={a} />
                        </div>
                      </td>
                      <td className="p-4 pr-6 align-middle">
                        {cancelButton(a) ?? <span className="text-xs text-muted-foreground">—</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <PaginationControls
              className="flex flex-col gap-3 px-5 pb-5 sm:flex-row sm:items-center sm:justify-between sm:px-6 sm:pb-6"
              currentPage={page}
              totalItems={filtered.length}
              onPageChange={setPage}
            />
          </section>
        ) : null}

        {!loading && filtered.length > 0 ? (
          <div className="md:hidden">
            <PaginationControls
              className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
              currentPage={page}
              totalItems={filtered.length}
              onPageChange={setPage}
            />
          </div>
        ) : null}
          </div>
        </>
      ) : null}

      <FloatingActionButton label="Apply for leave" onClick={() => setApplyOpen(true)} disabled={activeTypes.length === 0}>
        <Plus className="h-6 w-6" />
      </FloatingActionButton>

      {applyOpen ? (
        <ApplyLeaveModal
          types={activeTypes}
          balances={balances}
          onClose={() => setApplyOpen(false)}
          onCreated={async (app) => {
            setMine((cur) => [app, ...cur]);
            setBalances(await getLeaveBalances());
          }}
        />
      ) : null}

      {selected ? (
        <LeaveDetailsModal
          application={selected}
          typeName={typeName(selected.leaveTypeId)}
          onClose={() => setSelected(null)}
          footer={cancelButton(selected, "detail")}
        />
      ) : null}

      {requesting ? (
        <RequestCancellationDialog
          application={requesting}
          typeName={typeName(requesting.leaveTypeId)}
          onKeep={() => setRequesting(null)}
          onSent={(updated) => {
            replace(updated);
            setRequesting(null);
            // Cancelled at once when nobody is above them — the days are back.
            if (updated.status === "CANCELLED") void getLeaveBalances().then(setBalances);
          }}
        />
      ) : null}

      {confirming ? (
        <CancelLeaveDialog
          application={confirming}
          typeName={typeName(confirming.leaveTypeId)}
          busy={busyId === confirming.id}
          onKeep={() => setConfirming(null)}
          onConfirm={() => void cancelMine(confirming.id)}
        />
      ) : null}
    </>
  );
}

// Same shape as the Xero disconnect confirmation: what is about to happen,
// what it costs, then the destructive action on the left and the way out on
// the right.
function CancelLeaveDialog({
  application,
  typeName,
  busy,
  onKeep,
  onConfirm,
}: {
  application: LeaveApplication;
  typeName: string;
  busy: boolean;
  onKeep: () => void;
  onConfirm: () => void;
}) {
  useBodyScrollLock();

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="text-xl font-black text-foreground">Cancel this leave request?</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          It will be withdrawn from your approver's queue. Applying again starts a new request that
          has to be approved from scratch.
        </p>

        <div className="mt-4 rounded-2xl border border-border/60 bg-surface-low p-4">
          <p className="text-base font-black text-foreground">{typeName}</p>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {formatDateRange(application.startDate, application.endDate)} ·{" "}
            {application.totalDays} {application.totalDays === 1 ? "day" : "days"}
          </p>
        </div>

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-destructive text-sm font-bold text-destructive-foreground transition hover:opacity-90 disabled:opacity-60"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Cancel request
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onKeep}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-60"
          >
            Keep it
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}

// Asking to cancel APPROVED leave. Unlike withdrawing a pending request this
// goes to the approver(s) — the leave, and its days, stand until they agree —
// so it says that, and lets the employee say why.
function RequestCancellationDialog({
  application,
  typeName,
  onKeep,
  onSent,
}: {
  application: LeaveApplication;
  typeName: string;
  onKeep: () => void;
  onSent: (updated: LeaveApplication) => void;
}) {
  useBodyScrollLock();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function send() {
    setBusy(true);
    setError(null);
    try {
      onSent(await requestLeaveCancellation(application.id, reason.trim() || undefined));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not send the request.");
      setBusy(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="text-xl font-black text-foreground">Ask to cancel this leave?</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Your approver reviews it, just like the original request. The leave stays booked until
          they agree — then it's cancelled and the days go back into your balance.
        </p>

        <div className="mt-4 rounded-2xl border border-border/60 bg-surface-low p-4">
          <p className="text-base font-black text-foreground">{typeName}</p>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {formatDateRange(application.startDate, application.endDate)} ·{" "}
            {application.totalDays} {application.totalDays === 1 ? "day" : "days"}
          </p>
        </div>

        <label className="mt-4 block space-y-2">
          <span className="text-sm font-bold text-foreground">
            Reason <span className="font-medium text-muted-foreground">(optional)</span>
          </span>
          <textarea
            value={reason}
            disabled={busy}
            maxLength={1000}
            onChange={(event) => setReason(event.target.value)}
            placeholder="e.g. Trip postponed"
            className="min-h-24 w-full resize-none rounded-[18px] border border-border bg-card px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-60"
          />
        </label>
        {error ? <p className="mt-2 text-sm font-semibold text-destructive">{error}</p> : null}

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={busy}
            onClick={() => void send()}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Send request
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onKeep}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-60"
          >
            Keep my leave
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
