import type { ReactNode } from "react";
import { CheckCheck, LoaderCircle, X } from "lucide-react";
import type { BulkResult } from "@/shared/lib/use-bulk-selection";

// The visible half of the bulk-approval design, shared by the claims, leave,
// overtime and attendance queues. Written for claims first; these are the pieces
// that were copied out of it rather than re-implemented per screen, so the four
// queues cannot drift into four slightly different gestures.
//
// Every one of them is approve-only on purpose. Rejection requires a remark, and
// one remark stapled to a dozen unrelated requests tells each employee nothing
// about why theirs was refused — so rejection stays one at a time, where the
// reason can be about that request.

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// The entry point into selection mode. Phone-only on the table-backed queues —
// there the desktop table has its own checkbox column, so there is nothing to
// toggle into. A card-only queue like overtime shows it at every width.
export function SelectModeButton({
  active,
  onToggle,
}: {
  active: boolean;
  onToggle: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      className={`shrink-0 rounded-full border px-3.5 py-1.5 text-xs font-bold transition ${
        active
          ? "border-primary/40 bg-primary/10 text-primary"
          : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
      }`}
    >
      {active ? "Done" : "Select"}
    </button>
  );
}

// Select-all, sized to sit inline beside SelectModeButton in the header row.
// It lives next to Done rather than on a line of its own: the two are one
// control strip — "pick everything" and "stop picking" — and splitting them
// pushed the list down by a row the moment you entered the mode.
export function SelectAllPill({
  inputRef,
  total,
  allSelected,
  onToggleAll,
}: {
  inputRef: React.RefObject<HTMLInputElement | null>;
  total: number;
  allSelected: boolean;
  onToggleAll: () => void;
}) {
  return (
    <label className="flex shrink-0 cursor-pointer items-center gap-2 rounded-full border border-border/60 bg-card px-3.5 py-1.5">
      <input
        ref={inputRef}
        type="checkbox"
        checked={allSelected}
        onChange={onToggleAll}
        className="h-4 w-4 cursor-pointer accent-primary"
      />
      <span className="whitespace-nowrap text-xs font-bold text-foreground">
        {allSelected ? `All ${total}` : `Select all ${total}`}
      </span>
    </label>
  );
}

// The one explanation of what the mode changed — said once, below the control
// strip, instead of every card trying to say it. `className` is where a caller
// scopes it: the table-backed queues pass "md:hidden".
export function SelectHint({
  children,
  className = "",
}: {
  children: ReactNode;
  className?: string;
}) {
  return <p className={`px-1 text-xs text-muted-foreground ${className}`}>{children}</p>;
}

// The sticky bar that appears once something is ticked. `summary` is the line
// under the count — the amount, the days, the hours: a number alone hides what
// is being signed off, a total does not.
export function BulkActionBar({
  count,
  noun,
  summary,
  busy,
  onClear,
  onApprove,
}: {
  count: number;
  noun: string;
  summary?: ReactNode;
  busy: boolean;
  onClear: () => void;
  onApprove: () => void;
}) {
  return (
    <div className="sticky top-20 z-20 flex flex-wrap items-center justify-between gap-3 rounded-[24px] border border-primary/30 bg-primary/5 px-5 py-4 backdrop-blur-sm">
      <div className="min-w-0">
        <p className="text-sm font-bold text-foreground">
          {count} {noun}
          {count === 1 ? "" : "s"} selected
        </p>
        {summary ? <p className="text-xs text-muted-foreground">{summary}</p> : null}
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <button
          type="button"
          disabled={busy}
          onClick={onClear}
          className="rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
        >
          Clear
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onApprove}
          className="inline-flex items-center gap-2 rounded-full bg-secondary px-5 py-2 text-sm font-bold text-secondary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
        >
          {busy ? (
            <LoaderCircle className="h-4 w-4 animate-spin" />
          ) : (
            <CheckCheck className="h-4 w-4" />
          )}
          Approve {count}
        </button>
      </div>
    </div>
  );
}

// What actually happened, per id. The failures are the reason this exists: a
// batch that reports "18 approved" and nothing else leaves the approver to work
// out which two are still sitting in their queue, and why.
export function BulkResultPanel({
  result,
  onDismiss,
}: {
  result: BulkResult;
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

// The desktop table's header checkbox and row checkbox. Separate components only
// so the `disabled` and `accent` styling can't drift between the four tables.
export function BulkSelectAllCheckbox({
  inputRef,
  checked,
  disabled,
  onChange,
}: {
  inputRef: React.RefObject<HTMLInputElement | null>;
  checked: boolean;
  disabled: boolean;
  onChange: () => void;
}) {
  return (
    <input
      ref={inputRef}
      type="checkbox"
      aria-label="Select every row that can be bulk-approved"
      checked={checked}
      disabled={disabled}
      onChange={onChange}
      className="h-4 w-4 cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-40"
    />
  );
}

export function BulkRowCheckbox({
  label,
  checked,
  disabled,
  title,
  onChange,
}: {
  label: string;
  checked: boolean;
  disabled: boolean;
  title?: string;
  onChange: () => void;
}) {
  return (
    <input
      type="checkbox"
      aria-label={label}
      checked={checked}
      disabled={disabled}
      title={title}
      onChange={onChange}
      className="h-4 w-4 cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-40"
    />
  );
}
