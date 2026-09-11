import { useEffect, useState } from "react";
import {
  getXeroTrackingCategories,
  XERO_ACCRUAL_SLOTS,
  XERO_EXPENSE_SLOTS,
  type PayrollXeroMapping,
  type XeroAggregationMode,
  type XeroTrackingCategory,
} from "../../api";
import { getAccounts, type ChartOfAccount } from "@/features/settings/api";
import { CARD, HINT, LABEL, NOTE_PANEL, WARN_PANEL } from "../../lib/ui";
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
  const [accounts, setAccounts] = useState<ChartOfAccount[]>([]);
  const [categories, setCategories] = useState<XeroTrackingCategory[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let live = true;

    // Both degrade to empty rather than taking the section down: the chart
    // of accounts is local, the categories need Xero reachable, and an admin
    // should still be able to set the aggregation mode either way.
    Promise.all([
      getAccounts().catch(() => [] as ChartOfAccount[]),
      getXeroTrackingCategories().catch(() => [] as XeroTrackingCategory[]),
    ])
      .then(([nextAccounts, nextCategories]) => {
        if (!live) return;
        setAccounts(nextAccounts);
        setCategories(nextCategories);
      })
      .finally(() => live && setLoading(false));

    return () => {
      live = false;
    };
  }, []);

  const setAccount = (slot: string, value: string | null) =>
    onChange({ ...mapping, accounts: { ...mapping.accounts, [slot]: value } });

  if (loading) return <p className={NOTE_PANEL}>Loading your Xero chart of accounts…</p>;

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
        mapping={mapping}
        accounts={accounts}
        onPick={setAccount}
      />

      <AccountGroup
        title="Accrual accounts"
        subtitle="The credit side — what is owed until it is paid out. One line per agency, always summed."
        slots={XERO_ACCRUAL_SLOTS}
        mapping={mapping}
        accounts={accounts}
        onPick={setAccount}
      />
    </div>
  );
}

function AccountGroup({
  title,
  subtitle,
  slots,
  mapping,
  accounts,
  onPick,
}: {
  title: string;
  subtitle: string;
  slots: readonly { key: string; label: string }[];
  mapping: PayrollXeroMapping;
  accounts: ChartOfAccount[];
  onPick: (slot: string, value: string | null) => void;
}) {
  return (
    <section className={CARD}>
      <header className="mb-4">
        <h3 className="text-[15px] font-semibold text-foreground">{title}</h3>
        <p className={HINT}>{subtitle}</p>
      </header>

      <div className="grid gap-4 sm:grid-cols-2">
        {slots.map((slot) => (
          <div key={slot.key}>
            <label className={LABEL} htmlFor={`xero-${slot.key}`}>
              {slot.label}
            </label>
            {/* Unset is a real state, not an error: an org with no HRDF never
                needs the HRDF slots, and the sync only requires the accounts a
                given run actually uses. */}
            <PayrollSelect
              id={`xero-${slot.key}`}
              value={mapping.accounts[slot.key] ?? null}
              emptyLabel="Not mapped"
              onChange={(next) => onPick(slot.key, next)}
              options={accounts.map((account) => ({
                value: account.id,
                // An account with no code must not render as "· Name" — the
                // separator only earns its place between two things.
                label: [account.code, account.name].filter(Boolean).join(" · "),
              }))}
            />
          </div>
        ))}
      </div>
    </section>
  );
}
