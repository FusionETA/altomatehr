import { cn } from "@/shared/lib/utils";

// Placeholders shaped like the content that's coming.
//
// A skeleton is worth more than the word "Loading…" for one reason: it occupies
// the same space the real thing will, so nothing jumps when the data lands and
// the eye already knows where to look. That only holds if the shapes match the
// real layout — a skeleton that guesses wrong is a layout shift with extra
// steps, so these are built to mirror the components that use them.
//
// motion-reduce turns the pulse off: a shimmer across a full page is exactly
// the kind of movement that triggers discomfort for some people, and the
// placeholder still reads as "not here yet" without it.

export function Skeleton({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn("animate-pulse rounded-md bg-muted motion-reduce:animate-none", className)}
    />
  );
}

/**
 * Rows for a table that's still loading. `widths` gives each column's bar width
 * as a Tailwind class, so the bars line up under their real headers.
 */
export function SkeletonRows({
  // Table rows are compact, so more of them read as "a table is coming" without
  // filling the screen. Kept as the default for every table for the same
  // reason SkeletonCards has one: consistency across screens.
  rows = 5,
  widths,
}: {
  rows?: number;
  widths: string[];
}) {
  return (
    <>
      {Array.from({ length: rows }, (_, r) => (
        <tr key={r} className="border-b border-border/60 last:border-0">
          {widths.map((w, c) => (
            <td key={c} className="px-3 py-3">
              <Skeleton className={cn("h-4", w)} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

/**
 * A person cell: name over a secondary line, matching a two-line identity row.
 * Kept here rather than inlined because three screens list people this way.
 */
export function SkeletonPerson() {
  return (
    <div className="space-y-1.5">
      <Skeleton className="h-4 w-40" />
      <Skeleton className="h-3 w-56" />
    </div>
  );
}

/**
 * A stack of card placeholders, for the list-of-cards layout the claims, leave
 * and overtime screens use on narrow widths and in their queues.
 *
 * A list's real length is unknowable before it loads, so unlike the fixed
 * sections above this one CAN'T be matched exactly. The default is therefore
 * the convention — every card list in the app shows the same number, so the
 * app looks like itself while loading. Cards are tall, hence fewer than the
 * table default.
 */
export function SkeletonCards({
  count = 3,
  className,
}: {
  count?: number;
  className?: string;
}) {
  return (
    <div className={cn("grid gap-3 sm:gap-4", className)}>
      {Array.from({ length: count }, (_, i) => (
        <div
          key={i}
          className="rounded-[28px] border border-border/70 bg-card/90 p-4 shadow-ambient sm:p-5"
        >
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1 space-y-2">
              <Skeleton className="h-3 w-24" />
              <Skeleton className="h-4 w-48" />
              <Skeleton className="h-3 w-32" />
            </div>
            <div className="space-y-2 text-right">
              <Skeleton className="ml-auto h-5 w-24" />
              <Skeleton className="ml-auto h-5 w-20 rounded-full" />
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * A row of summary tiles.
 *
 * `count` and `className` must MATCH the real section — same number of tiles,
 * same grid classes. A guessed count reflows the page the moment the data
 * lands, which defeats the point. Fixed sections (three stat cards, six
 * panels) can be matched exactly; a variable-length list can't be, so those
 * use a small constant instead.
 */
export function SkeletonStats({
  count = 3,
  className = "grid gap-3 sm:grid-cols-2 lg:grid-cols-3",
}: {
  count?: number;
  className?: string;
}) {
  return (
    <div className={className}>
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient">
          <Skeleton className="h-3 w-28" />
          <Skeleton className="mt-3 h-7 w-20" />
          <Skeleton className="mt-2 h-3 w-36" />
        </div>
      ))}
    </div>
  );
}

/** Several panels in a grid, matching a dashboard of equal-weight sections. */
export function SkeletonPanels({
  count = 2,
  className = "grid gap-6 lg:grid-cols-2",
}: {
  count?: number;
  className?: string;
}) {
  return (
    <div className={className}>
      {Array.from({ length: count }, (_, i) => (
        <SkeletonPanel key={i} />
      ))}
    </div>
  );
}

/** A plain panel placeholder, for a section whose shape isn't a list. */
export function SkeletonPanel({ className }: { className?: string }) {
  return (
    <div className={cn("rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient", className)}>
      <Skeleton className="h-4 w-40" />
      <Skeleton className="mt-3 h-3 w-full max-w-md" />
      <Skeleton className="mt-2 h-3 w-full max-w-sm" />
    </div>
  );
}
