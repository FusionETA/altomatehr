import { useEffect, useMemo, useState } from "react";
import { ChevronDown, ChevronLeft, ChevronRight, Users } from "lucide-react";
import {
  getLeaveTypes,
  getTeamLeaveBalances,
  type LeaveBalance,
  type LeaveType,
} from "../api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { TablePager } from "@/shared/components/TablePager";
import { usePaged } from "@/shared/lib/use-paged";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";
const CURRENT_YEAR = new Date().getFullYear();
const YEAR_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1];

// Rows with no TeamId (a direct report not on any team the caller supervises)
// still need a group to sit in — this is that group's id.
const DIRECT_GROUP = "__direct__";

// A page of people. Cards collapse to a two-line summary now, so this fits a
// phone screen where the old expanded tile — seven bars each — did not. The
// pager stays: a 30-person team as one list is still a long scroll with no
// sense of where you are in it.
const PAGE_SIZE = 12;

// A supervisor's team balances, one team at a time — mirrors Attendance's
// TeamPresence: a stepper over the caller's teams (no merged "everyone" tab,
// since a roster only means something scoped to one team), plus a "Direct
// reports" group for anyone reached only through the flat supervisor model.
//
// One card per person rather than a row in a leave-type-per-column table. That
// table was as wide as the org's leave types and scrolled sideways on a phone:
// the names stayed put and every number sat off the right edge, so the screen
// showed who was on the team and nothing about their leave.
export function TeamBalancesView() {
  const [year, setYear] = useState(CURRENT_YEAR);
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

  // Read straight off the queries. Copying them into state through an effect
  // meant the first frame of a revisit was built from empty arrays even though
  // the answer was already cached — and nothing here mutates either list.
  const rows = useMemo(() => balancesQuery.data?.data ?? [], [balancesQuery.data]);
  const types = useMemo(() => typesQuery.data ?? [], [typesQuery.data]);

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

  const paged = usePaged(filtered, PAGE_SIZE);
  const { setPage } = paged;

  // Back to the first page whenever the list underneath changes. usePaged only
  // clamps, so stepping to a bigger team while on page 3 would land a third of
  // the way down a roster the supervisor has not seen the top of.
  useEffect(() => {
    setPage(0);
  }, [setPage, groupId, searchTerm, year]);

  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-black text-foreground">Team balances</h2>
        <Select value={String(year)} onValueChange={(next) => setYear(Number(next))}>
          <SelectTrigger className="h-10 w-28 rounded-xl">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {YEAR_OPTIONS.map((y) => (
              <SelectItem key={y} value={String(y)}>
                {y}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
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
            <section className={CARD}>
              {/* Two columns from `md` up: the same tiles read fine on a laptop,
                  and one layout is one thing to keep right. */}
              <div className="grid gap-3 px-4 py-4 sm:px-6 md:grid-cols-2">
                {paged.pageItems.map((row) => (
                  <EmployeeBalanceCard
                    key={`${row.teamId ?? "direct"}:${row.userId}`}
                    name={buildName(row.email)}
                    email={row.email}
                    balances={row.balances}
                    types={activeTypes}
                  />
                ))}
              </div>
              <TablePager paged={paged} noun="person" />
            </section>
          )}
        </>
      )}
    </div>
  );
}

