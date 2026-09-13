import { useEffect, useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  LoaderCircle,
  Plus,
  Search,
  TriangleAlert,
} from "lucide-react";
import {
  createPayrollRun,
  getPayrollRunPicker,
  type PayrollPickerMember,
  type PayrollPickerPolicy,
  type PayrollRun,
} from "../api";
import { CheckBox } from "./PayrollCheckbox";
import { ModalPortal } from "./ModalPortal";
import { PayrollSelect } from "./PayrollSelect";
import { YtdImportPanel } from "./YtdImportPanel";
import {
  MONTHS,
  rm,
  shortDate,
  sourceLabels,
  statusLabels,
  statusTone,
} from "../lib/payroll-format";
import {
  BADGE,
  BUTTON,
  BUTTON_GHOST,
  CARD,
  ERROR_PANEL,
  HINT,
  INPUT,
  LABEL,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
} from "../lib/ui";

// Every payroll run, newest period first, and the picker that starts a new one.
//
// The table leads with the period and the status because that is what an
// admin scans for — "is March done?" — and carries the three figures that
// get reconciled: gross, net, and what the month actually costs the employer.
export function PayrollRunsList({
  runs,
  loading,
  error,
  onOpen,
  onCreated,
}: {
  runs: PayrollRun[];
  loading: boolean;
  error: string | null;
  onOpen: (runId: string) => void;
  onCreated: () => void;
}) {
  const [pickerOpen, setPickerOpen] = useState(false);

  return (
    <div className="space-y-6">
      <section className={CARD}>
        <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-foreground">Start a payroll run</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              One run per month. Pick the period and which policies and employees to include —
              nothing is calculated until you generate the draft.
            </p>
          </div>
          <button type="button" className={BUTTON} onClick={() => setPickerOpen(true)}>
            <Plus className="size-4" aria-hidden />
            Create draft
          </button>
        </header>
      </section>

      {/* Importing prior-year history is part of STARTING payroll (a mid-year
          migration seeds the months before this system took over), so it lives
          beside "Start a payroll run" — not on the annual-forms tab. */}
      <YtdImportPanel year={new Date().getFullYear()} onImported={onCreated} />

      {error ? (
        <section className={ERROR_PANEL}>Error: {error}</section>
      ) : loading ? (
        <section className={NOTE_PANEL}>Loading payroll runs…</section>
      ) : runs.length === 0 ? (
        <section className={NOTE_PANEL}>
          No payroll runs yet. Create one above to get started.
        </section>
      ) : (
        <section className={`${CARD} overflow-x-auto p-0 sm:p-0`}>
          <table className="w-full min-w-[860px] border-collapse">
            <thead>
              <tr className="border-b border-border/70">
                <th className={TH}>Period</th>
                <th className={TH}>Status</th>
                <th className={TH_NUM}>Staff</th>
                <th className={TH_NUM}>Gross</th>
                <th className={TH_NUM}>Net pay</th>
                <th className={TH_NUM}>Cost to employer</th>
                <th className={TH}>Generated</th>
                <th className={TH} aria-label="Open" />
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <tr
                  key={run.id}
                  className="cursor-pointer border-b border-border/40 transition last:border-0 hover:bg-muted/40"
                  onClick={() => onOpen(run.id)}
                >
                  <td className={`${TD} font-medium`}>
                    <div className="flex items-center gap-2">
                      {run.periodLabel}
                      {run.source === "IMPORTED" ? (
                        <span className={`${BADGE} border-border bg-muted/60 text-muted-foreground`}>
                          {sourceLabels.IMPORTED}
                        </span>
                      ) : null}
                    </div>
                  </td>

                  <td className={TD}>
                    <div className="flex items-center gap-2">
                      <span className={`${BADGE} ${statusTone[run.status]}`}>
                        {statusLabels[run.status]}
                      </span>
                      {/* Stale means the figures on screen are behind the
                          inputs that produced them — worth seeing from the
                          list, not just after opening the run. */}
                      {run.isStale ? (
                        <span
                          className={`${BADGE} border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400`}
                          title="Inputs changed since these payslips were generated"
                        >
                          <TriangleAlert className="size-3" aria-hidden />
                          Stale
                        </span>
                      ) : null}
                    </div>
                  </td>

                  <td className={TD_NUM}>{run.employeeCount || "—"}</td>
                  <td className={TD_NUM}>{rm(run.totalGross)}</td>
                  <td className={`${TD_NUM} font-semibold`}>{rm(run.totalNet)}</td>
                  <td className={TD_NUM}>{rm(run.totalCostToEmployer)}</td>
                  <td className={`${TD} text-muted-foreground`}>
                    {shortDate(run.generatedAt)}
                  </td>
                  <td className={`${TD} text-right`}>
                    <ChevronRight
                      className="ml-auto size-4 text-muted-foreground"
                      aria-hidden
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {pickerOpen ? (
        <NewRunPicker
          onClose={() => setPickerOpen(false)}
          onCreated={(runId) => {
            setPickerOpen(false);
            onCreated();
            onOpen(runId);
          }}
        />
      ) : null}
    </div>
  );
}

// ─── The create-draft picker ─────────────────────────────────────────────
//
// Period + policy scope, in one dialog. The scope block is one list of policy
// rows; expanding a row reveals its members so the admin can drop individuals
// without unticking the whole policy. A search filters members across every row
// and auto-expands rows with matches. Only payable (complete-profile) employees
// are listed — the backend leaves incomplete ones out either way.
function NewRunPicker({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (runId: string) => void;
}) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);

  const [policies, setPolicies] = useState<PayrollPickerPolicy[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [excluded, setExcluded] = useState<Set<string>>(new Set());
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");

  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Load the roster once, and default every policy that has members to ticked.
  useEffect(() => {
    let live = true;
    getPayrollRunPicker()
      .then((data) => {
        if (!live) return;
        setPolicies(data.policies);
        setSelected(new Set(data.policies.filter((p) => p.members.length > 0).map((p) => p.id)));
      })
      .catch((err) => {
        if (live) setLoadError(err instanceof Error ? err.message : "Could not load employees.");
      });
    return () => {
      live = false;
    };
  }, []);

  const trimmed = query.trim().toLowerCase();

  function matches(m: PayrollPickerMember) {
    if (!trimmed) return true;
    return (
      m.name.toLowerCase().includes(trimmed) ||
      m.employeeId.toLowerCase().includes(trimmed) ||
      m.jobTitle.toLowerCase().includes(trimmed)
    );
  }

  function toggle(set: Set<string>, id: string, update: (next: Set<string>) => void) {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    update(next);
  }

  // "Included" = members of ticked policies who aren't excluded.
  const { includedCount, totalCount, excludedForSubmit } = useMemo(() => {
    let included = 0;
    let total = 0;
    const excludedIds: string[] = [];
    for (const policy of policies ?? []) {
      if (!selected.has(policy.id)) continue;
      for (const m of policy.members) {
        total += 1;
        if (excluded.has(m.employeeProfileId)) excludedIds.push(m.employeeProfileId);
        else included += 1;
      }
    }
    return { includedCount: included, totalCount: total, excludedForSubmit: excludedIds };
  }, [policies, selected, excluded]);

  const noneSelected = selected.size === 0;
  const canSubmit = !creating && !noneSelected && includedCount > 0;

  async function handleCreate() {
    setCreating(true);
    setCreateError(null);
    try {
      const run = await createPayrollRun({
        periodYear: year,
        periodMonth: month,
        policyIds: [...selected],
        excludedEmployeeProfileIds: excludedForSubmit,
      });
      onCreated(run.id);
    } catch (err) {
      // One run per period is a backend invariant, so "already exists" lands
      // here as a plain message rather than a duplicate row.
      setCreateError(err instanceof Error ? err.message : "Could not create the run.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <ModalPortal label="Start a payroll run" onClose={onClose}>
      <header className="border-b border-border/70 px-6 py-4">
        <h2 className="text-base font-semibold text-foreground">Start a payroll run</h2>
        <p className={HINT}>
          Pick the period, tick which policies to include, and (optionally) expand a policy to drop
          specific employees from this run.
        </p>
      </header>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5 pl-1">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className={LABEL} htmlFor="runMonth">
              Month
            </label>
            <PayrollSelect
              id="runMonth"
              value={String(month)}
              onChange={(next) => next && setMonth(Number(next))}
              options={MONTHS.map((name, index) => ({ value: String(index + 1), label: name }))}
            />
          </div>
          <div>
            <label className={LABEL} htmlFor="runYear">
              Year
            </label>
            <input
              id="runYear"
              type="number"
              min={2000}
              max={2100}
              className={INPUT}
              value={year}
              onChange={(e) => setYear(Number(e.target.value))}
            />
          </div>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <span className={LABEL}>Policies and employees</span>
            <span className="text-xs text-muted-foreground">
              {includedCount} of {totalCount} included
            </span>
          </div>

          <div className="relative">
            <Search
              className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden
            />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search name, employee ID, or job title"
              className={`${INPUT} pl-9`}
            />
          </div>

          {loadError ? (
            <p className="text-sm font-medium text-destructive">{loadError}</p>
          ) : policies === null ? (
            <div className="flex items-center gap-2 rounded-2xl border border-border/60 bg-card px-4 py-6 text-sm text-muted-foreground">
              <LoaderCircle className="size-4 animate-spin" aria-hidden />
              Loading employees…
            </div>
          ) : (
            <div className="max-h-[45vh] overflow-y-auto rounded-2xl border border-border/60 bg-card">
              <div className="divide-y divide-border/60">
                {policies.map((policy) => {
                  // Hide policies with no payable members — they contribute
                  // nothing and only clutter the list.
                  if (policy.members.length === 0) return null;
                  const matching = policy.members.filter(matches);
                  // During a search, hide rows with no matches and force-expand
                  // the ones that do.
                  if (trimmed && matching.length === 0) return null;
                  const isOpen = trimmed ? matching.length > 0 : expanded.has(policy.id);
                  const isSelected = selected.has(policy.id);
                  const excludedInPolicy = policy.members.filter((m) =>
                    excluded.has(m.employeeProfileId),
                  ).length;

                  return (
                    <PolicyRow
                      key={policy.id}
                      policy={policy}
                      isSelected={isSelected}
                      excludedInPolicy={excludedInPolicy}
                      isOpen={isOpen}
                      lockExpand={!!trimmed}
                      onTogglePolicy={() => toggle(selected, policy.id, setSelected)}
                      onToggleExpand={() => toggle(expanded, policy.id, setExpanded)}
                    >
                      {matching.map((m) => (
                        <MemberRow
                          key={m.employeeProfileId}
                          member={m}
                          included={isSelected && !excluded.has(m.employeeProfileId)}
                          disabled={!isSelected}
                          onToggle={() => toggle(excluded, m.employeeProfileId, setExcluded)}
                        />
                      ))}
                    </PolicyRow>
                  );
                })}
              </div>
            </div>
          )}

          {policies !== null && !loadError ? (
            noneSelected ? (
              <p className="text-xs text-destructive">Pick at least one policy.</p>
            ) : includedCount === 0 ? (
              <p className="text-xs text-destructive">At least one employee has to be included.</p>
            ) : null
          ) : null}
        </div>

        {createError ? (
          <p className="text-sm font-medium text-destructive">{createError}</p>
        ) : null}
      </div>

      <footer className="flex justify-end gap-2 border-t border-border/70 px-6 py-4">
        <button type="button" className={BUTTON_GHOST} onClick={onClose} disabled={creating}>
          Cancel
        </button>
        <button type="button" className={BUTTON} onClick={() => void handleCreate()} disabled={!canSubmit}>
          {creating ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Plus className="size-4" aria-hidden />
          )}
          {creating ? "Creating…" : "Create draft"}
        </button>
      </footer>
    </ModalPortal>
  );
}

function PolicyRow({
  policy,
  isSelected,
  excludedInPolicy,
  isOpen,
  lockExpand,
  onTogglePolicy,
  onToggleExpand,
  children,
}: {
  policy: PayrollPickerPolicy;
  isSelected: boolean;
  excludedInPolicy: number;
  isOpen: boolean;
  // True while a search is active — expansion follows the matches, so the
  // manual chevron is disabled to avoid fighting it.
  lockExpand: boolean;
  onTogglePolicy: () => void;
  onToggleExpand: () => void;
  children: React.ReactNode;
}) {
  const count = policy.members.length;
  return (
    <div className={isSelected ? undefined : "opacity-60"}>
      <div className="flex items-center gap-3 px-3 py-2.5">
        <CheckBox
          checked={isSelected}
          onChange={onTogglePolicy}
          ariaLabel={`Include ${policy.name}`}
        />
        <div className="flex min-w-0 flex-1 items-center justify-between gap-2">
          <span className="truncate text-sm font-medium text-foreground">
            {policy.name}
            {policy.isDefault ? (
              <span className="ml-1.5 text-xs font-normal text-muted-foreground">(default)</span>
            ) : null}
          </span>
          <span className="shrink-0 text-xs text-muted-foreground">
            {isSelected && excludedInPolicy > 0
              ? `${count - excludedInPolicy} of ${count}`
              : `${count} employee${count === 1 ? "" : "s"}`}
          </span>
        </div>
        <button
          type="button"
          onClick={lockExpand ? undefined : onToggleExpand}
          disabled={lockExpand}
          className="shrink-0 rounded-lg p-1 text-muted-foreground transition hover:bg-muted/60 hover:text-foreground disabled:opacity-50"
          aria-label={isOpen ? "Collapse" : "Expand"}
        >
          {isOpen ? (
            <ChevronDown className="size-4" aria-hidden />
          ) : (
            <ChevronRight className="size-4" aria-hidden />
          )}
        </button>
      </div>
      {isOpen ? <div className="border-t border-border/60 bg-muted/20 py-1">{children}</div> : null}
    </div>
  );
}

function MemberRow({
  member,
  included,
  disabled,
  onToggle,
}: {
  member: PayrollPickerMember;
  included: boolean;
  // True when the parent policy is un-ticked — the row still renders so the
  // admin can see who would be in, but its checkbox is inert.
  disabled: boolean;
  onToggle: () => void;
}) {
  return (
    <div
      className={`flex items-center gap-3 px-3 py-1.5 text-sm transition ${
        disabled ? "opacity-50" : "hover:bg-muted/40"
      }`}
    >
      <CheckBox
        checked={included}
        onChange={onToggle}
        disabled={disabled}
        ariaLabel={`Include ${member.name}`}
      />
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-foreground">{member.name}</span>
        <span className="truncate text-xs text-muted-foreground">
          {[member.employeeId, member.jobTitle].filter(Boolean).join(" · ")}
        </span>
      </div>
    </div>
  );
}
