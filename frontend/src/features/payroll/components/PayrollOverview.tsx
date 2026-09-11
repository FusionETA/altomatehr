import { useEffect, useMemo, useState } from "react";
import {
  ArrowRight,
  ClipboardCheck,
  HandCoins,
  Inbox,
  TriangleAlert,
  type LucideIcon,
} from "lucide-react";
import {
  getEmployeeLoans,
  getPayrollEmployees,
  getPayrollRuns,
  type EmployeeLoan,
  type PayrollEmployee,
  type PayrollRun,
} from "../api";
import { rm, statusLabels, statusTone } from "../lib/payroll-format";
import { BADGE, CARD, ERROR_PANEL, HINT } from "../lib/ui";
import { CardHead, EmptyState } from "@/features/admin/components/DashboardCard";

// The payroll dashboard.
//
// Same shape as the claims and leave dashboards: three tiles for what needs
// attention, then cards for the detail. Everything here is a number an admin
// can act on — what is waiting on them, what would stop a filing, and what
// the year owes so far. The statutory rules are documented in the module's
// own reference, not on a page someone has to scroll past every month.
const TONES = {
  primary: { text: "text-primary", bg: "bg-primary/10", border: "hover:border-primary/40" },
  tertiary: { text: "text-tertiary", bg: "bg-tertiary/10", border: "hover:border-tertiary/40" },
  destructive: {
    text: "text-destructive",
    bg: "bg-destructive/10",
    border: "hover:border-destructive/40",
  },
} as const;

export function PayrollOverview({
  onGo,
  onOpen,
}: {
  onGo: (key: string) => void;
  onOpen?: (parentId: string, childId: string) => void;
}) {
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [employees, setEmployees] = useState<PayrollEmployee[]>([]);
  const [loans, setLoans] = useState<EmployeeLoan[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let live = true;

    Promise.all([getPayrollRuns(), getPayrollEmployees(), getEmployeeLoans()])
      .then(([nextRuns, nextEmployees, nextLoans]) => {
        if (!live) return;
        setRuns(nextRuns);
        setEmployees(nextEmployees);
        setLoans(nextLoans);
      })
      .catch((e: unknown) => live && setError(e instanceof Error ? e.message : String(e)))
      .finally(() => live && setLoading(false));

    return () => {
      live = false;
    };
  }, []);

  const awaiting = runs.filter((run) => run.status === "PENDING_APPROVAL");
  const stale = runs.filter((run) => run.isStale && run.status !== "SUBMITTED");

  // Anyone a statutory file would reject, plus anyone who would be paid
  // nothing — both stop a run being submitted, so they are one number.
  const blocked = employees.filter(
    (employee) =>
      employee.notPayableReason === null &&
      (employee.missing.length > 0 ||
        (employee.salaryType === "MONTHLY" ? employee.monthlySalary : employee.hourlyRate) ===
          null),
  );

  // Newest period first, regardless of when rows were created.
  const byPeriod = useMemo(
    () =>
      [...runs].sort(
        (a, b) => b.periodYear * 12 + b.periodMonth - (a.periodYear * 12 + a.periodMonth),
      ),
    [runs],
  );

  const latest = byPeriod[0];
  const year = latest?.periodYear ?? new Date().getFullYear();

  // Only APPROVED months are money that actually moved.
  const filed = runs.filter((run) => run.status === "SUBMITTED" && run.periodYear === year);

  const ytd = filed.reduce(
    (acc, run) => ({
      net: acc.net + run.totalNet,
      epf: acc.epf + run.totalEmployeeEpf + run.totalEmployerEpf,
      socso: acc.socso + run.totalEmployeeSocso + run.totalEmployerSocso,
      eis: acc.eis + run.totalEmployeeEis + run.totalEmployerEis,
      pcb: acc.pcb + run.totalPcb + run.totalCp38,
      cost: acc.cost + run.totalCostToEmployer,
    }),
    { net: 0, epf: 0, socso: 0, eis: 0, pcb: 0, cost: 0 },
  );

  const outstanding = loans
    .filter((loan) => loan.status === "ACTIVE")
    .reduce((total, loan) => total + loan.remainingAmount, 0);

  if (error) return <section className={ERROR_PANEL}>Error: {error}</section>;
  if (loading) return <section className={CARD}>Loading payroll…</section>;

  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-3">
        <Tile
          icon={Inbox}
          count={awaiting.length}
          label="Waiting for your approval"
          detail={`RM ${rm(awaiting.reduce((t, run) => t + run.totalNet, 0))} of net pay`}
          clearText="Nothing waiting"
          tone="primary"
          onClick={() => onGo("runs")}
        />
        <Tile
          icon={ClipboardCheck}
          count={blocked.length}
          label="Employees not ready to file"
          detail="A missing number or salary blocks the whole run"
          clearText="Everyone is ready"
          tone="destructive"
          onClick={() => onGo("employees")}
        />
        <Tile
          icon={TriangleAlert}
          count={stale.length}
          label="Drafts behind their inputs"
          detail="Regenerate before filing"
          clearText="No stale drafts"
          tone="tertiary"
          onClick={() => onGo("runs")}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* ── The month in front of you ────────────────────────────── */}
        <section className={CARD}>
          <CardHead
            title="Latest run"
            meta={latest ? latest.periodLabel : undefined}
          />

          {latest ? (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center gap-2">
                <span className={`${BADGE} ${statusTone[latest.status]}`}>
                  {statusLabels[latest.status]}
                </span>
                {latest.isStale ? (
                  <span
                    className={`${BADGE} border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400`}
                  >
                    Behind its inputs
                  </span>
                ) : null}
                <span className="text-xs text-muted-foreground">
                  {latest.employeeCount} employee(s)
                </span>
              </div>

              <dl className="grid grid-cols-3 gap-3">
                <Figure label="Gross" value={latest.totalGross} />
                <Figure label="Net pay" value={latest.totalNet} emphasis />
                <Figure label="Cost" value={latest.totalCostToEmployer} />
              </dl>

              <Link label="Open this run" onClick={() => onGo("runs")} />
            </div>
          ) : (
            <div className="space-y-4">
              <EmptyState text="No payroll run yet. Create one to get started." />
              <Link label="Start a run" onClick={() => onGo("runs")} />
            </div>
          )}
        </section>

        {/* ── What the year has cost ──────────────────────────────── */}
        <section className={CARD}>
          <CardHead title="Filed so far" meta={String(year)} />

          {filed.length === 0 ? (
            <EmptyState
              text={`No approved runs in ${year} yet — nothing has been filed.`}
            />
          ) : (
            <div className="space-y-4">
              <p className={HINT}>
                Across {filed.length} approved month(s). Statutory figures add both
                halves, because that is what is remitted.
              </p>

              <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                <Figure label="Net paid" value={ytd.net} emphasis />
                <Figure label="EPF" value={ytd.epf} />
                <Figure label="SOCSO" value={ytd.socso} />
                <Figure label="EIS" value={ytd.eis} />
                <Figure label="PCB" value={ytd.pcb} />
                <Figure label="Total cost" value={ytd.cost} />
              </dl>

              <Link label="Annual forms" onClick={() => onGo("annual")} />
            </div>
          )}
        </section>
      </div>

      {/* ── Everything else, as one row of ways in ─────────────────── */}
      <section className={CARD}>
        <CardHead title="Elsewhere in payroll" />

        <ul className="grid gap-3 sm:grid-cols-3">
          <Shortcut
            icon={HandCoins}
            title="Loans"
            detail={
              outstanding > 0
                ? `RM ${rm(outstanding)} still to repay`
                : "No loans outstanding"
            }
            onClick={() => onGo("loans")}
          />
          <Shortcut
            icon={ClipboardCheck}
            title="Employees"
            detail="Salary and statutory numbers, on each profile"
            onClick={() => onOpen?.("company", "manage-employee")}
          />
          <Shortcut
            icon={Inbox}
            title="Settings"
            detail="Rules, employer particulars, bank and Xero"
            onClick={() => onGo("settings")}
          />
        </ul>
      </section>
    </div>
  );
}

