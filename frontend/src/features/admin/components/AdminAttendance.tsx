import { useEffect, useMemo, useState } from "react";
import { getAttendanceHistory, type AttendanceRecord } from "@/features/attendance/api";
import { AttendanceStatusBadge } from "@/features/attendance/components/AttendanceStatusBadge";
import { getEmployees } from "@/features/employees/api";
import { getProjects } from "@/features/settings/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";

function timeLabel(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });
}

function dateLabel(key: string): string {
  const d = new Date(key);
  return Number.isNaN(d.getTime())
    ? key
    : d.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}

export function AdminAttendance() {
  const [records, setRecords] = useState<AttendanceRecord[]>([]);
  const [emails, setEmails] = useState<Map<string, string>>(new Map());
  const [projectNames, setProjectNames] = useState<Map<string, string>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");

  useEffect(() => {
    // Admins get the whole org back from /attendance (roll call).
    Promise.all([getAttendanceHistory(), getEmployees().catch(() => []), getProjects().catch(() => [])])
      .then(([recs, employees, projects]) => {
        setRecords(recs);
        setEmails(new Map(employees.map((e) => [e.id, e.email])));
        setProjectNames(new Map(projects.map((p) => [p.id, p.name])));
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  const name = (employeeId: string) => {
    const email = emails.get(employeeId);
    return email ? buildName(email) : employeeId;
  };

  const sorted = useMemo(
    () =>
      [...records].sort((a, b) =>
        a.date === b.date
          ? (b.timeIn ?? "").localeCompare(a.timeIn ?? "")
          : b.date.localeCompare(a.date),
      ),
    [records],
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return sorted;
    return sorted.filter((r) => {
      const proj = r.projectId ? projectNames.get(r.projectId) ?? "" : "";
      return [name(r.employeeId), emails.get(r.employeeId) ?? "", r.location ?? "", proj, r.status]
        .join(" ")
        .toLowerCase()
        .includes(q);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sorted, query, emails, projectNames]);

  const meta = (r: AttendanceRecord) =>
    [r.projectId ? projectNames.get(r.projectId) : undefined, r.location].filter(Boolean).join(" · ");

  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-muted-foreground">
          Showing <span className="font-semibold text-foreground">{filtered.length}</span> of{" "}
          <span className="font-semibold text-foreground">{records.length}</span> records
        </p>
        <SearchInput
          value={query}
          onChange={setQuery}
          placeholder="Search by employee, project, or status"
          className="max-w-sm"
        />
      </div>

      {loading ? (
        <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 text-sm text-muted-foreground shadow-ambient backdrop-blur-sm">
          Loading attendance…
        </section>
      ) : null}

      {error ? (
        <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
          Error: {error}
        </section>
      ) : null}

      {!loading && !error && filtered.length === 0 ? (
        <section className="rounded-[28px] border border-border/70 bg-card/90 p-8 text-center text-sm text-muted-foreground shadow-ambient backdrop-blur-sm">
          No attendance records yet.
        </section>
      ) : null}

      {/* Mobile cards */}
      {!loading && !error && filtered.length > 0 ? (
        <div className="grid gap-3 md:hidden">
          {filtered.map((r) => (
            <article
              key={r.id}
              className="rounded-[24px] border border-border/70 bg-card/90 p-4 shadow-ambient backdrop-blur-sm"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate text-base font-bold text-foreground">{name(r.employeeId)}</p>
                  <p className="text-xs text-muted-foreground">{dateLabel(r.date)}</p>
                  {meta(r) ? <p className="mt-1 text-xs text-muted-foreground">{meta(r)}</p> : null}
                </div>
                <AttendanceStatusBadge status={r.status} />
              </div>
              <div className="mt-3 grid grid-cols-2 gap-3">
                <div>
                  <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Clock in</p>
                  <p className="mt-1 text-sm font-semibold text-foreground">{timeLabel(r.timeIn)}</p>
                </div>
                <div>
                  <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Clock out</p>
                  <p className="mt-1 text-sm font-semibold text-foreground">{timeLabel(r.timeOut)}</p>
                </div>
              </div>
            </article>
          ))}
        </div>
      ) : null}

      {/* Desktop table */}
      {!loading && !error && filtered.length > 0 ? (
        <section className="hidden rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm md:block">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] caption-bottom text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["Employee", "Date", "Clock in", "Clock out", "Location", "Status"].map((h) => (
                    <th
                      key={h}
                      className="h-12 px-6 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {filtered.map((r) => (
                  <tr key={r.id} className="border-b border-border/60 transition-colors hover:bg-muted/70">
                    <td className="p-4 pl-6 align-middle">
                      <p className="font-bold text-foreground">{name(r.employeeId)}</p>
                      <p className="text-xs text-muted-foreground">{emails.get(r.employeeId) ?? ""}</p>
                    </td>
                    <td className="p-4 align-middle">{dateLabel(r.date)}</td>
                    <td className="p-4 align-middle">{timeLabel(r.timeIn)}</td>
                    <td className="p-4 align-middle">{timeLabel(r.timeOut)}</td>
                    <td className="p-4 align-middle text-muted-foreground">{meta(r) || "—"}</td>
                    <td className="p-4 pr-6 align-middle">
                      <AttendanceStatusBadge status={r.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}
    </div>
  );
}
