import { Fragment, useCallback, useEffect, useState, type ReactNode } from "react";
import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  FileText,
  KeyRound,
  LoaderCircle,
  LogOut,
  MousePointerClick,
  RefreshCw,
  XCircle,
} from "lucide-react";
import { getSession, type SessionDetail, type TimelineItem } from "../api";
import {
  counts,
  dayLabel,
  describeAction,
  describeClick,
  describePage,
  formatDateTime,
  formatDuration,
  formatTime,
  sessionLength,
  viaLabel,
} from "../lib/describe";
import { SessionStatusDot } from "./SessionStatusDot";

// One sign-in, step by step: what they opened (and for how long), what they
// clicked, what they changed — and, in red, what failed and what they were told.
export function SessionTimeline({ sessionId, onBack }: { sessionId: string; onBack: () => void }) {
  const [detail, setDetail] = useState<SessionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setDetail(await getSession(sessionId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load this session.");
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    void load();
  }, [load]);

  const session = detail?.session;

  // Someone who never logs out keeps one session for days or weeks: then each
  // day gets a heading, since the rows only show the time.
  const endAt = session ? (session.endedAt ?? session.lastSeenAt) : null;
  const multiDay = !!session && !!endAt && dayLabel(session.startedAt) !== dayLabel(endAt);
  let prevDay = session ? dayLabel(session.startedAt) : "";
  const dayBreak = (iso: string) => {
    const day = dayLabel(iso);
    const changed = multiDay && day !== prevDay;
    prevDay = day;
    return changed ? <DayHeading label={day} /> : null;
  };

  return (
    <section className="rounded-[28px] border border-border/60 bg-card p-5 shadow-ambient sm:p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <button
          type="button"
          onClick={onBack}
          className="inline-flex items-center gap-1.5 rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted"
        >
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> All sign-ins
        </button>
        <button
          type="button"
          onClick={() => void load()}
          disabled={loading}
          className="inline-flex items-center gap-1.5 rounded-full border border-border/70 px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-60"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} aria-hidden />
          Refresh
        </button>
      </div>

      {error ? <p className="mt-4 text-sm font-medium text-destructive">{error}</p> : null}

      {loading && !detail ? (
        <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
          <LoaderCircle className="h-4 w-4 animate-spin" /> Loading session…
        </div>
      ) : session ? (
        <>
          {/* ── Who ─────────────────────────────────────────────────── */}
          <div className="mt-5 flex flex-wrap items-start justify-between gap-4 border-b border-border/50 pb-5">
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h2 className="truncate text-lg font-black text-foreground">{session.name}</h2>
                {session.isStaff ? (
                  <span className="rounded-full border border-border/70 px-2 py-0.5 text-[11px] font-semibold text-muted-foreground">
                    Fusioneta
                  </span>
                ) : null}
              </div>
              <p className="truncate text-sm text-muted-foreground">{session.email}</p>
              <p className="mt-1 text-sm text-foreground">
                {session.companyName} · {session.role}
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                {viaLabel(session.via)}
                {session.ipAddress ? ` · IP ${session.ipAddress}` : ""}
                {session.device ? ` · ${session.device}` : ""}
              </p>
            </div>
            <div className="text-right text-sm">
              <SessionStatusDot status={session.status} withLabel />
              <p className="mt-1 text-muted-foreground">{formatDateTime(session.startedAt)}</p>
              <p className="text-xs text-muted-foreground">
                {formatDuration(sessionLength(session.startedAt, session.lastSeenAt, session.endedAt))}
                {" · "}
                {counts(session.pageViews, session.clicks, session.actions)}
                {session.failedActions > 0 ? ` (${session.failedActions} failed)` : ""}
              </p>
            </div>
          </div>

          {/* ── What they did ────────────────────────────────────────── */}
          <ol className="mt-4 space-y-1">
            {multiDay ? <DayHeading label={dayLabel(session.startedAt)} /> : null}
            <Row
              icon={<KeyRound className="h-4 w-4 text-primary" aria-hidden />}
              time={formatTime(session.startedAt)}
              title="Signed in"
              detail={`${viaLabel(session.via)}${session.ipAddress ? ` · ${session.ipAddress}` : ""}`}
            />
            {detail?.truncated ? (
              <li className="px-2 py-1.5 text-xs text-muted-foreground">
                … earlier steps not shown. This is the latest 2,000.
              </li>
            ) : null}
            {(detail?.items ?? []).map((item, i) => (
              <Fragment key={`${item.at}-${i}`}>
                {dayBreak(item.at)}
                <TimelineRow item={item} />
              </Fragment>
            ))}
            {endAt ? dayBreak(endAt) : null}
            {session.endedAt ? (
              <Row
                icon={<LogOut className="h-4 w-4 text-muted-foreground" aria-hidden />}
                time={formatTime(session.endedAt)}
                title="Signed out"
              />
            ) : (
              <Row
                icon={<SessionStatusDot status={session.status} />}
                time={formatTime(session.lastSeenAt)}
                title={session.status === "online" ? "Online now" : "Last seen"}
                detail={session.status === "online" ? undefined : "Closed the tab or went idle without signing out"}
              />
            )}
          </ol>

          {(detail?.items.length ?? 0) === 0 ? (
            <p className="mt-3 text-xs text-muted-foreground">
              Nothing recorded yet in this session. Page views arrive within about 10 seconds.
            </p>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function TimelineRow({ item }: { item: TimelineItem }) {
  const badges = (
    <>
      {item.isSupport ? <Badge>support mode</Badge> : null}
      {item.companyName ? <Badge>in {item.companyName}</Badge> : null}
    </>
  );

  if (item.kind === "page") {
    return (
      <Row
        icon={<FileText className="h-4 w-4 text-muted-foreground" aria-hidden />}
        time={formatTime(item.at)}
        title={<>Opened {describePage(item.name)}</>}
        detail={item.durationMs ? `~${formatDuration(item.durationMs)}` : undefined}
        raw={item.name}
        badges={badges}
      />
    );
  }

  if (item.kind === "click") {
    return (
      <Row
        icon={<MousePointerClick className="h-4 w-4 text-muted-foreground" aria-hidden />}
        time={formatTime(item.at)}
        title={<>Clicked {describeClick(item.name)}</>}
        raw={item.name}
        badges={badges}
      />
    );
  }

  const failed = (item.statusCode ?? 0) >= 400;
  if (item.kind === "error") {
    const { where } = describeAction(item.name);
    return (
      <Row
        tone="error"
        icon={<AlertTriangle className="h-4 w-4 text-destructive" aria-hidden />}
        time={formatTime(item.at)}
        title={<>Couldn't load {where || item.name}</>}
        detail={`${item.statusCode}${item.message ? ` · ${item.message}` : ""}`}
        raw={item.name}
      />
    );
  }

  const { verb, where } = describeAction(item.name);
  return (
    <Row
      tone={failed ? "error" : undefined}
      icon={
        failed ? (
          <XCircle className="h-4 w-4 text-destructive" aria-hidden />
        ) : (
          <CheckCircle2 className="h-4 w-4 text-success" aria-hidden />
        )
      }
      time={formatTime(item.at)}
      title={
        <>
          {failed ? "Tried to " : ""}
          {failed ? verb.toLowerCase() : verb}
          {where ? <span className="font-normal text-muted-foreground"> · {where}</span> : null}
        </>
      }
      detail={[
        item.targetId ? `#${item.targetId}` : null,
        failed ? `failed (${item.statusCode})${item.message ? `: ${item.message}` : ""}` : null,
        item.durationMs !== null ? `${item.durationMs} ms` : null,
      ]
        .filter(Boolean)
        .join(" · ")}
      raw={item.name}
      badges={badges}
    />
  );
}

function Row({
  icon,
  time,
  title,
  detail,
  raw,
  badges,
  tone,
}: {
  icon: ReactNode;
  time: string;
  title: ReactNode;
  detail?: string;
  raw?: string;
  badges?: ReactNode;
  tone?: "error";
}) {
  return (
    <li
      className={`grid grid-cols-[4.75rem_1.25rem_minmax(0,1fr)] items-start gap-x-2 rounded-xl px-2 py-1.5 sm:grid-cols-[5.5rem_1.25rem_minmax(0,1fr)] ${
        tone === "error" ? "bg-destructive/5" : ""
      }`}
    >
      <span className="pt-0.5 text-xs tabular-nums text-muted-foreground">{time}</span>
      <span className="flex h-5 items-center justify-center">{icon}</span>
      <div className="min-w-0">
        <p className={`text-sm font-semibold ${tone === "error" ? "text-destructive" : "text-foreground"}`}>
          {title} {badges}
        </p>
        {detail ? (
          <p className={`break-words text-xs ${tone === "error" ? "text-destructive/90" : "text-muted-foreground"}`}>
            {detail}
          </p>
        ) : null}
        {raw ? <p className="truncate font-mono text-[11px] text-muted-foreground/70">{raw}</p> : null}
      </div>
    </li>
  );
}

function DayHeading({ label }: { label: string }) {
  return (
    <li className="px-2 pb-1 pt-3 text-xs font-bold uppercase tracking-wider text-muted-foreground">{label}</li>
  );
}

function Badge({ children }: { children: ReactNode }) {
  return (
    <span className="ml-1 inline-block rounded-full border border-border/70 px-1.5 py-px align-middle text-[10px] font-semibold text-muted-foreground">
      {children}
    </span>
  );
}
