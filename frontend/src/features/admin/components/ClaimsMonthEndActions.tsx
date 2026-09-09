import { useState } from "react";
import { Download } from "lucide-react";
import {
  exportClaimsSummary,
  type ClaimsExportFilters,
  type ExportFormat,
} from "@/features/claims/api";
import { saveFile } from "@/shared/lib/api-client";
import { EYEBROW } from "../lib/dashboard-styles";
import { ACTION_MENU_ITEM, ActionMenu } from "./ActionMenu";

// Month-end, in reach but out of the way. Getting claims out of the system
// matters once a month; what needs a decision matters every day. So this sits
// beside the tabs as a menu rather than a banner above the dashboard — one
// click away, but never the first thing an admin reads.
//
// EXPORT ONLY. Claims import was removed: the reference app has never had one
// (its only claims route is an export), and ours wrote rows straight to
// APPROVED with no receipt, no approver and nothing marking them as imported —
// a money record asserting an approval that never happened, indistinguishable
// from one this app actually approved.

const EXPORT_FORMATS: ExportFormat[] = ["csv", "xlsx", "pdf"];

export function ClaimsMonthEndActions({
  filters,
  filterSummary,
}: {
  // The export mirrors what the admin is looking at — export what you filtered,
  // not everything.
  filters: ClaimsExportFilters;
  filterSummary: string;
}) {
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<"export" | null>(null);

  async function run(key: string, action: () => Promise<void>) {
    setBusy(key);
    setError(null);
    try {
      await action();
      setOpen(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(null);
    }
  }

  const exportAs = (format: ExportFormat) =>
    run(`export:${format}`, async () => saveFile(await exportClaimsSummary(format, filters)));


  const working = busy !== null;

  return (
    <div className="relative flex items-center gap-2">
      {error ? (
        <p className="max-w-[16rem] truncate text-xs font-semibold text-destructive" title={error}>
          {error}
        </p>
      ) : null}

      <ActionMenu
        label="Export"
        icon={<Download className="h-3.5 w-3.5" />}
        open={open === "export"}
        onOpenChange={(next) => setOpen(next ? "export" : null)}
        busy={busy?.startsWith("export") ?? false}
        disabled={working}
      >
        <>
            <p className={`px-3 pb-1.5 pt-1 ${EYEBROW}`}>Summary as</p>
            {EXPORT_FORMATS.map((format) => (
              <button
                key={format}
                type="button"
                disabled={working}
                onClick={() => exportAs(format)}
                className={ACTION_MENU_ITEM}
              >
                <Download className="h-3.5 w-3.5 shrink-0" />
                {format.toUpperCase()}
              </button>
            ))}
            <p className="px-3 pb-1 pt-2 text-[11px] leading-snug text-muted-foreground">
              {filterSummary}
            </p>

        </>
      </ActionMenu>

    </div>
  );
}

