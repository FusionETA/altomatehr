import { useState, type ChangeEvent } from "react";
import { ChevronDown, Download, LoaderCircle, Upload } from "lucide-react";
import {
  downloadPayrollEmployeeExport,
  downloadPayrollEmployeeTemplate,
  importPayrollEmployees,
  type TabularFormat,
  type TabularImportResult,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { PayrollSelect } from "./PayrollSelect";
import { BUTTON_GHOST_SM, BUTTON_SM, FOCUS_RING } from "../lib/ui";

// Export → edit in a spreadsheet → import back.
//
// The export and the template are the SAME columns, which is the whole point:
// filling thirty people's EPF numbers one profile at a time is how an org
// gives up and files late. Exporting the current state rather than a blank
// template also means an admin can see what is already there before typing.
//
// Sized as a toolbar rather than a form, and it brings NO card of its own:
// it is the bottom half of the Employees header card, above the two rosters
// it fills in. A page of this many lists does not also need a page of cards,
// so the round-trip lives with the roster's other controls.
//
// Collapsed by default. Most visits to this screen are to read the roster or
// open one person; a spreadsheet round-trip is an occasional job, and left
// open it pushed the list that IS the page down by a third of a screen.
export function PayrollBulkFillPanel({ onImported }: { onImported: () => void }) {
  const [open, setOpen] = useState(false);
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
    <section className="border-t border-border/60 pt-4">
      {/* The whole row is the toggle, not just the chevron — a 16px target for
          something this easy to mis-click is a needless miss. */}
      <button
        type="button"
        onClick={() => setOpen((was) => !was)}
        aria-expanded={open}
        aria-controls="bulkFill"
        className={`flex w-full items-center justify-between gap-3 rounded-xl text-left ${FOCUS_RING}`}
      >
        <span className="text-sm font-bold text-foreground">Payroll details in bulk</span>
        <span className="flex shrink-0 items-center gap-1.5 text-xs font-semibold text-muted-foreground">
          {open ? "Hide" : "Show"}
          <ChevronDown
            className={`size-4 transition-transform ${open ? "rotate-180" : ""}`}
            aria-hidden
          />
        </span>
      </button>

      {open ? (
        <div id="bulkFill" className="mt-3 space-y-4">
          <p className="text-xs text-muted-foreground">
            Export what is on file, edit it in a spreadsheet, and bring it back. Rows match on email
            or name, and a blank cell leaves the existing value alone.
          </p>

          <div className="flex flex-wrap items-center gap-2">
            <PayrollSelect
              id="bulkFormat"
              ariaLabel="File format"
              value={format}
              onChange={(next) => next && setFormat(next as TabularFormat)}
              options={[
                { value: "Xlsx", label: "XLSX" },
                { value: "Csv", label: "CSV" },
              ]}
              className="h-9 w-[88px] rounded-xl text-xs"
            />

            <button
              type="button"
              className={BUTTON_GHOST_SM}
              disabled={busy !== null}
              onClick={() => void get("export", () => downloadPayrollEmployeeExport(format))}
            >
              {busy === "export" ? (
                <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
              ) : (
                <Download className="size-3.5" aria-hidden />
              )}
              Export current details
            </button>

            <button
              type="button"
              className={BUTTON_GHOST_SM}
              disabled={busy !== null}
              onClick={() => void get("template", () => downloadPayrollEmployeeTemplate(format))}
            >
              {busy === "template" ? (
                <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
              ) : (
                <Download className="size-3.5" aria-hidden />
              )}
              Blank template
            </button>
          </div>

          <div className="flex flex-wrap items-center gap-3 border-t border-border/60 pt-4">
            <label className="text-xs font-semibold text-foreground" htmlFor="bulkFile">
              Upload the edited file
            </label>
            <input
              id="bulkFile"
              type="file"
              accept=".csv,.xlsx"
              onChange={pick}
              className="min-w-[200px] flex-1 text-sm text-muted-foreground file:mr-3 file:rounded-full file:border-0 file:bg-muted file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-foreground"
            />

            <button
              type="button"
              className={BUTTON_SM}
              disabled={busy !== null || !file}
              onClick={() => void upload()}
            >
              {busy === "import" ? (
                <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
              ) : (
                <Upload className="size-3.5" aria-hidden />
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
              {/* Imported, but kept as typed — listed apart from the errors so
                  it does not read as a failed row. */}
              {result.warnings && result.warnings.length > 0 ? (
                <ul className="mt-2 max-h-48 space-y-1 overflow-y-auto text-xs text-amber-700 dark:text-amber-400">
                  {result.warnings.map((w) => (
                    <li key={`${w.row}-${w.message}`}>
                      Row {w.row}: {w.message}
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
