import { useEffect, useState } from "react";

export type Paged<T> = {
  page: number;
  pageCount: number;
  pageItems: T[];
  total: number;
  /** 1-based index of the first row on this page, or 0 when there are none. */
  from: number;
  /** 1-based index of the last row on this page. */
  to: number;
  setPage: (page: number) => void;
};

/**
 * Client-side pagination over a list already in hand.
 *
 * For tables whose rows are filtered in the browser, which is what the admin
 * history tables do — the whole range is fetched once and narrowed locally, so
 * paging server-side would mean a round trip per click for data already here.
 */
export function usePaged<T>(items: T[], pageSize = 25): Paged<T> {
  const [page, setPage] = useState(0);

  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  // Clamped rather than reset to zero. Narrowing a filter can drop the page
  // you were on, and rendering an empty table under "Page 7 of 3" reads as
  // the rows having gone missing.
  const current = Math.min(page, pageCount - 1);

  useEffect(() => {
    if (page !== current) setPage(current);
  }, [page, current]);

  const start = current * pageSize;
  const pageItems = items.slice(start, start + pageSize);

  return {
    page: current,
    pageCount,
    pageItems,
    total: items.length,
    from: items.length === 0 ? 0 : start + 1,
    to: start + pageItems.length,
    setPage,
  };
}
