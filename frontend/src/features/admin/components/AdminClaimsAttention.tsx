import { useMemo } from "react";
import {
  ArrowRight,
  Clock,
  Inbox,
  ShieldAlert,
  TrendingUp,
  TriangleAlert,
  UserX,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type { Claim } from "@/features/claims/api";
import { formatCurrency } from "@/features/claims/lib/claim-formatters";
import {
  isOverLimitPending,
  isPendingClaim,
  isStaleClaim,
  projectSpendThisMonth,
  stuckWithApprovers,
  sumAmount,
  STALE_AFTER_DAYS,
} from "@/features/claims/lib/claim-insights";
import { displayPerson } from "@/features/employee-portal/lib/employee-formatters";
import type { AdminOverview } from "../api";
import { CARD, EYEBROW, TILE } from "../lib/dashboard-styles";
import {
  awaitingApprovalDrilldown,
  claimIdsDrilldown,
  monthSpendDrilldown,
  overLimitDrilldown,
  overturnedDrilldown,
  projectSpendDrilldown,
  stalePendingDrilldown,
  type ClaimDrilldown,
} from "../lib/claims-drilldown";
import { CardHead, EmptyState } from "./DashboardCard";

// The claims dashboard's landing surface. It opens on what needs a decision —
// what is stuck, and with whom — not on how much was spent. Every figure here
// is a button: clicking it opens the claims behind it, so no number has to be
// taken on trust.

export function AdminClaimsAttention({
  claims,
  overview,
  projectNames,
  onDrill,
}: {
  claims: Claim[];
  overview: AdminOverview | null;
  projectNames: Map<string, string>;
  onDrill: (drilldown: ClaimDrilldown) => void;
}) {
  const stale = useMemo(() => claims.filter((claim) => isStaleClaim(claim)), [claims]);
  const pending = useMemo(() => claims.filter(isPendingClaim), [claims]);
  const overLimit = useMemo(() => claims.filter(isOverLimitPending), [claims]);

  const spend = useMemo(
    () => projectSpendThisMonth(claims, projectNames),
    [claims, projectNames],
  );

  // Grouped from the overview, because only the backend knows the approval
  // chain — a claim's current approver isn't on the claim itself.
  const stuck = useMemo(
    () => stuckWithApprovers(overview?.stalePendingClaims ?? []),
    [overview],
  );

  const overturned = overview?.overturnedSupervisors;

  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-3">
        <AttentionTile
          icon={Clock}
          count={stale.length}
          label={`Pending over ${STALE_AFTER_DAYS} days`}
          value={sumAmount(stale)}
          clearText="Nothing is stuck"
          tone="tertiary"
          onClick={() => onDrill(stalePendingDrilldown())}
        />
        <AttentionTile
          icon={Inbox}
          count={pending.length}
          label="Awaiting approval"
          value={sumAmount(pending)}
          clearText="The queue is clear"
          tone="primary"
          onClick={() => onDrill(awaitingApprovalDrilldown())}
        />
        <AttentionTile
          icon={TriangleAlert}
          count={overLimit.length}
          label="Over limit, still pending"
          value={sumAmount(overLimit)}
          clearText="Nothing over limit"
          tone="destructive"
          onClick={() => onDrill(overLimitDrilldown())}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <StuckWithCard stuck={stuck} onDrill={onDrill} />
        <ProjectSpendCard spend={spend} onDrill={onDrill} />
      </div>

      <ApprovalTrustCard
        total={overturned?.total ?? 0}
        samples={overturned?.samples ?? []}
        onDrill={onDrill}
      />
    </div>
  );
}

// ─── Attention tiles ─────────────────────────────────────────────────────────

const TONES = {
  primary: { text: "text-primary", bg: "bg-primary/10", border: "hover:border-primary/40" },
  tertiary: { text: "text-tertiary", bg: "bg-tertiary/10", border: "hover:border-tertiary/40" },
  destructive: {
    text: "text-destructive",
    bg: "bg-destructive/10",
    border: "hover:border-destructive/40",
  },
} as const;

