import { useEffect, useMemo, useRef, useState, type Dispatch, type SetStateAction } from "react";
import { Check, CircleAlert, Loader2, Sparkles, X } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { Switch } from "@/shared/components/ui/switch";
import { getAccountsWithLiabilities, type ChartOfAccount } from "@/features/settings/api";
import {
  checkXeroSplit,
  generateXeroSplit,
  XERO_SPLIT_SOURCE_LABELS,
  type PayrollXeroMapping,
  type XeroSplitAssignment,
  type XeroSplitResult,
  type XeroSplitRule,
} from "../../api";
import {
  BADGE,
  BUTTON,
  CARD,
  ERROR_PANEL,
  HINT,
  INPUT_SM,
  LINK_BUTTON,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TEXTAREA,
  TH,
  TH_NUM,
  WARN_PANEL,
} from "../../lib/ui";

// Xero sync → AI split (advanced).
//
// The admin describes the split in words; AI turns that into rules ONCE, when
// Generate is pressed. The rules are saved with the settings, and posting
// applies them by code — the same run always splits the same way. Every
// check below is code too; the AI never produces an amount.
//
// The standard mapping stays the base: the split re-divides its lines (salary
// into one account by default) by who people are — department, project, job
// title, employment type, role, policy — and, for allowances and deductions,
// by type. The payables (EPF/SOCSO/EIS/PCB/HRDF, net pay) are never split.

const SUGGESTIONS: { label: string; text: string }[] = [
  {
    label: "By project, directors apart",
    text: "Split salary, overtime, allowances and employer EPF, SOCSO and EIS. Job title DIRECTOR first: salary to <account>, allowances to <account>, EPF/SOCSO/EIS to <accounts>. Then members of project <HQ project>: salary to <account>, overtime to <account>, allowances to <account>, EPF/SOCSO/EIS to <accounts>. Everyone else is the remainder: <accounts>.",
  },
  {
    label: "Commission to its own account",
    text: "Split allowances: commission for everyone to <account>; all other allowances stay on <allowance account>.",
  },
  {
    label: "Loan repayments by group",
    text: "Split deductions: loan repayments to <staff loan account>; every other deduction stays on <deductions account>.",
  },
  {
    label: "Split salary by department",
    text: "Split salary by each employee's department: Sales staff to <sales account>, Operations staff to <operations account>, everyone else to <admin account>. Tag each line with the Department tracking option of the same name.",
  },
  {
    label: "Percentages, rest to Admin",
    text: "Split salary: 40% to <account>, 35% to <account>, remainder to <account>. Keep everything else as it is.",
  },
  {
    label: "Fixed RM amounts, rest to Admin",
    text: "Split salary: RM 5,000 to <account>, remainder to <account>.",
  },
  {
    label: "Use the saved rules",
    text: "Keep the saved rules, but also split EPF, SOCSO and EIS employer contributions the same way as salary.",
  },
];