// One person, collapsed to what a supervisor is actually scanning for.
//
// This used to render every leave type the person holds, always. The comment
// here assumed most people would hold two or three — but an org seeds an
// entitlement for ALL of them, so every card drew the full set. Four people
// came to twenty-eight rows, twenty-seven of them a full untouched bar, and
// the one number that mattered (10/14) was buried among identical ones.
//
// So the default is now a SUMMARY: the types with activity on them, which is
// the whole reason to open this screen. The per-type detail is still one click
// away, unchanged — it just isn't the first thing on the page.
function EmployeeBalanceCard({
  name,
  email,
  balances,
  types,
}: {
  name: string;
  email: string;
  balances: LeaveBalance[];
  types: LeaveType[];
}) {
  const [open, setOpen] = useState(false);
  const [showAll, setShowAll] = useState(false);

  // Driven by the type list, not by the person's own balances, so the types
  // that do show keep the same order on every tile.
  const cells = useMemo(
    () =>
      types.map((type) => {
        const balance = balances.find((b) => b.leaveTypeId === type.id) ?? null;
        return {
          type,
          balance,
          // "Holds it" means there is something to look at: an entitlement, days
          // already taken, or — the one an approver must never have hidden —
          // days waiting on a decision.
          held:
            balance != null &&
            (balance.entitlementDays > 0 || balance.takenDays > 0 || balance.pendingDays > 0),
        };
      }),
    [types, balances],
  );

  // What the collapsed card leads with: only types this person has actually
  // moved. A full entitlement nobody has touched is the default state of every
  // type for every person, so it carries no information at this zoom level.
  const active = useMemo(
    () =>
      cells.filter(
        ({ balance }) => balance != null && (balance.takenDays > 0 || balance.pendingDays > 0),
      ),
    [cells],
  );

  const pendingDays = active.reduce((n, c) => n + (c.balance?.pendingDays ?? 0), 0);
  // Entitled but nothing left. Worth surfacing collapsed: it is the answer to
  // "can they take this?" before anyone opens the card.
  const spent = cells.filter(
    ({ balance }) => balance != null && balance.entitlementDays > 0 && balance.remainingDays <= 0,
  );

  const held = cells.filter((c) => c.held);
  // Everything at zero is still worth drawing — an empty tile would read as a
  // failed load rather than as someone with no entitlements.
  const visible = showAll || held.length === 0 ? cells : held;
  const hidden = cells.length - visible.length;

  return (
    <article className="rounded-2xl border border-border/60 bg-surface-low/50">
      {/* The whole header toggles, not just the chevron — on a phone the
          chevron alone is a target you have to aim at. */}
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="flex w-full items-center gap-3 rounded-2xl p-3.5 text-left transition hover:bg-card/60"
      >
        <span className="min-w-0 flex-1">
          <span className="block truncate font-bold text-foreground">{name}</span>
          <span className="block truncate text-xs text-muted-foreground">{email}</span>

          <span className="mt-2 flex flex-wrap items-center gap-1.5">
            {active.length === 0 ? (
              <span className="text-[11px] font-semibold text-muted-foreground">
                Nothing taken this year
              </span>
            ) : (
              active.map(({ type, balance }) => (
                <span
                  key={type.id}
                  className="inline-flex items-center gap-1 rounded-full bg-card px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.08em] text-muted-foreground"
                >
                  {type.code}
                  <span className="tabular-nums text-foreground">
                    {balance!.remainingDays}/{balance!.entitlementDays}
                  </span>
                </span>
              ))
            )}

            {/* An approver's own queue, on the card. remainingDays does not
                subtract these, so approving what is already waiting can take
                someone past the number beside it. */}
            {pendingDays > 0 ? (
              <span className="inline-flex items-center rounded-full bg-tertiary/15 px-2 py-0.5 text-[10px] font-bold text-tertiary">
                {pendingDays} pending
              </span>
            ) : null}

            {spent.length > 0 ? (
              <span className="inline-flex items-center rounded-full bg-destructive/10 px-2 py-0.5 text-[10px] font-bold text-destructive">
                {spent.length === 1 ? `${spent[0].type.code} used up` : `${spent.length} used up`}
              </span>
            ) : null}
          </span>
        </span>

        <ChevronDown
          className={`h-4 w-4 shrink-0 text-muted-foreground transition ${open ? "rotate-180" : ""}`}
          aria-hidden
        />
      </button>

      {open ? (
        <div className="border-t border-border/60 px-3.5 pb-3.5 pt-3">
          <div className="space-y-1.5">
            {visible.map(({ type, balance }) => (
              <BalanceRow key={type.id} code={type.code} balance={balance} />
            ))}
          </div>

          {hidden > 0 || showAll ? (
            <button
              type="button"
              onClick={() => setShowAll((v) => !v)}
              aria-expanded={showAll}
              className="mt-2 inline-flex items-center gap-1 rounded-full text-[11px] font-bold text-muted-foreground transition hover:text-foreground"
            >
              {showAll ? "Show fewer" : `${hidden} more with no balance`}
              <ChevronDown className={`h-3 w-3 transition ${showAll ? "rotate-180" : ""}`} />
            </button>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}

// Days left out of the entitlement, with a bar for the share already taken.
//
// Pending days are called out separately because `remainingDays` does not
// subtract them: approving what is already in the queue can take someone past
// what the number here says they have.
function BalanceRow({ code, balance }: { code: string; balance: LeaveBalance | null }) {
  const entitlement = balance?.entitlementDays ?? 0;
  const remaining = balance?.remainingDays ?? 0;
  const usedShare =
    balance && entitlement > 0
      ? Math.min(100, Math.max(0, (balance.takenDays / entitlement) * 100))
      : 0;
  const spent = balance != null && remaining <= 0;

  return (
    <div>
      <div className="flex items-center gap-2.5">
        <span className="w-14 shrink-0 text-[10px] font-bold uppercase tracking-[0.1em] text-muted-foreground">
          {code}
        </span>
        <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-border/70">
          <span
            className={`block h-full rounded-full ${spent ? "bg-destructive" : "bg-primary"}`}
            style={{ width: `${usedShare}%` }}
          />
        </span>
        {balance ? (
          <span className="w-14 shrink-0 text-right text-xs tabular-nums">
            <span className={`font-black ${spent ? "text-destructive" : "text-foreground"}`}>
              {remaining}
            </span>
            <span className="font-semibold text-muted-foreground">/{entitlement}</span>
          </span>
        ) : (
          <span className="w-14 shrink-0 text-right text-xs font-semibold text-muted-foreground">
            —
          </span>
        )}
      </div>
      {balance && balance.pendingDays > 0 ? (
        <p className="text-right text-[10px] font-semibold tabular-nums text-tertiary">
          {balance.pendingDays} pending
        </p>
      ) : null}
    </div>
  );
}
