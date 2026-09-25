import { useState } from "react";
import { CircleAlert, CircleCheck, Copy, Download, LoaderCircle, Upload } from "lucide-react";
import {
  downloadEmployeeExport,
  downloadEmployeeImportTemplate,
  importEmployees,
  type EmployeeImportBlankCells,
  type EmployeeImportResult,
} from "@/features/employees/api";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { saveFile } from "@/shared/lib/api-client";

// The one employee spreadsheet: add new people, or export everyone, edit, and
// import back to update them in bulk.
//
// What a blank cell means is the admin's call, made here rather than in the
// file: keep the existing value (the default — a partial sheet can't wipe
// anything) or erase it. Erase asks once more before anything is saved.
//
// The passwords for accounts this creates come back ONCE, in the response, and
// are stored nowhere. So the dialog will not let them go by accident — the
// result stays on screen until dismissed, with a copy-all button, and says
// plainly that closing loses them.
const CARD = "rounded-2xl border border-border/60 bg-card p-4";
const LABEL = "text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground";

export function ImportEmployeesDialog({
  onClose,
  onImported,
}: {
  onClose: () => void;
  onImported: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [downloading, setDownloading] = useState<"template" | "current" | null>(null);
  const [importing, setImporting] = useState(false);
  const [result, setResult] = useState<EmployeeImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [blankCells, setBlankCells] = useState<EmployeeImportBlankCells>("Keep");
  const [confirm, confirmDialog] = useConfirm();

  // Two starting points, because they answer different needs: a blank template
  // for onboarding new hires, the current roster for filling a field in for
  // people who are already here. Both re-import through the same columns.
  async function download(which: "template" | "current") {
    setDownloading(which);
    setError(null);
    try {
      saveFile(
        which === "template"
          ? await downloadEmployeeImportTemplate()
          : await downloadEmployeeExport(),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not build that file.");
    } finally {
      setDownloading(null);
    }
  }

  async function upload() {
    if (!file) return;
    if (
      blankCells === "Erase" &&
      !(await confirm({
        title: "Erase data in blank cells?",
        message:
          "Every blank cell in this file will erase that field for the person on that row. " +
          "Columns you deleted from the file, and people not in it, are not affected.",
        confirmLabel: "Erase and import",
        destructive: true,
      }))
    )
      return;
    setImporting(true);
    setError(null);
    setResult(null);
    try {
      const imported = await importEmployees(file, blankCells);
      setResult(imported);
      // Refresh the list even on a partial import: the rows that landed are
      // real people, and leaving them off the table until a reload is worse
      // than showing them beside the errors.
      onImported();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not import that file.");
    } finally {
      setImporting(false);
    }
  }

  function copyAll() {
    if (!result?.createdAccounts.length) return;
    const text = result.createdAccounts
      .map((a) => `${a.name}\t${a.email}\t${a.password}`)
      .join("\n");
    void navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    });
  }

  const accounts = result?.createdAccounts ?? [];

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm">
      <div className="flex max-h-[90vh] w-full max-w-[640px] flex-col overflow-hidden rounded-[26px] border border-border bg-background shadow-2xl">
        <header className="border-b border-border/70 px-6 py-4">
          <h2 className="text-base font-semibold text-foreground">Import employees</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Add new people, or update everyone's details in bulk — profile, payroll, bank and
            statutory fields included. An email already in this company updates that person.
          </p>
        </header>

        <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
          <section className="space-y-2">
            <h3 className={LABEL}>Step 1 — start from a file</h3>
            <div className="flex flex-wrap gap-2">
              <button
                type="button"
                onClick={() => void download("template")}
                disabled={downloading !== null}
                className="inline-flex h-10 items-center gap-2 rounded-2xl border border-border/70 bg-card px-4 text-sm font-bold text-foreground hover:bg-muted disabled:opacity-60"
              >
                {downloading === "template" ? (
                  <LoaderCircle className="size-4 animate-spin" aria-hidden />
                ) : (
                  <Download className="size-4" aria-hidden />
                )}
                Blank template
              </button>

              <button
                type="button"
                onClick={() => void download("current")}
                disabled={downloading !== null}
                className="inline-flex h-10 items-center gap-2 rounded-2xl border border-border/70 bg-card px-4 text-sm font-bold text-foreground hover:bg-muted disabled:opacity-60"
              >
                {downloading === "current" ? (
                  <LoaderCircle className="size-4 animate-spin" aria-hidden />
                ) : (
                  <Download className="size-4" aria-hidden />
                )}
                Export current
              </button>
            </div>
            <p className="text-xs text-muted-foreground">
              <span className="font-semibold text-foreground">Blank template</span> for new hires;{" "}
              <span className="font-semibold text-foreground">Export current</span> to update people
              already here — edit the cells you want and import the same file. The file's{" "}
              <span className="font-semibold text-foreground">READ ME</span> sheet explains every
              column.
            </p>
          </section>

          <section className="space-y-2">
            <h3 className={LABEL}>Step 2 — what should a blank cell do?</h3>
            <div role="radiogroup" aria-label="Blank cells" className="grid gap-2 sm:grid-cols-2">
              <BlankCellsOption
                value="Keep"
                selected={blankCells}
                onSelect={setBlankCells}
                title="Keep existing values"
                description="Blank leaves that field as it is. Safe for a file with only some details filled in."
              />
              <BlankCellsOption
                value="Erase"
                selected={blankCells}
                onSelect={setBlankCells}
                title="Erase existing values"
                description="Blank clears that field. Use when the file is the full, up-to-date record."
              />
            </div>
            {blankCells === "Erase" ? (
              <p className="flex items-start gap-1.5 rounded-2xl border border-destructive/25 bg-destructive/5 px-3 py-2 text-xs text-destructive">
                <CircleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />
                Every blank cell erases data for that person. Name, Role, Employee No, Salary Type
                and EPF/EIS can't be erased — a blank there fails the row. Columns you deleted from
                the file are left alone.
              </p>
            ) : null}
          </section>

          <section className="space-y-2">
            <h3 className={LABEL}>Step 3 — upload it back</h3>
            <input
              type="file"
              accept=".xlsx,.csv"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                setResult(null);
                setError(null);
              }}
              className="block w-full text-sm text-muted-foreground file:mr-3 file:rounded-xl file:border file:border-border/70 file:bg-card file:px-3 file:py-2 file:text-sm file:font-bold file:text-foreground hover:file:bg-muted"
            />
          </section>

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          {result ? (
            <div className="space-y-3">
              <p className="flex items-center gap-1.5 text-sm font-bold text-foreground">
                <CircleCheck className="size-4 text-success" aria-hidden />
                {result.created} created, {result.updated} updated.
              </p>

              {result.errors.length > 0 ? (
                <ul className="max-h-40 space-y-1 overflow-y-auto rounded-2xl border border-destructive/20 bg-destructive/5 p-3 text-xs">
                  {result.errors.map((row) => (
                    <li key={`${row.row}-${row.message}`} className="text-destructive">
                      <span className="font-bold">Row {row.row}:</span> {row.message}
                    </li>
                  ))}
                </ul>
              ) : null}

              {accounts.length > 0 ? (
                <div className={CARD}>
                  <div className="mb-2 flex items-start justify-between gap-3">
                    <p className="flex items-start gap-1.5 text-xs font-bold text-foreground">
                      <CircleAlert className="mt-0.5 size-3.5 shrink-0 text-warning-foreground" aria-hidden />
                      {/* The one thing in this dialog that cannot be recovered. */}
                      Copy these now — the passwords are shown once and are not stored anywhere.
                    </p>
                    <button
                      type="button"
                      onClick={copyAll}
                      className="inline-flex shrink-0 items-center gap-1.5 rounded-xl border border-border/70 bg-card px-2.5 py-1.5 text-xs font-bold text-foreground hover:bg-muted"
                    >
                      <Copy className="size-3.5" aria-hidden />
                      {copied ? "Copied" : "Copy all"}
                    </button>
                  </div>

                  <div className="max-h-48 overflow-y-auto">
                    <table className="w-full text-xs">
                      <tbody>
                        {accounts.map((a) => (
                          <tr key={a.email} className="border-b border-border/60 last:border-0">
                            <td className="py-1.5 pr-3 font-semibold text-foreground">{a.name}</td>
                            <td className="py-1.5 pr-3 text-muted-foreground">{a.email}</td>
                            <td className="py-1.5 font-mono tabular-nums text-foreground">
                              {a.password}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}
            </div>
          ) : null}
        </div>

        <footer className="flex justify-end gap-2 border-t border-border/70 px-6 py-4">
          <button
            type="button"
            onClick={onClose}
            disabled={importing}
            className="rounded-2xl border border-border/70 bg-card px-4 py-2 text-sm font-bold text-foreground hover:bg-muted disabled:opacity-60"
          >
            {result ? "Done" : "Cancel"}
          </button>
          <button
            type="button"
            onClick={() => void upload()}
            disabled={!file || importing}
            className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-bold text-primary-foreground disabled:opacity-60"
          >
            {importing ? (
              <LoaderCircle className="size-4 animate-spin" aria-hidden />
            ) : (
              <Upload className="size-4" aria-hidden />
            )}
            {importing ? "Importing…" : "Import"}
          </button>
        </footer>
      </div>
      {confirmDialog}
    </div>
  );
}

function BlankCellsOption({
  value,
  selected,
  onSelect,
  title,
  description,
}: {
  value: EmployeeImportBlankCells;
  selected: EmployeeImportBlankCells;
  onSelect: (value: EmployeeImportBlankCells) => void;
  title: string;
  description: string;
}) {
  const checked = value === selected;
  return (
    <button
      type="button"
      role="radio"
      aria-checked={checked}
      onClick={() => onSelect(value)}
      className={`rounded-2xl border px-3 py-2.5 text-left transition ${
        checked
          ? value === "Erase"
            ? "border-destructive/50 bg-destructive/5 ring-1 ring-destructive/30"
            : "border-primary/50 bg-primary/5 ring-1 ring-primary/30"
          : "border-border/70 bg-card hover:bg-muted"
      }`}
    >
      <span className="flex items-center gap-2 text-sm font-bold text-foreground">
        <span
          aria-hidden
          className={`flex size-4 shrink-0 items-center justify-center rounded-full border ${
            checked ? (value === "Erase" ? "border-destructive" : "border-primary") : "border-border"
          }`}
        >
          {checked ? (
            <span
              className={`size-2 rounded-full ${value === "Erase" ? "bg-destructive" : "bg-primary"}`}
            />
          ) : null}
        </span>
        {title}
      </span>
      <span className="mt-1 block text-xs text-muted-foreground">{description}</span>
    </button>
  );
}
