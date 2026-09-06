import { useEffect, useRef, type ReactNode } from "react";
import { ChevronDown, LoaderCircle } from "lucide-react";

export const ACTION_MENU_ITEM =
  "flex w-full items-center gap-2.5 rounded-xl px-3 py-2.5 text-left text-sm font-bold text-muted-foreground transition-colors hover:bg-surface-low hover:text-foreground disabled:pointer-events-none disabled:opacity-50";

export const ACTION_MENU_EYEBROW =
  "text-[10px] font-bold uppercase tracking-[0.16em] text-muted-foreground";

const TRIGGER =
  "inline-flex h-9 items-center gap-1.5 rounded-full border border-border/60 bg-card px-3.5 text-xs font-bold text-muted-foreground shadow-sm transition hover:border-primary/40 hover:text-primary disabled:pointer-events-none disabled:opacity-50";

const MENU =
  "absolute right-0 top-[calc(100%+0.45rem)] z-50 min-w-52 overflow-hidden rounded-2xl border border-border/70 bg-card/98 p-2 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl";

// A pill that opens a small menu of actions — the Export / Import controls on
// the admin screens.
//
// Extracted rather than copied: the trigger and menu are a specific visual
// treatment, and two screens carrying their own copies of the same six class
// strings is how they quietly stop matching.
//
// Closing is handled here (outside pointer-down, Escape) because every caller
// wants exactly that, and each one reimplementing it is how one of them ends up
// with a menu that won't dismiss.
export function ActionMenu({
  label,
  icon,
  open,
  onOpenChange,
  busy = false,
  disabled = false,
  children,
}: {
  label: string;
  icon: ReactNode;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Swaps the icon for a spinner while this menu's action runs. */
  busy?: boolean;
  disabled?: boolean;
  children: ReactNode;
}) {
  const root = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: PointerEvent) {
      if (!root.current?.contains(event.target as Node)) onOpenChange(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") onOpenChange(false);
    }

    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, onOpenChange]);

  return (
    <div className="relative" ref={root}>
      <button
        type="button"
        disabled={disabled}
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => onOpenChange(!open)}
        className={TRIGGER}
      >
        {busy ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : icon}
        {label}
        <ChevronDown className="h-3 w-3" />
      </button>

      {open ? (
        <div className={MENU} role="menu">
          {children}
        </div>
      ) : null}
    </div>
  );
}