const money = (n: number) =>
  n.toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function XeroAiSplitSection({
  mapping,
  onChange,
}: {
  mapping: PayrollXeroMapping;
  onChange: Dispatch<SetStateAction<PayrollXeroMapping>>;
}) {
  const accountsQuery = useCachedQuery("/accounts?includeLiabilities=true", () =>
    getAccountsWithLiabilities().catch(() => [] as ChartOfAccount[]),
  );
  const accounts = accountsQuery.data ?? [];

  const [prompt, setPrompt] = useState(mapping.splitPrompt ?? "");
  const [result, setResult] = useState<XeroSplitResult | null>(null);
  const [busy, setBusy] = useState<"generate" | "check" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const rules = mapping.splitRules ?? [];
  const aiOn = mapping.journalMode === "AI_SPLIT";
  const preview = result?.preview ?? null;
  const passed = preview?.ok ?? false;

  // Saved rules are re-checked when the tab opens, so the list below speaks
  // about today's accounts and staff, not the day they were generated.
  const checkedOnce = useRef(false);
  useEffect(() => {
    if (checkedOnce.current || rules.length === 0) return;
    checkedOnce.current = true;
    setBusy("check");
    checkXeroSplit(rules)
      .then(setResult)
      .catch((e: Error) => setError(e.message))
      .finally(() => setBusy(null));
  }, [rules]);

  const accountName = useMemo(() => {
    const byCode = new Map(accounts.map((a) => [a.code?.toUpperCase(), a.name]));
    return (code: string) => byCode.get(code.toUpperCase());
  }, [accounts]);

  const salaryAccountId = mapping.accounts.salary ?? null;
  const salaryAccount = accounts.find((a) => a.id === salaryAccountId);

  const generate = async () => {
    setBusy("generate");
    setError(null);
    try {
      const next = await generateXeroSplit(prompt);
      setResult(next);
      // Applied to the mapping as it is NOW: the AI can take a while, and
      // the admin may have changed the standard accounts meanwhile.
      onChange((current) => ({
        ...current,
        splitPrompt: prompt.trim(),
        splitRules: next.rules,
        // Rules that fail a check can't post. Rather than leave the switch on
        // over them, turn it off; the admin turns it back on once they pass.
        journalMode: next.ok ? current.journalMode : "STANDARD",
      }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const clearRules = () => {
    setResult(null);
    onChange((current) => ({ ...current, splitRules: [], journalMode: "STANDARD" }));
  };

  const splitSources = new Set(rules.map((r) => r.source));
  const splitLines = (preview?.lines ?? []).filter((l) => l.source && splitSources.has(l.source));
  const splitTotal = splitLines.reduce((sum, l) => sum + l.amount, 0);
  const checks = preview?.checks ?? [];
  const passedCount = checks.filter((c) => c.passed).length;

  return (
    <div className="space-y-5">
      {/* ── On / off ──────────────────────────────────────────────── */}
      <section className={CARD}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 className="text-[15px] font-semibold text-foreground">Use the AI split when posting</h3>
            <p className={HINT}>
              Off: runs post exactly as the standard mapping says. On: the same journal, with the
              lines below re-divided by the saved rules. Posting applies the saved rules — it
              never calls AI — and is blocked if any check fails.
            </p>
          </div>
          <Switch
            aria-label="Use the AI split when posting"
            checked={aiOn}
            disabled={!aiOn && (!passed || rules.length === 0)}
            onCheckedChange={(on) =>
              onChange((current) => ({ ...current, journalMode: on ? "AI_SPLIT" : "STANDARD" }))
            }
          />
        </div>
        {!aiOn && rules.length > 0 && preview && !passed ? (
          <p className="mt-3 text-xs text-destructive">
            Can't switch on until every check below passes.
          </p>
        ) : null}
        {!aiOn && rules.length === 0 ? (
          <p className="mt-3 text-xs text-muted-foreground">
            Generate rules below first.
          </p>
        ) : null}
      </section>

      {/* ── 1 · Default journal ───────────────────────────────────── */}
      <section className={CARD}>
        <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
          1 · Default manual journal
        </p>
        <div className="mt-3 grid gap-4 sm:grid-cols-[1fr_auto_auto] sm:items-end">
          <div>
            <p className="text-sm font-semibold text-foreground">
              {preview?.runLabel ? `Payroll — ${preview.runLabel}` : "No payroll run to try it on yet"}
            </p>
            <p className={HINT}>
              {preview?.runLabel
                ? "The latest run with payslips. The split is tried on its real figures."
                : "Generate a payroll run to see the split on real figures; the rule checks still run."}
            </p>
          </div>
          <div>
            <p className="text-xs text-muted-foreground">Debit · default salary account</p>
            <p className="text-sm font-semibold text-foreground">
              {salaryAccount
                ? [salaryAccount.code, salaryAccount.name].filter(Boolean).join(" · ")
                : "Not mapped — set it on Standard mapping"}
            </p>
          </div>
          {preview && preview.replaced.length > 0 ? (
            <div className="sm:text-right">
              <p className="text-xs text-muted-foreground">Amount to split</p>
              <p className="text-xl font-semibold tabular-nums text-primary">
                RM {money(preview.replacedTotal)}
              </p>
            </div>
          ) : null}
        </div>
      </section>

      {/* ── 2 · Prompt ────────────────────────────────────────────── */}
      <section className={CARD}>
        <label
          htmlFor="xeroSplitPrompt"
          className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground"
        >
          2 · Tell the engine how to split
        </label>
        <textarea
          id="xeroSplitPrompt"
          rows={4}
          maxLength={4000}
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          placeholder="e.g. Split 6100 Salaries & Wages: 40% to 6110 Salaries – Sales, 35% to 6120 Salaries – Operations, remainder to 6130 Salaries – Admin. Tag each line with the Department tracking option of the same name."
          className={`${TEXTAREA} mt-3`}
        />
        <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-muted-foreground">Try:</span>
            {SUGGESTIONS.map((s) => (
              <button
                key={s.label}
                type="button"
                onClick={() => setPrompt(s.text)}
                className="rounded-full border border-border/60 bg-muted/50 px-3 py-1 text-xs text-foreground transition hover:bg-muted"
              >
                {s.label}
              </button>
            ))}
          </div>
          <button
            type="button"
            className={BUTTON}
            disabled={busy !== null || prompt.trim().length < 5}
            onClick={generate}
          >
            {busy === "generate" ? (
              <Loader2 aria-hidden className="size-4 animate-spin" />
            ) : (
              <Sparkles aria-hidden className="size-4" />
            )}
            {busy === "generate" ? "Generating…" : "Generate split"}
          </button>
        </div>
        <p className={HINT}>
          AI reads this once and writes rules; nothing is saved until you save the settings.
          It can group people by department, project, job title, employment type, role, policy
          or name, and pick out allowance and deduction types — using only accounts, tracking
          options and values that exist in this company.
        </p>
        {error ? <div className={`${ERROR_PANEL} mt-3`}>{error}</div> : null}
        {result?.summary ? (
          <div className={`${NOTE_PANEL} mt-3`}>
            <p>{result.summary}</p>
            {result.provider ? (
              <p className="mt-1 text-xs text-muted-foreground">
                Read by {result.provider === "claude" ? "Claude" : "Gemini (fallback)"}.
              </p>
            ) : null}
          </div>
        ) : null}
        {result && result.questions.length > 0 ? (
          <div className={`${WARN_PANEL} mt-3`}>
            <p className="font-semibold">The AI wasn't sure about:</p>
            <ul className="mt-1 list-disc pl-5">
              {result.questions.map((q) => (
                <li key={q}>{q}</li>
              ))}
            </ul>
          </div>
        ) : null}
      </section>

      {rules.length > 0 ? (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
          {/* ── 3 · Split journal ─────────────────────────────────── */}
          <section className={CARD}>
            <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
              <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                3 · Split journal (engine output)
              </p>
              {preview && splitLines.length > 0 ? (
                <p className="text-xs text-muted-foreground">
                  {splitLines.length} lines · replace {preview.replaced.length} standard line
                  {preview.replaced.length === 1 ? "" : "s"}
                </p>
              ) : null}
            </div>

            <RulesTable rules={rules} accountName={accountName} />

            {splitLines.length > 0 ? (
              <div className="mt-5 overflow-x-auto">
                <table className="w-full min-w-[520px]">
                  <thead>
                    <tr>
                      <th className={TH}>Account</th>
                      <th className={TH}>Description</th>
                      <th className={TH}>Tracking</th>
                      <th className={TH_NUM}>Amount</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/60">
                    {splitLines.map((line, i) => (
                      <tr key={`${line.accountCode}-${i}`}>
                        <td className={TD}>
                          {line.accountCode}
                          {accountName(line.accountCode) ? ` · ${accountName(line.accountCode)}` : ""}
                        </td>
                        <td className={TD}>{line.description}</td>
                        <td className={TD}>{line.trackingOption ?? "—"}</td>
                        <td className={TD_NUM}>{money(line.amount)}</td>
                      </tr>
                    ))}
                    <tr className="font-semibold">
                      <td className={TD} colSpan={3}>
                        Total (standard lines replaced: {money(preview?.replacedTotal ?? 0)})
                      </td>
                      <td className={TD_NUM}>{money(splitTotal)}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            ) : busy === "check" ? (
              <p className="mt-4 text-sm text-muted-foreground">Checking the saved rules…</p>
            ) : null}

            <div className="mt-4">
              <button
                type="button"
                className="text-xs font-medium text-destructive hover:underline"
                onClick={clearRules}
              >
                Clear the rules
              </button>
            </div>
          </section>

          {/* ── 4 · Checks ────────────────────────────────────────── */}
          <section className={CARD}>
            <div className="mb-3 flex items-center justify-between gap-2">
              <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                4 · Validation
              </p>
              {checks.length > 0 ? (
                <span
                  className={`${BADGE} ${
                    passed
                      ? "bg-emerald-600 text-white"
                      : "bg-destructive/10 text-destructive"
                  }`}
                >
                  {passedCount} of {checks.length} passed
                </span>
              ) : null}
            </div>

            {checks.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                {busy ? "Checking…" : "Generate rules to see the checks."}
              </p>
            ) : (
              <ul className="space-y-3">
                {checks.map((check) => (
                  <li key={check.label} className="flex gap-2 text-sm">
                    {check.passed ? (
                      <Check aria-hidden className="mt-0.5 size-4 shrink-0 text-emerald-600" />
                    ) : (
                      <X aria-hidden className="mt-0.5 size-4 shrink-0 text-destructive" />
                    )}
                    <span>
                      <span className="text-foreground">{check.label}</span>
                      {check.detail ? (
                        <span
                          className={`block text-xs ${
                            check.passed ? "text-muted-foreground" : "text-destructive"
                          }`}
                        >
                          {check.detail}
                        </span>
                      ) : null}
                    </span>
                  </li>
                ))}
              </ul>
            )}

            {checks.length > 0 && !passed ? (
              <div className={`${WARN_PANEL} mt-4 flex gap-2`}>
                <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
                <span>Posting stays blocked until every check passes.</span>
              </div>
            ) : null}
          </section>

          {preview && preview.assignments.length > 0 ? (
            <WhoWentWhere assignments={preview.assignments} accountName={accountName} />
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function RulesTable({
  rules,
  accountName,
}: {
  rules: XeroSplitRule[];
  accountName: (code: string) => string | undefined;
}) {
  const share = (r: XeroSplitRule) => {
    const who = (() => {
      switch (r.basis) {
        case "PERCENT":
          return `${r.value ?? 0}%`;
        case "FIXED":
          return `RM ${money(r.value ?? 0)}`;
        case "REMAINDER":
          return "Remainder";
        case "EVERYONE":
          return "Everyone";
        case "EMPLOYEE":
          return `${r.match.length} named staff`;
        default:
          return `${BASIS_LABELS[r.basis]}: ${r.match.join(", ")}`;
      }
    })();
    const types = r.categories ?? [];
    return types.length > 0 ? `${who} · ${types.map(typeLabel).join(", ")}` : who;
  };

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[520px]">
        <thead>
          <tr>
            <th className={TH}>Splits</th>
            <th className={TH}>Rule</th>
            <th className={TH}>To account</th>
            <th className={TH}>Tracking</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border/60">
          {rules.map((r, i) => (
            <tr key={i}>
              <td className={TD}>{XERO_SPLIT_SOURCE_LABELS[r.source] ?? r.source}</td>
              <td className={TD}>
                {r.label ? `${r.label} · ` : ""}
                {share(r)}
              </td>
              <td className={TD}>
                {r.accountCode}
                {accountName(r.accountCode) ? ` · ${accountName(r.accountCode)}` : ""}
              </td>
              <td className={TD}>
                {r.tracking.length === 0
                  ? "—"
                  : r.tracking.map((t) => `${t.category}: ${t.option}`).join(", ")}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const BASIS_LABELS: Record<XeroSplitRule["basis"], string> = {
  DEPARTMENT: "Dept",
  PROJECT: "Project",
  JOB_TITLE: "Job title",
  EMPLOYMENT_TYPE: "Employment type",
  ROLE: "Role",
  POLICY: "Policy",
  EMPLOYEE: "Named staff",
  EVERYONE: "Everyone",
  FIXED: "Fixed",
  PERCENT: "Percent",
  REMAINDER: "Remainder",
};

// "wages_commission" → "Commission", for a rule's types. The server's labels
// arrive with the who-went-where list; a rule shows its codes readably.
function typeLabel(code: string) {
  const words = code.replace(/^(wages|allowance|deduct|bik)_/, "").replace(/_/g, " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

const PEOPLE_SHOWN = 15;

// Who went where: every person on the run, and which rule took each of their
// amounts — so an admin can check someone on two projects, or a director,
// landed where they meant before anything posts.
function WhoWentWhere({
  assignments,
  accountName,
}: {
  assignments: XeroSplitAssignment[];
  accountName: (code: string) => string | undefined;
}) {
  const [search, setSearch] = useState("");
  const [showAll, setShowAll] = useState(false);

  // How many people each rule took, across every line it splits.
  const byRule = useMemo(() => {
    const people = new Map<string, Set<string>>();
    for (const a of assignments) {
      for (const p of a.placements) {
        const key = `${p.source} → ${p.rule}`;
        if (!people.has(key)) people.set(key, new Set());
        people.get(key)!.add(a.employeeName);
      }
    }
    return [...people.entries()].map(([rule, names]) => ({ rule, count: names.size }));
  }, [assignments]);

  const term = search.trim().toLowerCase();
  const matching = term
    ? assignments.filter((a) => a.employeeName.toLowerCase().includes(term))
    : assignments;
  const shown = showAll || term ? matching : matching.slice(0, PEOPLE_SHOWN);

  return (
    <section className={`${CARD} lg:col-span-2`}>
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
          5 · Who went where
        </p>
        <p className="text-xs text-muted-foreground">
          {assignments.length} {assignments.length === 1 ? "person" : "people"} on this run
        </p>
      </div>

      <ul className="mb-4 flex flex-wrap gap-2">
        {byRule.map(({ rule, count }) => (
          <li
            key={rule}
            className="rounded-full border border-border/60 bg-muted/50 px-3 py-1 text-xs text-foreground"
          >
            {rule} · {count}
          </li>
        ))}
      </ul>

      <input
        type="search"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Find a person"
        aria-label="Find a person"
        className={`${INPUT_SM} mb-3 sm:max-w-xs`}
      />

      <div className="overflow-x-auto">
        <table className="w-full min-w-[640px]">
          <thead>
            <tr>
              <th className={TH}>Person</th>
              <th className={TH}>Line</th>
              <th className={TH}>Rule</th>
              <th className={TH}>Account</th>
              <th className={TH_NUM}>Amount</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border/60">
            {shown.flatMap((a) =>
              a.placements.map((p, i) => (
                <tr key={`${a.employeeName}-${i}`}>
                  <td className={`${TD} font-medium`}>{i === 0 ? a.employeeName : ""}</td>
                  <td className={TD}>{p.type ? `${p.source} · ${p.type}` : p.source}</td>
                  <td className={TD}>{p.rule}</td>
                  <td className={TD}>
                    {p.accountCode
                      ? `${p.accountCode}${accountName(p.accountCode) ? ` · ${accountName(p.accountCode)}` : ""}`
                      : "Shared pool"}
                  </td>
                  <td className={TD_NUM}>{money(p.amount)}</td>
                </tr>
              )),
            )}
          </tbody>
        </table>
      </div>

      {term && matching.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">Nobody on this run matches “{search}”.</p>
      ) : null}
      {!term && !showAll && matching.length > PEOPLE_SHOWN ? (
        <button type="button" className={`${LINK_BUTTON} mt-3`} onClick={() => setShowAll(true)}>
          Show all {matching.length} people
        </button>
      ) : null}
    </section>
  );
}
