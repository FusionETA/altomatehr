import { useState } from "react";
import { Download } from "lucide-react";
import {
  exportApprovalAudit,
  type AdminAttendanceFilter,
  type ExportFormat,
} from "@/features/attendance/api";
import { saveFile } from "@/shared/lib/api-client";
import { EYEBROW } from "../lib/dashboard-styles";
import { ACTION_MENU_ITEM, ActionMenu } from "./ActionMenu";

// Getting the approval trail out of the system, modelled on
// ClaimsMonthEndActions so the two admin screens behave the same way: a menu
// beside the tabs rather than a button above the table, because exporting is an
// occasional act and what needs a decision is the daily one.
//
// EXPORT ONLY, and deliberately so — there is no import counterpart and
// shouldn't be. This is a record of decisions the system actually made; a file
// that could write into it would let an approval be asserted that nobody gave.

const EXPORT_FORMATS: ExportFormat[] = ["csv", "xlsx", "pdf"];

export function ApprovalTrailExport({
  from,
  to,
  filter,
  rowCount,
}: {
  from: string;
  to: string;
  // The export mirrors what the admin is looking at — export what you filtered,
  // not everything.
  filter: AdminAttendanceFilter;
  rowCount: number;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);

  async function exportAs(format: ExportFormat) {
    setBusy(true);
    setError(null);
    try {
      saveFile(await exportApprovalAudit(format, from, to, filter));
      setOpen(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  // Spells out what the file will contain, since the filters that shaped it sit
  // in a panel that may well be collapsed by the time someone clicks Export.
  const narrowed = [
    filter.projectId ? "project" : null,
    filter.teamId ? "team" : null,
    filter.q?.trim() ? "search" : null,
  ].filter(Boolean);

  const summary =
    `${rowCount} request${rowCount === 1 ? "" : "s"}, ${from} to ${to}` +
    (narrowed.length > 0 ? ` — narrowed by ${narrowed.join(", ")}.` : ".");

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
        open={open}
        onOpenChange={setOpen}
        busy={busy}
        disabled={busy}
      >
        <>
          <p className={`px-3 pb-1.5 pt-1 ${EYEBROW}`}>Approval trail as</p>
          {EXPORT_FORMATS.map((format) => (
            <button
              key={format}
              type="button"
              disabled={busy}
              onClick={() => void exportAs(format)}
              className={ACTION_MENU_ITEM}
            >
              <Download className="h-3.5 w-3.5 shrink-0" />
              {format.toUpperCase()}
            </button>
          ))}
          <p className="px-3 pb-1 pt-2 text-[11px] leading-snug text-muted-foreground">
            {summary}
          </p>
        </>
      </ActionMenu>
    </div>
  );
}
