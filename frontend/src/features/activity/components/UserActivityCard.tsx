import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { ChevronLeft, ChevronRight, LoaderCircle, RefreshCw } from "lucide-react";
import { SearchInput } from "@/shared/components/SearchInput";
import { Switch } from "@/shared/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { getSessions, type ActivitySession, type SessionPage } from "../api";
import { counts, formatDateTime, formatDuration, sessionLength, VIA_OPTIONS, viaLabel } from "../lib/describe";
import { SessionStatusDot } from "./SessionStatusDot";
import { SessionTimeline } from "./SessionTimeline";

const PAGE_SIZE = 25;
// Radix Select can't hold "" as a value.
const ALL = "__all";

const RANGES = [
  { key: "today", label: "Today", days: 0 },
  { key: "7d", label: "7 days", days: 7 },
  { key: "30d", label: "30 days", days: 30 },
  { key: "90d", label: "90 days", days: 90 },
] as const;
type RangeKey = (typeof RANGES)[number]["key"];

function rangeFrom(key: RangeKey): string {
  const range = RANGES.find((r) => r.key === key)!;
  const from = new Date();
  if (range.days === 0) from.setHours(0, 0, 0, 0);
  else from.setDate(from.getDate() - range.days);
  return from.toISOString();
}

export type ActivityCompany = { id: string; name: string };

