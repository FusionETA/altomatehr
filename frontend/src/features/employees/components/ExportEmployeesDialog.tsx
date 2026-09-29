import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { Download, FileSpreadsheet, FileText, LoaderCircle, X } from "lucide-react";
import {
  downloadEmployeeDetails,
  getEmployeeExportFields,
  type EmployeeExportFormat,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { useCachedQuery } from "@/shared/lib/use-cached-query";

// Landscape A4 fits about this many columns before they get too narrow to
// read (TabularPdfRenderer.ComfortableColumnCount on the server).
const PDF_COMFORTABLE_COLUMNS = 13;

// The last choice, so a monthly export is one click. Per browser only —
// losing it just means ticking the fields again.
const STORAGE_KEY = "altomatehr.employee-export";

type Saved = { format: EmployeeExportFormat; all: boolean; fields: string[]; includeArchived: boolean };

function loadSaved(): Saved | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as Saved) : null;
  } catch {
    return null;
  }
}

function save(value: Saved) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
  } catch {
    // Private window or blocked storage: the export still works.
  }
}

const choice = (active: boolean) =>
  `flex flex-1 items-center gap-2 rounded-2xl border px-4 py-3 text-sm font-semibold transition ${
    active ? "border-primary bg-primary/5 text-primary" : "border-border/70 bg-card text-foreground hover:bg-muted"
  }`;

// Export employee details: Excel or PDF, every field or just the ones ticked,
// with or without archived employees.
export function ExportEmployeesDialog({ onClose }: { onClose: () => void }) {
  useBodyScrollLock();

  const fieldsQuery = useCachedQuery("/employees/export/fields", getEmployeeExportFields);
  const fields = useMemo(() => fieldsQuery.data ?? [], [fieldsQuery.data]);
  const saved = useMemo(loadSaved, []);

  const [format, setFormat] = useState<EmployeeExportFormat>(saved?.format ?? "Xlsx");
  const [all, setAll] = useState(saved?.all ?? true);
  const [picked, setPicked] = useState<Set<string>>(new Set(saved?.fields ?? []));
  const [includeArchived, setIncludeArchived] = useState(saved?.includeArchived ?? false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // A remembered field this admin can no longer export (Payroll access
  // removed, a column renamed) drops out rather than being sent.
  useEffect(() => {
    if (fields.length === 0) return;
    const allowed = new Set(fields.map((f) => f.key));
    setPicked((cur) => new Set([...cur].filter((k) => allowed.has(k))));
  }, [fields]);

  const groups = useMemo(() => {
    const byGroup = new Map<string, typeof fields>();
    for (const f of fields) byGroup.set(f.group, [...(byGroup.get(f.group) ?? []), f]);
    return [...byGroup.entries()];
  }, [fields]);

  const chosen = all ? fields.map((f) => f.key) : fields.filter((f) => picked.has(f.key)).map((f) => f.key);
  const crampedPdf = format === "Pdf" && chosen.length > PDF_COMFORTABLE_COLUMNS;

  function toggle(keys: string[], on: boolean) {
    setPicked((cur) => {
      const next = new Set(cur);
      for (const k of keys) (on ? next.add(k) : next.delete(k));
      return next;
    });
  }

  async function run() {
    if (chosen.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      saveFile(await downloadEmployeeDetails(format, chosen, includeArchived));
      save({ format, all, fields: [...picked], includeArchived });
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not export.");
      setBusy(false);
    }
  }

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md"
      onClick={onClose}
    >
      <section
        className="flex max-h-[calc(100vh-2.5rem)] w-full max-w-2xl flex-col overflow-hidden rounded-[28px] border border-border/70 bg-card shadow-[0_24px_70px_rgba(32,10,55,0.24)]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 p-5 pb-3 sm:p-6 sm:pb-3">
          <div>
            <h2 className="flex items-center gap-2 text-xl font-black text-foreground">
              <Download className="h-5 w-5 text-primary" />
              Export employee details
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Choose the format and which details to include.
            </p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-5 pb-2 sm:px-6">
          <div className="space-y-2">
            <p className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Format</p>
            <div className="flex gap-2">
              <button type="button" onClick={() => setFormat("Xlsx")} className={choice(format === "Xlsx")}>
                <FileSpreadsheet className="h-4 w-4" /> Excel (.xlsx)
              </button>
              <button type="button" onClick={() => setFormat("Pdf")} className={choice(format === "Pdf")}>
                <FileText className="h-4 w-4" /> PDF
              </button>
            </div>
          </div>

          <div className="space-y-2">
            <p className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Fields</p>
            <div className="flex gap-2">
              <button type="button" onClick={() => setAll(true)} className={choice(all)}>
                All fields{fields.length ? ` (${fields.length})` : ""}
              </button>
              <button type="button" onClick={() => setAll(false)} className={choice(!all)}>
                Choose fields{!all ? ` (${chosen.length})` : ""}
              </button>
            </div>
          </div>

          {fieldsQuery.loading ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" /> Loading fields…
            </p>
          ) : fieldsQuery.error ? (
            <p className="text-sm font-medium text-destructive">{fieldsQuery.error}</p>
          ) : !all ? (
            <div className="space-y-4 rounded-2xl border border-border/60 bg-surface-low p-4">
              <div className="flex flex-wrap gap-3 text-xs font-bold">
                <button type="button" onClick={() => toggle(fields.map((f) => f.key), true)} className="text-primary hover:underline">
                  Select all
                </button>
                <button type="button" onClick={() => setPicked(new Set())} className="text-muted-foreground hover:underline">
                  Clear
                </button>
              </div>
              {groups.map(([group, items]) => {
                const keys = items.map((f) => f.key);
                const allIn = keys.every((k) => picked.has(k));
                return (
                  <div key={group}>
                    <label className="flex items-center gap-2 text-sm font-bold text-foreground">
                      <input type="checkbox" checked={allIn} onChange={(e) => toggle(keys, e.target.checked)} />
                      {group}
                    </label>
                    <div className="mt-2 grid gap-x-4 gap-y-1.5 pl-6 sm:grid-cols-2">
                      {items.map((f) => (
                        <label key={f.key} className="flex items-center gap-2 text-sm text-foreground">
                          <input
                            type="checkbox"
                            checked={picked.has(f.key)}
                            onChange={(e) => toggle([f.key], e.target.checked)}
                          />
                          {f.label}
                        </label>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          ) : null}

          <label className="flex items-center gap-2 text-sm text-foreground">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
            />
            Include archived employees
          </label>

          {crampedPdf ? (
            <p className="rounded-2xl border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-900 dark:text-amber-200">
              A PDF page fits about {PDF_COMFORTABLE_COLUMNS} columns comfortably; {chosen.length} will be
              cramped. Choose fewer fields, or use Excel for a wide export.
            </p>
          ) : null}
          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
        </div>

        <div className="grid gap-2 border-t border-border/60 p-5 sm:grid-cols-2 sm:p-6">
          <button
            type="button"
            disabled={busy || chosen.length === 0}
            onClick={() => void run()}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
            Export {format === "Pdf" ? "PDF" : "Excel"}
          </button>
          <button
            type="button"
            onClick={onClose}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
