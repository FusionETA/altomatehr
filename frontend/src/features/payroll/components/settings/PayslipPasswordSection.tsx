import { useState } from "react";
import { LoaderCircle, LockKeyhole } from "lucide-react";
import { savePayslipPassword, type PayrollSettings } from "../../api";
import { BUTTON, CARD, ERROR_PANEL, WARN_PANEL } from "../../lib/ui";
import { CheckBox } from "../PayrollCheckbox";

// Payslip passwords: every payslip PDF that leaves the app — downloaded,
// zipped, emailed or fetched over the API — is locked with that employee's
// own payslip password. It starts random; the employee can change it on
// their Payslips page, and a payroll admin can see or reset it on a run's
// Payslips table.
//
// Saved on its own button, apart from the rest of the settings: it changes no
// figure, so it shouldn't ride along on (or wait for) a payroll settings save.
export function PayslipPasswordSection({
  settings,
  onSaved,
}: {
  settings: PayrollSettings;
  onSaved: (saved: PayrollSettings) => void;
}) {
  const [lock, setLock] = useState(settings.lockPayslips);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedNote, setSavedNote] = useState<string | null>(null);

  const dirty = lock !== settings.lockPayslips;

  async function save() {
    setSaving(true);
    setError(null);
    setSavedNote(null);
    try {
      const saved = await savePayslipPassword(lock);
      onSaved(saved);
      setSavedNote(saved.lockPayslips ? "Payslips are now password-protected." : "Payslip passwords are off.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the payslip password.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className={CARD}>
      <div className="flex items-start gap-3">
        <LockKeyhole aria-hidden className="mt-0.5 size-5 shrink-0 text-muted-foreground" />
        <div className="min-w-0">
          <h3 className="text-[15px] font-semibold text-foreground">Payslip password</h3>
          <p className="mt-1 text-sm text-muted-foreground">
            Lock every payslip PDF with a password — when it's downloaded, put in a ZIP, emailed or
            fetched by an integration. Only payslips; other payroll documents stay as they are.
          </p>
        </div>
      </div>

      <div className="mt-5 space-y-5">
        <label
          htmlFor="lockPayslips"
          className="group inline-flex min-h-11 w-full cursor-pointer items-center justify-between gap-4 rounded-2xl border border-border/70 bg-card px-4 py-2.5 text-sm shadow-sm transition hover:border-primary/40"
        >
          <span className="font-medium text-foreground">Password-protect payslips</span>
          <span className="inline-flex shrink-0 items-center gap-2">
            <CheckBox id="lockPayslips" checked={lock} onChange={setLock} />
            <span className="font-medium text-foreground">Yes</span>
          </span>
        </label>

        <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">
          <li>
            Each employee has one password that opens all their payslips. It starts as a random one,
            for example K7PM-X4QR.
          </li>
          <li>Employees see it, and can change it to their own, on their Payslips page.</li>
          <li>
            Payroll admins can see it, or reset it to a new random one, on a run's Payslips table.
          </li>
          <li>It is never put in an email.</li>
        </ul>

        {lock ? (
          <div className={WARN_PANEL}>
            Staff who don't sign in to AltomateHR can't see their password. A payroll admin can look it
            up on the run's Payslips table and pass it on.
          </div>
        ) : null}

        {error ? <p className={ERROR_PANEL}>{error}</p> : null}

        <div className="flex flex-wrap items-center gap-3">
          <button type="button" className={BUTTON} disabled={!dirty || saving} onClick={save}>
            {saving ? <LoaderCircle aria-hidden className="size-4 animate-spin" /> : null}
            Save payslip password
          </button>
          {savedNote && !dirty ? (
            <span className="text-sm text-muted-foreground">{savedNote}</span>
          ) : null}
        </div>
      </div>
    </section>
  );
}
