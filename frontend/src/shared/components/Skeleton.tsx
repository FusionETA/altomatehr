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
