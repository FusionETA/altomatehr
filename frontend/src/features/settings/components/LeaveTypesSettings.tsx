import { useEffect, useState } from "react";
import { LoaderCircle, Plus } from "lucide-react";
import {
  archiveLeaveType,
  createLeaveType,
  getLeaveTypes,
  restoreLeaveType,
  updateLeaveType,
  type LeaveType,
} from "@/features/leave/api";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground";
const TH = "h-11 px-3 text-left text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

type Draft = { code: string; name: string; paid: boolean; defaultDays: string };
const emptyDraft: Draft = { code: "", name: "", paid: true, defaultDays: "0" };

export function LeaveTypesSettings() {
  const [types, setTypes] = useState<LeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<Draft>(emptyDraft);
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [edit, setEdit] = useState<Draft>(emptyDraft);
  const [savingEdit, setSavingEdit] = useState(false);

  useEffect(() => {
    getLeaveTypes()
      .then(setTypes)
      .catch((e: unknown) => setError(message(e, "Could not load leave types.")))
      .finally(() => setLoading(false));
  }, []);

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
      });
      setTypes((cur) => cur.map((t) => (t.id === updated.id ? updated : t)));
      setEditingId(null);
    } catch (err) {
      setError(message(err, "Could not save changes."));
    } finally {
      setSavingEdit(false);
    }
  }

  return (
    <div className="space-y-5">
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
          <p className="text-sm text-muted-foreground">Loading leave types…</p>
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
    </div>
  );
}
