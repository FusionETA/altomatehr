import { useEffect, useMemo, useRef, useState } from "react";
import { MoreHorizontal } from "lucide-react";
import { cn } from "@/shared/lib/utils";

export type OverflowTabItem<T extends string = string> = {
  id: T;
  label: string;
  badge?: number;
};

type OverflowTabListProps<T extends string = string> = {
  items: OverflowTabItem<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
  menuClassName?: string;
  variant?: "underline" | "segmented";
  ariaLabel?: string;
  gapPx?: number;
};

function classesFor(variant: "underline" | "segmented", active: boolean) {
  if (variant === "segmented") {
    return cn(
      "h-8 min-w-[66px] flex-1 shrink-0 rounded-lg px-3 text-xs font-bold transition-colors sm:min-w-[76px] sm:text-sm",
      active
        ? "bg-card text-primary shadow-sm"
        : "text-muted-foreground hover:bg-card/70 hover:text-foreground",
    );
  }

  return cn(
    "relative inline-flex h-11 shrink-0 items-center gap-1.5 border-b-2 px-0.5 text-sm font-bold transition-colors",
    active
      ? "border-primary text-primary"
      : "border-transparent text-muted-foreground hover:text-foreground",
  );
}

function badgeClasses(active: boolean) {
  return cn(
    "flex h-5 min-w-[1.25rem] shrink-0 items-center justify-center rounded-full px-1 text-[10px] font-bold",
    active ? "bg-destructive text-destructive-foreground" : "bg-muted text-muted-foreground",
  );
}

function Badge({ count, active }: { count?: number; active: boolean }) {
  if (!count || count <= 0) return null;
  return <span className={badgeClasses(active)}>{count > 99 ? "99+" : count}</span>;
}

export function OverflowTabList<T extends string>({
  items,
  value,
  onChange,
  className,
  menuClassName,
  variant = "underline",
  ariaLabel = "Tabs",
  gapPx,
}: OverflowTabListProps<T>) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const measureRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const moreMeasureRef = useRef<HTMLButtonElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const [visibleCount, setVisibleCount] = useState(items.length);
  const [open, setOpen] = useState(false);
  const gap = gapPx ?? (variant === "segmented" ? 4 : 20);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    function measure() {
      const width = container?.clientWidth ?? 0;
      const itemWidths = items.map((_, index) => measureRefs.current[index]?.offsetWidth ?? 0);
      const moreWidth = moreMeasureRef.current?.offsetWidth ?? 44;
      const total = itemWidths.reduce((sum, itemWidth) => sum + itemWidth, 0) + gap * Math.max(0, items.length - 1);

      if (total <= width) {
        setVisibleCount(items.length);
        setOpen(false);
        return;
      }

      let used = moreWidth + gap;
      let count = 0;
      for (const itemWidth of itemWidths) {
        const next = used + itemWidth + (count > 0 ? gap : 0);
        if (next > width) break;
        used = next;
        count += 1;
      }

      setVisibleCount(Math.max(1, Math.min(count, items.length - 1)));
    }

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(container);
    window.addEventListener("resize", measure);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", measure);
    };
  }, [gap, items, variant]);

  useEffect(() => {
    if (!open) return;

    function handlePointerDown(event: PointerEvent) {
      if (!menuRef.current?.contains(event.target as Node)) setOpen(false);
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  const visibleItems = useMemo(() => items.slice(0, visibleCount), [items, visibleCount]);
  const overflowItems = useMemo(() => items.slice(visibleCount), [items, visibleCount]);
  const activeInOverflow = overflowItems.some((item) => item.id === value);

  const listClasses =
    variant === "segmented"
      ? "flex w-full max-w-full items-center gap-1 rounded-xl border border-border/60 bg-surface-low p-1"
      : "flex min-h-11 w-full items-end";

  return (
    <div className={cn("relative", className)} ref={menuRef}>
      <div
        ref={containerRef}
        role="tablist"
        aria-label={ariaLabel}
        className={cn(listClasses)}
        style={variant === "underline" ? { columnGap: gap } : undefined}
      >
        {visibleItems.map((item) => {
          const active = item.id === value;
          return (
            <button
              key={item.id}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => onChange(item.id)}
              className={classesFor(variant, active)}
            >
              {item.label}
              <Badge count={item.badge} active={active} />
            </button>
          );
        })}

        {overflowItems.length > 0 ? (
          <button
            type="button"
            aria-label={open ? "Hide more tabs" : "Show more tabs"}
            aria-expanded={open}
            onClick={() => setOpen((next) => !next)}
            className={cn(
              variant === "segmented"
                ? "grid h-9 w-9 shrink-0 place-items-center rounded-lg transition-colors"
                : "ml-auto grid h-11 w-10 shrink-0 place-items-center border-b-2 transition-colors",
              activeInOverflow
                ? "border-primary bg-card text-primary shadow-sm"
                : "border-transparent text-muted-foreground hover:bg-card/70 hover:text-foreground",
            )}
          >
            <MoreHorizontal className="h-5 w-5" />
          </button>
        ) : null}
      </div>

      {open && overflowItems.length > 0 ? (
        <div
          className={cn(
            "absolute right-0 top-[calc(100%+0.45rem)] z-50 min-w-44 overflow-hidden rounded-2xl border border-border/70 bg-card/98 p-2 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl",
            menuClassName,
          )}
        >
          {overflowItems.map((item) => {
            const active = item.id === value;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => {
                  onChange(item.id);
                  setOpen(false);
                }}
                className={cn(
                  "flex w-full items-center justify-between gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-bold transition-colors",
                  active ? "bg-primary/10 text-primary" : "text-muted-foreground hover:bg-surface-low hover:text-foreground",
                )}
              >
                <span>{item.label}</span>
                <Badge count={item.badge} active={active} />
              </button>
            );
          })}
        </div>
      ) : null}

      <div className="pointer-events-none absolute left-0 top-0 -z-10 flex h-0 overflow-hidden opacity-0">
        {items.map((item, index) => (
          <button
            key={item.id}
            ref={(node) => {
              measureRefs.current[index] = node;
            }}
            type="button"
            className={classesFor(variant, item.id === value)}
            tabIndex={-1}
          >
            {item.label}
            <Badge count={item.badge} active={item.id === value} />
          </button>
        ))}
        <button
          ref={moreMeasureRef}
          type="button"
          className={cn(
            variant === "segmented"
              ? "grid h-9 w-9 shrink-0 place-items-center rounded-lg"
              : "grid h-11 w-10 shrink-0 place-items-center border-b-2",
          )}
          tabIndex={-1}
        >
          <MoreHorizontal className="h-5 w-5" />
        </button>
      </div>
    </div>
  );
}
