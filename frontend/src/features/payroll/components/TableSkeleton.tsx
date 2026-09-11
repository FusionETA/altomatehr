// A table-shaped placeholder for the moment before the rows arrive.
//
// Better than the word "Loading…" because it reserves the space the table
// will occupy: the page does not jump when the data lands, and the admin can
// already see how many columns they are about to read. Purely decorative, so
// it is hidden from assistive tech and the live region carries the status.
export function TableSkeleton({
  columns,
  rows = 3,
  label = "Loading",
}: {
  columns: number;
  rows?: number;
  label?: string;
}) {
  return (
    <div className="px-1 py-2">
      <span className="sr-only" role="status">
        {label}
      </span>

      <div aria-hidden className="space-y-3">
        {Array.from({ length: rows }, (_, row) => (
          <div key={row} className="flex gap-3">
            {Array.from({ length: columns }, (_, column) => (
              <div
                key={column}
                className="h-4 flex-1 animate-pulse rounded bg-muted"
                // The first column is a name and the rest are figures, so a
                // uniform grid would read as a spreadsheet rather than as
                // the table it is standing in for.
                style={{ maxWidth: column === 0 ? undefined : "6rem" }}
              />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}
