import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight, RefreshCw, Users } from "lucide-react";
import { getTeamToday, type TeamAttendanceMember } from "../api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonCards } from "@/shared/components/Skeleton";

const CARD = "rounded-2xl border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";
type Presence = "ON_SHIFT" | "DONE" | "NOT_STARTED";

function presenceOf(member: TeamAttendanceMember): Presence {
  if (!member.record?.timeIn) return "NOT_STARTED";
  return member.record.timeOut ? "DONE" : "ON_SHIFT";
}

// A supervisor's teams for today: who's on shift, who's finished, who hasn't
// started — one project at a time.
//
// A supervisor runs a crew per site, so the project IS the unit they switch
// between: standing on one site, "who is here with me" is the question, and a
// merged list of everyone they oversee doesn't answer it.
//
// Tabs come from the projects the supervisor's TEAMS belong to — not from where
// people happen to have clocked in. A site whose whole crew is absent still gets
// a tab, because "nobody turned up today" is the answer a supervisor most needs
// and it can't be shown by a tab that isn't there.
export function TeamPresence() {
  const [members, setMembers] = useState<TeamAttendanceMember[]>([]);
  // Empty until the first load names a project — there is no "all" option.
  const [projectId, setProjectId] = useState<string>("");
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function load(isRefresh = false) {
    if (isRefresh) setRefreshing(true);
    getTeamToday()
      .then((team) => {
        setMembers(team);
        setError(null);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setRefreshing(false));
  }

  // Today's presence is cached like anything else, but with a short life: it is
  // the one screen where a stale answer is actively misleading ("is my crew on
  // site right now?"), so the refresh button stays and drives load() directly.
  const presenceQuery = useCachedQuery("/attendance/team/today", getTeamToday);
  const loading = presenceQuery.loading;
  useEffect(() => {
    if (presenceQuery.data) setMembers(presenceQuery.data);
  }, [presenceQuery.data]);
  useEffect(() => {
    if (presenceQuery.error) setError(presenceQuery.error);
  }, [presenceQuery.error]);


  // One tab per project the supervisor has a team in.
  const tabs = useMemo(() => {
    const seen = new Map<string, string>();
    for (const m of members) {
      if (!seen.has(m.projectId)) seen.set(m.projectId, m.projectName ?? "Project");
    }
    return [...seen].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
  }, [members]);

  // Only the projects this supervisor actually has a team in. There is no
  // "All projects" entry: a merged roll-call of two sites answers nobody's
  // question — standing on one site you want that site's crew, and the counts
  // only mean something scoped to one.
  const currentIndex = Math.max(0, tabs.findIndex((t) => t.id === projectId));
  const current = tabs[currentIndex] ?? null;

  function step(delta: number) {
    if (tabs.length === 0) return;
    setProjectId(tabs[(currentIndex + delta + tabs.length) % tabs.length].id);
  }

  // Land on the first project, and recover if the one being viewed disappears
  // because a team was restructured between refreshes.
  useEffect(() => {
    if (tabs.length > 0 && !tabs.some((t) => t.id === projectId)) setProjectId(tabs[0].id);
  }, [tabs, projectId]);

  const visible = useMemo(
    () => members.filter((m) => m.projectId === projectId),
    [members, projectId],
  );

  const onShift = visible.filter((m) => presenceOf(m) === "ON_SHIFT").length;

  return (
    <div className="space-y-4">
      {/* The project being watched IS the heading — a "Your team" title above
          it would push the one piece of information that changes off the top.
          Stepper rather than tabs, as in the previous app: a supervisor on six
          sites can't fit six tabs on a phone. */}
      {current ? (
        <div className="flex items-center justify-between gap-2 rounded-2xl border border-border/60 bg-surface-low px-2 py-1.5">
          {/* Arrows only when there's somewhere to go. On one project the bar
              still names it — this IS the heading, so it can't disappear. */}
          {tabs.length > 1 ? (
            <button
              type="button"
              onClick={() => step(-1)}
              aria-label="Previous project"
              className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-card hover:text-foreground"
            >
              <ChevronLeft className="h-5 w-5" />
            </button>
          ) : (
            <span className="h-9 w-9 shrink-0" aria-hidden />
          )}
          <div className="min-w-0 text-center">
            <p className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
              {tabs.length > 1 ? `Project ${currentIndex + 1} / ${tabs.length} · ` : ""}
              {onShift} present
            </p>
            <p className="truncate text-sm font-bold text-foreground">{current.name}</p>
          </div>
          {tabs.length > 1 ? (
            <button
              type="button"
              onClick={() => step(1)}
              aria-label="Next project"
              className="grid h-9 w-9 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-card hover:text-foreground"
            >
              <ChevronRight className="h-5 w-5" />
            </button>
          ) : (
            <span className="h-9 w-9 shrink-0" aria-hidden />
          )}
        </div>
      ) : null}

      <div className="flex items-center justify-between gap-3">
        <span className="text-xs text-muted-foreground">
          {visible.length} {visible.length === 1 ? "person" : "people"} &middot; {onShift} on shift
        </span>
        <button
          type="button"
          onClick={() => load(true)}
          disabled={refreshing}
          className="grid h-8 w-8 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          aria-label="Refresh team"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? "animate-spin" : ""}`} />
        </button>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <SkeletonCards />
      ) : visible.length === 0 ? (
        <section className={`${CARD} border-dashed bg-surface-low p-8 text-center`}>
          <Users className="mx-auto h-6 w-6 text-primary" />
          <p className="mt-3 text-sm font-medium text-foreground">
            {members.length === 0
              ? "You don't have a team below you yet."
              : `No one on ${current?.name ?? "this project"}'s team.`}
          </p>
        </section>
      ) : (
        <section className={`${CARD} overflow-hidden`}>
          {visible.map((m) => (
            <MemberRow key={`${m.teamId}:${m.employeeId}`} member={m} />
          ))}
        </section>
      )}
    </div>
  );
}

function MemberRow({ member }: { member: TeamAttendanceMember }) {
  const state = presenceOf(member);
  const record = member.record;
  const name = member.employeeEmail ? buildName(member.employeeEmail) : "Employee";

  return (
    <div className="flex items-center justify-between gap-3 border-t border-border/50 px-4 py-3.5 first:border-t-0">
      <div className="min-w-0">
        <p className="truncate text-sm font-bold text-foreground">{name}</p>
        <p className="mt-0.5 truncate text-xs text-muted-foreground">
          {state === "NOT_STARTED"
            ? `Not clocked in · ${member.teamName}`
            : `${clockTime(record?.timeIn)}${record?.timeOut ? ` – ${clockTime(record.timeOut)}` : ""} · ${member.teamName}`}
        </p>
      </div>
      <span
        className={`shrink-0 rounded-full px-2.5 py-1 text-[11px] font-bold ${
          state === "ON_SHIFT"
            ? "bg-success/15 text-success"
            : state === "DONE"
              ? "bg-muted text-muted-foreground"
              : "bg-tertiary/15 text-tertiary"
        }`}
      >
        {state === "ON_SHIFT" ? "On shift" : state === "DONE" ? "Done" : "Not in"}
      </span>
    </div>
  );
}

function clockTime(iso?: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" });
}
