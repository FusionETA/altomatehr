import { useCallback, useEffect, useState } from "react";
import { Activity, LoaderCircle, RefreshCw, X } from "lucide-react";
import {
  getApiMonitoringErrors,
  getApiMonitoringSummary,
  type ApiEndpointSummary,
  type ApiMonitoringSummary,
  type ApiRequestError,
} from "../api";
import { count, ms, percent, RANGES, rangeBounds, when, type RangeKey } from "../lib/format";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const BUTTON_GHOST =
  "inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary disabled:opacity-50";
const TH = "whitespace-nowrap px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-muted-foreground";
const TH_NUM = `${TH} text-right`;
const TD = "whitespace-nowrap px-3 py-2.5 text-sm";
const TD_NUM = `${TD} text-right tabular-nums`;

// Radix Select can't hold "" as a value, so "every company" needs a token.
const ALL = "__all";

// Settings → API monitoring (Fusioneta superadmins only): how the API behaved
// across every company — which endpoints are slow, which fail, and the latest
// failures with the message the caller saw. 4xx and 5xx are counted apart
// throughout: a 409 is usually the API correctly refusing something, a 500 is
// a bug.
export function ApiMonitoringPanel() {
  const [range, setRange] = useState<RangeKey>("24h");
  const [company, setCompany] = useState<string>(ALL);
  // Set by clicking an endpoint: narrows the error list to it.
  const [route, setRoute] = useState<string | null>(null);

  const [summary, setSummary] = useState<ApiMonitoringSummary | null>(null);
  const [errors, setErrors] = useState<ApiRequestError[]>([]);
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setFailure(null);
    const filter = {
      ...rangeBounds(range),
      organizationId: company === ALL ? null : company,
    };
    try {
      const [nextSummary, nextErrors] = await Promise.all([
        getApiMonitoringSummary(filter),
        getApiMonitoringErrors({ ...filter, route, limit: 50 }),
      ]);
      setSummary(nextSummary);
      setErrors(nextErrors);
    } catch (err) {
      setFailure(err instanceof Error ? err.message : "Could not load API monitoring.");
    } finally {
      setLoading(false);
    }
  }, [range, company, route]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading && !summary) return <SkeletonPanel />;

  const companies = summary?.companies ?? [];
  const companyName = (id: string | null) =>
    id ? (companies.find((c) => c.organizationId === id)?.organizationName ?? id) : "—";

  return (
    <div className="space-y-4">
      {/* ── Filters ─────────────────────────────────────────────────── */}
      <section className={`${CARD} flex flex-wrap items-center justify-between gap-3`}>
        <div className="flex items-center gap-2">
          <Activity className="size-4 text-primary" aria-hidden />
          <div>
            <h2 className="text-base font-black text-foreground">API monitoring</h2>
            <p className="text-xs text-muted-foreground">
              Every company · kept for 30 days · no request data is stored
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <div role="group" aria-label="Time range" className="flex gap-1 rounded-xl border border-border bg-card p-1">
            {RANGES.map((r) => (
              <button
                key={r.key}
                type="button"
                aria-pressed={range === r.key}
                onClick={() => setRange(r.key)}
                className={`rounded-lg px-3 py-1.5 text-xs font-bold transition ${
                  range === r.key
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                {r.label}
              </button>
            ))}
          </div>

          <Select value={company} onValueChange={setCompany}>
            <SelectTrigger className="h-9 w-[220px] bg-card" aria-label="Company">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All companies</SelectItem>
              {companies.map((c) => (
                <SelectItem key={c.organizationId} value={c.organizationId}>
                  {c.organizationName ?? c.organizationId} ({count(c.calls)})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <button type="button" className={BUTTON_GHOST} disabled={loading} onClick={() => void load()}>
            {loading ? (
              <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
            ) : (
              <RefreshCw className="size-3.5" aria-hidden />
            )}
            Refresh
          </button>
        </div>
      </section>

      {failure ? <p className="text-sm font-medium text-destructive">{failure}</p> : null}

      {summary ? (
        <>
          {/* ── Totals ────────────────────────────────────────────────── */}
          <section className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Stat label="Calls" value={count(summary.totalCalls)} />
            <Stat label="Client errors (4xx)" value={count(summary.totalClientErrors)} />
            <Stat
              label="Server errors (5xx)"
              value={count(summary.totalServerErrors)}
              alarm={summary.totalServerErrors > 0}
            />
            <Stat label="Average time" value={ms(summary.averageMs)} />
          </section>

          {/* ── Per endpoint ──────────────────────────────────────────── */}
          <section className={`${CARD} p-0 sm:p-0`}>
            <div className="px-5 pt-5 sm:px-6">
              <h3 className="text-sm font-bold text-foreground">Endpoints</h3>
              <p className="text-xs text-muted-foreground">
                Failing first, then busiest. Click one to see only its errors below.
              </p>
            </div>
            {summary.endpoints.length === 0 ? (
              <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">
                No calls in this range.
              </p>
            ) : (
              <div className="nice-scrollbar mt-3 max-h-[28rem] overflow-auto">
                <table className="w-full min-w-[760px] border-collapse">
                  <thead className="sticky top-0 bg-card">
                    <tr className="border-b border-border/70">
                      <th className={TH}>Endpoint</th>
                      <th className={TH_NUM}>Calls</th>
                      <th className={TH_NUM}>4xx</th>
                      <th className={TH_NUM}>5xx</th>
                      <th className={TH_NUM}>Error rate</th>
                      <th className={TH_NUM}>Average</th>
                      <th className={TH_NUM}>Slowest</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.endpoints.map((e) => (
                      <EndpointRow
                        key={`${e.method} ${e.route}`}
                        endpoint={e}
                        selected={route === e.route}
                        onSelect={() => setRoute((cur) => (cur === e.route ? null : e.route))}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {/* ── Recent failures ───────────────────────────────────────── */}
          <section className={CARD}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <h3 className="text-sm font-bold text-foreground">Recent errors</h3>
                <p className="text-xs text-muted-foreground">Latest 50 failed calls, newest first.</p>
              </div>
              {route ? (
                <button type="button" className={BUTTON_GHOST} onClick={() => setRoute(null)}>
                  <span className="font-mono">{route}</span>
                  <X className="size-3.5" aria-hidden />
                </button>
              ) : null}
            </div>

            {errors.length === 0 ? (
              <p className="mt-4 text-sm text-muted-foreground">No failed calls in this range.</p>
            ) : (
              <ul className="mt-4 space-y-2">
                {errors.map((e) => (
                  <li key={e.id} className="rounded-2xl border border-border/60 bg-card/40 px-4 py-3">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                      <StatusBadge status={e.statusCode} />
                      <span className="font-mono font-semibold text-foreground">
                        {e.method} {e.route}
                      </span>
                      <span className="text-muted-foreground">{when(e.createdAt)}</span>
                      <span className="text-muted-foreground">{ms(e.durationMs)}</span>
                      <span className="text-muted-foreground">
                        {e.organizationName ?? companyName(e.organizationId)} · {e.callerType}
                      </span>
                    </div>
                    {e.errorMessage ? (
                      <p className="mt-1.5 break-words text-sm text-foreground">{e.errorMessage}</p>
                    ) : null}
                    {e.exceptionType ? (
                      <p className="mt-1 break-all font-mono text-xs text-muted-foreground">
                        {e.exceptionType}
                        {e.exceptionSource ? ` at ${e.exceptionSource}` : ""}
                      </p>
                    ) : null}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      ) : null}
    </div>
  );
}

function Stat({ label, value, alarm = false }: { label: string; value: string; alarm?: boolean }) {
  return (
    <div className={`${CARD} p-4 sm:p-4`}>
      <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className={`mt-1 text-2xl font-black tabular-nums ${alarm ? "text-destructive" : "text-foreground"}`}>
        {value}
      </p>
    </div>
  );
}

function EndpointRow({
  endpoint: e,
  selected,
  onSelect,
}: {
  endpoint: ApiEndpointSummary;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <tr
      onClick={onSelect}
      aria-selected={selected}
      className={`cursor-pointer border-b border-border/40 transition last:border-0 ${
        selected ? "bg-primary/5" : "hover:bg-muted/40"
      }`}
    >
      <td className={`${TD} font-mono text-xs`}>
        <span className="mr-2 font-bold text-muted-foreground">{e.method}</span>
        <span className="text-foreground">{e.route}</span>
      </td>
      <td className={TD_NUM}>{count(e.calls)}</td>
      <td className={`${TD_NUM} ${e.clientErrors > 0 ? "text-foreground" : "text-muted-foreground"}`}>
        {count(e.clientErrors)}
      </td>
      <td className={`${TD_NUM} ${e.serverErrors > 0 ? "font-bold text-destructive" : "text-muted-foreground"}`}>
        {count(e.serverErrors)}
      </td>
      <td className={TD_NUM}>{percent(e.errorRate)}</td>
      <td className={TD_NUM}>{ms(e.averageMs)}</td>
      <td className={TD_NUM}>{ms(e.slowestMs)}</td>
    </tr>
  );
}

function StatusBadge({ status }: { status: number }) {
  const server = status >= 500;
  return (
    <span
      className={`inline-flex rounded-full border px-2 py-0.5 font-bold tabular-nums ${
        server
          ? "border-destructive/30 bg-destructive/10 text-destructive"
          : "border-amber-500/30 bg-amber-500/10 text-amber-800 dark:text-amber-300"
      }`}
    >
      {status}
    </span>
  );
}