function AttentionTile({
  icon: Icon,
  count,
  label,
  value,
  clearText,
  tone,
  onClick,
}: {
  icon: LucideIcon;
  count: number;
  label: string;
  value: number;
  // What the tile says when the count is zero. A bare "0" reads as missing
  // data; "Nothing is stuck" reads as an answer.
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
        <span
          className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl ${
            clear ? "bg-secondary/40 text-muted-foreground" : `${colors.bg} ${colors.text}`
          }`}
        >
          <Icon className="h-5 w-5" />
        </span>
        {clear ? null : (
          <ArrowRight
            className={`h-4 w-4 shrink-0 ${colors.text} opacity-0 transition group-hover:opacity-100`}
          />
        )}
      </div>

      <p
        className={`mt-4 text-3xl font-black leading-none tabular-nums ${
          clear ? "text-muted-foreground" : colors.text
        }`}
      >
        {clear ? "0" : count}
      </p>
      <p className="mt-2 text-sm font-bold text-foreground">{label}</p>
      <p className="mt-0.5 text-xs text-muted-foreground">
        {clear ? clearText : `${formatCurrency(value)} held up`}
      </p>
    </button>
  );
}

// ─── Stuck with whom ─────────────────────────────────────────────────────────

function StuckWithCard({
  stuck,
  onDrill,
}: {
  stuck: ReturnType<typeof stuckWithApprovers>;
  onDrill: (drilldown: ClaimDrilldown) => void;
}) {
  return (
    <section className={CARD}>
      <CardHead
        icon={UserX}
        title="Stuck with"
        meta={`> ${STALE_AFTER_DAYS} days`}
        tone="text-tertiary"
        toneBg="bg-tertiary/10"
      />
      <div className="space-y-3">
        {stuck.length === 0 ? (
          <EmptyState text={`Nothing has been waiting more than ${STALE_AFTER_DAYS} days.`} />
        ) : (
          <>
            <p className="px-1 text-xs text-muted-foreground">
              Late claims, grouped by the approver they are waiting on.
            </p>
            {stuck.map((group) => (
              <button
                key={group.approver}
                type="button"
                onClick={() =>
                  onDrill(
                    claimIdsDrilldown(
                      group.claimIds,
                      group.unassigned ? "Claims with no approver" : "Stuck with",
                      group.unassigned ? undefined : displayPerson(group.approver),
                    ),
                  )
                }
                className={`flex w-full items-center justify-between gap-3 text-left transition ${
                  group.unassigned
                    ? "rounded-2xl border border-destructive/20 bg-destructive/5 p-4 hover:border-destructive/40"
                    : `${TILE} hover:border-tertiary/40`
                }`}
              >
                <div className="min-w-0">
                  <p
                    className={`truncate text-sm font-bold ${
                      group.unassigned ? "text-destructive" : "text-foreground"
                    }`}
                  >
                    {group.unassigned ? group.approver : displayPerson(group.approver)}
                  </p>
                  <p className="mt-0.5 text-xs text-muted-foreground">
                    {group.claimIds.length} claim{group.claimIds.length === 1 ? "" : "s"} ·{" "}
                    {formatCurrency(group.amount)}
                    {group.unassigned ? " · nobody can approve these" : ""}
                  </p>
                </div>
                <div className="shrink-0 text-right">
                  <p
                    className={`text-base font-black tabular-nums ${
                      group.unassigned ? "text-destructive" : "text-tertiary"
                    }`}
                  >
                    {group.oldestDays}d
                  </p>
                  <p className={EYEBROW}>oldest</p>
                </div>
              </button>
            ))}
          </>
        )}
      </div>
    </section>
  );
}

// ─── Where the money is going ────────────────────────────────────────────────

function ProjectSpendCard({
  spend,
  onDrill,
}: {
  spend: ReturnType<typeof projectSpendThisMonth>;
  onDrill: (drilldown: ClaimDrilldown) => void;
}) {
  const total = spend.reduce((sum, row) => sum + row.total, 0);

  return (
    <section className={CARD}>
      <CardHead icon={TrendingUp} title="Where the money is going" meta="This month" />
      <div className="space-y-3">
        {spend.length === 0 ? (
          <EmptyState text="No claims submitted this month yet." />
        ) : (
          <>
            {spend.map((row) => {
              const share = total > 0 ? Math.round((row.total / total) * 100) : 0;
              return (
                <button
                  key={row.projectId ?? row.project}
                  type="button"
                  onClick={() => onDrill(projectSpendDrilldown(row.projectId, row.project))}
                  className={`block w-full text-left transition ${TILE} hover:border-primary/40`}
                >
                  <div className="flex items-baseline justify-between gap-3">
                    <p className="truncate text-sm font-bold text-foreground">{row.project}</p>
                    <p className="shrink-0 text-base font-black tabular-nums text-foreground">
                      {formatCurrency(row.total)}
                    </p>
                  </div>
                  <div className="mt-2 flex items-center justify-between gap-3">
                    <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-border/60">
                      <div
                        className={`h-full rounded-full ${
                          row.trend === "spike" ? "bg-destructive" : "bg-primary"
                        }`}
                        style={{ width: `${share}%` }}
                      />
                    </div>
                    <p className="shrink-0 text-xs text-muted-foreground">
                      {row.count} claim{row.count === 1 ? "" : "s"}
                    </p>
                  </div>
                  <div className="mt-2">
                    <TrendChip row={row} />
                  </div>
                </button>
              );
            })}
            <button
              type="button"
              onClick={() => onDrill(monthSpendDrilldown())}
              className="block w-full px-1 pt-1 text-left text-xs text-muted-foreground transition hover:text-foreground"
            >
              Total this month:{" "}
              <span className="font-semibold text-foreground underline decoration-border underline-offset-4">
                {formatCurrency(total)}
              </span>{" "}
              · compared against each project's average of the last 3 months.
            </button>
          </>
        )}
      </div>
    </section>
  );
}

// Answers "is that normal?" in place, so the admin doesn't have to remember
// last month's figure to read this one.
function TrendChip({ row }: { row: ReturnType<typeof projectSpendThisMonth>[number] }) {
  if (row.trend === "new") {
    return (
      <span className="inline-flex items-center rounded-full bg-surface-low px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
        First month of spend
      </span>
    );
  }

  const change = Math.round(row.changePct ?? 0);
  const styles =
    row.trend === "spike"
      ? "bg-destructive/10 text-destructive"
      : row.trend === "down"
        ? "bg-secondary text-secondary-foreground"
        : "bg-surface-low text-muted-foreground";

  const wording =
    row.trend === "spike"
      ? `${change > 0 ? "+" : ""}${change}% vs usual`
      : row.trend === "down"
        ? `${change}% vs usual`
        : "In line with usual";

  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] ${styles}`}
    >
      {wording}
      <span className="font-medium normal-case tracking-normal opacity-80">
        ({formatCurrency(row.baseline)}/mo)
      </span>
    </span>
  );
}

