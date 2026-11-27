import { useState } from "react";
import { Download } from "lucide-react";
import {
  exportApprovalAudit,
  exportAttendanceRecords,
  type AdminAttendanceFilter,
  type ExportFormat,
} from "@/features/attendance/api";
import { saveFile } from "@/shared/lib/api-client";
import { EYEBROW } from "../lib/dashboard-styles";
import { ACTION_MENU_ITEM, ActionMenu } from "./ActionMenu";

// Getting the History tab out of the system, modelled on ClaimsMonthEndActions
// so the two admin screens behave the same way: a menu beside the tabs rather
// than a button above the table, because exporting is an occasional act and
// what needs a decision is the daily one.
//
// Two files, one per table on the tab: the approval trail, and the attendance
// records (with the hours summary over them). One menu rather than two buttons
// because they share the date range and filters, and an admin reaching for one
// is usually a click from wanting the other.
//
// EXPORT ONLY, and deliberately so — there is no import counterpart here. The
// approval trail is a record of decisions the system actually made; a file that
// could write into it would let an approval be asserted that nobody gave.

const EXPORT_FORMATS: ExportFormat[] = ["csv", "xlsx", "pdf"];

type Report = "audit" | "records";

const REPORTS: { key: Report; label: string }[] = [
  { key: "records", label: "Attendance records as" },
  { key: "audit", label: "Approval trail as" },
];

export function HistoryExport({
  from,
  to,
  filter,
  auditCount,
}: {
  from: string;
  to: string;
  // The export mirrors what the admin is looking at — export what you filtered,
  // not everything.
  filter: AdminAttendanceFilter;
  auditCount: number;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);

  async function exportAs(report: Report, format: ExportFormat) {
    setBusy(true);
    setError(null);
    try {
      const file =
        report === "records"
          ? await exportAttendanceRecords(format, from, to, filter)
          : await exportApprovalAudit(format, from, to, filter);
      saveFile(file);
      setOpen(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  // Spells out what the files will contain, since the filters that shaped them
  // sit in a panel that may well be collapsed by the time someone clicks Export.
  const narrowed = [
    filter.projectId ? "project" : null,
    filter.teamId ? "team" : null,
    filter.q?.trim() ? "search" : null,
  ].filter(Boolean);

  const summary =
    `${from} to ${to}` +
    (narrowed.length > 0 ? ` — narrowed by ${narrowed.join(", ")}.` : ", everyone.") +
    ` Approval trail: ${auditCount} request${auditCount === 1 ? "" : "s"}.`;

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
          {REPORTS.map((report, i) => (
            <div key={report.key} className={i > 0 ? "mt-1 border-t border-border/60 pt-1" : ""}>
              <p className={`whitespace-nowrap px-3 pb-1.5 pt-1 ${EYEBROW}`}>{report.label}</p>
              {EXPORT_FORMATS.map((format) => (
                <button
                  key={format}
                  type="button"
                  disabled={busy}
                  onClick={() => void exportAs(report.key, format)}
                  className={ACTION_MENU_ITEM}
                >
                  <Download className="h-3.5 w-3.5 shrink-0" />
                  {format.toUpperCase()}
                </button>
              ))}
            </div>
          ))}
          <p className="max-w-64 px-3 pb-1 pt-2 text-[11px] leading-snug text-muted-foreground">
            {summary}
          </p>
        </>
      </ActionMenu>
    </div>
  );
}
