import { useState } from "react";
import { Check, LoaderCircle, Plus } from "lucide-react";
import { createLeaveType, type LeaveType } from "@/features/leave/api";
import { suggestLeaveTypeCode } from "@/features/leave/lib/leave-type-code";

const INPUT =
  "h-9 w-full rounded-xl border border-border bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50";

type Row = {
  name: string;
  rows: number;
  code: string;
  paid: boolean;
  days: string;
  state: "idle" | "saving" | "added";
  error: string | null;
};

// The leave types an import asked for that the org doesn't have, each with a
// one-line form to add it on the spot. The name is the file's own spelling, so
// re-importing the same file matches it; code, paid and annual days are the
// admin's call (they can be changed later under Leave → Types).
export function MissingLeaveTypes({
  missing,
  existing,
  onAdded,
}: {
  missing: { value: string; rows: number }[];
  existing: LeaveType[];
  onAdded: (type: LeaveType) => void;
}) {
  // The import matches a row on a type's name OR its code, so a code must not
  // spell another type's name or another value in this file — "CL" for
  // Childcare Leave would swallow the rows that meant a type called "CL".
  const reserved = (exceptName: string) =>
    new Set(
      [...existing.flatMap((t) => [t.code, t.name]), ...missing.map((m) => m.value)]
        .filter((v) => v.trim().toUpperCase() !== exceptName.trim().toUpperCase())
        .map((v) => v.trim().toUpperCase()),
    );

  const [rows, setRows] = useState<Row[]>(() => {
    const taken = [...existing.flatMap((t) => [t.code, t.name]), ...missing.map((m) => m.value)];
    return missing.map((m) => {
      const code = suggestLeaveTypeCode(m.value, taken);
      taken.push(code);
      return {
        name: m.value,
        rows: m.rows,
        code,
        paid: !/\bunpaid\b/i.test(m.value),
        days: "0",
        state: "idle",
        error: null,
      };
    });
  });

  const patch = (i: number, next: Partial<Row>) =>
    setRows((cur) => cur.map((r, j) => (j === i ? { ...r, ...next } : r)));

  async function add(i: number) {
    const row = rows[i];
    if (!row.code.trim()) return;
    const added = rows.filter((r, j) => j !== i && r.state === "added").map((r) => r.code.trim().toUpperCase());
    if (reserved(row.name).has(row.code.trim().toUpperCase()) || added.includes(row.code.trim().toUpperCase())) {
      patch(i, { error: `"${row.code.trim()}" is already a leave type's name or code, or another value in this file. Pick another code.` });
      return;
    }
    patch(i, { state: "saving", error: null });
    try {
      const created = await createLeaveType({
        code: row.code.trim(),
        name: row.name,
        paid: row.paid,
        defaultDays: row.paid ? Number(row.days) || 0 : 0,
      });
      patch(i, { state: "added" });
      onAdded(created);
    } catch (e) {
      patch(i, { state: "idle", error: e instanceof Error ? e.message : "Could not add the leave type." });
    }
  }

  return (
    <div className="mt-4 rounded-2xl border border-border/70 bg-card p-4">
      <p className="text-sm font-semibold text-foreground">Leave types not set up yet</p>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Add them here, then import the same file again. Rows already imported are skipped.
      </p>
      <ul className="mt-3 space-y-3">
        {rows.map((row, i) => (
          <li key={row.name} className="space-y-1.5">
            <p className="text-sm text-foreground">
              <span className="font-semibold">{row.name}</span>{" "}
              <span className="text-muted-foreground">
                · {row.rows} {row.rows === 1 ? "row" : "rows"}
              </span>
            </p>
            {row.state === "added" ? (
              <p className="inline-flex items-center gap-1.5 text-xs font-semibold text-primary">
                <Check className="h-3.5 w-3.5" /> Added as {row.code.trim()}
              </p>
            ) : (
              <div className="grid grid-cols-[5.5rem_5.5rem_auto_auto] items-center gap-2">
                <input
                  aria-label={`Code for ${row.name}`}
                  className={INPUT}
                  value={row.code}
                  maxLength={20}
                  disabled={row.state === "saving"}
                  onChange={(e) => patch(i, { code: e.target.value })}
                />
                <input
                  aria-label={`Annual days for ${row.name}`}
                  className={INPUT}
                  type="number"
                  min="0"
                  step="0.5"
                  disabled={!row.paid || row.state === "saving"}
                  value={row.paid ? row.days : "0"}
                  onChange={(e) => patch(i, { days: e.target.value })}
                />
                <label className="flex items-center gap-1.5 text-xs font-medium text-foreground">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-border accent-primary"
                    checked={row.paid}
                    disabled={row.state === "saving"}
                    onChange={(e) => patch(i, { paid: e.target.checked })}
                  />
                  Paid
                </label>
                <button
                  type="button"
                  disabled={row.state === "saving" || !row.code.trim()}
                  onClick={() => add(i)}
                  className="inline-flex h-9 items-center justify-center gap-1.5 rounded-xl bg-primary px-3 text-xs font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
                >
                  {row.state === "saving" ? (
                    <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Plus className="h-3.5 w-3.5" />
                  )}
                  Add
                </button>
              </div>
            )}
            {row.state !== "added" ? (
              <p className="text-[11px] text-muted-foreground">Code · annual days per person · paid</p>
            ) : null}
            {row.error ? <p className="text-xs font-semibold text-destructive">{row.error}</p> : null}
          </li>
        ))}
      </ul>
    </div>
  );
}
