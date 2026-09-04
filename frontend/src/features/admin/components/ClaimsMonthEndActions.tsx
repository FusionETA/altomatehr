import { useEffect, useRef, useState } from "react";
import { ChevronDown, Download, LoaderCircle, Upload, X } from "lucide-react";
import {
  downloadClaimsImportTemplate,
  exportClaimsSummary,
  importClaims,
  type ClaimsExportFilters,
  type ClaimsImportResult,
  type ExportFormat,
  type ImportFormat,
} from "@/features/claims/api";
import { saveFile } from "@/shared/lib/api-client";
import { EYEBROW } from "../lib/dashboard-styles";

// Month-end, in reach but out of the way. Getting claims out of the system (and
// history back into it) matters once a month; what needs a decision matters
// every day. So these sit beside the tabs as two menus rather than as a banner
// above the dashboard — one click away, but never the first thing an admin reads.

const TRIGGER =
  "inline-flex h-9 items-center gap-1.5 rounded-full border border-border/60 bg-card px-3.5 text-xs font-bold text-muted-foreground shadow-sm transition hover:border-primary/40 hover:text-primary disabled:pointer-events-none disabled:opacity-50";
const MENU =
  "absolute right-0 top-[calc(100%+0.45rem)] z-50 min-w-52 overflow-hidden rounded-2xl border border-border/70 bg-card/98 p-2 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl";
const ITEM =
  "flex w-full items-center gap-2.5 rounded-xl px-3 py-2.5 text-left text-sm font-bold text-muted-foreground transition-colors hover:bg-surface-low hover:text-foreground disabled:pointer-events-none disabled:opacity-50";

const EXPORT_FORMATS: ExportFormat[] = ["csv", "xlsx", "pdf"];
const TEMPLATE_FORMATS: ImportFormat[] = ["csv", "xlsx"];

export function ClaimsMonthEndActions({
  filters,
  filterSummary,
  onImported,
  onReport,
}: {
  // The export mirrors what the admin is looking at — export what you filtered,
  // not everything.
  filters: ClaimsExportFilters;
  filterSummary: string;
  onImported: () => void;
  // The import report is raised to the page, which shows it under the tabs —
  // a menu that closes shouldn't take the result with it.
  onReport: (report: ClaimsImportResult | null) => void;
}) {
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<"export" | "import" | null>(null);
  const fileInput = useRef<HTMLInputElement | null>(null);
  const root = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: PointerEvent) {
      if (!root.current?.contains(event.target as Node)) setOpen(null);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(null);
    }

    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

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

  const template = (format: ImportFormat) =>
    run(`template:${format}`, async () => saveFile(await downloadClaimsImportTemplate(format)));

  function pickFile(file: File | undefined) {
    if (!file) return;
    onReport(null);
    run("import", async () => {
      const result = await importClaims(file);
      onReport(result);
      // Rows landed, so whatever the page is showing is now out of date.
      if (result.imported > 0) onImported();
    });
  }

  const working = busy !== null;

  return (
    <div className="relative flex items-center gap-2" ref={root}>
      {error ? (
        <p className="max-w-[16rem] truncate text-xs font-semibold text-destructive" title={error}>
          {error}
        </p>
      ) : null}

      <div className="relative">
        <button
          type="button"
          disabled={working}
          aria-expanded={open === "export"}
          onClick={() => setOpen((cur) => (cur === "export" ? null : "export"))}
          className={TRIGGER}
        >
          {busy?.startsWith("export") ? (
            <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <Download className="h-3.5 w-3.5" />
          )}
          Export
          <ChevronDown className="h-3 w-3" />
        </button>

        {open === "export" ? (
          <div className={MENU}>
            <p className={`px-3 pb-1.5 pt-1 ${EYEBROW}`}>Summary as</p>
            {EXPORT_FORMATS.map((format) => (
              <button
                key={format}
                type="button"
                disabled={working}
                onClick={() => exportAs(format)}
                className={ITEM}
              >
                <Download className="h-3.5 w-3.5 shrink-0" />
                {format.toUpperCase()}
              </button>
            ))}
            <p className="px-3 pb-1 pt-2 text-[11px] leading-snug text-muted-foreground">
              {filterSummary}
            </p>
          </div>
        ) : null}
      </div>

      <div className="relative">
        <button
          type="button"
          disabled={working}
          aria-expanded={open === "import"}
          onClick={() => setOpen((cur) => (cur === "import" ? null : "import"))}
          className={TRIGGER}
        >
          {busy === "import" || busy?.startsWith("template") ? (
            <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <Upload className="h-3.5 w-3.5" />
          )}
          Import
          <ChevronDown className="h-3 w-3" />
        </button>

        {open === "import" ? (
          <div className={MENU}>
            <p className={`px-3 pb-1.5 pt-1 ${EYEBROW}`}>Blank template</p>
            {TEMPLATE_FORMATS.map((format) => (
              <button
                key={format}
                type="button"
                disabled={working}
                onClick={() => template(format)}
                className={ITEM}
              >
                <Download className="h-3.5 w-3.5 shrink-0" />
                {format.toUpperCase()} template
              </button>
            ))}

            <div className="my-1.5 h-px bg-border/60" />

            <button
              type="button"
              disabled={working}
              onClick={() => fileInput.current?.click()}
              className={`${ITEM} text-primary hover:bg-primary/10 hover:text-primary`}
            >
              <Upload className="h-3.5 w-3.5 shrink-0" />
              Upload a filled file
            </button>
            <p className="px-3 pb-1 pt-1 text-[11px] leading-snug text-muted-foreground">
              Re-uploading is safe — rows already here are skipped.
            </p>
          </div>
        ) : null}
      </div>

      <input
        ref={fileInput}
        type="file"
        accept=".csv,.xlsx"
        className="hidden"
        onChange={(event) => {
          pickFile(event.target.files?.[0]);
          // Let the same file be picked again after a fix.
          event.target.value = "";
        }}
      />
    </div>
  );
}

// What actually happened to an uploaded file, in the three buckets that matter:
// what landed, what was already there, and what needs fixing.
export function ClaimsImportReport({
  report,
  onDismiss,
}: {
  report: ClaimsImportResult;
  onDismiss: () => void;
}) {
  return (
    <section className="rounded-[24px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm">
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm font-bold text-foreground">Import result</p>
        <button
          type="button"
          onClick={onDismiss}
          aria-label="Dismiss import result"
          className="flex h-7 w-7 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>

      <div className="mt-3 grid grid-cols-3 gap-3">
        <ReportStat label="Imported" value={report.imported} tone="text-foreground" />
        <ReportStat label="Skipped" value={report.skipped} tone="text-muted-foreground" />
        <ReportStat
          label="Failed"
          value={report.failed}
          tone={report.failed > 0 ? "text-destructive" : "text-muted-foreground"}
        />
      </div>

      {report.errors.length > 0 ? (
        <ul className="nice-scrollbar mt-3 max-h-48 space-y-1.5 overflow-y-auto">
          {report.errors.map((issue, index) => (
            <li
              key={`${issue.row}-${index}`}
              className="rounded-xl bg-destructive/5 px-3 py-2 text-xs text-destructive"
            >
              <span className="font-bold">Row {issue.row}</span> — {issue.message}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function ReportStat({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className="rounded-xl border border-border/60 bg-surface-low p-3">
      <p className={`text-xl font-black tabular-nums ${tone}`}>{value}</p>
      <p className={`mt-0.5 ${EYEBROW}`}>{label}</p>
    </div>
  );
}