// Support → User activity (Fusioneta only). Every sign-in — the same ones the
// "🔑 Sign-in" alerts post to the team chat — searchable, with how long it
// lasted and how much was done; open one to see it step by step.
export function UserActivityCard({ companies }: { companies: ActivityCompany[] }) {
  const [range, setRange] = useState<RangeKey>("7d");
  const [company, setCompany] = useState<string>(ALL);
  const [via, setVia] = useState<string>(ALL);
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [onlineOnly, setOnlineOnly] = useState(false);
  const [includeStaff, setIncludeStaff] = useState(false);
  const [page, setPage] = useState(1);
  const [data, setData] = useState<SessionPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  // Filters change faster than answers arrive: only the latest request may land.
  const latest = useRef(0);

  // A keystroke is not a round trip.
  useEffect(() => {
    const t = setTimeout(() => setQuery(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  // Any filter change starts again from the first page.
  useEffect(() => {
    setPage(1);
  }, [range, company, via, query, onlineOnly, includeStaff]);

  const load = useCallback(async () => {
    const mine = ++latest.current;
    setLoading(true);
    setError(null);
    try {
      const next = await getSessions({
          from: rangeFrom(range),
          organizationId: company === ALL ? undefined : company,
          via: via === ALL ? undefined : via,
          search: query || undefined,
          onlineOnly,
          includeStaff,
          page,
          limit: PAGE_SIZE,
      });
      if (mine === latest.current) setData(next);
    } catch (err) {
      if (mine === latest.current) setError(err instanceof Error ? err.message : "Could not load sign-ins.");
    } finally {
      if (mine === latest.current) setLoading(false);
    }
  }, [range, company, via, query, onlineOnly, includeStaff, page]);

  useEffect(() => {
    void load();
  }, [load]);

  if (openId) {
    return <SessionTimeline sessionId={openId} onBack={() => setOpenId(null)} />;
  }

  const sessions = data?.sessions ?? [];
  const total = data?.total ?? 0;
  const first = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1;
  const last = Math.min(page * PAGE_SIZE, total);

  return (
    <section className="rounded-[28px] border border-border/60 bg-card p-5 shadow-ambient sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-lg font-black text-foreground">User activity</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Every sign-in, and what was opened, clicked and changed in it. Most recently active first; a
            range shows sessions in use during it. Fusioneta only, never shown to customers. Kept for 90
            days. Nothing typed or shown on screen is recorded.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void load()}
          disabled={loading}
          className="inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-60"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} aria-hidden />
          Refresh
        </button>
      </div>

      {/* ── Filters ─────────────────────────────────────────────────── */}
      <div className="mt-4 grid gap-2 sm:grid-cols-2 lg:grid-cols-[minmax(0,1fr)_auto_auto]">
        <SearchInput value={search} onChange={setSearch} placeholder="Search name or email" className="sm:col-span-2 lg:col-span-1" />
        <Select value={company} onValueChange={setCompany}>
          <SelectTrigger className="h-11 w-full bg-card lg:w-[220px]" aria-label="Company">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All companies</SelectItem>
            {companies.map((c) => (
              <SelectItem key={c.id} value={c.id}>
                {c.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={via} onValueChange={setVia}>
          <SelectTrigger className="h-11 w-full bg-card lg:w-[180px]" aria-label="Signed in via">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>Any sign-in method</SelectItem>
            {VIA_OPTIONS.map((o) => (
              <SelectItem key={o.value} value={o.value}>
                {o.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-3">
        <div role="group" aria-label="Active in" className="flex gap-1 rounded-xl border border-border bg-card p-1">
          {RANGES.map((r) => (
            <button
              key={r.key}
              type="button"
              aria-pressed={range === r.key}
              onClick={() => setRange(r.key)}
              className={`rounded-lg px-3 py-1.5 text-xs font-bold transition ${
                range === r.key ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {r.label}
            </button>
          ))}
        </div>
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-foreground">
          <Switch checked={onlineOnly} onCheckedChange={setOnlineOnly} aria-label="Online now only" />
          Online now
        </label>
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-foreground">
          <Switch checked={includeStaff} onCheckedChange={setIncludeStaff} aria-label="Include Fusioneta staff" />
          Include Fusioneta staff
        </label>
      </div>

      {error ? <p className="mt-4 text-sm font-medium text-destructive">{error}</p> : null}

      {/* ── Sign-ins ────────────────────────────────────────────────── */}
      {loading && !data ? (
        <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
          <LoaderCircle className="h-4 w-4 animate-spin" /> Loading sign-ins…
        </div>
      ) : sessions.length === 0 ? (
        <p className="py-10 text-center text-sm text-muted-foreground">
          {error ? "" : onlineOnly ? "Nobody is online right now." : "No sign-ins match."}
        </p>
      ) : (
        <ul className={`mt-4 divide-y divide-border/40 rounded-2xl border border-border/50 ${loading ? "opacity-60" : ""}`}>
          {sessions.map((s) => (
            <SessionRow key={s.id} session={s} onOpen={() => setOpenId(s.id)} />
          ))}
        </ul>
      )}

      {total > PAGE_SIZE ? (
        <div className="mt-3 flex items-center justify-between gap-3 text-sm text-muted-foreground">
          <span>
            {first}–{last} of {total} sign-ins
          </span>
          <div className="flex gap-1">
            <PagerButton label="Previous page" disabled={page === 1 || loading} onClick={() => setPage(page - 1)}>
              <ChevronLeft className="h-4 w-4" />
            </PagerButton>
            <PagerButton label="Next page" disabled={last >= total || loading} onClick={() => setPage(page + 1)}>
              <ChevronRight className="h-4 w-4" />
            </PagerButton>
          </div>
        </div>
      ) : total > 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">
          {total} sign-in{total === 1 ? "" : "s"}
        </p>
      ) : null}
    </section>
  );
}

function SessionRow({ session: s, onOpen }: { session: ActivitySession; onOpen: () => void }) {
  return (
    <li>
      <button
        type="button"
        onClick={onOpen}
        className="grid w-full gap-x-4 gap-y-1 px-4 py-3 text-left transition hover:bg-muted/50 sm:grid-cols-[minmax(0,1fr)_auto]"
      >
        <div className="min-w-0">
          <div className="flex min-w-0 items-center gap-2">
            <SessionStatusDot status={s.status} />
            <span className="truncate font-semibold text-foreground">{s.name}</span>
            {s.isStaff ? (
              <span className="shrink-0 rounded-full border border-border/70 px-1.5 py-px text-[10px] font-semibold text-muted-foreground">
                Fusioneta
              </span>
            ) : null}
          </div>
          <p className="truncate pl-[18px] text-xs text-muted-foreground">{s.email}</p>
          <p className="truncate pl-[18px] text-sm text-foreground">
            {s.companyName} · {s.role}
          </p>
          <p className="truncate pl-[18px] text-xs text-muted-foreground">
            {viaLabel(s.via)}
            {s.ipAddress ? ` · ${s.ipAddress}` : ""}
            {s.device ? ` · ${s.device}` : ""}
          </p>
        </div>
        <div className="pl-[18px] text-xs text-muted-foreground sm:pl-0 sm:text-right">
          <p className="text-sm text-foreground">{formatDateTime(s.startedAt)}</p>
          <p>
            {formatDuration(sessionLength(s.startedAt, s.lastSeenAt, s.endedAt))}
            {s.status === "signed-out" ? " · signed out" : s.status === "online" ? " · online" : ""}
          </p>
          <p>
            {counts(s.pageViews, s.clicks, s.actions)}
            {s.failedActions > 0 ? <span className="text-destructive"> · {s.failedActions} failed</span> : null}
          </p>
        </div>
      </button>
    </li>
  );
}

function PagerButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="flex h-8 w-8 items-center justify-center rounded-full border border-border/70 text-foreground transition hover:bg-muted disabled:opacity-40"
    >
      {children}
    </button>
  );
}
