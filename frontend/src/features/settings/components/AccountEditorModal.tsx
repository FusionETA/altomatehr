import { useState } from "react";
import { LoaderCircle, X } from "lucide-react";
import { updateAccount, type ChartOfAccount } from "../api";

// Editor for the fields THIS APP owns on a chart-of-account row.
//
// Code, name and type belong to Xero once it is connected, so they are shown
// read-only rather than hidden — an admin needs to know which account they are
// configuring. Everything else is AltomateHR's: whether employees may code a
// claim to it, whether it accepts mileage and at what rate, and the spend limit
// that decides ExceedsLimit.
//
// Without this, connecting Xero left those settings unreachable: the create
// form is refused while Xero owns the chart, and a synced account arrives with
// mileage off, so mileage claims had no account to point at.

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-60";
const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";

export function AccountEditorModal({
  account,
  onClose,
  onSaved,
}: {
  account: ChartOfAccount;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [isSelectable, setIsSelectable] = useState(account.isSelectable);
  const [allowMileage, setAllowMileage] = useState(account.allowMileageClaim);
  const [mileageRate, setMileageRate] = useState(
    account.mileageRate === null || account.mileageRate === undefined
      ? ""
      : String(account.mileageRate),
  );
  const [limit, setLimit] = useState(
    account.limitAmount === null || account.limitAmount === undefined
      ? ""
      : String(account.limitAmount),
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fromXero = !!account.xeroAccountId;

  async function save() {
    setSaving(true);
    setError(null);
    try {
      await updateAccount(account.id, {
        // Passed back unchanged — Xero owns these, and the API expects the
        // whole shape.
        code: account.code,
        name: account.name,
        type: account.type,
        isSelectable,
        allowMileageClaim: allowMileage,
        // Blank means "no limit" / "use the org default rate", which is a
        // meaningful choice, so it clears rather than sending 0.
        limitAmount: limit.trim() === "" ? null : Number(limit),
        mileageRate: !allowMileage || mileageRate.trim() === "" ? null : Number(mileageRate),
      });
      onSaved();
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not save the account.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm">
      <section className="nice-scrollbar max-h-[90vh] w-full max-w-[520px] overflow-y-auto rounded-[26px] border border-white/40 bg-card p-6 shadow-[0_18px_48px_rgba(76,26,134,0.16)]">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Account settings
            </p>
            <h3 className="mt-1 truncate text-xl font-black text-foreground">
              {account.code ? `${account.code} · ` : ""}
              {account.name}
            </h3>
            <p className="mt-1 text-xs text-muted-foreground">
              {fromXero
                ? "Code and name come from Xero. The settings below are this app's."
                : "A local account — not linked to Xero."}
            </p>
          </div>
          <button
            type="button"
            aria-label="Close account settings"
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 space-y-4">
          <label className="flex items-start gap-3">
            <input
              type="checkbox"
              checked={isSelectable}
              onChange={(event) => setIsSelectable(event.target.checked)}
              className="mt-0.5 h-4 w-4 cursor-pointer accent-primary"
            />
            <span>
              <span className="block text-sm font-bold text-foreground">Selectable for claims</span>
              <span className="block text-xs text-muted-foreground">
                Employees can code an expense claim to this account.
              </span>
            </span>
          </label>

          <label className="flex items-start gap-3">
            <input
              type="checkbox"
              checked={allowMileage}
              onChange={(event) => setAllowMileage(event.target.checked)}
              className="mt-0.5 h-4 w-4 cursor-pointer accent-primary"
            />
            <span>
              <span className="block text-sm font-bold text-foreground">Allow mileage claims</span>
              <span className="block text-xs text-muted-foreground">
                Mileage claims can only be coded to an account with this on — so at least one
                needs it, or the mileage form has nothing to pick.
              </span>
            </span>
          </label>

          {allowMileage ? (
            <div className="space-y-1.5">
              <label className={LABEL}>Mileage rate (per km)</label>
              <input
                className={INPUT}
                type="number"
                step="0.01"
                min="0"
                value={mileageRate}
                onChange={(event) => setMileageRate(event.target.value)}
                placeholder="Leave blank to use the org default"
              />
            </div>
          ) : null}

          <div className="space-y-1.5">
            <label className={LABEL}>Spend limit (optional)</label>
            <input
              className={INPUT}
              type="number"
              step="0.01"
              min="0"
              value={limit}
              onChange={(event) => setLimit(event.target.value)}
              placeholder="No limit"
            />
            <p className="text-xs text-muted-foreground">
              A claim above this is flagged over-limit — a caution for the approver, not a refusal.
            </p>
          </div>
        </div>

        {error ? (
          <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive">
            {error}
          </p>
        ) : null}

        <div className="mt-6 grid grid-cols-2 gap-3">
          <button
            type="button"
            disabled={saving}
            onClick={onClose}
            className="h-12 rounded-[18px] border border-border/70 bg-card text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={saving}
            onClick={save}
            className="inline-flex h-12 items-center justify-center gap-2 rounded-[18px] bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Save
          </button>
        </div>
      </section>
    </div>
  );
}
