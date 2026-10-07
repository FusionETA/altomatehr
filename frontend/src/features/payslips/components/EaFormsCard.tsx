import { useState } from "react";
import { Download, LoaderCircle } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { saveFile } from "@/shared/lib/api-client";
import { downloadMyEaForm, getMyEaForms, type EaFormYear } from "../api";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// The employee's own Form EA, one row per year they were paid in.
//
// A year that is not ready yet still gets a row saying so: the form is due by
// 28 February, and an employee looking for it then should learn it is coming
// rather than conclude it is missing.
export function EaFormsCard() {
  const [downloading, setDownloading] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const formsQuery = useCachedQuery("/payslips/ea-forms", getMyEaForms);
  const forms = formsQuery.data ?? [];

  async function download(year: number) {
    setDownloading(year);
    setError(null);
    try {
      saveFile(await downloadMyEaForm(year));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not download that EA form.");
    } finally {
      setDownloading(null);
    }
  }

  // Quiet while loading or on error: the payslips below are what this screen
  // is for, and this card is an extra.
  if (forms.length === 0) return null;

  return (
    <section className={`${CARD} mb-4 p-5`}>
      <p className="text-sm font-bold text-foreground">Form EA</p>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Your yearly statement of pay, for filing your income tax return.
      </p>

      {error ? <p className="mt-3 text-xs font-medium text-destructive">{error}</p> : null}

      <ul className="mt-3 divide-y divide-border/50 border-t border-border/50">
        {forms.map((form) => (
          <li key={form.year} className="flex flex-wrap items-center justify-between gap-3 py-3">
            <div className="min-w-0">
              <p className="text-sm font-semibold tabular-nums text-foreground">{form.year}</p>
              {form.available ? null : (
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {notReadyText(form)}
                </p>
              )}
            </div>
            {form.available ? (
              <button
                type="button"
                onClick={() => void download(form.year)}
                disabled={downloading === form.year}
                className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
              >
                {downloading === form.year ? (
                  <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                ) : (
                  <Download className="h-3.5 w-3.5" />
                )}
                Download EA
              </button>
            ) : null}
          </li>
        ))}
      </ul>
    </section>
  );
}

// Someone who left during the year only waits for payroll up to their leaving
// month, so the line names that month rather than "all 12".
function notReadyText(form: EaFormYear) {
  const progress = `${form.approvedMonths} of ${form.requiredMonths} so far`;
  if (form.requiredMonths === 12) {
    return `Ready once all 12 months of ${form.year} payroll are approved (${progress}).`;
  }
  const month = new Date(form.year, form.requiredMonths - 1, 1).toLocaleString("en-GB", {
    month: "long",
  });
  return `Ready once ${form.year} payroll is approved up to ${month} (${progress}).`;
}
