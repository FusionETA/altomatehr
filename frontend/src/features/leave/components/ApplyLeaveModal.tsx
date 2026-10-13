import { type FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle, Paperclip, X } from "lucide-react";
import {
  applyLeave,
  editLeave,
  previewLeaveDays,
  uploadLeaveAttachment,
  type LeaveApplication,
  type LeaveBalance,
  type LeaveType,
} from "../api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

// Inclusive CALENDAR-day span. Only a fallback for when the server's count
// can't be had — it includes weekends and holidays, so it is shown as "about"
// and never used to warn against the balance.
function calendarDaysBetween(start: string, end: string): number | null {
  if (!start || !end) return null;
  const s = new Date(`${start}T00:00:00`);
  const e = new Date(`${end}T00:00:00`);
  if (e < s) return null;
  return Math.round((e.getTime() - s.getTime()) / 86_400_000) + 1;
}

// Days read better as "12" than "12.0", but a half day has to survive.
const formatDays = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));

// How long the dates must sit still before the server is asked to count them.
const PREVIEW_DEBOUNCE_MS = 300;

type Preview =
  | { key: string; state: "ready"; totalDays: number }
  | { key: string; state: "failed" };

export function ApplyLeaveModal({
  types,
  balances,
  editing = null,
  onClose,
  onCreated,
}: {
  /** Types to offer. When editing, include the request's own type even if it
   *  has since been archived — it is labelled so, and the server lets a
   *  request keep it (only switching TO an archived type is refused). */
  types: LeaveType[];
  /** The caller already holds these; without them this form asks people to
   *  guess how many days they have left. */
  balances: LeaveBalance[];
  /** A pending request to change instead of applying anew. Saved with PUT, so
   *  it keeps its place in the approver's queue. */
  editing?: LeaveApplication | null;
  onClose: () => void;
  /** Called with the new request — or, when editing, the updated one. */
  onCreated: (app: LeaveApplication) => void;
}) {
  const [leaveTypeId, setLeaveTypeId] = useState(editing?.leaveTypeId ?? types[0]?.id ?? "");
  const [startDate, setStartDate] = useState(editing?.startDate ?? "");
  const [endDate, setEndDate] = useState(editing?.endDate ?? "");
  const [reason, setReason] = useState(editing?.reason ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // An MC or hospital slip. Optional — most leave needs no evidence, and
  // requiring one would block annual leave on a document that doesn't exist.
  const [attachment, setAttachment] = useState<File | null>(null);
  const attachmentInput = useRef<HTMLInputElement | null>(null);

  // This form has no half-day control, so an edit carries the request's own
  // duration through unchanged — a half day stays a half day (0.5) as long as
  // it stays on one date; the server refuses a half day across several.
  const duration = editing?.duration ?? "FULL_DAY";
  const halfDay = duration !== "FULL_DAY";

  const datesValid = startDate !== "" && endDate !== "" && endDate >= startDate;
  const halfDayAcrossDates = halfDay && datesValid && startDate !== endDate;

  // An edit that leaves the type and dates alone keeps the request's stored
  // day count — the server doesn't recompute it — so that figure is the
  // honest one until they change.
  const unchangedEdit =
    editing !== null &&
    leaveTypeId === editing.leaveTypeId &&
    startDate === editing.startDate &&
    endDate === editing.endDate;

  // The server counts the days (working days only, holidays skipped), so the
  // figure here is the one it will charge rather than a calendar-day guess.
  const previewKey = datesValid && !halfDayAcrossDates && !unchangedEdit
    ? `${startDate}|${endDate}|${duration}`
    : null;
  const [preview, setPreview] = useState<Preview | null>(null);

  useEffect(() => {
    if (previewKey === null) return;
    const [start, end] = previewKey.split("|");
    let stale = false;
    const timer = window.setTimeout(() => {
      previewLeaveDays(start, end, duration)
        .then((r) => {
          if (!stale) setPreview({ key: previewKey, state: "ready", totalDays: r.totalDays });
        })
        .catch(() => {
          if (!stale) setPreview({ key: previewKey, state: "failed" });
        });
    }, PREVIEW_DEBOUNCE_MS);
    return () => {
      stale = true;
      window.clearTimeout(timer);
    };
  }, [previewKey, duration]);

  // Only a preview for the CURRENT dates counts; an older one is stale.
  const current = preview !== null && preview.key === previewKey ? preview : null;

  // The server's figure when known: the stored one for an unchanged edit, the
  // preview otherwise. Null while counting, or when the count failed.
  const days: number | null = unchangedEdit
    ? (editing?.totalDays ?? null)
    : current?.state === "ready"
      ? current.totalDays
      : null;
  const counting = previewKey !== null && current === null;
  const countFailed = current?.state === "failed";
  const noWorkingDays = days === 0;
  const calendarSpan = countFailed ? calendarDaysBetween(startDate, endDate) : null;

  const balanceByType = useMemo(
    () => new Map(balances.map((b) => [b.leaveTypeId, b])),
    [balances],
  );
  const selectedBalance = balanceByType.get(leaveTypeId) ?? null;

  // The server refuses an application that exceeds the balance. Saying so here
  // means the refusal isn't the first time somebody learns it — but it stays a
  // warning, not a block: the balance shown is today's, and the server decides
  // against the start date, which can differ on a pro-rated type. Only warned
  // on the server's own count, never on a calendar-day guess.
  const overBalance =
    selectedBalance !== null &&
    selectedBalance.paid &&
    days !== null &&
    days > selectedBalance.remainingDays;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!leaveTypeId || !startDate || !endDate) return;
    setSaving(true);
    setError(null);
    try {
      // Uploaded first and separately: a file the server refuses (wrong type,
      // too big) then costs a retry rather than a half-written application.
      const uploaded = attachment ? await uploadLeaveAttachment(attachment) : null;

      const body = {
        leaveTypeId,
        startDate,
        endDate,
        reason: reason.trim() || undefined,
        attachmentUrl: uploaded?.attachmentUrl,
        attachmentName: uploaded?.attachmentName,
        // A Xero-hosted file is served by its id, so it travels with the url.
        xeroFileId: uploaded?.xeroFileId ?? undefined,
      };
      const app = editing
        ? await editLeave(editing.id, { ...body, duration })
        : await applyLeave(body);
      onCreated(app);
      onClose();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : editing
            ? "Could not save your changes."
            : "Could not submit your leave.",
      );
    } finally {
      setSaving(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-[520px] overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl">
        <form onSubmit={handleSubmit} className="nice-scrollbar max-h-[90vh] overflow-y-auto p-6 sm:p-8">
          <div className="flex items-start justify-between gap-4 border-b border-border/60 pb-4">
            <div>
              <h2 className="text-2xl font-black text-foreground">
                {editing ? "Edit leave" : "Apply for leave"}
              </h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {editing
                  ? "Change the type, dates or reason. It stays pending for your approver."
                  : "Pick a type and your dates."}
              </p>
            </div>
            <button
              type="button"
              aria-label="Close"
              onClick={onClose}
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          <div className="mt-5 space-y-4">
            <div className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Leave type</span>
              <Select value={leaveTypeId} onValueChange={setLeaveTypeId}>
                <SelectTrigger>
                  <SelectValue placeholder="Select a type" />
                </SelectTrigger>
                <SelectContent searchPlaceholder="Search types…">
                  {types.map((t) => {
                    const b = balanceByType.get(t.id);
                    return (
                      <SelectItem key={t.id} value={t.id}>
                        {t.name}
                        {t.isArchived ? " (archived)" : ""}
                        {!t.paid
                          ? " (unpaid)"
                          : b
                            ? ` · ${formatDays(b.remainingDays)} left`
                            : ""}
                      </SelectItem>
                    );
                  })}
                </SelectContent>
              </Select>

              {/* The number people actually open this form wondering about. */}
              {selectedBalance ? (
                selectedBalance.paid ? (
                  <p className="text-xs text-muted-foreground">
                    <span className="font-semibold text-foreground">
                      {formatDays(selectedBalance.remainingDays)} day
                      {selectedBalance.remainingDays === 1 ? "" : "s"} left
                    </span>{" "}
                    of {formatDays(selectedBalance.entitlementDays)} · {formatDays(selectedBalance.takenDays)}{" "}
                    taken
                    {selectedBalance.pendingDays > 0
                      ? ` · ${formatDays(selectedBalance.pendingDays)} awaiting approval`
                      : ""}
                  </p>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    Unpaid — no balance to draw down.{" "}
                    {formatDays(selectedBalance.takenDays)} day
                    {selectedBalance.takenDays === 1 ? "" : "s"} taken so far.
                  </p>
                )
              ) : null}
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Start date</span>
                <input
                  required
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className={INPUT}
                />
              </label>
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">End date</span>
                <input
                  required
                  type="date"
                  min={startDate || undefined}
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className={INPUT}
                />
              </label>
            </div>

            {halfDayAcrossDates ? (
              <p className="rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                A half day has to start and end on the same date.
              </p>
            ) : noWorkingDays ? (
              <p className="rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                Those dates are all weekends or public holidays — there are no working days to take off.
              </p>
            ) : days !== null ? (
              <p className="text-sm text-muted-foreground">
                Duration:{" "}
                <span className="font-semibold text-foreground">
                  {formatDays(days)} working day{days === 1 ? "" : "s"}
                  {halfDay ? (duration === "MORNING" ? " (morning)" : " (afternoon)") : ""}
                </span>
              </p>
            ) : counting ? (
              <p className="text-sm text-muted-foreground">Counting working days…</p>
            ) : calendarSpan !== null ? (
              // The count failed: the calendar span is all that's left, so it
              // is labelled for what it is and no balance warning is drawn from it.
              <p className="text-sm text-muted-foreground">
                About {calendarSpan} calendar day{calendarSpan === 1 ? "" : "s"} — days off and public
                holidays won't be charged.
              </p>
            ) : null}

            {overBalance && selectedBalance && days !== null ? (
              <p className="rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                That's {formatDays(days)} day{days === 1 ? "" : "s"} against{" "}
                {formatDays(selectedBalance.remainingDays)} left — this will likely be refused.
              </p>
            ) : null}

            <label className="block space-y-2">
              <span className="text-sm font-semibold text-foreground">
                Reason <span className="font-normal text-muted-foreground">(optional)</span>
              </span>
              <textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="min-h-[88px] w-full rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>

            <div className="space-y-2">
              <span className="text-sm font-semibold text-foreground">
                Attachment <span className="font-normal text-muted-foreground">(optional)</span>
              </span>
              <input
                ref={attachmentInput}
                id="leaveAttachment"
                type="file"
                className="sr-only"
                accept="image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf"
                onChange={(e) => setAttachment(e.target.files?.[0] ?? null)}
              />
              {attachment ? (
                <div className="flex items-center gap-3 rounded-2xl border border-border bg-white/80 px-4 py-3 shadow-sm">
                  <Paperclip className="h-4 w-4 shrink-0 text-primary" />
                  <span className="min-w-0 flex-1 truncate text-sm text-foreground">
                    {attachment.name}
                  </span>
                  <button
                    type="button"
                    onClick={() => {
                      setAttachment(null);
                      // Clearing the input too, or re-picking the same file
                      // fires no change event and the chip never comes back.
                      if (attachmentInput.current) attachmentInput.current.value = "";
                    }}
                    className="shrink-0 text-xs font-semibold text-muted-foreground transition hover:text-destructive"
                  >
                    Remove
                  </button>
                </div>
              ) : editing?.attachmentName ? (
                <label
                  htmlFor="leaveAttachment"
                  className="flex cursor-pointer items-center gap-3 rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm shadow-sm transition hover:border-primary/50"
                >
                  <Paperclip className="h-4 w-4 shrink-0 text-primary" />
                  <span className="min-w-0 flex-1 truncate text-foreground">{editing.attachmentName}</span>
                  {/* Kept unless a new file is picked — the edit never drops it. */}
                  <span className="shrink-0 text-xs font-semibold text-muted-foreground">Replace</span>
                </label>
              ) : (
                <label
                  htmlFor="leaveAttachment"
                  className="flex cursor-pointer items-center gap-3 rounded-2xl border border-dashed border-border bg-white/60 px-4 py-3 text-sm text-muted-foreground shadow-sm transition hover:border-primary/50"
                >
                  <Paperclip className="h-4 w-4 shrink-0" />
                  Attach an MC or supporting document
                </label>
              )}
              <p className="text-xs text-muted-foreground">
                JPG, PNG, WEBP, HEIC, HEIF or PDF, up to 8 MB.
              </p>
            </div>
          </div>

          {error ? (
            <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              {error}
            </p>
          ) : null}

          <div className="mt-6 flex justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={saving || !leaveTypeId || !datesValid || halfDayAcrossDates || noWorkingDays}
              className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              {editing ? "Save changes" : "Submit"}
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body,
  );
}
