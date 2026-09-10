import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight, Users } from "lucide-react";
import {
  getLeaveTypes,
  getTeamLeaveBalances,
  type EmployeeLeaveBalances,
  type LeaveType,
} from "../api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";
const CURRENT_YEAR = new Date().getFullYear();
const YEAR_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1];

// Rows with no TeamId (a direct report not on any team the caller supervises)
// still need a group to sit in — this is that group's id.
const DIRECT_GROUP = "__direct__";


// A supervisor's team balances, one team at a time — mirrors Attendance's
// TeamPresence: a stepper over the caller's teams (no merged "everyone" tab,
// since a roster only means something scoped to one team), plus a "Direct
// reports" group for anyone reached only through the flat supervisor model.
export function TeamBalancesView() {
  const [year, setYear] = useState(CURRENT_YEAR);
  const [rows, setRows] = useState<EmployeeLeaveBalances[]>([]);
  const [types, setTypes] = useState<LeaveType[]>([]);
  const [error, setError] = useState<string | null>(null);
  // Empty until the first load names a group — there is no "all" option.
  const [groupId, setGroupId] = useState("");
  const [searchTerm, setSearchTerm] = useState("");

  // The key carries the year, matching the request path, so flipping between
  // years remembers each one instead of refetching every switch.
  const balancesQuery = useCachedQuery(`/leave/team/balances?year=${year}`, () =>
    getTeamLeaveBalances(year),
  );
  const typesQuery = useCachedQuery("/leave-types", getLeaveTypes);
  const loading = balancesQuery.loading || typesQuery.loading;

  useEffect(() => {
    if (balancesQuery.data) setRows(balancesQuery.data.data);
  }, [balancesQuery.data]);
  useEffect(() => {
    if (typesQuery.data) setTypes(typesQuery.data);
  }, [typesQuery.data]);
  useEffect(() => {
    setError(balancesQuery.error ?? typesQuery.error);
  }, [balancesQuery.error, typesQuery.error]);

  const activeTypes = useMemo(() => types.filter((t) => !t.isArchived), [types]);

  const groups = useMemo(() => {
    const seen = new Map<string, string>();
    for (const r of rows) {
      const id = r.teamId ?? DIRECT_GROUP;
      if (!seen.has(id)) seen.set(id, r.teamName ?? "Direct reports");
    }
    return [...seen]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [rows]);

  const currentIndex = Math.max(0, groups.findIndex((g) => g.id === groupId));
  const current = groups[currentIndex] ?? null;

  function step(delta: number) {
    if (groups.length === 0) return;
    setGroupId(groups[(currentIndex + delta + groups.length) % groups.length].id);
  }

  // Land on the first group, and recover if the one being viewed disappears
  // because a team was restructured between refreshes.
  useEffect(() => {
    if (groups.length > 0 && !groups.some((g) => g.id === groupId)) setGroupId(groups[0].id);
  }, [groups, groupId]);

  const inGroup = useMemo(
    () => rows.filter((r) => (r.teamId ?? DIRECT_GROUP) === groupId),
    [rows, groupId],
  );

  const filtered = useMemo(() => {
    const q = searchTerm.trim().toLowerCase();
    if (!q) return inGroup;
    return inGroup.filter((r) => [r.email, buildName(r.email)].join(" ").toLowerCase().includes(q));
  }, [inGroup, searchTerm]);

  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-black text-foreground">Team balances</h2>
        <select
          value={year}
          onChange={(e) => setYear(Number(e.target.value))}
          className="h-10 rounded-xl border border-border/70 bg-card px-3 text-sm font-semibold text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          {YEAR_OPTIONS.map((y) => (
            <option key={y} value={y}>
              {y}
            </option>
          ))}
        </select>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <SkeletonPanel />
      ) : rows.length === 0 ? (
        <section className={`${CARD} border-dashed bg-surface-low p-8 text-center`}>
          <Users className="mx-auto h-6 w-6 text-primary" />
          <p className="mt-3 text-sm font-medium text-foreground">
            You don't have a team below you yet.
          </p>
        </section>
      ) : (
        <>
          {/* The team being viewed IS the heading, same as Attendance's team
              presence — a "Your team" title above it would push the one piece
              of information that changes off the top. Stepper, not tabs: a
              supervisor on six sites can't fit six tabs on a phone. */}
          {current ? (
            <div className="flex items-center justify-between gap-2 rounded-2xl border border-border/60 bg-surface-low px-2 py-1.5">
              {groups.length > 1 ? (
                <button
                  type="button"
                  onClick={() => step(-1)}
                  aria-label="Previous team"
                  className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-card hover:text-foreground"
                >
                  <ChevronLeft className="h-5 w-5" />
                </button>
              ) : (
                <span className="h-9 w-9 shrink-0" aria-hidden />
              )}
              <div className="min-w-0 text-center">
                <p className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                  {groups.length > 1 ? `Team ${currentIndex + 1} / ${groups.length} · ` : ""}
                  {inGroup.length} {inGroup.length === 1 ? "person" : "people"}
                </p>
                <p className="truncate text-sm font-bold text-foreground">{current.name}</p>
              </div>
              {groups.length > 1 ? (
                <button
                  type="button"
                  onClick={() => step(1)}
                  aria-label="Next team"
                  className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-card hover:text-foreground"
                >
                  <ChevronRight className="h-5 w-5" />
                </button>
              ) : (
                <span className="h-9 w-9 shrink-0" aria-hidden />
              )}
            </div>
          ) : null}

          <SearchInput
            value={searchTerm}
            onChange={setSearchTerm}
            placeholder="Search by employee"
            className="max-w-sm"
          />

          {filtered.length === 0 ? (
            <section className={`${CARD} p-8 text-center`}>
              <p className="text-sm text-muted-foreground">
                {inGroup.length === 0
                  ? `No one on ${current?.name ?? "this team"}.`
                  : "No one matches this search."}
              </p>
            </section>
          ) : (
            <section className={`${CARD} !p-0`}>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[560px] caption-bottom text-sm">
                  <thead>
                    <tr className="border-b border-border/60">
                      <th className="h-12 px-6 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                        Employee
                      </th>
                      {activeTypes.map((t) => (
                        <th
                          key={t.id}
                          className="h-12 px-4 text-right text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground"
                        >
                          {t.code}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map((row) => (
                      <tr
                        key={`${row.teamId ?? "direct"}:${row.userId}`}
                        className="border-b border-border/60 last:border-0"
                      >
                        <td className="p-4 pl-6 align-middle">
                          <p className="font-bold text-foreground">{buildName(row.email)}</p>
                          <p className="text-xs text-muted-foreground">{row.email}</p>
                        </td>
                        {activeTypes.map((t) => {
                          const b = row.balances.find((x) => x.leaveTypeId === t.id);
                          return (
                            <td key={t.id} className="p-4 text-right align-middle tabular-nums">
                              {b ? (
                                <span
                                  className={b.remainingDays <= 0 ? "text-destructive" : "text-foreground"}
                                >
                                  {b.remainingDays}/{b.entitlementDays}
                                </span>
                              ) : (
                                <span className="text-muted-foreground">—</span>
                              )}
                            </td>
                          );
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}
    </div>
  );
}
