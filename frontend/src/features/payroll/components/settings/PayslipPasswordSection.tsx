import { useState } from "react";
import { LoaderCircle, LockKeyhole } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  getPayrollEmployees,
  savePayslipPassword,
  type PayrollEmployee,
  type PayrollSettings,
  type PayslipPasswordRule,
} from "../../api";
import { BUTTON, CARD, ERROR_PANEL, HINT, LABEL, WARN_PANEL } from "../../lib/ui";
import { CheckBox } from "../PayrollCheckbox";
import { PayrollSelect } from "../PayrollSelect";

// Payslip passwords: every payslip PDF that leaves the app — downloaded,
// zipped, emailed or fetched over the API — is locked with a password under
// the rule chosen here: one from the employee's own details, or a random one
// made for each payslip.
//
// Saved on its own button, apart from the rest of the settings: it changes no
// figure, so it shouldn't ride along on (or wait for) a payroll settings save.
const RULES: { value: PayslipPasswordRule; label: string; example: string }[] = [
  {
    value: "IC_DIGITS",
    label: "Employee's IC number, digits only",
    example: "For example 900101-14-5567 → 900101145567. Each employee has their own.",
  },
  {
    value: "IC_LAST6",
    label: "Last 6 digits of employee's IC",
    example: "For example 900101-14-5567 → 145567. Each employee has their own.",
  },
  {
    value: "DOB_DDMMYYYY",
    label: "Employee's date of birth (DDMMYYYY)",
    example: "For example 15 June 1990 → 15061990. Each employee has their own.",
  },
  {
    value: "RANDOM",
    label: "A random password for each payslip",
    example:
      "For example K7PM-X4QR — a new one for every payslip. Employees see it next to the payslip on their Payslips page; payroll admins can see it on the run's Payslips table. It is never put in an email.",
  },
];

// Who an employee-detail rule has nothing for — their payslip can't be
// locked, so it's refused until the field is filled in. Passports count as
// an IC. A random password needs nothing from them.
function missingFor(rule: PayslipPasswordRule, people: PayrollEmployee[]): PayrollEmployee[] {
  if (rule === "RANDOM") return [];
  return people.filter((person) =>
    rule === "DOB_DDMMYYYY"
      ? !person.dateOfBirth
      : !(person.idNumber ?? "").replace(/[^a-z0-9]/gi, ""),
  );
}

export function PayslipPasswordSection({
  settings,
  onSaved,
}: {
  settings: PayrollSettings;
  onSaved: (saved: PayrollSettings) => void;
}) {
  const [lock, setLock] = useState(settings.lockPayslips);
  const [rule, setRule] = useState<PayslipPasswordRule>(settings.payslipPasswordRule);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedNote, setSavedNote] = useState<string | null>(null);

  // The same cached roster the Employees tab reads.
  const employeesQuery = useCachedQuery("/payroll/employees", () => getPayrollEmployees());
  const people = (employeesQuery.data ?? []).filter((p) => p.hasPayrollProfile && !p.isArchived);
  const missing = missingFor(rule, people);

  const dirty = lock !== settings.lockPayslips || rule !== settings.payslipPasswordRule;
  const field = rule === "DOB_DDMMYYYY" ? "date of birth" : "IC / passport number";

  async function save() {
    setSaving(true);
    setError(null);
    setSavedNote(null);
    try {
      const saved = await savePayslipPassword(lock, rule);
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

        <div>
          <label className={LABEL} htmlFor="payslipPasswordRule">
            Password
          </label>
          <PayrollSelect
            id="payslipPasswordRule"
            value={rule}
            onChange={(next) => setRule(next as PayslipPasswordRule)}
            options={RULES.map((r) => ({ value: r.value, label: r.label }))}
          />
          <p className={HINT}>{RULES.find((r) => r.value === rule)?.example}</p>
        </div>

        {lock && rule === "RANDOM" ? (
          <div className={WARN_PANEL}>
            Staff who don't sign in to AltomateHR can't see their password. A payroll admin can look it
            up on the run's Payslips table and pass it on.
          </div>
        ) : null}

        {lock && missing.length > 0 ? (
          <div className={WARN_PANEL}>
            <p className="font-semibold">
              {missing.length === 1
                ? "1 person has no " + field
                : `${missing.length} people have no ${field}`}
            </p>
            <p className="mt-1">
              Their payslips can't be locked, so they won't download or email until it's added:{" "}
              {missing.map((p) => p.name).join(", ")}.
            </p>
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
