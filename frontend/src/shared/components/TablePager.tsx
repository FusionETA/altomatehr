import { ChevronLeft, ChevronRight } from "lucide-react";

import type { Paged } from "@/shared/lib/use-paged";

function PagerButton({
  onClick,
  disabled,
  label,
  children,
}: {
  onClick: () => void;
  disabled: boolean;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      className="inline-flex h-8 w-8 items-center justify-center rounded-full border border-border/60 text-muted-foreground transition-colors duration-150 hover:bg-surface-low hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-35"
    >
      {children}
    </button>
  );
}

/**
 * Footer for a paged table: which rows you are looking at, and the controls to
 * move. `noun` names the rows ("approval", "record") and is pluralised here.
 *
 * The range is always shown, even on a single page — "1–4 of 4" is how you know
 * nothing is being held back, which a bare table cannot tell you.
 */
export function TablePager<T>({ paged, noun }: { paged: Paged<T>; noun: string }) {
  const { page, pageCount, total, from, to, setPage } = paged;
  if (total === 0) return null;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border/60 px-6 py-3">
      <span className="text-xs tabular-nums text-muted-foreground">
        {from}–{to} of {total} {total === 1 ? noun : `${noun}s`}
      </span>

      {pageCount > 1 ? (
        <div className="flex items-center gap-1.5">
          <PagerButton onClick={() => setPage(page - 1)} disabled={page === 0} label="Previous page">
            <ChevronLeft className="h-4 w-4" aria-hidden />
          </PagerButton>
          <span className="px-1 text-xs font-semibold tabular-nums text-foreground">
            Page {page + 1} of {pageCount}
          </span>
          <PagerButton
            onClick={() => setPage(page + 1)}
            disabled={page >= pageCount - 1}
            label="Next page"
          >
            <ChevronRight className="h-4 w-4" aria-hidden />
          </PagerButton>
        </div>
      ) : null}
    </div>
  );
}
