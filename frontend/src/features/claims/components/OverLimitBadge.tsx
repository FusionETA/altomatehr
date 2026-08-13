import { TriangleAlert } from "lucide-react";

// Shown on claims whose amount blew past the selected account's spend limit
// (backend sets `exceedsLimit`). A caution, not a rejection — hence amber.
export function OverLimitBadge() {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-amber-800">
      <TriangleAlert className="h-3 w-3" />
      Over limit
    </span>
  );
}
