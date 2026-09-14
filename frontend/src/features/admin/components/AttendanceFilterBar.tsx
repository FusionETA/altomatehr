import { useState, type ReactNode } from "react";
import { ChevronDown, SlidersHorizontal } from "lucide-react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { SearchInput } from "@/shared/components/SearchInput";
import type { AdminAttendanceFilter } from "@/features/attendance/api";
import { ALL_FILTER } from "../lib/attendance-format";

const CONTROL =
  "h-11 w-full rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2";

export type FilterOption = { id: string; name: string };

// Search-first, mirroring the claims screen: one full-width search always
// visible, with project/team (and any tab-specific extras passed as children —
// a date range, an OT status) folded behind a "Filters" toggle. A badge on the
// toggle counts what's narrowing the list while the panel is collapsed, so a
// hidden filter is never a silent one.
//
// Project and team narrow through the same server-side resolution the reports
// use, rather than being applied to whatever the client happened to fetch —
// otherwise a filtered total and a filtered table could disagree.
export function AttendanceFilterBar({
  value,
  onChange,
  projects,
  teams,
  searchPlaceholder = "Search employee",
  activeExtra = 0,
  children,
}: {
  value: AdminAttendanceFilter;
  onChange: (next: AdminAttendanceFilter) => void;
  projects: FilterOption[];
  teams: FilterOption[];
  searchPlaceholder?: string;
  // Active filters that live in `children` (e.g. an OT status other than
  // "all"), so the badge count stays honest about what is applied.
  activeExtra?: number;
  children?: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const set = <K extends keyof AdminAttendanceFilter>(
    key: K,
    next: AdminAttendanceFilter[K],
  ) => onChange({ ...value, [key]: next });

  const activeCount = (value.projectId ? 1 : 0) + (value.teamId ? 1 : 0) + activeExtra;

  return (
    <div className="space-y-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <SearchInput
          value={value.q ?? ""}
          onChange={(q) => set("q", q)}
          placeholder={searchPlaceholder}
          className="sm:flex-1"
          inputClassName="h-12"
        />

        <button
          type="button"
          aria-expanded={open}
          aria-controls="attendance-filter-panel"
          onClick={() => setOpen((o) => !o)}
          className={`inline-flex h-12 shrink-0 items-center gap-2 rounded-2xl border px-4 text-sm font-bold shadow-sm transition ${
            open || activeCount > 0
              ? "border-primary/40 bg-primary/5 text-primary"
              : "border-border/70 bg-card text-muted-foreground hover:text-foreground"
          }`}
        >
          <SlidersHorizontal className="h-4 w-4" />
          Filters
          {activeCount > 0 ? (
            <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-[11px] font-black text-primary-foreground">
              {activeCount}
            </span>
          ) : null}
          <ChevronDown className={`h-4 w-4 transition-transform ${open ? "rotate-180" : ""}`} />
        </button>
      </div>

      <div id="attendance-filter-panel" hidden={!open} className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <Select
            value={value.projectId ?? ALL_FILTER}
            onValueChange={(v) => set("projectId", v === ALL_FILTER ? undefined : v)}
          >
            <SelectTrigger className={`${CONTROL} sm:w-48`} aria-label="Project">
              <SelectValue placeholder="All projects" />
            </SelectTrigger>
            <SelectContent searchPlaceholder="Search projects…">
              <SelectItem value={ALL_FILTER}>All projects</SelectItem>
              {projects.map((project) => (
                <SelectItem key={project.id} value={project.id}>
                  {project.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={value.teamId ?? ALL_FILTER}
            onValueChange={(v) => set("teamId", v === ALL_FILTER ? undefined : v)}
          >
            <SelectTrigger className={`${CONTROL} sm:w-48`} aria-label="Team">
              <SelectValue placeholder="All teams" />
            </SelectTrigger>
            <SelectContent searchPlaceholder="Search teams…">
              <SelectItem value={ALL_FILTER}>All teams</SelectItem>
              {teams.map((team) => (
                <SelectItem key={team.id} value={team.id}>
                  {team.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {children}
      </div>
    </div>
  );
}

// From / To, shared by the tabs that report over a range.
export function DateRangeBar({
  from,
  to,
  onChange,
}: {
  from: string;
  to: string;
  onChange: (from: string, to: string) => void;
}) {
  const LABEL = "text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground";
  const INPUT =
    "h-11 rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2";

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className={LABEL}>From</span>
      <input
        type="date"
        aria-label="From date"
        value={from}
        max={to || undefined}
        onChange={(event) => onChange(event.target.value, to)}
        className={INPUT}
      />
      <span className={LABEL}>To</span>
      <input
        type="date"
        aria-label="To date"
        value={to}
        min={from || undefined}
        onChange={(event) => onChange(from, event.target.value)}
        className={INPUT}
      />
    </div>
  );
}

