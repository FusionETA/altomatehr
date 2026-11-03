import { Fragment, useEffect, useMemo, useState } from "react";
import { useMyAccess } from "@/features/settings/lib/module-access";
import {
  ChevronDown,
  ChevronRight,
  Download,
  LoaderCircle,
  Plus,
  Search,
  TriangleAlert,
} from "lucide-react";
import {
  createPayrollRun,
  downloadMonthSummary,
  getPayrollRunPicker,
  type PayrollPickerMember,
  type PayrollPickerPolicy,
  type PayrollRun,
} from "../api";
import { SkeletonRows } from "@/shared/components/Skeleton";
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
//
// A month split into several runs (one per staff group) gets an "all runs"
// row above its runs: the month's combined figures and its combined summary,
// read-only — each run is still approved, filed and paid on its own.
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
  const access = useMyAccess();
  const canRun = access.allEmployees && access.canManage("payroll");

  // Newest month first; within a month, in the order the runs were started.
  const months = useMemo(() => {
    const sorted = [...runs].sort(
      (a, b) =>
        b.periodYear * 12 + b.periodMonth - (a.periodYear * 12 + a.periodMonth) ||
        a.runNumber - b.runNumber,
    );
    const groups: PayrollRun[][] = [];
    for (const run of sorted) {
      const last = groups[groups.length - 1];
      if (last && last[0].periodYear === run.periodYear && last[0].periodMonth === run.periodMonth)
        last.push(run);
      else groups.push([run]);
    }
    return groups;
  }, [runs]);

  return (
    <div className="space-y-6">
      <section className={CARD}>
        <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-foreground">Start a payroll run</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              Pick the period and which policies and employees to include — nothing is calculated
              until you run payroll. To pay a month in separate runs (say, office staff and
              workers), start another run for it and give it a name; each person can be in only
              one run a month.
            </p>
          </div>
          {canRun ? (
            <button type="button" className={BUTTON} onClick={() => setPickerOpen(true)}>
              <Plus className="size-4" aria-hidden />
              Create draft
            </button>
          ) : (
            <p className="max-w-xs text-xs text-muted-foreground">
              {access.allEmployees
                ? "Your payroll access is view only."
                : "Runs cover the whole company; your access covers some employees, so you can view runs but not start one."}
            </p>
          )}
        </header>
      </section>

      {error ? (
        <section className={ERROR_PANEL}>Error: {error}</section>
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
              {loading ? (
                // First visit only — a revisit renders the cached rows at once.
                <SkeletonRows
                  rows={4}
                  widths={["w-28", "w-20", "w-6", "w-16", "w-16", "w-24", "w-20", "w-4"]}
                />
              ) : runs.length === 0 ? (
                <tr>
                  <td
                    colSpan={8}
                    className="px-3 py-10 text-center text-sm text-muted-foreground"
                  >
                    No payroll runs yet. Create one above to get started.
                  </td>
                </tr>
              ) : (
                months.map((monthRuns) => (
                <Fragment key={monthRuns[0].id}>
                {monthRuns.length > 1 ? <MonthRow runs={monthRuns} /> : null}
                {monthRuns.map((run) => (
                <tr
                  key={run.id}
                  className="cursor-pointer border-b border-border/40 transition last:border-0 hover:bg-muted/40"
                  onClick={() => onOpen(run.id)}
                >
                  <td className={`${TD} ${monthRuns.length > 1 ? "pl-8" : ""}`}>
                    <div className="font-medium">{monthRuns.length > 1 ? run.name ?? run.periodLabel : run.label}</div>
                    {monthRuns.length > 1 && run.name ? (
                      <div className="text-xs text-muted-foreground">{run.periodLabel}</div>
                    ) : null}
                    {run.source === "IMPORTED" ? (
                      <div className="text-xs text-muted-foreground">{sourceLabels.IMPORTED}</div>
                    ) : null}
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
                          title="Something changed since payroll was run — re-run it"
                        >
                          <TriangleAlert className="size-3" aria-hidden />
                          Stale
                        </span>
                      ) : null}
                    </div>
                  </td>

                  <td className={TD_NUM}>{run.employeeCount || "—"}</td>
                  <td className={TD_NUM}>{rm(run.totalGrossShown)}</td>
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
                </Fragment>
                ))
              )}
            </tbody>
          </table>
        </section>
      )}

      {/* Importing prior-year history is part of STARTING payroll (a mid-year
          migration seeds the months before this system took over), so it lives
          on this screen — but at the FOOT, below the runs, since day-to-day it
          is the rarest action here. */}
      <YtdImportPanel year={new Date().getFullYear()} onImported={onCreated} />

      {pickerOpen ? (
        <NewRunPicker
          runs={runs}
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
  runs,
  onClose,
  onCreated,
}: {
  runs: PayrollRun[];
  onClose: () => void;
  onCreated: (runId: string) => void;
}) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [name, setName] = useState("");
  // A month that already has a run: this one joins it. At most one run a
  // month goes unnamed, so it needs a name while an unnamed one exists.
  const monthRuns = runs.filter((r) => r.periodYear === year && r.periodMonth === month);
  const needsName = monthRuns.some((r) => !r.name);

  const [policies, setPolicies] = useState<PayrollPickerPolicy[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [excluded, setExcluded] = useState<Set<string>>(new Set());
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");

  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Load the roster for the chosen month (who's payable depends on it — a
  // leaver is owed their last month), and default every policy with members
  // to ticked.
  useEffect(() => {
    let live = true;
    getPayrollRunPicker(year, month)
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
  }, [year, month]);

  const trimmed = query.trim().toLowerCase();

  function matches(m: PayrollPickerMember) {
    if (!trimmed) return true;
    return (
      m.name.toLowerCase().includes(trimmed) ||
      m.employeeId.toLowerCase().includes(trimmed) ||
      m.jobTitle.toLowerCase().includes(trimmed)
    );
  }

  // Resolved up front rather than inside the map: a map that returns null for
  // every row leaves an empty bordered box with nothing in it and no height,
  // which reads as a broken dialog rather than an empty one.
  const visible = (policies ?? [])
    .filter((policy) => policy.members.length > 0)
    .map((policy) => ({ policy, matching: policy.members.filter(matches) }))
    .filter(({ matching }) => !trimmed || matching.length > 0);

  function toggle(set: Set<string>, id: string, update: (next: Set<string>) => void) {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    update(next);
  }

  // "Included" = members of ticked policies who aren't excluded — and aren't
  // already in another run this month (the server leaves those out anyway).
  const { includedCount, totalCount, takenCount, excludedForSubmit } = useMemo(() => {
    let included = 0;
    let total = 0;
    let taken = 0;
    const excludedIds: string[] = [];
    for (const policy of policies ?? []) {
      if (!selected.has(policy.id)) continue;
      for (const m of policy.members) {
        if (m.takenByRunId) {
          taken += 1;
          continue;
        }
        total += 1;
        if (excluded.has(m.employeeProfileId)) excludedIds.push(m.employeeProfileId);
        else included += 1;
      }
    }
    return {
      includedCount: included,
      totalCount: total,
      takenCount: taken,
      excludedForSubmit: excludedIds,
    };
  }, [policies, selected, excluded]);

  const noneSelected = selected.size === 0;
  const nameMissing = needsName && name.trim() === "";
  const canSubmit = !creating && !noneSelected && includedCount > 0 && !nameMissing;

  async function handleCreate() {
    setCreating(true);
    setCreateError(null);
    try {
      const run = await createPayrollRun({
        periodYear: year,
        periodMonth: month,
        name: name.trim() || undefined,
        policyIds: [...selected],
        excludedEmployeeProfileIds: excludedForSubmit,
      });
      onCreated(run.id);
    } catch (err) {
      // A missing or repeated name, or nobody left to pay, lands here as the
      // server's message.
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

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
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

        <div>
          <label className={LABEL} htmlFor="runName">
            Run name{" "}
            {needsName ? null : <span className="font-normal text-muted-foreground">(optional)</span>}
          </label>
          <input
            id="runName"
            maxLength={60}
            className={INPUT}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Workers, Management"
          />
          <p className={HINT}>
            {monthRuns.length > 0
              ? `${MONTHS[month - 1]} ${year} already has ${monthRuns.length === 1 ? "a run" : `${monthRuns.length} runs`} (${monthRuns
                  .map((r) => r.name ?? "unnamed")
                  .join(", ")}). Name this one so the runs and their files can be told apart. Anyone already in another run this month is greyed out below.`
              : "Only needed if you'll pay this month in more than one run — for example one per staff group."}
          </p>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <span className={LABEL}>Policies and employees</span>
            <span className="text-xs text-muted-foreground">
              {includedCount} of {totalCount} included
              {takenCount > 0 ? ` · ${takenCount} in another run` : ""}
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
            <div className="max-h-[45vh] min-h-[7rem] overflow-y-auto rounded-2xl border border-border/60 bg-card">
              {visible.length === 0 ? (
                // Three different nothings, and an admin can only act on the
                // one they're actually in — so each says what to do next
                // instead of leaving a blank panel to interpret.
                <div className="flex h-28 flex-col items-center justify-center gap-1 px-6 text-center">
                  <p className="text-sm font-medium text-foreground">
                    {trimmed
                      ? "No one matches that search"
                      : policies.length === 0
                        ? "No policies yet"
                        : "No employees are ready to be paid"}
                  </p>
                  <p className={HINT}>
                    {trimmed
                      ? "Clear the search to see every policy again."
                      : policies.length === 0
                        ? "Add a policy under System Settings → Policies, then start the run."
                        : "A profile needs its personal, employment and statutory details before payroll can include it — the Employees tab flags who is missing what."}
                  </p>
                </div>
              ) : (
              <div className="divide-y divide-border/60">
                {visible.map(({ policy, matching }) => {
                  // During a search the matching rows are force-expanded.
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
                          included={isSelected && !m.takenByRunId && !excluded.has(m.employeeProfileId)}
                          disabled={!isSelected || !!m.takenByRunId}
                          onToggle={() => toggle(excluded, m.employeeProfileId, setExcluded)}
                        />
                      ))}
                    </PolicyRow>
                  );
                })}
              </div>
              )}
            </div>
          )}

          {/* Only a prompt when there is something to act on: "Pick at least one
              policy" above an empty list is an instruction that can't be
              followed, and the panel already explains itself. */}
          {policies !== null && !loadError && visible.length > 0 ? (
            noneSelected ? (
              <p className="text-xs text-destructive">Pick at least one policy.</p>
            ) : includedCount === 0 ? (
              <p className="text-xs text-destructive">
                {totalCount === 0 && takenCount > 0
                  ? "Everyone here is already in another run this month."
                  : "At least one employee has to be included."}
              </p>
            ) : nameMissing ? (
              <p className="text-xs text-destructive">Give this run a name.</p>
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
  const taken = policy.members.filter((m) => m.takenByRunId).length;
  const count = policy.members.length - taken;
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
            {taken > 0 ? ` · ${taken} in another run` : ""}
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
          {member.takenByRunLabel
            ? `Already in ${member.takenByRunLabel}`
            : [member.employeeId, member.jobTitle].filter(Boolean).join(" · ")}
        </span>
      </div>
    </div>
  );
}

// A split month as a whole: its runs' combined figures and the combined
// summary. Read-only — each run below it is opened, approved and filed alone.
function MonthRow({ runs }: { runs: PayrollRun[] }) {
  const [busy, setBusy] = useState<"pdf" | "xlsx" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const sum = (pick: (run: PayrollRun) => number) => runs.reduce((t, run) => t + pick(run), 0);
  const { periodYear: year, periodMonth: month, periodLabel } = runs[0];

  async function get(format: "pdf" | "xlsx") {
    setBusy(format);
    setError(null);
    try {
      await downloadMonthSummary(year, month, format);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not download the summary.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <tr className="border-b border-border/40 bg-muted/30">
      <td className={TD}>
        <div className="font-semibold">{periodLabel} — all runs</div>
        <div className="text-xs text-muted-foreground">
          {runs.length} runs
          {runs.some((r) => r.status !== "SUBMITTED") ? " · not all approved yet" : ""}
        </div>
        {error ? <div className="mt-1 text-xs text-destructive">{error}</div> : null}
      </td>
      <td className={TD} />
      <td className={TD_NUM}>{sum((r) => r.employeeCount) || "—"}</td>
      <td className={TD_NUM}>{rm(sum((r) => r.totalGrossShown))}</td>
      <td className={`${TD_NUM} font-semibold`}>{rm(sum((r) => r.totalNet))}</td>
      <td className={TD_NUM}>{rm(sum((r) => r.totalCostToEmployer))}</td>
      <td className={TD} colSpan={2}>
        <div className="flex items-center gap-1">
          {(["pdf", "xlsx"] as const).map((format) => (
            <button
              key={format}
              type="button"
              className={BUTTON_GHOST}
              disabled={busy !== null}
              onClick={() => void get(format)}
              title={`The month's combined summary (${format === "pdf" ? "PDF" : "Excel"})`}
            >
              {busy === format ? (
                <LoaderCircle className="size-4 animate-spin" aria-hidden />
              ) : (
                <Download className="size-4" aria-hidden />
              )}
              {format === "pdf" ? "Summary" : "Excel"}
            </button>
          ))}
        </div>
      </td>
    </tr>
  );
}
