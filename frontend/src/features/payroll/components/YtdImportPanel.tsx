import { useState, type ChangeEvent } from "react";
import { Download, LoaderCircle, Upload } from "lucide-react";
import {
  downloadYtdTemplate,
  previewYtdImport,
  runYtdImport,
  type TabularFormat,
  type YtdImportPreview,
  type YtdImportResult,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { PayrollSelect } from "./PayrollSelect";
import { monthName, rm } from "../lib/payroll-format";
import {
  BUTTON,
  BUTTON_GHOST,
  CARD,
  HINT,
  LABEL,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
  WARN_PANEL,
} from "../lib/ui";

// Seeding a part-year of history when an org moves onto this system.
//
// Two steps on purpose. Writing a year of payslips off an uploaded file
// without showing the admin the match list first is how one person's history
// ends up on someone else's record — and these figures then feed everyone's
// PCB for the rest of the year, so a wrong match is not a cosmetic problem.
export function YtdImportPanel({ year, onImported }: { year: number; onImported: () => void }) {
  const [format, setFormat] = useState<TabularFormat>("Xlsx");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<YtdImportPreview | null>(null);
  const [result, setResult] = useState<YtdImportResult | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function reset(next: File | null) {
    setFile(next);
    setPreview(null);
    setResult(null);
    setError(null);
  }

  async function act(key: string, action: () => Promise<void>) {
    setBusy(key);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err instanceof Error ? err.message : "That did not work.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <section className={`${CARD} space-y-5`}>
      <header>
        <h2 className="text-base font-semibold text-foreground">
          Import earlier {year} payroll
        </h2>
        <p className={HINT}>
          For an org that moved onto this system part-way through the year. The imported months
          are taken exactly as typed — never recalculated — and they count towards everyone's
          year-to-date, so the rest of {year}'s PCB is worked out on the full year.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3">
        <div className="w-32">
          <label className={LABEL} htmlFor="ytdFormat">
            Format
          </label>
          <PayrollSelect
            id="ytdFormat"
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
          onClick={() =>
            void act("template", async () => saveFile(await downloadYtdTemplate(year, format)))
          }
        >
          {busy === "template" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Download className="size-4" aria-hidden />
          )}
          Template for {year}
        </button>
      </div>

      <div className="flex flex-wrap items-end gap-3 border-t border-border/60 pt-5">
        <div className="min-w-[260px] flex-1">
          <label className={LABEL} htmlFor="ytdFile">
            Completed sheet
          </label>
          <input
            id="ytdFile"
            type="file"
            accept=".csv,.xlsx"
            onChange={(e: ChangeEvent<HTMLInputElement>) => reset(e.target.files?.[0] ?? null)}
            className="mt-1 block w-full text-sm text-muted-foreground file:mr-3 file:rounded-full file:border-0 file:bg-primary file:px-4 file:py-2 file:text-xs file:font-semibold file:text-primary-foreground"
          />
        </div>

        <button
          type="button"
          className={BUTTON_GHOST}
          disabled={busy !== null || !file}
          onClick={() =>
            void act("preview", async () => setPreview(await previewYtdImport(year, file!)))
          }
        >
          {busy === "preview" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : null}
          Check the file
        </button>

        <button
          type="button"
          className={BUTTON}
          // Importing without looking at the match list first is exactly what
          // the two-step exists to prevent, so this stays shut until a clean
          // preview has been seen.
          disabled={busy !== null || !file || !preview?.ok}
          title={preview?.ok ? undefined : "Check the file first"}
          onClick={() =>
            void act("import", async () => {
              const next = await runYtdImport(year, file!);
              setResult(next);
              if (next.ok) onImported();
            })
          }
        >
          {busy === "import" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Upload className="size-4" aria-hidden />
          )}
          Import {year} history
        </button>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {preview && !result ? <Preview preview={preview} /> : null}
      {result ? <Result result={result} /> : null}
    </section>
  );
}

function Preview({ preview }: { preview: YtdImportPreview }) {
  return (
    <div className="space-y-3">
      {preview.errors.length > 0 ? (
        <div className="rounded-2xl border border-destructive/20 bg-destructive/5 p-4 text-sm text-destructive">
          <p className="font-semibold">This file cannot be imported yet</p>
          <ul className="mt-2 space-y-1">
            {preview.errors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {/* Not errors — the import can go ahead — but a replaced month or an
          ignored column is worth seeing before it happens. */}
      {preview.warnings.length > 0 ? (
        <div className={WARN_PANEL}>
          <ul className="space-y-1">
            {preview.warnings.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {/* Surfaced by name so the admin can fix a spelling, rather than
          wondering afterwards who was dropped. */}
      {preview.unmatchedNames.length > 0 ? (
        <div className={WARN_PANEL}>
          <p className="font-semibold">
            {preview.unmatchedNames.length} name(s) in the sheet matched nobody
          </p>
          <p className="mt-1">
            They will be skipped: <strong>{preview.unmatchedNames.join(", ")}</strong>.
          </p>
        </div>
      ) : null}

      {preview.employees.length > 0 ? (
        <div className="overflow-x-auto rounded-2xl border border-border/70">
          <table className="w-full min-w-[560px] border-collapse">
            <thead>
              <tr className="border-b border-border/70 bg-muted/40">
                <th className={TH}>Employee</th>
                <th className={TH}>Months</th>
                <th className={TH_NUM}>Gross</th>
                <th className={TH_NUM}>PCB</th>
              </tr>
            </thead>
            <tbody>
              {preview.employees.map((row) => (
                <tr key={row.employeeProfileId} className="border-b border-border/40 last:border-0">
                  <td className={`${TD} font-medium`}>{row.employeeName}</td>
                  <td className={`${TD} text-muted-foreground`}>
                    {row.months.map((m) => monthName(m).slice(0, 3)).join(", ")}
                  </td>
                  <td className={TD_NUM}>{rm(row.totalGross)}</td>
                  <td className={TD_NUM}>{rm(row.totalPcb)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : preview.errors.length === 0 ? (
        <p className={NOTE_PANEL}>Nothing in this file matched an employee.</p>
      ) : null}
    </div>
  );
}

function Result({ result }: { result: YtdImportResult }) {
  if (!result.ok) {
    return (
      <div className="rounded-2xl border border-destructive/20 bg-destructive/5 p-4 text-sm text-destructive">
        <p className="font-semibold">Nothing was imported</p>
        <ul className="mt-2 space-y-1">
          {result.errors.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      </div>
    );
  }

  return (
    <div className="rounded-2xl border border-border/70 bg-muted/40 p-4 text-sm">
      <p className="font-semibold text-foreground">
        {result.payslipsImported} payslip(s) across {result.monthsImported} month(s) imported.
      </p>

      {/* A month this system already computed is left alone rather than
          overwritten — the computed figures are the ones that were filed. */}
      {result.skippedMonths.length > 0 ? (
        <p className="mt-1 text-muted-foreground">
          Left alone because this system already has them:{" "}
          {result.skippedMonths.join(", ")}.
        </p>
      ) : null}

      {result.unmatchedNames.length > 0 ? (
        <p className="mt-1 text-muted-foreground">
          Skipped, no matching employee: {result.unmatchedNames.join(", ")}.
        </p>
      ) : null}
    </div>
  );
}