// ─── Approval trust ──────────────────────────────────────────────────────────

function ApprovalTrustCard({
  total,
  samples,
  onDrill,
}: {
  total: number;
  samples: NonNullable<AdminOverview["overturnedSupervisors"]>["samples"];
  onDrill: (drilldown: ClaimDrilldown) => void;
}) {
  return (
    <section className={CARD}>
      <CardHead
        icon={ShieldAlert}
        title="Approvals worth a second look"
        meta="Last 90 days"
        tone="text-tertiary"
        toneBg="bg-tertiary/10"
      />
      <div className="space-y-3">
        {samples.length === 0 ? (
          <EmptyState text="No first-line approval has been overturned recently." />
        ) : (
          <>
            <p className="px-1 text-xs text-muted-foreground">
              These approvers signed off on claims a later approver then rejected. That usually
              means the rule they were applying is unclear — read the claims before drawing a
              conclusion.
            </p>
            {samples.map((approver) => (
              <button
                key={approver.supervisorId}
                type="button"
                onClick={() =>
                  onDrill(
                    claimIdsDrilldown(
                      approver.claimIds,
                      "Overturned approvals",
                      displayPerson(approver.supervisorName),
                    ),
                  )
                }
                className={`flex w-full items-center justify-between gap-3 text-left transition ${TILE} hover:border-tertiary/40`}
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-foreground">
                    {displayPerson(approver.supervisorName)}
                  </p>
                  <p className="mt-0.5 text-xs text-muted-foreground">
                    across {approver.affectedEmployees}{" "}
                    {approver.affectedEmployees === 1 ? "person" : "people"} — read the claims
                  </p>
                </div>
                <div className="shrink-0 text-right">
                  <p className="text-base font-black tabular-nums text-tertiary">
                    {approver.overturnedCount}
                  </p>
                  <p className={EYEBROW}>overturned</p>
                </div>
              </button>
            ))}
            <button
              type="button"
              onClick={() => onDrill(overturnedDrilldown())}
              className="block w-full px-1 pt-1 text-left text-xs text-muted-foreground transition hover:text-foreground"
            >
              <span className="font-semibold text-foreground underline decoration-border underline-offset-4">
                {total}
              </span>{" "}
              first-line approval{total === 1 ? "" : "s"} overturned in the last 90 days.
            </button>
          </>
        )}
      </div>
    </section>
  );
}
