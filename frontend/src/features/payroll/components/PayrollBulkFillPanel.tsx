import { useState, type ChangeEvent } from "react";
import { Download, LoaderCircle, Upload } from "lucide-react";
import {
  downloadPayrollEmployeeExport,
  downloadPayrollEmployeeTemplate,
  importPayrollEmployees,
  type TabularFormat,
  type TabularImportResult,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { PayrollSelect } from "./PayrollSelect";
import { BUTTON, BUTTON_GHOST, CARD, HINT, LABEL } from "../lib/ui";

// Export → edit in a spreadsheet → import back.
//
// The export and the template are the SAME columns, which is the whole point:
// filling thirty people's EPF numbers one profile at a time is how an org
// gives up and files late. Exporting the current state rather than a blank
// template also means an admin can see what is already there before typing.
export function PayrollBulkFillPanel({ onImported }: { onImported: () => void }) {
  const [format, setFormat] = useState<TabularFormat>("Xlsx");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<TabularImportResult | null>(null);

  async function get(key: string, download: () => Promise<{ blob: Blob; fileName: string }>) {
    setBusy(key);
    setError(null);
    try {
      saveFile(await download());
    } catch (err) {
      setError(err instanceof Error ? err.message : "That download did not work.");
    } finally {
      setBusy(null);
    }
  }

  function pick(event: ChangeEvent<HTMLInputElement>) {
    setFile(event.target.files?.[0] ?? null);
    setResult(null);
    setError(null);
  }

  async function upload() {
    if (!file) return;

    setBusy("import");
    setError(null);
    try {
      const next = await importPayrollEmployees(file);
      setResult(next);
      // Even a partly failed import changed some rows, so the roster behind
      // this panel is stale either way.
      onImported();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not read that file.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className={`${CARD} space-y-5`}>
      <header>
        <h2 className="text-base font-semibold text-foreground">Fill these in bulk</h2>
        <p className={HINT}>
          Export what is on file, edit it in a spreadsheet, and bring it back. Matching is by
          email or name, and only the columns you change are written — a blank cell leaves the
          existing value alone.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3">
        <div className="w-32">
          <label className={LABEL} htmlFor="bulkFormat">
            Format
          </label>
          <PayrollSelect
            id="bulkFormat"
            value={format}
            onChange={(next) => next && setFormat(next as TabularFormat)}
            options={[
              { value: "Xlsx", label: "XLSX" },
              { value: "Csv", label: "CSV" },
            ]}
          />
        </div>

        <button
          type="button"
          className={BUTTON_GHOST}
          disabled={busy !== null}
          onClick={() => void get("export", () => downloadPayrollEmployeeExport(format))}
        >
          {busy === "export" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Download className="size-4" aria-hidden />
          )}
          Export current details
        </button>

        <button
          type="button"
          className={BUTTON_GHOST}
          disabled={busy !== null}
          onClick={() => void get("template", () => downloadPayrollEmployeeTemplate(format))}
        >
          {busy === "template" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Download className="size-4" aria-hidden />
          )}
          Blank template
        </button>
      </div>

      <div className="flex flex-wrap items-end gap-3 border-t border-border/60 pt-5">
        <div className="min-w-[260px] flex-1">
          <label className={LABEL} htmlFor="bulkFile">
            File to import
          </label>
          <input
            id="bulkFile"
            type="file"
            accept=".csv,.xlsx"
            onChange={pick}
            className="mt-1 block w-full text-sm text-muted-foreground file:mr-3 file:rounded-full file:border-0 file:bg-primary file:px-4 file:py-2 file:text-xs file:font-semibold file:text-primary-foreground"
          />
        </div>

        <button type="button" className={BUTTON} disabled={busy !== null || !file} onClick={() => void upload()}>
          {busy === "import" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Upload className="size-4" aria-hidden />
          )}
          Import
        </button>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {result ? (
        <div className="rounded-2xl border border-border/70 bg-muted/40 p-4">
          <p className="text-sm font-semibold text-foreground">
            {result.imported} updated · {result.skipped} unchanged · {result.failed} failed
          </p>
          {/* Skipped is not a failure: the import is idempotent, so a
              corrected file can be re-uploaded and the untouched rows report
              as unchanged rather than being written twice. */}
          {result.errors.length > 0 ? (
            <ul className="mt-2 max-h-48 space-y-1 overflow-y-auto text-xs text-destructive">
              {result.errors.map((e) => (
                <li key={`${e.row}-${e.message}`}>
                  Row {e.row}: {e.message}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
