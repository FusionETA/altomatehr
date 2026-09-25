import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { AlertTriangle } from "lucide-react";

// The app's own "are you sure?" — in place of `window.confirm`, whose native
// box says "localhost:5173 says", ignores the theme, and cannot mark an action
// as destructive.
//
// Promise-based, so a call site reads exactly like the confirm it replaces:
//
//   const [confirm, confirmDialog] = useConfirm();
//   if (!(await confirm({ title: "Delete team?", ... }))) return;
//   …
//   return <>{…}{confirmDialog}</>;

export type ConfirmOptions = {
  title: string;
  message?: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Deleting or sending something that can't be taken back — red button. */
  destructive?: boolean;
};

type Pending = ConfirmOptions & { resolve: (ok: boolean) => void };

export function useConfirm(): [(options: ConfirmOptions) => Promise<boolean>, React.ReactNode] {
  const [pending, setPending] = useState<Pending | null>(null);

  const confirm = useCallback(
    (options: ConfirmOptions) =>
      new Promise<boolean>((resolve) => setPending({ ...options, resolve })),
    [],
  );

  const settle = useCallback(
    (ok: boolean) => {
      pending?.resolve(ok);
      setPending(null);
    },
    [pending],
  );

  const dialog = pending ? <ConfirmDialog {...pending} onSettle={settle} /> : null;
  return [confirm, dialog];
}

function ConfirmDialog({
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive = false,
  onSettle,
}: ConfirmOptions & { onSettle: (ok: boolean) => void }) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    // Capture phase, and stopped there: opened from inside another modal,
    // Escape should cancel THIS prompt, not also close the modal beneath it.
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.stopPropagation();
      onSettle(false);
    };
    window.addEventListener("keydown", onKey, true);

    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    // A destructive prompt starts on Cancel, so a reflexive Enter never deletes.
    (destructive ? cancelRef : confirmRef).current?.focus();

    return () => {
      window.removeEventListener("keydown", onKey, true);
      document.body.style.overflow = overflow;
    };
  }, [onSettle, destructive]);

  // Rendered at the document root: `position: fixed` resolves against the
  // nearest ancestor with a transform or backdrop-filter, and several cards
  // carry `backdrop-blur`, which would shrink the overlay to the card.
  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40 p-4 backdrop-blur-sm"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="confirm-dialog-title"
      onClick={() => onSettle(false)}
    >
      <div
        className="w-full max-w-md rounded-[28px] border border-border bg-background p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start gap-3">
          {destructive ? (
            <span className="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-full bg-destructive/10 text-destructive">
              <AlertTriangle className="size-4" aria-hidden />
            </span>
          ) : null}
          <div className="min-w-0">
            <h2 id="confirm-dialog-title" className="text-base font-semibold text-foreground">
              {title}
            </h2>
            {message ? <div className="mt-1.5 text-sm text-muted-foreground">{message}</div> : null}
          </div>
        </div>

        <div className="mt-6 flex justify-end gap-2">
          <button
            ref={cancelRef}
            type="button"
            onClick={() => onSettle(false)}
            className="inline-flex h-10 items-center justify-center rounded-2xl border border-border bg-card px-4 text-sm font-semibold text-foreground shadow-sm transition hover:bg-muted/60"
          >
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            onClick={() => onSettle(true)}
            className={`inline-flex h-10 items-center justify-center rounded-2xl px-4 text-sm font-semibold shadow-sm transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 ${
              destructive
                ? "bg-destructive text-white hover:bg-destructive/90 focus-visible:ring-destructive"
                : "bg-primary text-primary-foreground hover:bg-primary/90 focus-visible:ring-primary"
            }`}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
