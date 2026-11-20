import { useState } from "react";
import { Eye, EyeOff, LoaderCircle, LockKeyhole } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  getPayrollEmployees,
  revealPayslipPassword,
  savePayslipPassword,
  type PayrollEmployee,
  type PayrollSettings,
  type PayslipPasswordRule,
} from "../../api";
import { BUTTON, BUTTON_GHOST_SM, CARD, ERROR_PANEL, HINT, INPUT, LABEL, WARN_PANEL } from "../../lib/ui";
import { CheckBox } from "../PayrollCheckbox";
import { PayrollSelect } from "../PayrollSelect";

// Payslip passwords: every payslip PDF that leaves the app — downloaded,
// zipped, emailed or fetched over the API — is locked with a password under
// the rule chosen here: one from the employee's own details, or one password
// the company sets.
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
    value: "COMPANY",
    label: "A password the company sets",
    example:
      "One password for every payslip. You tell your staff what it is — it is never put in an email.",
  },
];

const MIN_COMPANY_PASSWORD = 6;

// Who an employee-based rule has nothing for — their payslip can't be locked,
// so it's refused until the field is filled in. Passports count as an IC.
function missingFor(rule: PayslipPasswordRule, people: PayrollEmployee[]): PayrollEmployee[] {
  if (rule === "COMPANY") return [];
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
  // A NEW company password being typed. Blank keeps the saved one.
  const [password, setPassword] = useState("");
  const [showTyped, setShowTyped] = useState(false);
  // The saved one, after an explicit Reveal (audited on the server).
  const [revealed, setRevealed] = useState<string | null>(null);
  const [revealing, setRevealing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedNote, setSavedNote] = useState<string | null>(null);

  // The same cached roster the Employees tab reads.
  const employeesQuery = useCachedQuery("/payroll/employees", () => getPayrollEmployees());
  const people = (employeesQuery.data ?? []).filter((p) => p.hasPayrollProfile && !p.isArchived);
  const missing = missingFor(rule, people);

  const isCompany = rule === "COMPANY";
  const hasSaved = settings.hasPayslipCompanyPassword;
  const tooShort = password.length > 0 && password.length < MIN_COMPANY_PASSWORD;
  const needsPassword = lock && isCompany && !hasSaved && password.length === 0;

  const dirty =
    lock !== settings.lockPayslips ||
    rule !== settings.payslipPasswordRule ||
    (isCompany && password.length > 0);
  const field = rule === "DOB_DDMMYYYY" ? "date of birth" : "IC / passport number";

  async function save() {
    setSaving(true);
    setError(null);
    setSavedNote(null);
    try {
      const saved = await savePayslipPassword(lock, rule, isCompany ? password : undefined);
      onSaved(saved);
      setPassword("");
      setRevealed(null);
      setSavedNote(saved.lockPayslips ? "Payslips are now password-protected." : "Payslip passwords are off.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the payslip password.");
    } finally {
      setSaving(false);
    }
  }

  async function toggleReveal() {
    if (revealed !== null) {
      setRevealed(null);
      return;
    }
    setRevealing(true);
    setError(null);
    try {
      setRevealed((await revealPayslipPassword()).password);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not show the payslip password.");
    } finally {
      setRevealing(false);
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

        {isCompany ? (
          <div>
            <label className={LABEL} htmlFor="payslipCompanyPassword">
              {hasSaved ? "Change the company payslip password" : "Company payslip password"}
            </label>
            <div className="relative">
              <input
                id="payslipCompanyPassword"
                type={showTyped ? "text" : "password"}
                autoComplete="new-password"
                maxLength={64}
                className={`${INPUT} pr-12`}
                placeholder={hasSaved ? "Leave blank to keep the current password" : `At least ${MIN_COMPANY_PASSWORD} characters`}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
              <button
                type="button"
                onClick={() => setShowTyped((v) => !v)}
                aria-label={showTyped ? "Hide password" : "Show password"}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              >
                {showTyped ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
              </button>
            </div>
            {tooShort ? (
              <p className={`${HINT} text-destructive`}>
                Use at least {MIN_COMPANY_PASSWORD} characters.
              </p>
            ) : (
              <p className={HINT}>
                Stored encrypted. Changing it only affects payslips made afterwards — ones already sent
                keep the old password.
              </p>
            )}

            {hasSaved ? (
              <div className="mt-3 flex flex-wrap items-center gap-3 rounded-2xl border border-border/60 bg-muted/30 px-4 py-2.5 text-sm">
                <span className="text-muted-foreground">Current password:</span>
                <span className="font-mono font-semibold text-foreground">
                  {revealed ?? "••••••••"}
                </span>
                <button type="button" className={BUTTON_GHOST_SM} onClick={toggleReveal} disabled={revealing}>
                  {revealing ? <LoaderCircle aria-hidden className="size-3.5 animate-spin" /> : null}
                  {revealed !== null ? "Hide" : "Reveal"}
                </button>
              </div>
            ) : null}
          </div>
        ) : null}

        {lock && isCompany ? (
          <div className={WARN_PANEL}>
            Every employee opens their payslip with the same password, so anyone who has it can open a
            colleague's payslip if it's forwarded. The employee-based options don't have this problem.
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
          <button
            type="button"
            className={BUTTON}
            disabled={!dirty || saving || tooShort || needsPassword}
            onClick={save}
          >
            {saving ? <LoaderCircle aria-hidden className="size-4 animate-spin" /> : null}
            Save payslip password
          </button>
          {needsPassword ? (
            <span className="text-sm text-muted-foreground">Set a password to switch this on.</span>
          ) : savedNote && !dirty ? (
            <span className="text-sm text-muted-foreground">{savedNote}</span>
          ) : null}
        </div>
      </div>
    </section>
  );
}
