import { useEffect, useState } from "react";
import { LoaderCircle, Plus, Trash2 } from "lucide-react";
import {
  archiveOvertimeType,
  createOvertimeType,
  getOvertimeTypes,
  OVERTIME_TYPES_PATH,
  updateOvertimeType,
  type OvertimeType,
} from "@/features/policies/api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-10 rounded-2xl border border-border bg-white/80 px-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";

type Draft = { name: string; multiplier: string };

// The company's overtime types, each × the hourly rate. Three are built in —
// approved overtime lands in them by the calendar — and can be renamed or
// re-rated; the rest are the company's own, which an employee picks on an
// overtime request. A policy can pay any type at its own rate.
export function OvertimeTypesSettings() {
  const query = useCachedQuery(OVERTIME_TYPES_PATH, getOvertimeTypes);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [adding, setAdding] = useState<Draft>({ name: "", multiplier: "" });
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const types = query.data ?? [];

  // Seed the inputs from what's saved, whenever it changes.
  useEffect(() => {
    setDrafts(
      Object.fromEntries(
        (query.data ?? []).map((t) => [t.id, { name: t.name, multiplier: String(t.multiplier) }]),
      ),
    );
  }, [query.data]);

  async function run(key: string, action: () => Promise<unknown>) {
    setBusy(key);
    setError(null);
    try {
      await action();
      await query.refresh();
      return true;
    } catch (err) {
      setError(err instanceof Error ? err.message : "That did not go through.");
      return false;
    } finally {
      setBusy(null);
    }
  }

  const changed = (t: OvertimeType) => {
    const d = drafts[t.id];
    return d !== undefined && (d.name.trim() !== t.name || Number(d.multiplier) !== t.multiplier);
  };
  const valid = (d: Draft) =>
    d.name.trim().length > 0 && d.multiplier.trim() !== "" && Number(d.multiplier) >= 0 && Number(d.multiplier) <= 99.99;

  return (
    <div className={`${CARD} space-y-4`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Overtime types</h2>
        <p className="text-sm text-muted-foreground">
          What overtime pays, × the hourly rate. Normal day, rest day and public holiday are filled
          automatically by the calendar. Add your own (e.g. night shift) for staff to pick when they
          request overtime. Each policy can set its own rate for any type.
        </p>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {query.loading ? (
        <SkeletonPanel />
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {types.map((t) => {
            const d = drafts[t.id] ?? { name: t.name, multiplier: String(t.multiplier) };
            const builtIn = t.kind !== "CUSTOM";
            return (
              <li key={t.id} className="flex flex-wrap items-center gap-2 px-4 py-3">
                <input
                  aria-label="Name"
                  maxLength={60}
                  className={`${INPUT} min-w-0 flex-1`}
                  value={d.name}
                  onChange={(e) => setDrafts((cur) => ({ ...cur, [t.id]: { ...d, name: e.target.value } }))}
                />
                <label className="flex items-center gap-1.5 text-sm text-muted-foreground">
                  <input
                    aria-label={`${t.name} multiplier`}
                    type="number"
                    min="0"
                    max="99.99"
                    step="0.05"
                    className={`${INPUT} w-24`}
                    value={d.multiplier}
                    onChange={(e) =>
                      setDrafts((cur) => ({ ...cur, [t.id]: { ...d, multiplier: e.target.value } }))
                    }
                  />
                  ×
                </label>
                {builtIn ? (
                  <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.12em] text-muted-foreground">
                    Built in
                  </span>
                ) : null}
                {changed(t) ? (
                  <button
                    type="button"
                    disabled={busy !== null || !valid(d)}
                    onClick={() =>
                      void run(t.id, () =>
                        updateOvertimeType(t.id, { name: d.name.trim(), multiplier: Number(d.multiplier) }),
                      )
                    }
                    className="inline-flex items-center gap-1.5 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
                  >
                    {busy === t.id ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
                    Save
                  </button>
                ) : null}
                {!builtIn ? (
                  <button
                    type="button"
                    aria-label={`Remove ${t.name}`}
                    title="Remove — hours already approved for it are still paid"
                    disabled={busy !== null}
                    onClick={() => void run(`archive-${t.id}`, () => archiveOvertimeType(t.id))}
                    className="rounded-full p-2 text-muted-foreground transition hover:bg-destructive/10 hover:text-destructive disabled:opacity-50"
                  >
                    {busy === `archive-${t.id}` ? (
                      <LoaderCircle className="h-4 w-4 animate-spin" />
                    ) : (
                      <Trash2 className="h-4 w-4" />
                    )}
                  </button>
                ) : null}
              </li>
            );
          })}

          <li className="flex flex-wrap items-center gap-2 bg-muted/30 px-4 py-3">
            <input
              aria-label="New type name"
              maxLength={60}
              placeholder="New type, e.g. Night shift"
              className={`${INPUT} min-w-0 flex-1`}
              value={adding.name}
              onChange={(e) => setAdding((a) => ({ ...a, name: e.target.value }))}
            />
            <label className="flex items-center gap-1.5 text-sm text-muted-foreground">
              <input
                aria-label="New type multiplier"
                type="number"
                min="0"
                max="99.99"
                step="0.05"
                placeholder="1.5"
                className={`${INPUT} w-24`}
                value={adding.multiplier}
                onChange={(e) => setAdding((a) => ({ ...a, multiplier: e.target.value }))}
              />
              ×
            </label>
            <button
              type="button"
              disabled={busy !== null || !valid(adding)}
              onClick={async () => {
                const ok = await run("add", () =>
                  createOvertimeType({ name: adding.name.trim(), multiplier: Number(adding.multiplier) }),
                );
                if (ok) setAdding({ name: "", multiplier: "" });
              }}
              className="inline-flex items-center gap-1.5 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
            >
              {busy === "add" ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Plus className="h-3.5 w-3.5" />}
              Add
            </button>
          </li>
        </ul>
      )}
    </div>
  );
}
