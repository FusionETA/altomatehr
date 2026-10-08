import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { AlertTriangle, CheckCircle2, ExternalLink, FileDown, LoaderCircle, PencilLine, Undo2, X } from "lucide-react";
import { ApiError, saveFile } from "@/shared/lib/api-client";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { getEmployees, STAFF_ROLES } from "@/features/employees/api";
import {
  generateLetter,
  getTemplates,
  resolveLetter,
  TEMPLATES_PATH,
  type DocumentTemplate,
  type ResolvedField,
  type ResolvedLetter,
} from "../api";
import { categoryLabel } from "../lib/categories";

const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";
const INPUT =
  "w-full rounded-xl border bg-card px-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";

// Per-field state in the dialog. `value` is what the admin typed: a fill for a
// gap, an input.* answer, or an override of what is on file (`override`).
type FieldState = {
  override: boolean;
  value: string;
  leaveBlank: boolean;
  saveToRecord: boolean;
};

const blankState = (): FieldState => ({ override: false, value: "", leaveBlank: false, saveToRecord: false });

// Generate one letter: pick the template and the employee, check every detail
// the letter will print, fill the gaps, download.
//
// The letter never goes out with a blank where a detail should be: each field
// with nothing on file is flagged and has to be filled in — or explicitly
// left blank — before Generate unlocks. Typed values are for this letter only,
// unless "Also save to employee record" is ticked on a gap the record can hold.
export function GenerateLetterDialog({
  template: presetTemplate,
  employeeUserId: presetEmployee,
  onClose,
  onGenerated,
  onOpenPayroll,
}: {
  template?: DocumentTemplate | null;
  employeeUserId?: string | null;
  onClose: () => void;
  onGenerated?: () => void;
  /** Opens Payroll, where company details and the signatory are set. */
  onOpenPayroll?: () => void;
}) {
  useBodyScrollLock();

  const templatesQuery = useCachedQuery(TEMPLATES_PATH, getTemplates, { enabled: !presetTemplate });
  const employeesQuery = useCachedQuery("/employees", getEmployees);
  const templates = presetTemplate ? [presetTemplate] : (templatesQuery.data ?? []);
  const employees = useMemo(
    () =>
      (employeesQuery.data ?? [])
        .filter((e) => (STAFF_ROLES as readonly string[]).includes(e.role) || e.id === presetEmployee)
        .sort((a, b) => a.name.localeCompare(b.name)),
    [employeesQuery.data, presetEmployee],
  );

  const [templateId, setTemplateId] = useState<string>(presetTemplate?.id ?? "");
  const [employeeUserId, setEmployeeUserId] = useState<string>(presetEmployee ?? "");
  const [resolved, setResolved] = useState<ResolvedLetter | null>(null);
  const [resolving, setResolving] = useState(false);
  const [state, setState] = useState<Record<string, FieldState>>({});
  const [keepOnFile, setKeepOnFile] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [serverMissing, setServerMissing] = useState<string[]>([]);
  const [done, setDone] = useState<string | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !generating) onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, generating]);

  // Re-read the record whenever the pair changes: what is missing depends on
  // both the template's fields and this person's record.
  useEffect(() => {
    if (!templateId || !employeeUserId) {
      setResolved(null);
      return;
    }
    let cancelled = false;
    setResolving(true);
    setError(null);
    setServerMissing([]);
    setDone(null);
    resolveLetter(templateId, employeeUserId)
      .then((r) => {
        if (cancelled) return;
        setResolved(r);
        setState(Object.fromEntries(r.fields.map((f) => [f.key, blankState()])));
      })
      .catch((e) => {
        if (!cancelled) {
          setResolved(null);
          setError(e instanceof Error ? e.message : "Could not read this employee's details.");
        }
      })
      .finally(() => {
        if (!cancelled) setResolving(false);
      });
    return () => {
      cancelled = true;
    };
  }, [templateId, employeeUserId]);

  const fields = resolved?.fields ?? [];
  const onFile = fields.filter((f) => !f.missing);
  const gaps = fields.filter((f) => f.missing && f.source !== "Input");
  const inputs = fields.filter((f) => f.source === "Input");

  const patch = (key: string, next: Partial<FieldState>) =>
    setState((s) => ({ ...s, [key]: { ...(s[key] ?? blankState()), ...next } }));

  // A field is settled when it will print something the admin has seen: the
  // record's value, a typed value, or an explicit blank.
  const unsettled = fields.filter((f) => {
    const s = state[f.key] ?? blankState();
    const typed = s.value.trim() !== "";
    if (!f.missing) return s.override && !typed;
    return !typed && !s.leaveBlank;
  });

  async function generate() {
    if (!resolved) return;
    setGenerating(true);
    setError(null);
    setServerMissing([]);
    try {
      const values: Record<string, string> = {};
      const leaveBlank: string[] = [];
      const saveToEmployee: string[] = [];
      for (const f of fields) {
        const s = state[f.key] ?? blankState();
        const typed = s.value.trim();
        if (f.missing) {
          if (typed) {
            values[f.key] = typed;
            if (f.writableToEmployee && s.saveToRecord) saveToEmployee.push(f.key);
          } else if (s.leaveBlank) {
            leaveBlank.push(f.key);
          }
        } else if (s.override && typed) {
          values[f.key] = typed;
        }
      }

      const file = await generateLetter(resolved.templateId, {
        employeeUserId: resolved.employeeUserId,
        values,
        leaveBlank,
        saveToEmployee,
        save: keepOnFile,
      });
      saveFile(file);
      setDone(
        keepOnFile
          ? `Downloaded, and kept on ${resolved.employeeName}'s file (admins only).`
          : "Downloaded. Not kept on file.",
      );
      onGenerated?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not generate the letter.");
      if (e instanceof ApiError && e.body && typeof e.body === "object" && "missingFields" in e.body) {
        const list = (e.body as { missingFields?: unknown }).missingFields;
        if (Array.isArray(list)) setServerMissing(list.filter((x): x is string => typeof x === "string"));
      }
    } finally {
      setGenerating(false);
    }
  }

  const selectedTemplate = templates.find((t) => t.id === templateId) ?? null;

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="generate-letter-title"
    >
      <section className="nice-scrollbar flex max-h-[92vh] w-full max-w-[680px] flex-col overflow-hidden rounded-[26px] border border-border/60 bg-card shadow-2xl">
        <header className="flex items-start justify-between gap-4 border-b border-border/60 px-6 py-5">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Generate letter
            </p>
            <h3 id="generate-letter-title" className="mt-1 truncate text-xl font-black text-foreground">
              {selectedTemplate?.name ?? "Choose a template"}
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={generating}
            aria-label="Close"
            className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="nice-scrollbar flex-1 space-y-5 overflow-y-auto px-6 py-5">
          <div className="grid gap-4 sm:grid-cols-2">
            {presetTemplate ? null : (
              <div className="sm:col-span-2">
                <span className={LABEL}>Template</span>
                <Select value={templateId} onValueChange={setTemplateId}>
                  <SelectTrigger className="mt-1.5 h-11 bg-card">
                    <SelectValue placeholder={templatesQuery.loading ? "Loading…" : "Pick a template"} />
                  </SelectTrigger>
                  <SelectContent>
                    {templates.map((t) => (
                      <SelectItem key={t.id} value={t.id}>
                        {t.name} · {categoryLabel(t.category)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {!templatesQuery.loading && templates.length === 0 ? (
                  <p className="mt-1.5 text-xs text-muted-foreground">
                    No templates yet — add some under Documents → Templates.
                  </p>
                ) : null}
              </div>
            )}
            {presetEmployee ? null : (
              <div className="sm:col-span-2">
                <span className={LABEL}>Employee</span>
                <Select value={employeeUserId} onValueChange={setEmployeeUserId}>
                  <SelectTrigger className="mt-1.5 h-11 bg-card">
                    <SelectValue placeholder={employeesQuery.loading ? "Loading…" : "Pick an employee"} />
                  </SelectTrigger>
                  <SelectContent>
                    {employees.map((e) => (
                      <SelectItem key={e.id} value={e.id}>
                        {e.name || e.email}
                        {e.employeeNumber ? ` · ${e.employeeNumber}` : ""}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}
          </div>

          {resolving ? (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" /> Reading the records…
            </div>
          ) : null}

          {resolved && !resolving ? (
            <>
              {gaps.length > 0 ? (
                <FieldGroup
                  title={`Missing details (${gaps.length})`}
                  hint="Nothing on file for these. Fill each one in for this letter, or choose to leave it blank."
                  tone="warning"
                >
                  {gaps.map((f) => (
                    <GapField
                      key={f.key}
                      field={f}
                      state={state[f.key] ?? blankState()}
                      flagged={serverMissing.includes(f.key)}
                      onChange={(next) => patch(f.key, next)}
                      onOpenPayroll={onOpenPayroll}
                    />
                  ))}
                </FieldGroup>
              ) : null}

              {inputs.length > 0 ? (
                <FieldGroup
                  title="For this letter"
                  hint="Details the template asks for each time — they aren't kept on any record."
                >
                  {inputs.map((f) => (
                    <GapField
                      key={f.key}
                      field={f}
                      state={state[f.key] ?? blankState()}
                      flagged={serverMissing.includes(f.key)}
                      onChange={(next) => patch(f.key, next)}
                    />
                  ))}
                </FieldGroup>
              ) : null}

              <FieldGroup
                title="From the records"
                hint="Filled in from the employee's and company's records. Change one for this letter only with Edit."
              >
                {onFile.map((f) => (
                  <OnFileField
                    key={f.key}
                    field={f}
                    state={state[f.key] ?? blankState()}
                    onChange={(next) => patch(f.key, next)}
                  />
                ))}
              </FieldGroup>
            </>
          ) : null}

          {error ? (
            <p className="rounded-2xl border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive">
              {error}
            </p>
          ) : null}
          {done ? (
            <p className="flex items-center gap-2 rounded-2xl border border-success/40 bg-success/10 px-4 py-3 text-sm font-medium text-success">
              <CheckCircle2 className="h-4 w-4 shrink-0" /> {done}
            </p>
          ) : null}
        </div>

        <footer className="space-y-3 border-t border-border/60 px-6 py-4">
          <label className="flex cursor-pointer items-start gap-2 text-sm text-foreground">
            <input
              type="checkbox"
              checked={keepOnFile}
              onChange={(e) => setKeepOnFile(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-border accent-primary"
            />
            <span>
              Save a copy to the employee's file
              <span className="block text-xs text-muted-foreground">
                Kept under Documents → Letters. Admins only — the employee doesn't see it.
              </span>
            </span>
          </label>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs text-muted-foreground">
              {!resolved
                ? "Pick a template and an employee."
                : unsettled.length === 0
                  ? "Every detail is filled in."
                  : `${unsettled.length} detail${unsettled.length === 1 ? "" : "s"} still needed: ${unsettled
                      .map((f) => f.label)
                      .join(", ")}.`}
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={onClose}
                disabled={generating}
                className="inline-flex h-10 items-center rounded-2xl border border-border bg-card px-4 text-sm font-semibold text-foreground transition hover:bg-muted/60"
              >
                {done ? "Close" : "Cancel"}
              </button>
              <button
                type="button"
                onClick={() => void generate()}
                disabled={!resolved || resolving || generating || unsettled.length > 0}
                className="inline-flex h-10 items-center gap-2 rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:pointer-events-none disabled:opacity-50"
              >
                {generating ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <FileDown className="h-4 w-4" />}
                {done ? "Generate again" : "Generate PDF"}
              </button>
            </div>
          </div>
        </footer>
      </section>
    </div>,
    document.body,
  );
}

function FieldGroup({
  title,
  hint,
  tone,
  children,
}: {
  title: string;
  hint: string;
  tone?: "warning";
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2">
      <div>
        <p className={`flex items-center gap-1.5 text-sm font-black ${tone === "warning" ? "text-warning-foreground" : "text-foreground"}`}>
          {tone === "warning" ? <AlertTriangle className="h-4 w-4" /> : null}
          {title}
        </p>
        <p className="text-xs text-muted-foreground">{hint}</p>
      </div>
      <div className="space-y-2">{children}</div>
    </div>
  );
}

// The input that suits the field: a date picker, a number, a block of lines.
function ValueInput({
  field,
  value,
  onChange,
  disabled,
  autoFocus,
}: {
  field: ResolvedField;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  autoFocus?: boolean;
}) {
  if (field.kind === "Multiline") {
    return (
      <textarea
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        autoFocus={autoFocus}
        rows={3}
        placeholder={field.label}
        className={`${INPUT} border-border py-2`}
      />
    );
  }
  const type =
    field.kind === "Date" ? "date" : field.kind === "Number" || field.kind === "Money" ? "number" : "text";
  return (
    <input
      type={type}
      step={field.kind === "Money" ? "0.01" : field.kind === "Number" ? "1" : undefined}
      min={field.kind === "Number" || field.kind === "Money" ? 0 : undefined}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      disabled={disabled}
      autoFocus={autoFocus}
      placeholder={field.kind === "Money" ? "e.g. 4500.00" : field.label}
      className={`${INPUT} h-10 border-border`}
    />
  );
}

// A field with nothing on file, or a typed-per-letter one.
function GapField({
  field,
  state,
  flagged,
  onChange,
  onOpenPayroll,
}: {
  field: ResolvedField;
  state: FieldState;
  flagged: boolean;
  onChange: (next: Partial<FieldState>) => void;
  onOpenPayroll?: () => void;
}) {
  const typed = state.value.trim() !== "";
  const settled = typed || state.leaveBlank;
  const isInput = field.source === "Input";
  const isCompanyLevel = field.source === "Company" || field.source === "Signatory";

  return (
    <div
      className={`rounded-2xl border p-3 ${
        settled
          ? "border-border/60 bg-card"
          : flagged
            ? "border-destructive/60 bg-destructive/5"
            : isInput
              ? "border-border bg-surface-low/40"
              : "border-warning-foreground/40 bg-warning/20"
      }`}
    >
      <div className="mb-1.5 flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-semibold text-foreground">
          {field.label}
          {!isInput ? (
            <span className="ml-2 rounded-full bg-warning/50 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-warning-foreground">
              Not on file
            </span>
          ) : null}
        </span>
        <code className="text-[11px] text-muted-foreground">{`{{${field.key}}}`}</code>
      </div>

      <ValueInput
        field={field}
        value={state.value}
        disabled={state.leaveBlank}
        onChange={(value) => onChange({ value, leaveBlank: value.trim() ? false : state.leaveBlank })}
      />

      <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1.5">
        {field.writableToEmployee ? (
          <label className="flex cursor-pointer items-center gap-1.5 text-xs text-foreground">
            <input
              type="checkbox"
              checked={state.saveToRecord}
              disabled={!typed}
              onChange={(e) => onChange({ saveToRecord: e.target.checked })}
              className="h-3.5 w-3.5 rounded border-border accent-primary"
            />
            Also save to employee record
          </label>
        ) : null}
        <label className="flex cursor-pointer items-center gap-1.5 text-xs text-muted-foreground">
          <input
            type="checkbox"
            checked={state.leaveBlank}
            onChange={(e) => onChange({ leaveBlank: e.target.checked, value: e.target.checked ? "" : state.value })}
            className="h-3.5 w-3.5 rounded border-border accent-primary"
          />
          Leave blank on this letter
        </label>
      </div>

      {field.fixHint ? (
        <p className="mt-1.5 flex flex-wrap items-center gap-x-2 text-xs text-muted-foreground">
          {field.fixHint}
          {isCompanyLevel && onOpenPayroll ? (
            <button
              type="button"
              onClick={onOpenPayroll}
              className="inline-flex items-center gap-1 font-semibold text-primary hover:underline"
            >
              Open Payroll <ExternalLink className="h-3 w-3" />
            </button>
          ) : null}
        </p>
      ) : null}
    </div>
  );
}

// A field filled from the records: shown read-only, editable for this letter.
function OnFileField({
  field,
  state,
  onChange,
}: {
  field: ResolvedField;
  state: FieldState;
  onChange: (next: Partial<FieldState>) => void;
}) {
  return (
    <div className="rounded-2xl border border-border/60 bg-card px-3 py-2.5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-xs font-semibold text-muted-foreground">{field.label}</p>
          {state.override ? (
            <div className="mt-1.5">
              <ValueInput
                field={field}
                value={state.value}
                autoFocus
                onChange={(value) => onChange({ value })}
              />
              <p className="mt-1 text-[11px] text-muted-foreground">
                For this letter only. On file: {field.value}
              </p>
            </div>
          ) : (
            <p className="whitespace-pre-line text-sm text-foreground">{field.value}</p>
          )}
        </div>
        {state.override ? (
          <button
            type="button"
            onClick={() => onChange({ override: false, value: "" })}
            className="inline-flex shrink-0 items-center gap-1 rounded-full border border-border/60 px-2.5 py-1 text-xs font-semibold text-muted-foreground transition hover:text-foreground"
          >
            <Undo2 className="h-3 w-3" /> Use record
          </button>
        ) : (
          <button
            type="button"
            onClick={() => onChange({ override: true, value: field.editValue ?? field.value ?? "" })}
            className="inline-flex shrink-0 items-center gap-1 rounded-full border border-border/60 px-2.5 py-1 text-xs font-semibold text-muted-foreground transition hover:text-foreground"
          >
            <PencilLine className="h-3 w-3" /> Edit for this letter
          </button>
        )}
      </div>
    </div>
  );
}
