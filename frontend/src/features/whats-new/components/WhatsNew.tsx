import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Sparkles, X } from "lucide-react";
import { WHATS_NEW, type WhatsNewEntry } from "../lib/entries";
import { hasUnseen, markSeen, readSeen } from "../lib/seen";

// "What's new", for everyone signed in — admins and employees alike.
//
// The panel's open state lives in the shell (through this hook), not in the
// menu item: the account menu unmounts the moment it closes, and the panel
// opened from it has to outlive it.
export function useWhatsNew(email: string) {
  const [open, setOpen] = useState(false);
  const [unseen, setUnseen] = useState(() => hasUnseen(readSeen(email)));

  const show = useCallback(() => {
    setOpen(true);
    markSeen(email);
    setUnseen(false);
  }, [email]);

  const panel = open ? <WhatsNewPanel onClose={() => setOpen(false)} /> : null;
  return { show, unseen, panel };
}

/** The account-menu row. `unseen` adds the dot until the latest notes are read. */
export function WhatsNewMenuItem({
  unseen,
  onSelect,
  className,
}: {
  unseen: boolean;
  onSelect: () => void;
  className: string;
}) {
  return (
    <button type="button" onClick={onSelect} className={className}>
      <Sparkles className="mt-0.5 h-4 w-4 shrink-0" />
      <span className="flex-1">What&apos;s new</span>
      {unseen ? (
        <span className="mt-1.5 size-2 shrink-0 rounded-full bg-primary" aria-label="Unread" />
      ) : null}
    </button>
  );
}

const GROUPS: { key: "new" | "improved" | "fixed"; label: string; tone: string }[] = [
  { key: "new", label: "New", tone: "bg-primary/10 text-primary" },
  { key: "improved", label: "Improved", tone: "bg-muted text-foreground" },
  { key: "fixed", label: "Fixed", tone: "bg-muted text-muted-foreground" },
];

function formatDay(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

function WhatsNewPanel({ onClose }: { onClose: () => void }) {
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = overflow;
    };
  }, [onClose]);

  // At the document root, as ConfirmDialog does: a backdrop-blurred ancestor
  // would otherwise become the containing block for `position: fixed`.
  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40 p-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="whats-new-title"
      onClick={onClose}
    >
      <div
        className="flex max-h-[85vh] w-full max-w-2xl flex-col rounded-[28px] border border-border bg-background shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-4 border-b border-border/60 px-6 py-5">
          <div>
            <h2 id="whats-new-title" className="flex items-center gap-2 text-lg font-semibold text-foreground">
              <Sparkles className="size-5 text-primary" aria-hidden /> What&apos;s new
            </h2>
            <p className="mt-0.5 text-sm text-muted-foreground">
              Recent changes to AltomateHR, newest first.
            </p>
          </div>
          <button
            ref={closeRef}
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="flex size-9 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="size-4" />
          </button>
        </div>

        <div className="overflow-y-auto px-6 py-5">
          {WHATS_NEW.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nothing here yet.</p>
          ) : (
            <ol className="space-y-8">
              {WHATS_NEW.map((entry) => (
                <Release key={entry.date} entry={entry} />
              ))}
            </ol>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}

function Release({ entry }: { entry: WhatsNewEntry }) {
  return (
    <li>
      <h3 className="text-sm font-bold text-foreground">
        {formatDay(entry.date)}
        {entry.title ? <span className="font-medium text-muted-foreground"> · {entry.title}</span> : null}
      </h3>
      <div className="mt-3 space-y-3">
        {GROUPS.map(({ key, label, tone }) => {
          const items = entry[key];
          if (!items?.length) return null;
          return (
            <div key={key}>
              <span className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${tone}`}>
                {label}
              </span>
              <ul className="mt-2 space-y-1.5 pl-1">
                {items.map((item) => (
                  <li key={item} className="flex gap-2 text-sm text-foreground">
                    <span className="mt-2 size-1 shrink-0 rounded-full bg-muted-foreground/60" aria-hidden />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>
          );
        })}
      </div>
    </li>
  );
}
