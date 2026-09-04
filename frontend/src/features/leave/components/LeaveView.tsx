import { useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import { Plus } from "lucide-react";
import {
  cancelLeave,
  getLeaveBalances,
  getLeaveTypes,
  getMyLeave,
  type LeaveApplication,
  type LeaveBalance,
  type LeaveType,
} from "../api";
import { leaveMatchesStatus, type LeaveStatusFilter } from "../lib/leave-status";
import { formatDateRange, relativeDaysAgo } from "../lib/leave-formatters";
import { LeaveStatusBadge } from "./LeaveStatusBadge";
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

export function LeaveView() {
  const [tab, setTab] = useState<MyLeaveTab>("balances");
  const [types, setTypes] = useState<LeaveType[]>([]);
  const [balances, setBalances] = useState<LeaveBalance[]>([]);
  const [mine, setMine] = useState<LeaveApplication[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [applyOpen, setApplyOpen] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [status, setStatus] = useState<LeaveStatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<LeaveApplication | null>(null);

  useEffect(() => {
    Promise.all([getLeaveTypes(), getLeaveBalances(), getMyLeave()])
      .then(([t, b, m]) => {
        setTypes(t);
        setBalances(b);
        setMine([...m].sort((a, c) => c.createdAt.localeCompare(a.createdAt)));
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

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
    }
  }

  function cancelButton(application: LeaveApplication, variant: "compact" | "detail" = "compact") {
    if (application.status !== "PENDING") return null;
    const isDetail = variant === "detail";
    return (
      <button
        type="button"
        disabled={busyId === application.id}
        onClick={(event) => {
          event.stopPropagation();
          cancelMine(application.id);
        }}
        className={
          isDetail
            ? "inline-flex h-12 items-center justify-center rounded-[18px] border border-border/70 bg-card px-6 text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
            : "rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
        }
      >
        Cancel
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

        {loading ? <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading…</section> : null}

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
                  <LeaveStatusBadge status={a.status} />
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
                        <LeaveStatusBadge status={a.status} />
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

      <button
        type="button"
        aria-label="Apply for leave"
        onClick={() => setApplyOpen(true)}
        disabled={activeTypes.length === 0}
        className="fixed bottom-32 right-5 z-40 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-panel transition-transform hover:scale-105 active:scale-95 disabled:opacity-50 lg:bottom-8 lg:right-8"
      >
        <Plus className="h-6 w-6" />
      </button>

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

      {selected ? (
        <LeaveDetailsModal
          application={selected}
          typeName={typeName(selected.leaveTypeId)}
          onClose={() => setSelected(null)}
          footer={cancelButton(selected, "detail")}
        />
      ) : null}
    </>
  );
}
