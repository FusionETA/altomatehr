import { useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import { LoaderCircle, X } from "lucide-react";
import {
  approveLeave,
  getLeaveTypes,
  getTeamLeave,
  rejectLeave,
  type LeaveApplication,
  type LeaveType,
} from "../api";
import { formatDateRange, relativeDaysAgo, urgencyLabel } from "../lib/leave-formatters";
import { LeaveStatusBadge } from "./LeaveStatusBadge";
import { LeaveDetailsModal } from "./LeaveDetailsModal";
import { LEAVE_PAGE_SIZE, PaginationControls } from "./PaginationControls";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// GET /leave/team only ever returns PENDING requests where the caller is the
// current approver — there's no history to filter through, so this is a
// live queue (search + bulk actions), not a status-tabbed list like Claims.
export function LeaveApprovals() {
  const [team, setTeam] = useState<LeaveApplication[]>([]);
  const [types, setTypes] = useState<LeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [page, setPage] = useState(1);
  const [busyIds, setBusyIds] = useState<Set<string>>(new Set());
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [selectedApplication, setSelectedApplication] = useState<LeaveApplication | null>(null);
  const [rejecting, setRejecting] = useState<{ ids: string[]; label: string } | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);
  const [dialogBusy, setDialogBusy] = useState(false);

  useEffect(() => {
    Promise.all([getTeamLeave(), getLeaveTypes()])
      .then(([t, ty]) => {
        setTeam(t);
        setTypes(ty);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  const typeName = (id: string) => types.find((t) => t.id === id)?.name ?? "Leave";
  const employeeName = (a: LeaveApplication) => (a.employeeEmail ? buildName(a.employeeEmail) : "—");

  const filtered = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    const list = team.filter((a) => {
      if (query.length === 0) return true;
      return [employeeName(a), a.employeeEmail, typeName(a.leaveTypeId), a.reason, a.startDate, a.endDate]
        .filter(Boolean)
        .join(" ")
        .toLowerCase()
        .includes(query);
    });
    // Soonest-starting request first — the most time-sensitive one to review.
    return [...list].sort((a, b) => a.startDate.localeCompare(b.startDate));
  }, [team, searchTerm, types]);

  useEffect(() => setPage(1), [searchTerm]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / LEAVE_PAGE_SIZE));
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);
  const paginated = useMemo(() => {
    const start = (page - 1) * LEAVE_PAGE_SIZE;
    return filtered.slice(start, start + LEAVE_PAGE_SIZE);
  }, [filtered, page]);

  const allFilteredSelected = filtered.length > 0 && filtered.every((a) => selectedIds.has(a.id));

  function toggleSelect(id: string) {
    setSelectedIds((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleSelectAll() {
    setSelectedIds(allFilteredSelected ? new Set() : new Set(filtered.map((a) => a.id)));
  }

  // Shared by single-row actions and bulk toolbar actions — both just
  // process a list of ids and drop whichever ones succeed from the queue.
  async function processIds(ids: string[], fn: (id: string) => Promise<LeaveApplication>) {
    setBusyIds((s) => new Set([...s, ...ids]));
    setError(null);
    const results = await Promise.allSettled(ids.map((id) => fn(id)));
    const failed = ids.filter((_, i) => results[i].status === "rejected");
    setTeam((cur) => cur.filter((a) => !ids.includes(a.id) || failed.includes(a.id)));
    setSelectedIds((s) => {
      const next = new Set(s);
      ids.forEach((id) => {
        if (!failed.includes(id)) next.delete(id);
      });
      return next;
    });
    setSelectedApplication((cur) => (cur && ids.includes(cur.id) && !failed.includes(cur.id) ? null : cur));
    setBusyIds((s) => {
      const next = new Set(s);
      ids.forEach((id) => next.delete(id));
      return next;
    });
    if (failed.length > 0) {
      setError(`${failed.length} of ${ids.length} request${ids.length === 1 ? "" : "s"} could not be processed.`);
    }
    return failed.length === 0;
  }

  function openReject(ids: string[]) {
    setRejecting({ ids, label: ids.length === 1 ? "Reject leave request" : `Reject ${ids.length} requests` });
    setRejectNotes("");
    setRejectError(null);
  }

  function closeReject() {
    if (dialogBusy) return;
    setRejecting(null);
  }

  async function confirmReject() {
    if (!rejecting) return;
    const notes = rejectNotes.trim();
    if (!notes) {
      setRejectError("Remark is required when rejecting leave.");
      return;
    }
    setDialogBusy(true);
    const ok = await processIds(rejecting.ids, (id) => rejectLeave(id, notes));
    setDialogBusy(false);
    if (ok) setRejecting(null);
  }

  function actions(a: LeaveApplication, variant: "compact" | "detail" = "compact") {
    const busy = busyIds.has(a.id);
    const isDetail = variant === "detail";
    return (
      <div className={isDetail ? "grid w-full grid-cols-2 gap-3 sm:flex sm:w-auto sm:items-center" : "flex items-center gap-2"}>
        <button
          type="button"
          disabled={busy}
          onClick={(event) => {
            event.stopPropagation();
            processIds([a.id], approveLeave);
          }}
          className={
            isDetail
              ? "inline-flex h-12 items-center justify-center gap-2 rounded-[18px] bg-secondary px-6 text-sm font-bold text-secondary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
              : "inline-flex items-center gap-1.5 rounded-full bg-secondary px-3 py-1.5 text-xs font-semibold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
          }
        >
          {busy ? <LoaderCircle className={isDetail ? "h-4 w-4 animate-spin" : "h-3 w-3 animate-spin"} /> : null}
          Approve
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={(event) => {
            event.stopPropagation();
            openReject([a.id]);
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

  function handleRowKeyDown(event: KeyboardEvent, a: LeaveApplication) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setSelectedApplication(a);
    }
  }

  function urgencyPill(a: LeaveApplication) {
    const label = urgencyLabel(a.startDate);
    if (!label) return null;
    return (
      <span className="inline-flex items-center rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-amber-800">
        {label}
      </span>
    );
  }

  const selectedCount = selectedIds.size;

  return (
    <>
      <div className="mb-4 space-y-3 md:hidden">
        <SearchInput
          value={searchTerm}
          onChange={setSearchTerm}
          placeholder="Search by employee or leave type"
          inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
        />
      </div>

      <div className="space-y-4 sm:space-y-6">
        <section className={`hidden md:block ${CARD}`}>
          <div className="space-y-4 px-5 pb-5 pt-3 sm:space-y-5 sm:p-6">
            <div className="flex items-center justify-between gap-4">
              <h2 className="text-lg font-black text-foreground">Team approvals</h2>
              <p className="text-sm text-muted-foreground">
                <span className="font-semibold text-foreground">{filtered.length}</span> pending
              </p>
            </div>
            <SearchInput
              value={searchTerm}
              onChange={setSearchTerm}
              placeholder="Search by employee or leave type"
              className="max-w-sm"
              inputClassName="h-12"
            />
          </div>
        </section>

        <div className="text-sm text-muted-foreground md:hidden">
          <p>
            <span className="font-semibold text-foreground">{filtered.length}</span> pending
          </p>
        </div>

        {selectedCount > 0 ? (
          <section className="flex flex-wrap items-center justify-between gap-3 rounded-[22px] border border-primary/30 bg-primary/5 px-5 py-3">
            <p className="text-sm font-semibold text-foreground">{selectedCount} selected</p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => processIds([...selectedIds], approveLeave)}
                className="inline-flex items-center gap-1.5 rounded-full bg-secondary px-4 py-2 text-xs font-bold text-secondary-foreground transition hover:opacity-90"
              >
                Approve selected
              </button>
              <button
                type="button"
                onClick={() => openReject([...selectedIds])}
                className="rounded-full bg-destructive/10 px-4 py-2 text-xs font-bold text-destructive transition hover:bg-destructive/20"
              >
                Reject selected
              </button>
              <button
                type="button"
                onClick={() => setSelectedIds(new Set())}
                className="rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-semibold text-muted-foreground transition hover:text-foreground"
              >
                Clear
              </button>
            </div>
          </section>
        ) : null}

        {loading ? <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading approvals…</section> : null}

        {error ? (
          <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            {error}
          </section>
        ) : null}

        {!loading && filtered.length === 0 ? (
          <section className={`${CARD} p-8 text-center`}>
            <p className="text-lg font-bold text-foreground">Nothing waiting on you.</p>
            <p className="mt-2 text-sm text-muted-foreground">
              {searchTerm ? "Try a different search term." : "New leave requests from your team will show up here."}
            </p>
          </section>
        ) : null}

        {/* Mobile cards */}
        {!loading && filtered.length > 0 ? (
          <div className="grid gap-3 sm:gap-4 md:hidden">
            {paginated.map((a) => (
              <article
                key={a.id}
                role="button"
                tabIndex={0}
                onClick={() => setSelectedApplication(a)}
                onKeyDown={(event) => handleRowKeyDown(event, a)}
                className={`${CARD} cursor-pointer space-y-4 p-4 transition hover:border-primary/40 focus-visible:border-primary/50 focus-visible:outline-none sm:p-5`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="flex min-w-0 items-start gap-3">
                    <input
                      type="checkbox"
                      checked={selectedIds.has(a.id)}
                      onClick={(event) => event.stopPropagation()}
                      onChange={() => toggleSelect(a.id)}
                      className="mt-1 h-4 w-4 shrink-0 rounded border-border/70"
                      aria-label={`Select ${employeeName(a)}'s request`}
                    />
                    <div className="min-w-0">
                      <p className="text-base font-black">{employeeName(a)}</p>
                      <p className="text-sm text-muted-foreground">{typeName(a.leaveTypeId)}</p>
                    </div>
                  </div>
                  <div className="flex shrink-0 flex-col items-end gap-1.5">
                    <LeaveStatusBadge status={a.status} />
                    {urgencyPill(a)}
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Dates</p>
                    <p className="mt-1 text-sm font-semibold">{formatDateRange(a.startDate, a.endDate)}</p>
                  </div>
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Submitted</p>
                    <p className="mt-1 text-sm font-semibold">{relativeDaysAgo(a.createdAt)}</p>
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3 rounded-2xl bg-surface-low p-4">
                  <p className="text-sm font-semibold text-foreground">
                    {a.totalDays} day{a.totalDays === 1 ? "" : "s"}
                  </p>
                  {actions(a)}
                </div>
              </article>
            ))}
          </div>
        ) : null}

        {/* Desktop table */}
        {!loading && filtered.length > 0 ? (
          <section className={`hidden md:block ${CARD}`}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[960px] caption-bottom text-sm">
                <thead>
                  <tr className="border-b border-border/60">
                    <th className="h-12 w-12 pl-6 text-left">
                      <input
                        type="checkbox"
                        checked={allFilteredSelected}
                        onChange={toggleSelectAll}
                        className="h-4 w-4 rounded border-border/70"
                        aria-label="Select all pending requests"
                      />
                    </th>
                    {["Employee", "Type", "Dates", "Days", "Submitted", "Action"].map((h) => (
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
                  {paginated.map((a) => (
                    <tr
                      key={a.id}
                      tabIndex={0}
                      onClick={() => setSelectedApplication(a)}
                      onKeyDown={(event) => handleRowKeyDown(event, a)}
                      className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                    >
                      <td className="p-4 pl-6 align-middle">
                        <input
                          type="checkbox"
                          checked={selectedIds.has(a.id)}
                          onClick={(event) => event.stopPropagation()}
                          onChange={() => toggleSelect(a.id)}
                          className="h-4 w-4 rounded border-border/70"
                          aria-label={`Select ${employeeName(a)}'s request`}
                        />
                      </td>
                      <td className="p-4 align-middle">
                        <p className="font-bold text-foreground">{employeeName(a)}</p>
                        <p className="text-xs text-muted-foreground">{a.employeeEmail ?? ""}</p>
                      </td>
                      <td className="p-4 align-middle">{typeName(a.leaveTypeId)}</td>
                      <td className="p-4 align-middle">
                        <div className="flex flex-col items-start gap-1.5">
                          <span>{formatDateRange(a.startDate, a.endDate)}</span>
                          {urgencyPill(a)}
                        </div>
                      </td>
                      <td className="p-4 align-middle">{a.totalDays}</td>
                      <td className="p-4 align-middle">{relativeDaysAgo(a.createdAt)}</td>
                      <td className="p-4 pr-6 align-middle">{actions(a)}</td>
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

      {selectedApplication ? (
        <LeaveDetailsModal
          application={selectedApplication}
          typeName={typeName(selectedApplication.leaveTypeId)}
          employeeLabel={employeeName(selectedApplication)}
          showWhoElseIsOff
          onClose={() => setSelectedApplication(null)}
          footer={actions(selectedApplication, "detail")}
        />
      ) : null}

      {rejecting ? (
        <RejectRemarkDialog
          label={rejecting.label}
          value={rejectNotes}
          error={rejectError}
          busy={dialogBusy}
          onChange={(value) => {
            setRejectNotes(value);
            if (rejectError && value.trim()) setRejectError(null);
          }}
          onClose={closeReject}
          onConfirm={confirmReject}
        />
      ) : null}
    </>
  );
}

function RejectRemarkDialog({
  label,
  value,
  error,
  busy,
  onChange,
  onClose,
  onConfirm,
}: {
  label: string;
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
          <h3 className="text-xl font-black text-foreground">{label}</h3>
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
            placeholder="Explain why this leave is rejected."
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
