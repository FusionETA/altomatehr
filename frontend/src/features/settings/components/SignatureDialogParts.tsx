import { useEffect } from "react";
import { createPortal } from "react-dom";
import { PenLine, X } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { SIGNATURE_NAME_MAX, SIGNATURE_POSITION_MAX } from "../lib/signatures";

// The pieces the add and edit signature dialogs share: the modal shell, and
// the name / position / default fields printed under the signature.

const INPUT =
  "h-11 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";

export function SignatureDialogShell({
  title,
  busy,
  onClose,
  children,
  footer,
}: {
  title: string;
  /** While saving: Escape, the backdrop and × don't close it. */
  busy: boolean;
  onClose: () => void;
  children: React.ReactNode;
  footer: React.ReactNode;
}) {
  useBodyScrollLock();

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [busy, onClose]);

  return createPortal(
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm"
      onClick={() => {
        if (!busy) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="signature-dialog-title"
        className="nice-scrollbar flex max-h-[92vh] w-full max-w-lg flex-col overflow-hidden rounded-[28px] border border-border/70 bg-card shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 border-b border-border/60 px-6 py-4">
          <div className="flex items-center gap-2">
            <span className="flex size-9 items-center justify-center rounded-full bg-primary/10 text-primary">
              <PenLine className="h-4 w-4" />
            </span>
            <h3 id="signature-dialog-title" className="text-base font-black text-foreground">
              {title}
            </h3>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            disabled={busy}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:opacity-50"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="nice-scrollbar flex-1 space-y-4 overflow-y-auto px-6 py-5">{children}</div>
        <div className="flex justify-end gap-2 border-t border-border/60 px-6 py-4">{footer}</div>
      </div>
    </div>,
    document.body,
  );
}

export function SignatureFields({
  name,
  position,
  isDefault,
  onChange,
  disabled,
  defaultLocked,
}: {
  name: string;
  position: string;
  isDefault: boolean;
  onChange: (next: { name?: string; position?: string; isDefault?: boolean }) => void;
  disabled?: boolean;
  /** Why the default box can't be changed (it's the first one, or already the default). */
  defaultLocked?: string | null;
}) {
  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="block space-y-1.5">
          <span className="text-sm font-semibold text-foreground">Name</span>
          <input
            value={name}
            onChange={(e) => onChange({ name: e.target.value })}
            maxLength={SIGNATURE_NAME_MAX}
            disabled={disabled}
            placeholder="e.g. Tan Mei Ling"
            className={INPUT}
          />
        </label>
        <label className="block space-y-1.5">
          <span className="text-sm font-semibold text-foreground">Position</span>
          <input
            value={position}
            onChange={(e) => onChange({ position: e.target.value })}
            maxLength={SIGNATURE_POSITION_MAX}
            disabled={disabled}
            placeholder="e.g. HR Manager"
            className={INPUT}
          />
        </label>
      </div>
      <p className="text-xs text-muted-foreground">
        Printed under the signature, and used as the letter's signatory name and position.
      </p>
      <label
        className={`flex items-start gap-2 text-sm ${
          defaultLocked ? "cursor-not-allowed text-muted-foreground" : "cursor-pointer text-foreground"
        }`}
      >
        <input
          type="checkbox"
          checked={isDefault}
          disabled={disabled || !!defaultLocked}
          onChange={(e) => onChange({ isDefault: e.target.checked })}
          className="mt-0.5 h-4 w-4 rounded border-border accent-primary"
        />
        <span>
          Make this the company default
          <span className="block text-xs text-muted-foreground">
            {defaultLocked ?? "Used on letters unless the template or the letter picks another."}
          </span>
        </span>
      </label>
    </div>
  );
}
