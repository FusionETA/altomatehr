import { useRef, useState } from "react";
import { CircleCheck, Download, LoaderCircle, Upload } from "lucide-react";
import {
  downloadSalaryAdjustmentTemplate,
  importSalaryAdjustments,
  type SalaryAdjustmentImportResult,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { BUTTON, BUTTON_GHOST, HINT } from "../lib/ui";
import { ModalPortal } from "./ModalPortal";

// New salaries for many employees at once — an annual increment round —
// each recorded in their salary history with the date it took effect and why.
//
// Two steps, like the other imports: download the template (every payroll
// employee with their current salary), fill in New Salary for those whose
// pay is changing, upload it back. All or nothing: one bad row saves nothing.
export function ImportSalaryAdjustmentsDialog({
  onClose,
  onImported,
}: {
  onClose: () => void;
  onImported: () => void;
}) {
  const fileInput = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [importing, setImporting] = useState(false);
  const [result, setResult] = useState<SalaryAdjustmentImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [rowErrors, setRowErrors] = useState<{ row: number; message: string }[]>([]);

  async function download() {
    setDownloading(true);
    setError(null);
    try {
      saveFile(await downloadSalaryAdjustmentTemplate());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not build the template.");
    } finally {
      setDownloading(false);
    }
  }

  async function upload() {
    if (!file) return;

    setImporting(true);
    setError(null);
    setRowErrors([]);
    setResult(null);
    try {
      const imported = await importSalaryAdjustments(file);
      setResult(imported);
      onImported();
    } catch (e) {
      // A rejected file comes back as a 400 carrying the report: list every
      // row's problems, or the file-level message.
      const body = (e as { body?: Partial<SalaryAdjustmentImportResult> })?.body;
      if (body?.errors?.length) {
        setRowErrors(body.errors);
        setError("Nothing was saved — fix these rows and upload again.");
      } else {
        setError(body?.message ?? (e instanceof Error ? e.message : "Could not import that file."));
      }
    } finally {
      setImporting(false);
    }
  }

  const total = result ? result.changed + result.firstSalaries : 0;

  return (
    <ModalPortal label="Import salary adjustments" onClose={onClose}>
      <header className="border-b border-border/70 px-6 py-4">
        <h2 className="text-base font-semibold text-foreground">Import salary adjustments</h2>
        <p className={HINT}>
          New salaries for many employees at once — each recorded in their salary history with its
          effective date and reason.
        </p>
      </header>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
        <p className="rounded-2xl border border-warning/30 bg-warning/10 p-3 text-xs leading-snug text-foreground">
          <span className="font-bold">A new salary applies straight away.</span> The effective date
          can be today or earlier, not later — upload an increment on or after the day it takes
          effect, then re-run payroll. For a typo fix, edit the employee instead and choose
          &quot;Typo correction&quot;.
        </p>

        <section className="space-y-2">
          <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
            Step 1 — the template
          </h3>
          <button
            type="button"
            onClick={() => void download()}
            disabled={downloading}
            className={BUTTON_GHOST}
          >
            {downloading ? (
              <LoaderCircle className="size-4 animate-spin" aria-hidden />
            ) : (
              <Download className="size-4" aria-hidden />
            )}
            Download template
          </button>
          <p className={HINT}>
            Every payroll employee with their current salary. Fill in New Salary (and optionally
            Effective Date, Reason, Notes) only for those whose pay is changing — a blank row is
            left alone. For hourly staff, New Salary is the hourly rate.
          </p>
        </section>

        <section className="space-y-2">
          <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
            Step 2 — upload it back
          </h3>
          <input
            ref={fileInput}
            type="file"
            accept=".xlsx,.csv"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setResult(null);
              setRowErrors([]);
              setError(null);
            }}
            className="block w-full text-sm text-muted-foreground file:mr-3 file:rounded-xl file:border file:border-border/70 file:bg-card file:px-3 file:py-2 file:text-sm file:font-bold file:text-foreground hover:file:bg-muted"
          />
        </section>

        {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

        {rowErrors.length > 0 ? (
          <ul className="max-h-48 space-y-1 overflow-y-auto rounded-2xl border border-destructive/20 bg-destructive/5 p-3 text-xs">
            {rowErrors.map((row) => (
              <li key={`${row.row}-${row.message}`} className="text-destructive">
                <span className="font-bold">Row {row.row}:</span> {row.message}
              </li>
            ))}
          </ul>
        ) : null}

        {result?.ok ? (
          <div className="space-y-1.5 rounded-2xl border border-success/30 bg-success/10 p-3 text-sm">
            <p className="flex items-center gap-1.5 font-bold text-success">
              <CircleCheck className="size-4" aria-hidden />
              {total === 0
                ? "No salaries changed."
                : `${total} salar${total === 1 ? "y" : "ies"} updated.`}
            </p>
            {result.changed > 0 ? (
              <p className="text-xs text-muted-foreground">
                {result.changed} recorded in the salary history.
              </p>
            ) : null}
            {result.firstSalaries > 0 ? (
              <p className="text-xs text-muted-foreground">
                {result.firstSalaries} had no salary before — set, but not recorded as a change.
              </p>
            ) : null}
            {result.unchanged > 0 ? (
              <p className="text-xs text-muted-foreground">
                {result.unchanged} already had that salary, so nothing changed for them.
              </p>
            ) : null}
            {total > 0 ? (
              <p className="text-xs text-muted-foreground">
                Draft payroll runs are marked for a re-run.
              </p>
            ) : null}
          </div>
        ) : null}
      </div>

      <footer className="flex justify-end gap-2 border-t border-border/70 px-6 py-4">
        <button type="button" className={BUTTON_GHOST} onClick={onClose} disabled={importing}>
          {result?.ok ? "Done" : "Cancel"}
        </button>
        <button
          type="button"
          className={BUTTON}
          onClick={() => void upload()}
          disabled={!file || importing}
        >
          {importing ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Upload className="size-4" aria-hidden />
          )}
          {importing ? "Importing…" : "Import"}
        </button>
      </footer>
    </ModalPortal>
  );
}
