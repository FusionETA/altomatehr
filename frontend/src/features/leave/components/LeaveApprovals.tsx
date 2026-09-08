import { useCallback, useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import { LoaderCircle, X } from "lucide-react";
import {
  approveLeave,
  bulkApproveLeave,
  getLeaveTypes,
  getTeamLeave,
  rejectLeave,
  type LeaveApplication,
  type LeaveBulkResult,
  type LeaveType,
} from "../api";
import { formatDateRange, relativeDaysAgo, urgencyLabel } from "../lib/leave-formatters";
import { LeaveStatusBadge } from "./LeaveStatusBadge";
import { LeaveDetailsModal } from "./LeaveDetailsModal";
import { LEAVE_PAGE_SIZE, PaginationControls } from "./PaginationControls";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import {
  BulkActionBar,
  BulkResultPanel,
  BulkRowCheckbox,
  SelectAllPill,
  SelectHint,
  SelectModeButton,
} from "@/shared/components/BulkApprove";
import { useBulkSelection } from "@/shared/lib/use-bulk-selection";
import { useRealtimeEvent } from "@/shared/lib/use-realtime";

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
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkResult, setBulkResult] = useState<LeaveBulkResult | null>(null);
  const [selectedApplication, setSelectedApplication] = useState<LeaveApplication | null>(null);
  const [rejecting, setRejecting] = useState<{ ids: string[]; label: string } | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);
  const [dialogBusy, setDialogBusy] = useState(false);

  const loadTeam = useCallback(() => {
    Promise.all([getTeamLeave(), getLeaveTypes()])
      .then(([t, ty]) => {
        setTeam(t);
        setTypes(ty);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(loadTeam, [loadTeam]);

  // Someone else deciding or submitting a leave request refreshes this queue
  // live instead of waiting for a manual reload.
  useRealtimeEvent(["LEAVE"], loadTeam);

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

  // /leave/team already returns only what is awaiting THIS approver, so every
  // row here is theirs to decide. The status check is a belt-and-braces guard:
  // a row that somehow arrives decided must not be selectable, because the
  // server would refuse it and the approver would never learn why.
  const isBulkable = (a: LeaveApplication) => a.status === "PENDING";

  const selection = useBulkSelection(filtered, (a) => a.id, isBulkable);
  const selectedDays = selection.selected.reduce((sum, a) => sum + a.totalDays, 0);

  // Approve in ONE request rather than N parallel ones. The old version fired a
  // POST per row and reported "3 of 8 could not be processed" without saying
  // which or why; the bulk endpoint answers per id.
  async function confirmBulkApprove() {
    if (selection.selected.length === 0) return;

    setBulkBusy(true);
    setError(null);
    try {
      const result = await bulkApproveLeave(selection.selected.map((a) => a.id));
      setBulkResult(result);
      selection.clear();
      // Re-read rather than patching rows: a request on a multi-step chain stays
      // PENDING and moves to the next approver, so it may leave this queue.
      setTeam(await getTeamLeave());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not approve those requests.");
    } finally {
      setBulkBusy(false);
    }
  }

  // Single-row approve and reject. Batches go through confirmBulkApprove and the
  // bulk endpoint instead — see above.
  async function processIds(ids: string[], fn: (id: string) => Promise<LeaveApplication>) {
    setBusyIds((s) => new Set([...s, ...ids]));
    setError(null);
    const results = await Promise.allSettled(ids.map((id) => fn(id)));
    const failed = ids.filter((_, i) => results[i].status === "rejected");
    setTeam((cur) => cur.filter((a) => !ids.includes(a.id) || failed.includes(a.id)));
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

  // One row at a time, always: the remark has to be about THAT request.
  function openReject(id: string) {
    setRejecting({ ids: [id], label: "Reject leave request" });
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
            openReject(a.id);
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
            <div className="flex flex-wrap items-center justify-between gap-4">
              <h2 className="text-lg font-black text-foreground">Team approvals</h2>
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-sm text-muted-foreground">
                  <span className="font-semibold text-foreground">{filtered.length}</span> pending
                </p>
                {/* The same control the phone gets — see ClaimsApprovals for why
                    a permanent checkbox column was the wrong desktop pattern. */}
                {selection.selectable.length > 0 ? (
                  <>
                    {selection.mode ? (
                      <SelectAllPill
                        inputRef={selection.selectAllRef}
                        total={selection.selectable.length}
                        allSelected={selection.allSelected}
                        onToggleAll={selection.toggleAll}
                      />
                    ) : null}
                    <SelectModeButton
                      active={selection.mode}
                      onToggle={() => (selection.mode ? selection.exit() : selection.enter())}
                    />
                  </>
                ) : null}
              </div>
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

        <div className="flex items-center justify-between gap-3 text-sm text-muted-foreground md:hidden">
          <p>
            <span className="font-semibold text-foreground">{filtered.length}</span> pending
          </p>

          {/* Only offered when there is something to select. On desktop the
              table has its own checkbox column, so this is phone-only. */}
          {selection.selectable.length > 0 ? (
            <div className="flex shrink-0 items-center gap-2">
              {selection.mode ? (
                <SelectAllPill
                  inputRef={selection.selectAllRef}
                  total={selection.selectable.length}
                  allSelected={selection.allSelected}
                  onToggleAll={selection.toggleAll}
                />
              ) : null}
              <SelectModeButton
                active={selection.mode}
                onToggle={() => (selection.mode ? selection.exit() : selection.enter())}
              />
            </div>
          ) : null}
        </div>

        {selection.mode && selection.selected.length === 0 ? (
          <SelectHint className="md:hidden">
            Or tap the requests you want to approve together.
          </SelectHint>
        ) : null}

        {bulkResult ? (
          <BulkResultPanel result={bulkResult} onDismiss={() => setBulkResult(null)} />
        ) : null}

        {/* Approve-only. Bulk reject used to live here with ONE shared remark for
            every ticked row, which told each employee nothing about why theirs
            was refused — so rejection went back to one at a time. */}
        {selection.selected.length > 0 ? (
          <BulkActionBar
            count={selection.selected.length}
            noun="request"
            summary={`Approving ${selectedDays} day${selectedDays === 1 ? "" : "s"} of leave in one go`}
            busy={bulkBusy}
            onClear={selection.clear}
            onApprove={confirmBulkApprove}
          />
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
                role={selection.mode && !isBulkable(a) ? undefined : "button"}
                aria-pressed={selection.mode && isBulkable(a) ? selection.has(a.id) : undefined}
                tabIndex={selection.mode && !isBulkable(a) ? -1 : 0}
                onClick={() => {
                  // In select mode the card IS the checkbox — a full-card target
                  // instead of a 16px one inside a card that is itself tappable.
                  if (selection.mode) {
                    if (isBulkable(a)) selection.toggle(a.id);
                    return;
                  }
                  setSelectedApplication(a);
                }}
                onKeyDown={(event) => handleRowKeyDown(event, a)}
                className={`${CARD} space-y-4 p-4 transition focus-visible:outline-none sm:p-5 ${
                  selection.mode && !isBulkable(a)
                    ? "cursor-default opacity-45"
                    : "cursor-pointer hover:border-primary/40 focus-visible:border-primary/50"
                } ${
                  selection.mode && selection.has(a.id)
                    ? "border-primary/50 bg-primary/5 ring-2 ring-primary/25"
                    : ""
                }`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="min-w-0">
                    <p className="text-base font-black">{employeeName(a)}</p>
                    <p className="text-sm text-muted-foreground">{typeName(a.leaveTypeId)}</p>
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
                  {/* Two ways to approve the same row on one card, one of which
                      also swallows the tap meant to tick it. */}
                  {selection.mode ? null : actions(a)}
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
                    {selection.mode ? <th className="h-12 w-12 pl-6 text-left" /> : null}
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
                      {selection.mode ? (
                        <td className="w-12 p-4 pl-6 align-middle" onClick={(e) => e.stopPropagation()}>
                          {isBulkable(a) ? (
                            <BulkRowCheckbox
                              label={`Select ${employeeName(a)}'s request`}
                              checked={selection.has(a.id)}
                              disabled={false}
                              onChange={() => selection.toggle(a.id)}
                            />
                          ) : null}
                        </td>
                      ) : null}
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
