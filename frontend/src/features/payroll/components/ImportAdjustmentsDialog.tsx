import { useRef, useState } from "react";
import { CircleAlert, CircleCheck, Download, LoaderCircle, Upload } from "lucide-react";
import {
  downloadAdjustmentTemplate,
  importAdjustments,
  type AdjustmentImportResult,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { BUTTON, BUTTON_GHOST, HINT } from "../lib/ui";
import { ModalPortal } from "./ModalPortal";

// Bulk adjustments, in two steps: download the template, upload it back.
//
// The template arrives pre-filled with the run's payable employees AND the
// manual lines already on it, because importing REPLACES those lines. That is
// the one thing about this dialog worth reading twice, so it is stated on the
// way in rather than explained afterwards in an error.
export function ImportAdjustmentsDialog({
  runId,
  periodLabel,
  onClose,
  onImported,
}: {
  runId: string;
  periodLabel: string;
  onClose: () => void;
  onImported: () => void;
}) {
  const fileInput = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [importing, setImporting] = useState(false);
  const [result, setResult] = useState<AdjustmentImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [rowErrors, setRowErrors] = useState<{ row: number; message: string }[]>([]);

  async function download() {
    setDownloading(true);
    setError(null);
    try {
      saveFile(await downloadAdjustmentTemplate(runId));
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
      const imported = await importAdjustments(runId, file);
      setResult(imported);
      onImported();
    } catch (e) {
      // Row errors come back as a 400 body rather than a message, so they are
      // pulled off the error and listed — "the file has errors" alone sends an
      // admin hunting through forty rows.
      const body = (e as { body?: { errors?: { row: number; message: string }[] } })?.body;
      if (body?.errors?.length) {
        setRowErrors(body.errors);
        setError("Nothing was imported — fix these rows and upload again.");
      } else {
        setError(e instanceof Error ? e.message : "Could not import that file.");
      }
    } finally {
      setImporting(false);
    }
  }

  return (
    <ModalPortal label="Import adjustments" onClose={onClose}>
      <header className="border-b border-border/70 px-6 py-4">
        <h2 className="text-base font-semibold text-foreground">Import adjustments</h2>
        <p className={HINT}>
          Bulk one-off lines for {periodLabel}, from a spreadsheet instead of one employee
          at a time.
        </p>
      </header>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
        {/* Stated before either button, because by the time an admin has
            uploaded it is too late to learn it. */}
        <p className="rounded-2xl border border-warning/30 bg-warning/10 p-3 text-xs leading-snug text-foreground">
          <span className="font-bold">Importing replaces every manual line on this run.</span>{" "}
          A line missing from the file is a line deleted, so start from the template — it comes
          pre-filled with what is already there. Salary is the exception: leave those columns
          blank and nobody's pay changes.
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
            One row per existing line, plus a blank row per employee. The Categories sheet lists
            every code the Category column accepts.
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
              {result.linesWritten} line{result.linesWritten === 1 ? "" : "s"} across{" "}
              {result.employeesAffected} employee{result.employeesAffected === 1 ? "" : "s"}.
            </p>

            {/* Both of these are consequences an admin should not have to infer
                from a payslip later. */}
            {result.employeesCleared > 0 ? (
              <p className="text-xs text-muted-foreground">
                {result.employeesCleared} employee
                {result.employeesCleared === 1 ? " was" : "s were"} not in the file, so their
                previous manual lines were removed.
              </p>
            ) : null}

            {result.salaryChangesApplied > 0 ? (
              <p className="text-xs text-muted-foreground">
                {result.salaryChangesApplied} salary change
                {result.salaryChangesApplied === 1 ? "" : "s"} applied and recorded in the
                salary history.
              </p>
            ) : null}

            {result.salarySkippedNotMonthly.length > 0 ? (
              <p className="flex items-start gap-1.5 text-xs text-foreground">
                <CircleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />
                <span>
                  Salary left unchanged for {result.salarySkippedNotMonthly.join(", ")} — the
                  column sets a monthly figure and they are paid hourly.
                </span>
              </p>
            ) : null}

            <p className="text-xs text-muted-foreground">
              Regenerate the payslips to see it on the run.
            </p>
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
