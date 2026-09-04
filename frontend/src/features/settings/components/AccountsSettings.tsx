import { useEffect, useState } from "react";
import { LoaderCircle, Plus, RefreshCw } from "lucide-react";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import {
  archiveAccount,
  createAccount,
  getAccounts,
  getXeroStatus,
  restoreAccount,
  syncXeroAccounts,
  type ChartOfAccount,
  type SaveAccount,
} from "../api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";
const TH = "h-11 px-3 text-left text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";

// Matches the claims table's page size, so both admin lists page the same way.
const ACCOUNTS_PER_PAGE = 10;

type AccountTab = "EXPENSE" | "BANK";

const emptyForm: SaveAccount = {
  code: "",
  name: "",
  type: "EXPENSE",
  isSelectable: true,
  limitAmount: null,
  allowMileageClaim: false,
  mileageRate: null,
};

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function AccountsSettings() {
  const [accounts, setAccounts] = useState<ChartOfAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<SaveAccount>(emptyForm);
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  // Null until known. While Xero is connected it owns the chart of accounts and
  // this screen mirrors it rather than authoring it.
  const [xeroConnected, setXeroConnected] = useState<boolean | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncNote, setSyncNote] = useState<string | null>(null);
  // Archived accounts are hidden by default. Syncing retires every Xero
  // account a claim cannot be coded to — revenue, receivables, equity — and a
  // list of struck-through "Sales" rows is noise on a screen whose job is
  // showing what a claim CAN be coded to. Still reachable, because archiving
  // has to be undoable.
  const [showArchived, setShowArchived] = useState(false);
  // Expense and bank accounts answer different questions — what a claim is
  // coded TO versus what company money is spent FROM — and a Xero org has far
  // more of the former, so they get their own lists.
  const [tab, setTab] = useState<AccountTab>("EXPENSE");
  const [page, setPage] = useState(1);

  useEffect(() => {
    getAccounts()
      .then(setAccounts)
      .catch((e: unknown) => setError(message(e, "Could not load accounts.")))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    getXeroStatus()
      .then((status) => setXeroConnected(status.connected))
      .catch(() => setXeroConnected(false));
  }, []);

  async function handleSyncFromXero() {
    setSyncing(true);
    setError(null);
    setSyncNote(null);
    try {
      const result = await syncXeroAccounts();
      setAccounts(await getAccounts());
      setSyncNote(
        `${result.imported} added · ${result.updated} updated · ${result.skipped} skipped`,
      );
    } catch (e) {
      setError(message(e, "Could not sync from Xero."));
    } finally {
      setSyncing(false);
    }
  }

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    if (!form.code.trim() || !form.name.trim()) return;
    setAdding(true);
    setError(null);
    try {
      const created = await createAccount({
        ...form,
        code: form.code.trim(),
        name: form.name.trim(),
        limitAmount: form.limitAmount ? Number(form.limitAmount) : null,
        mileageRate: form.allowMileageClaim && form.mileageRate ? Number(form.mileageRate) : null,
      });
      setAccounts((current) => [...current, created]);
      setForm(emptyForm);
    } catch (err) {
      setError(message(err, "Could not add the account."));
    } finally {
      setAdding(false);
    }
  }

  async function toggleArchive(account: ChartOfAccount) {
    setBusyId(account.id);
    setError(null);
    try {
      const updated = account.isArchived
        ? await restoreAccount(account.id)
        : await archiveAccount(account.id);
      setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a)));
    } catch (err) {
      setError(message(err, "Could not update the account."));
    } finally {
      setBusyId(null);
    }
  }

  const ofTab = accounts.filter((account) => account.type === tab);
  const archivedCount = ofTab.filter((account) => account.isArchived).length;
  const visible = showArchived ? ofTab : ofTab.filter((account) => !account.isArchived);

  // A Xero chart of accounts runs to dozens of expense codes; banks are a
  // handful, so only the long list is paged.
  const paged = tab === "EXPENSE"
    ? visible.slice((page - 1) * ACCOUNTS_PER_PAGE, page * ACCOUNTS_PER_PAGE)
    : visible;
  const totalPages = Math.max(1, Math.ceil(visible.length / ACCOUNTS_PER_PAGE));

  return (
    <div className="space-y-5">
      {/* Connected to Xero, Xero owns this list. The backend refuses hand-made
          accounts outright — this swaps the form for the only action that still
          makes sense, rather than leaving a form that can only 409. */}
      {xeroConnected ? (
        <section className={`${CARD} space-y-4`}>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-black text-foreground">Chart of Accounts</h2>
              <p className="text-sm text-muted-foreground">
                Xero owns these while it's connected. Add or rename an account in Xero, then sync.
              </p>
            </div>
            <button
              type="button"
              disabled={syncing}
              onClick={handleSyncFromXero}
              className="inline-flex h-11 shrink-0 items-center gap-2 rounded-2xl bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
            >
              {syncing ? (
                <LoaderCircle className="h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="h-4 w-4" />
              )}
              Sync from Xero
            </button>
          </div>

          {syncNote ? (
            <p className="rounded-2xl border border-border/60 bg-surface-low px-4 py-3 text-xs text-muted-foreground">
              {syncNote}
            </p>
          ) : null}

          <p className="text-xs text-muted-foreground">
            Spend limits and mileage settings stay editable below — those are this app's, not
            Xero's.
          </p>
        </section>
      ) : (
      <form onSubmit={handleAdd} className={`${CARD} space-y-4`}>
        <div>
          <h2 className="text-lg font-black text-foreground">Chart of Accounts</h2>
          <p className="text-sm text-muted-foreground">
            Accounts claims are posted to, with optional spend limits and mileage.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <label className={LABEL}>Code</label>
            <input
              className={INPUT}
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value })}
              placeholder="6100"
            />
          </div>
          <div className="space-y-1.5">
            <label className={LABEL}>Name</label>
            <input
              className={INPUT}
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Travel Expenses"
            />
          </div>
          <div className="space-y-1.5">
            <label className={LABEL}>Type</label>
            <Select value={form.type} onValueChange={(type) => setForm({ ...form, type })}>
              <SelectTrigger className="bg-card">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="EXPENSE">Expense</SelectItem>
                <SelectItem value="BANK">Bank</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <label className={LABEL}>Spend limit (optional)</label>
            <input
              className={INPUT}
              type="number"
              step="0.01"
              min="0"
              value={form.limitAmount ?? ""}
              onChange={(e) =>
                setForm({ ...form, limitAmount: e.target.value === "" ? null : Number(e.target.value) })
              }
              placeholder="No limit"
            />
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-5">
          <label className="inline-flex items-center gap-2 text-sm font-medium text-foreground">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-border accent-primary"
              checked={form.isSelectable}
              onChange={(e) => setForm({ ...form, isSelectable: e.target.checked })}
            />
            Selectable for claims
          </label>
          <label className="inline-flex items-center gap-2 text-sm font-medium text-foreground">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-border accent-primary"
              checked={form.allowMileageClaim}
              onChange={(e) => setForm({ ...form, allowMileageClaim: e.target.checked })}
            />
            Allow mileage
          </label>
          {form.allowMileageClaim ? (
            <input
              className={`${INPUT} max-w-[180px]`}
              type="number"
              step="0.0001"
              min="0"
              value={form.mileageRate ?? ""}
              onChange={(e) =>
                setForm({ ...form, mileageRate: e.target.value === "" ? null : Number(e.target.value) })
              }
              placeholder="Mileage rate"
            />
          ) : null}
        </div>

        {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

        <button
          type="submit"
          disabled={adding || !form.code.trim() || !form.name.trim()}
          className="inline-flex items-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
        >
          {adding ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Add account
        </button>
      </form>
      )}

      <OverflowTabList<AccountTab>
        items={[
          { id: "EXPENSE", label: "Expenses" },
          { id: "BANK", label: "Bank accounts" },
        ]}
        value={tab}
        onChange={(next) => {
          setTab(next);
          setPage(1);
          setShowArchived(false);
        }}
        variant="segmented"
        ariaLabel="Account type"
      />

      <div className={CARD}>
        {archivedCount > 0 ? (
          <div className="mb-3 flex justify-end">
            <button
              type="button"
              onClick={() => {
                setShowArchived((open) => !open);
                setPage(1);
              }}
              className="text-xs font-semibold text-muted-foreground transition hover:text-foreground"
            >
              {showArchived ? "Hide" : "Show"} {archivedCount} archived
            </button>
          </div>
        ) : null}

        {loading ? (
          <p className="text-sm text-muted-foreground">Loading accounts…</p>
        ) : visible.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {accounts.length === 0 ? "No accounts yet." : "No active accounts."}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  <th className={TH}>Code</th>
                  <th className={TH}>Name</th>
                  <th className={TH}>Limit</th>
                  <th className={TH}>Flags</th>
                  <th className="h-11 px-3" />
                </tr>
              </thead>
              <tbody>
                {paged.map((account) => (
                  <tr key={account.id} className="border-b border-border/60">
                    <td className="px-3 py-3 font-mono text-xs">{account.code}</td>
                    <td
                      className={`px-3 py-3 font-semibold ${
                        account.isArchived ? "text-muted-foreground line-through" : "text-foreground"
                      }`}
                    >
                      {account.name}
                    </td>
                    <td className="px-3 py-3">{account.limitAmount != null ? account.limitAmount : "—"}</td>
                    <td className="px-3 py-3 text-xs text-muted-foreground">
                      {[
                        account.isSelectable ? "selectable" : null,
                        account.allowMileageClaim ? "mileage" : null,
                      ]
                        .filter(Boolean)
                        .join(", ") || "—"}
                    </td>
                    <td className="px-3 py-3 text-right">
                      <button
                        type="button"
                        disabled={busyId === account.id}
                        onClick={() => toggleArchive(account)}
                        className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
                      >
                        {account.isArchived ? "Restore" : "Archive"}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {tab === "EXPENSE" && visible.length > ACCOUNTS_PER_PAGE ? (
          <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <p className="text-sm text-muted-foreground">
              Showing{" "}
              <span className="font-semibold text-foreground">
                {(page - 1) * ACCOUNTS_PER_PAGE + 1}-
                {Math.min(page * ACCOUNTS_PER_PAGE, visible.length)}
              </span>{" "}
              of <span className="font-semibold text-foreground">{visible.length}</span> accounts
            </p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={page === 1}
                onClick={() => setPage((current) => current - 1)}
                className="rounded-full px-3 py-2 text-sm font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:pointer-events-none disabled:opacity-45"
              >
                Previous
              </button>
              <span className="text-sm font-medium text-foreground">
                Page {page} of {totalPages}
              </span>
              <button
                type="button"
                disabled={page >= totalPages}
                onClick={() => setPage((current) => current + 1)}
                className="rounded-full px-3 py-2 text-sm font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:pointer-events-none disabled:opacity-45"
              >
                Next
              </button>
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}
