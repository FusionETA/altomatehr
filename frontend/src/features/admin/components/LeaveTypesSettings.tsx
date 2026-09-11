import { useEffect, useMemo, useState } from "react";
import { Info, LoaderCircle, Plus } from "lucide-react";
import {
  archiveLeaveType,
  createLeaveType,
  getLeaveTypes,
  restoreLeaveType,
  updateLeaveType,
  type LeaveAccrualMethod,
  type LeaveType,
} from "@/features/leave/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";
const TH = "h-11 px-3 text-left text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

// Carries the accrual/carry-forward fields through row edits even though this
// form has no controls for them — those live in the dedicated Annual leave
// card below, and saving here does a full replace, so dropping them would
// silently reset the Annual type's carry-forward settings.
type Draft = {
  code: string;
  name: string;
  paid: boolean;
  defaultDays: string;
  accrualMethod: LeaveAccrualMethod;
  carryForward: boolean;
  carryExpiryMonth: number | null;
  maxCarryForwardDays: number | null;
};
const emptyDraft: Draft = {
  code: "",
  name: "",
  paid: true,
  defaultDays: "0",
  accrualMethod: "LUMP_SUM",
  carryForward: false,
  carryExpiryMonth: null,
  maxCarryForwardDays: null,
};

export function LeaveTypesSettings() {
  const [types, setTypes] = useState<LeaveType[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<Draft>(emptyDraft);
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [edit, setEdit] = useState<Draft>(emptyDraft);
  const [savingEdit, setSavingEdit] = useState(false);

  const query = useCachedQuery("/leave-types", getLeaveTypes);
  const loading = query.loading;
  useEffect(() => {
    if (query.data) setTypes(query.data);
  }, [query.data]);
  useEffect(() => {
    if (query.error) setError(query.error);
  }, [query.error]);

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    if (!form.code.trim() || !form.name.trim()) return;
    setAdding(true);
    setError(null);
    try {
      const created = await createLeaveType({
        code: form.code.trim(),
        name: form.name.trim(),
        paid: form.paid,
        defaultDays: form.paid ? Number(form.defaultDays) || 0 : 0,
      });
      setTypes((cur) => [...cur, created]);
      setForm(emptyDraft);
    } catch (err) {
      setError(message(err, "Could not add the leave type."));
    } finally {
      setAdding(false);
    }
  }

  async function toggleArchive(type: LeaveType) {
    setBusyId(type.id);
    setError(null);
    try {
      const updated = type.isArchived
        ? await restoreLeaveType(type.id)
        : await archiveLeaveType(type.id);
      setTypes((cur) => cur.map((t) => (t.id === updated.id ? updated : t)));
    } catch (err) {
      setError(message(err, "Could not update the leave type."));
    } finally {
      setBusyId(null);
    }
  }

  function openEdit(type: LeaveType) {
    setEditingId(type.id);
    setEdit({
      code: type.code,
      name: type.name,
      paid: type.paid,
      defaultDays: String(type.defaultDays),
      accrualMethod: type.accrualMethod,
      carryForward: type.carryForward,
      carryExpiryMonth: type.carryExpiryMonth,
      maxCarryForwardDays: type.maxCarryForwardDays,
    });
    setError(null);
  }

  async function saveEdit(id: string) {
    if (!edit.code.trim() || !edit.name.trim()) return;
    setSavingEdit(true);
    setError(null);
    try {
      const updated = await updateLeaveType(id, {
        code: edit.code.trim(),
        name: edit.name.trim(),
        paid: edit.paid,
        defaultDays: edit.paid ? Number(edit.defaultDays) || 0 : 0,
        accrualMethod: edit.accrualMethod,
        carryForward: edit.carryForward,
        carryExpiryMonth: edit.carryExpiryMonth,
        maxCarryForwardDays: edit.maxCarryForwardDays,
      });
      setTypes((cur) => cur.map((t) => (t.id === updated.id ? updated : t)));
      setEditingId(null);
    } catch (err) {
      setError(message(err, "Could not save changes."));
    } finally {
      setSavingEdit(false);
    }
  }

  const annualType = useMemo(
    () => types.find((t) => t.code.trim().toUpperCase() === "ANNUAL"),
    [types],
  );

  return (
    <div className="space-y-5">
      <div className="flex items-start gap-3 rounded-2xl bg-surface-low p-4 text-sm text-muted-foreground">
        <Info className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
        <p>
          These days apply org-wide. To give a specific policy more or fewer days for a leave
          type, set an override under{" "}
          <span className="font-semibold text-foreground">System Settings → Policies</span>. To
          adjust one employee's entitlement instead, open them from the{" "}
          <span className="font-semibold text-foreground">Balances</span> tab.
        </p>
      </div>

      <form onSubmit={handleAdd} className={`${CARD} space-y-4`}>
        <div>
          <h2 className="text-lg font-black text-foreground">Leave types</h2>
          <p className="text-sm text-muted-foreground">
            Kinds of leave employees can apply for, with an annual entitlement.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <div className="space-y-1.5">
            <label className={LABEL}>Code</label>
            <input
              className={INPUT}
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value })}
              placeholder="AL"
            />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <label className={LABEL}>Name</label>
            <input
              className={INPUT}
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Annual Leave"
            />
          </div>
          <div className="space-y-1.5">
            <label className={LABEL}>Annual days</label>
            <input
              className={INPUT}
              type="number"
              min="0"
              step="0.5"
              disabled={!form.paid}
              value={form.paid ? form.defaultDays : "0"}
              onChange={(e) => setForm({ ...form, defaultDays: e.target.value })}
            />
          </div>
          <label className="flex items-center gap-2 self-end pb-3 text-sm font-medium text-foreground sm:col-span-2">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-border accent-primary"
              checked={form.paid}
              onChange={(e) => setForm({ ...form, paid: e.target.checked })}
            />
            Paid leave
          </label>
        </div>

        {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

        <button
          type="submit"
          disabled={adding || !form.code.trim() || !form.name.trim()}
          className="inline-flex items-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
        >
          {adding ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Add leave type
        </button>
      </form>

      <div className={CARD}>
        {loading ? (
          <SkeletonPanel />
        ) : types.length === 0 ? (
          <p className="text-sm text-muted-foreground">No leave types yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[560px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  <th className={TH}>Code</th>
                  <th className={TH}>Name</th>
                  <th className={TH}>Paid</th>
                  <th className={TH}>Annual days</th>
                  <th className="h-11 px-3" />
                </tr>
              </thead>
              <tbody>
                {types.map((type) =>
                  editingId === type.id ? (
                    <tr key={type.id} className="border-b border-border/60">
                      <td className="px-3 py-2">
                        <input
                          className={INPUT}
                          value={edit.code}
                          onChange={(e) => setEdit({ ...edit, code: e.target.value })}
                        />
                      </td>
                      <td className="px-3 py-2">
                        <input
                          className={INPUT}
                          value={edit.name}
                          onChange={(e) => setEdit({ ...edit, name: e.target.value })}
                        />
                      </td>
                      <td className="px-3 py-2">
                        <input
                          type="checkbox"
                          className="h-4 w-4 rounded border-border accent-primary"
                          checked={edit.paid}
                          onChange={(e) => setEdit({ ...edit, paid: e.target.checked })}
                        />
                      </td>
                      <td className="px-3 py-2">
                        <input
                          className={INPUT}
                          type="number"
                          min="0"
                          step="0.5"
                          disabled={!edit.paid}
                          value={edit.paid ? edit.defaultDays : "0"}
                          onChange={(e) => setEdit({ ...edit, defaultDays: e.target.value })}
                        />
                      </td>
                      <td className="px-3 py-2 text-right">
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            onClick={() => setEditingId(null)}
                            className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground hover:text-foreground"
                          >
                            Cancel
                          </button>
                          <button
                            type="button"
                            disabled={savingEdit}
                            onClick={() => saveEdit(type.id)}
                            className="inline-flex items-center gap-1 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground hover:opacity-90 disabled:opacity-50"
                          >
                            {savingEdit ? <LoaderCircle className="h-3 w-3 animate-spin" /> : null}
                            Save
                          </button>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    <tr key={type.id} className="border-b border-border/60">
                      <td className="px-3 py-3 font-mono text-xs">{type.code}</td>
                      <td
                        className={`px-3 py-3 font-semibold ${
                          type.isArchived ? "text-muted-foreground line-through" : "text-foreground"
                        }`}
                      >
                        {type.name}
                      </td>
                      <td className="px-3 py-3 text-xs text-muted-foreground">
                        {type.paid ? "Paid" : "Unpaid"}
                      </td>
                      <td className="px-3 py-3">{type.paid ? type.defaultDays : "—"}</td>
                      <td className="px-3 py-3 text-right">
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            onClick={() => openEdit(type)}
                            className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground hover:text-foreground"
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            disabled={busyId === type.id}
                            onClick={() => toggleArchive(type)}
                            className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground hover:text-foreground disabled:opacity-50"
                          >
                            {type.isArchived ? "Restore" : "Archive"}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {annualType ? (
        <AnnualLeaveCard
          type={annualType}
          onSaved={(updated) => setTypes((cur) => cur.map((t) => (t.id === updated.id ? updated : t)))}
        />
      ) : null}
    </div>
  );
}

// Accrual method and carry-forward only ever apply to the ANNUAL type — the
// backend rejects them for anything else — so they get their own card instead
// of cluttering every row's edit form with fields that are almost always
// inapplicable.
function AnnualLeaveCard({
  type,
  onSaved,
}: {
  type: LeaveType;
  onSaved: (updated: LeaveType) => void;
}) {
  const [accrualMethod, setAccrualMethod] = useState<LeaveAccrualMethod>(type.accrualMethod);
  const [carryForward, setCarryForward] = useState(type.carryForward);
  const [carryExpiryMonth, setCarryExpiryMonth] = useState(
    type.carryExpiryMonth != null ? String(type.carryExpiryMonth) : "",
  );
  const [maxCarryForwardDays, setMaxCarryForwardDays] = useState(
    type.maxCarryForwardDays != null ? String(type.maxCarryForwardDays) : "",
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Re-sync if a different Annual type loads (e.g. after archiving one and
  // creating another) — not on every keystroke, since `type` is a stable
  // reference between saves.
  useEffect(() => {
    setAccrualMethod(type.accrualMethod);
    setCarryForward(type.carryForward);
    setCarryExpiryMonth(type.carryExpiryMonth != null ? String(type.carryExpiryMonth) : "");
    setMaxCarryForwardDays(type.maxCarryForwardDays != null ? String(type.maxCarryForwardDays) : "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [type.id]);

  async function handleSave() {
    setError(null);
    if (carryForward) {
      const month = Number(carryExpiryMonth);
      if (!carryExpiryMonth || month < 1 || month > 12) {
        setError("Carry-forward requires an expiry month (1-12).");
        return;
      }
    }
    setSaving(true);
    try {
      const updated = await updateLeaveType(type.id, {
        code: type.code,
        name: type.name,
        paid: type.paid,
        defaultDays: type.defaultDays,
        accrualMethod,
        carryForward,
        carryExpiryMonth: carryForward ? Number(carryExpiryMonth) : null,
        maxCarryForwardDays: maxCarryForwardDays.trim() ? Number(maxCarryForwardDays) : null,
      });
      onSaved(updated);
    } catch (err) {
      setError(message(err, "Could not save these settings."));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className={`${CARD} space-y-4`}>
      <div>
        <h2 className="text-lg font-black text-foreground">
          {type.name} — accrual &amp; carry-forward
        </h2>
        <p className="text-sm text-muted-foreground">
          These only apply to {type.name} ({type.code}) — every other leave type is granted in
          full and doesn't roll over.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <div className="space-y-1.5">
          <label className={LABEL}>Accrual method</label>
          <Select
            value={accrualMethod}
            onValueChange={(v) => setAccrualMethod(v as LeaveAccrualMethod)}
          >
            <SelectTrigger className={INPUT}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="LUMP_SUM">Lump sum (all available at year start)</SelectItem>
              <SelectItem value="PRO_RATED">Pro-rated (entitlement / 12 each month)</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-1.5">
          <label className={LABEL}>Carry forward</label>
          <Select
            value={carryForward ? "yes" : "no"}
            onValueChange={(v) => setCarryForward(v === "yes")}
          >
            <SelectTrigger className={INPUT}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="no">No</SelectItem>
              <SelectItem value="yes">Yes</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div />

        <div className="space-y-1.5">
          <label className={LABEL}>Expiry month (1–12)</label>
          <input
            className={INPUT}
            type="number"
            min="1"
            max="12"
            disabled={!carryForward}
            value={carryExpiryMonth}
            onChange={(e) => setCarryExpiryMonth(e.target.value)}
            placeholder="e.g. 3"
          />
          <p className="text-xs text-muted-foreground">
            Carried days expire at the start of this month next year.
          </p>
        </div>

        <div className="space-y-1.5">
          <label className={LABEL}>Max carry-forward days</label>
          <input
            className={INPUT}
            type="number"
            min="0"
            step="0.5"
            disabled={!carryForward}
            value={maxCarryForwardDays}
            onChange={(e) => setMaxCarryForwardDays(e.target.value)}
            placeholder="Uncapped"
          />
        </div>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      <button
        type="button"
        disabled={saving}
        onClick={handleSave}
        className="inline-flex items-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
      >
        {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
        Save settings
      </button>
    </div>
  );
}