function Tile({
  icon: Icon,
  count,
  label,
  detail,
  clearText,
  tone,
  onClick,
}: {
  icon: LucideIcon;
  count: number;
  label: string;
  detail: string;
  // What it says at zero. A bare "0" reads as missing data; "Everyone is
  // ready" reads as an answer.
  clearText: string;
  tone: keyof typeof TONES;
  onClick: () => void;
}) {
  const clear = count === 0;
  const colors = TONES[tone];

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={clear}
      className={`group ${CARD} text-left transition disabled:cursor-default ${
        clear ? "" : colors.border
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p
            className={`text-3xl font-black leading-none tabular-nums ${
              clear ? "text-muted-foreground" : colors.text
            }`}
          >
            {clear ? "0" : count}
          </p>
          <p className="mt-2 text-sm font-bold text-foreground">{label}</p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {clear ? clearText : detail}
          </p>
        </div>

        <span
          className={`relative flex size-9 shrink-0 items-center justify-center rounded-xl ${
            clear ? "bg-secondary/40 text-muted-foreground" : `${colors.bg} ${colors.text}`
          }`}
        >
          <Icon
            className={`size-4 transition-opacity ${clear ? "" : "group-hover:opacity-0"}`}
          />
          {clear ? null : (
            <ArrowRight className="absolute size-4 opacity-0 transition-opacity group-hover:opacity-100" />
          )}
        </span>
      </div>
    </button>
  );
}

function Figure({
  label,
  value,
  emphasis,
}: {
  label: string;
  value: number;
  emphasis?: boolean;
}) {
  return (
    <div className="min-w-0 rounded-2xl bg-surface-low px-3 py-2.5">
      <p
        className={`truncate tabular-nums leading-tight ${
          emphasis ? "text-lg font-black text-foreground" : "text-base font-bold text-foreground"
        }`}
      >
        {rm(value)}
      </p>
      <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </p>
    </div>
  );
}

function Link({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex items-center gap-1.5 rounded-lg text-sm font-semibold text-primary transition hover:gap-2.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
    >
      {label}
      <ArrowRight className="size-4" aria-hidden />
    </button>
  );
}

function Shortcut({
  icon: Icon,
  title,
  detail,
  onClick,
}: {
  icon: LucideIcon;
  title: string;
  detail: string;
  onClick: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onClick}
        className="group flex w-full items-center gap-3 rounded-2xl border border-border/70 bg-card p-3 text-left transition hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
      >
        <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <Icon className="size-4" aria-hidden />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-semibold text-foreground">{title}</span>
          <span className="block truncate text-xs text-muted-foreground">{detail}</span>
        </span>
        <ArrowRight className="size-4 shrink-0 text-muted-foreground transition group-hover:translate-x-0.5" />
      </button>
    </li>
  );
}
