import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  getXeroTrackingCategories,
  XERO_ACCRUAL_SLOTS,
  XERO_EXPENSE_SLOTS,
  type PayrollXeroMapping,
  type XeroAggregationMode,
  type XeroTrackingCategory,
} from "../../api";
import { getAccountsWithLiabilities, type ChartOfAccount } from "@/features/settings/api";
import { CARD, HINT, LABEL, WARN_PANEL } from "../../lib/ui";
import { SkeletonPanel } from "@/shared/components/Skeleton";
import { PayrollSelect } from "../PayrollSelect";

// How an approved run lands in Xero as one manual journal.
//
// Debits are the expense side — what the month cost the P&L. Credits are the
// accruals: one liability per agency, always summed, because KWSP is owed
// one figure and not one per employee.
//
// Nothing here is required to run payroll. It only matters once the org
// turns on "post the journal when a run is approved", and the sync refuses
// rather than guessing when an account a given run needs is unmapped.
export function XeroSyncSection({
  mapping,
  onChange,
}: {
  mapping: PayrollXeroMapping;
  onChange: (next: PayrollXeroMapping) => void;
}) {
  // Cached. Both degrade to empty rather than taking the section down: the
  // chart of accounts is local, the categories need Xero reachable, and an
  // admin should still be able to set the aggregation mode either way.
  //
  // Read fresh on every mount, this section replaced itself with a skeleton
  // each time the settings page was opened — the same dropdowns, redrawn.
  const accountsQuery = useCachedQuery("/accounts?includeLiabilities=true", () =>
    getAccountsWithLiabilities().catch(() => [] as ChartOfAccount[]),
  );
  const categoriesQuery = useCachedQuery("/payroll/runs/xero/tracking-categories", () =>
    getXeroTrackingCategories().catch(() => [] as XeroTrackingCategory[]),
  );

  const accounts = accountsQuery.data ?? [];
  const categories = categoriesQuery.data ?? [];
  const loading = accountsQuery.loading || categoriesQuery.loading;

  const setAccount = (slot: string, value: string | null) =>
    onChange({ ...mapping, accounts: { ...mapping.accounts, [slot]: value } });

  if (loading) {
    return (
      <div className="space-y-5">
        <SkeletonPanel />
        <SkeletonPanel />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {accounts.length === 0 ? (
        <div className={WARN_PANEL}>
          No chart of accounts has been synced from Xero yet, so there is nothing to map
          to. Connect Xero and sync the accounts under System Settings → Accounts first.
        </div>
      ) : null}

      <section className={CARD}>
        <header className="mb-4">
          <h3 className="text-[15px] font-semibold text-foreground">Aggregation</h3>
          <p className={HINT}>
            How the expense lines roll up on the journal. Accruals are always summed
            whichever is chosen — they are one liability per agency, not one per person.
          </p>
        </header>

        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label className={LABEL} htmlFor="xeroAggregation">
              Expense line aggregation
            </label>
            <PayrollSelect
              id="xeroAggregation"
              value={mapping.aggregationMode}
              onChange={(next) =>
                onChange({
                  ...mapping,
                  aggregationMode: (next ?? "PER_EMPLOYEE") as XeroAggregationMode,
                })
              }
              options={[
                { value: "PER_EMPLOYEE", label: "One line per employee" },
                { value: "SUM_BY_PROJECT", label: "Sum by project" },
              ]}
            />
            <p className={HINT}>
              Per employee is what an accountant reading the journal usually wants. Sum by
              project suits a P&amp;L organised by project rather than by head.
            </p>
          </div>

          <div>
            <label className={LABEL} htmlFor="xeroTracking">
              Tracking category (for project)
            </label>
            <PayrollSelect
              id="xeroTracking"
              value={mapping.trackingCategoryId}
              disabled={categories.length === 0}
              emptyLabel="No tracking"
              onChange={(trackingCategoryId) => onChange({ ...mapping, trackingCategoryId })}
              options={categories.map((category) => ({
                value: category.trackingCategoryId,
                label: `${category.name} (${category.options.length} options)`,
              }))}
            />
            <p className={HINT}>
              {categories.length === 0
                ? "Xero is not connected, or it has no tracking categories — lines will carry no tracking."
                : "Each journal line is stamped with the project name as this category's value."}
            </p>
          </div>
        </div>
      </section>

      <AccountGroup
        title="Expense accounts"
        subtitle="The debit side. Charged to the P&L when a run posts."
        slots={XERO_EXPENSE_SLOTS}
        accountType="EXPENSE"
        mapping={mapping}
        accounts={accounts}
        onPick={setAccount}
      />

      <AccountGroup
        title="Accrual accounts"
        subtitle="The credit side — what is owed until it is paid out. One line per agency, always summed."
        slots={XERO_ACCRUAL_SLOTS}
        accountType="LIABILITY"
        mapping={mapping}
        accounts={accounts}
        onPick={setAccount}
      />
    </div>
  );
}

// Which slots take which kind of account. A payable on the credit side has to
// be a liability — an expense account there turns what the company OWES into a
// reduction of its costs.
type SlotAccountType = "EXPENSE" | "LIABILITY";

const TYPE_NOUN: Record<SlotAccountType, string> = {
  EXPENSE: "an expense account",
  LIABILITY: "a liability account",
};

function AccountGroup({
  title,
  subtitle,
  slots,
  accountType,
  mapping,
  accounts,
  onPick,
}: {
  title: string;
  subtitle: string;
  slots: readonly { key: string; label: string }[];
  accountType: SlotAccountType;
  mapping: PayrollXeroMapping;
  accounts: ChartOfAccount[];
  onPick: (slot: string, value: string | null) => void;
}) {
  const offered = accounts.filter(
    (account) => account.type === accountType && !account.isArchived,
  );

  return (
    <section className={CARD}>
      <header className="mb-4">
        <h3 className="text-[15px] font-semibold text-foreground">{title}</h3>
        <p className={HINT}>{subtitle}</p>
      </header>

      {accounts.length > 0 && offered.length === 0 ? (
        <div className={`${WARN_PANEL} mb-4`}>
          {accountType === "LIABILITY"
            ? "No liability accounts have been synced from Xero yet. Sync the accounts under System Settings → Accounts to pick the payables."
            : "No expense accounts have been synced from Xero yet. Sync the accounts under System Settings → Accounts first."}
        </div>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2">
        {slots.map((slot) => {
          const selected = mapping.accounts[slot.key] ?? null;
          // Only accounts of the slot's type that can still take a journal
          // line are offered. The one already picked stays visible whatever
          // it is — archived (inactive in Xero, or retired because the
          // connected org no longer has it) or the wrong type (saved before
          // this filter existed) — and says so. Hiding it would render the
          // slot as "Not mapped" while the journal posts to it anyway.
          const current = accounts.find((account) => account.id === selected);
          const options =
            current && !offered.includes(current) ? [current, ...offered] : offered;
          const selectedArchived = current?.isArchived ?? false;
          const selectedWrongType = current != null && current.type !== accountType;

          return (
            <div key={slot.key}>
              <label className={LABEL} htmlFor={`xero-${slot.key}`}>
                {slot.label}
              </label>
              {/* Unset is a real state, not an error: an org with no HRDF never
                  needs the HRDF slots, and the sync only requires the accounts a
                  given run actually uses. */}
              <PayrollSelect
                id={`xero-${slot.key}`}
                value={selected}
                emptyLabel="Not mapped"
                onChange={(next) => onPick(slot.key, next)}
                options={options.map((account) => ({
                  value: account.id,
                  // An account with no code must not render as "· Name" — the
                  // separator only earns its place between two things.
                  label:
                    [account.code, account.name].filter(Boolean).join(" · ") +
                    (account.isArchived ? " (archived)" : ""),
                }))}
              />
              {selectedArchived ? (
                <p className="mt-1.5 text-xs text-destructive">
                  Archived or no longer in Xero — the journal can't post to it. Pick a
                  current account.
                </p>
              ) : selectedWrongType ? (
                <p className="mt-1.5 text-xs text-destructive">
                  Not {TYPE_NOUN[accountType]}. Pick {TYPE_NOUN[accountType]} for this line.
                </p>
              ) : null}
            </div>
          );
        })}
      </div>
    </section>
  );
}
