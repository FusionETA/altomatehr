import { useMemo } from "react";
import { ArrowRight, BanknoteArrowUp, CircleCheck, Clock, Wallet } from "lucide-react";
import type { Claim } from "@/features/claims/api";
import { formatCurrency } from "@/features/claims/lib/claim-formatters";
import {
  isSettledCompanySpend,
  readyToPayByEmployee,
  sumAmount,
} from "@/features/claims/lib/claim-insights";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { CARD, EYEBROW, TILE } from "../lib/dashboard-styles";
import { claimIdsDrilldown, readyToPayDrilldown, type ClaimDrilldown } from "../lib/claims-drilldown";
import { CardHead, EmptyState } from "./DashboardCard";

// What the org owes its own people, once approval is done and arguing is over.
//
// This is the one surface where a total IS the action — it is the payment about
// to be made. But it is grouped BY PERSON, because a reimbursement is one
// transfer to one employee, not one per claim, and ordered by who has been out
// of pocket longest: with the decision already made, waiting is just the
// company holding someone else's money.

// An approved claim nobody has paid after this long is its own problem.
const OVERDUE_DAYS = 7;

export function AdminClaimsReadyToPay({
  claims,
  employeeEmails,
  onDrill,
}: {
  claims: Claim[];
  employeeEmails: Map<string, string>;
  onDrill: (drilldown: ClaimDrilldown) => void;
}) {
  const payees = useMemo(() => readyToPayByEmployee(claims), [claims]);

  const owed = useMemo(() => payees.reduce((total, payee) => total + payee.amount, 0), [payees]);
  const claimCount = useMemo(
    () => payees.reduce((total, payee) => total + payee.claimIds.length, 0),
    [payees],
  );
  const overdue = useMemo(() => payees.filter((p) => p.waitingDays >= OVERDUE_DAYS), [payees]);

  // Named so an admin isn't left wondering where the company-card claims went.
  const companySpend = useMemo(() => claims.filter(isSettledCompanySpend), [claims]);

  const name = (employeeId: string) => {
    const email = employeeEmails.get(employeeId);
    return email ? buildName(email) : employeeId;
  };

  if (payees.length === 0) {
    return (
      <div className="space-y-6">
        <section className={CARD}>
          <CardHead
            icon={CircleCheck}
            title="Nothing waiting on payment"
            tone="text-secondary-foreground"
            toneBg="bg-secondary"
          />
          <EmptyState text="Every approved out-of-pocket claim has been settled. Nothing is owed." />
        </section>
        <CompanySpendNote claims={companySpend} />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* The amount owed, and — the part that actually needs a decision — how
          long the longest-waiting person has been carrying it. */}
      <section className={CARD}>
        <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex items-start gap-3">
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
              <Wallet className="h-5 w-5" />
            </span>
            <div>
              <p className={EYEBROW}>Owed to employees</p>
              <p className="mt-1 text-3xl font-black leading-none tabular-nums text-foreground">
                {formatCurrency(owed)}
              </p>
              <p className="mt-1.5 text-xs text-muted-foreground">
                {claimCount} claim{claimCount === 1 ? "" : "s"} · {payees.length}{" "}
                {payees.length === 1 ? "person" : "people"} · approved and unpaid
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => onDrill(readyToPayDrilldown())}
            className="inline-flex h-11 shrink-0 items-center gap-2 rounded-full bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90"
          >
            <BanknoteArrowUp className="h-4 w-4" />
            Review the run
          </button>
        </div>

        {overdue.length > 0 ? (
          <p className="mt-4 rounded-2xl border border-tertiary/25 bg-tertiary/5 px-4 py-3 text-xs font-semibold text-tertiary">
            {overdue.length} {overdue.length === 1 ? "person has" : "people have"} been waiting more
            than {OVERDUE_DAYS} days since their claim was approved — the decision is made, so this
            is the company holding their money.
          </p>
        ) : null}
      </section>

      <section className={CARD}>
        <CardHead
          icon={Clock}
          title="Who to pay"
          meta="Longest waiting first"
          tone="text-primary"
          toneBg="bg-primary/10"
        />
        <div className="space-y-3">
          {payees.map((payee) => {
            const late = payee.waitingDays >= OVERDUE_DAYS;
            return (
              <button
                key={payee.employeeId}
                type="button"
                onClick={() =>
                  onDrill(
                    claimIdsDrilldown(
                      payee.claimIds,
                      "Ready to pay",
                      name(payee.employeeId),
                    ),
                  )
                }
                className={`group flex w-full items-center justify-between gap-3 text-left transition ${TILE} hover:border-primary/40`}
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-foreground">
                    {name(payee.employeeId)}
                  </p>
                  <p className="truncate text-xs text-muted-foreground">
                    {employeeEmails.get(payee.employeeId) ?? payee.employeeId}
                  </p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {payee.claimIds.length} claim{payee.claimIds.length === 1 ? "" : "s"} ·{" "}
                    <span className={late ? "font-semibold text-tertiary" : ""}>
                      waiting {payee.waitingDays}d
                    </span>
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-3 text-right">
                  <div>
                    <p className="text-base font-black tabular-nums text-foreground">
                      {formatCurrency(payee.amount)}
                    </p>
                    <p className={EYEBROW}>to pay</p>
                  </div>
                  <ArrowRight className="h-4 w-4 text-primary opacity-0 transition group-hover:opacity-100" />
                </div>
              </button>
            );
          })}
        </div>
      </section>

      <CompanySpendNote claims={companySpend} />
    </div>
  );
}

// Company-paid claims are approved spend, but nobody is owed anything — the
// money already left a company account. Stated rather than silently dropped,
// so the figure above reads as complete.
function CompanySpendNote({ claims }: { claims: Claim[] }) {
  if (claims.length === 0) return null;

  return (
    <p className="px-1 text-xs text-muted-foreground">
      Not counted above: {claims.length} approved claim{claims.length === 1 ? "" : "s"} worth{" "}
      <span className="font-semibold text-foreground">{formatCurrency(sumAmount(claims))}</span>{" "}
      {claims.length === 1 ? "was" : "were"} paid on a company account, so {claims.length === 1 ? "it needs" : "they need"} no reimbursement.
    </p>
  );
}
