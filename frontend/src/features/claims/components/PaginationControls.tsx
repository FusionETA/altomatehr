export const CLAIMS_PAGE_SIZE = 10;

export function PaginationControls({
  currentPage,
  totalItems,
  onPageChange,
  className = "",
}: {
  currentPage: number;
  totalItems: number;
  onPageChange: (page: number) => void;
  className?: string;
}) {
  const totalPages = Math.max(1, Math.ceil(totalItems / CLAIMS_PAGE_SIZE));
  const startItem = totalItems === 0 ? 0 : (currentPage - 1) * CLAIMS_PAGE_SIZE + 1;
  const endItem = Math.min(currentPage * CLAIMS_PAGE_SIZE, totalItems);

  if (totalItems <= CLAIMS_PAGE_SIZE) return null;

  return (
    <div className={className}>
      <div className="text-sm text-muted-foreground">
        Showing <span className="font-semibold text-foreground">{startItem}</span>-
        <span className="font-semibold text-foreground">{endItem}</span> of{" "}
        <span className="font-semibold text-foreground">{totalItems}</span> claims
      </div>

      <div className="flex items-center gap-2">
        <button
          type="button"
          disabled={currentPage === 1}
          onClick={() => onPageChange(currentPage - 1)}
          className="rounded-full px-3 py-2 text-sm font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:pointer-events-none disabled:opacity-45"
        >
          Previous
        </button>
        <span className="text-sm font-medium text-foreground">
          Page {currentPage} of {totalPages}
        </span>
        <button
          type="button"
          disabled={currentPage === totalPages}
          onClick={() => onPageChange(currentPage + 1)}
          className="rounded-full px-3 py-2 text-sm font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:pointer-events-none disabled:opacity-45"
        >
          Next
        </button>
      </div>
    </div>
  );
}
