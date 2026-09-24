import { Fragment, useCallback, useEffect, useState } from "react";
import { getAuditLog, type AuditEntry } from "@/features/audit/api";
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { SearchInput } from "@/shared/components/SearchInput";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { CARD_BARE } from "../lib/dashboard-styles";
import { SkeletonRows } from "@/shared/components/Skeleton";

// Matches the shared pager, which computes its page numbers from that constant
// — a different size here would make "Showing 1-10 of 47" disagree with the
// rows actually on screen.
const PAGE_SIZE = CLAIMS_PAGE_SIZE;

const ALL = "__all__";

// Namespaces, matching the "module.verb" prefix on each action. Sign-ins are
// far and away the highest-volume event once they are logged, so being able to
// pull them out — or look at nothing else — is what keeps the feed usable.
const ACTIVITY_TYPES: { value: string; label: string }[] = [
  { value: ALL, label: "All activity" },
  { value: "auth", label: "Sign-ins" },
  { value: "settings", label: "Settings" },
  { value: "employee", label: "People" },
  { value: "team", label: "Teams" },
  { value: "coa", label: "Accounts" },
  { value: "project", label: "Projects" },
  { value: "xero", label: "Xero" },
];
const CONTROL =
  "h-11 w-full rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2";

// Who changed the organisation's configuration, and who signed in — newest
// first.
//
// Approvals are deliberately absent. Claims, leave, attendance and overtime
// each show their own decisions on their own tab, with more context than a
// one-line audit row could carry; duplicating them here would bury the
// configuration changes this page exists for.
export function ActivityLog() {
  const [entries, setEntries] = useState<AuditEntry[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<string>(ALL);
  const [activityType, setActivityType] = useState<string>(ALL);

  // Which row is open. One at a time: the detail is a few lines, and several
  // expanded at once turns the feed back into a wall of JSON.
  const [openId, setOpenId] = useState<string | null>(null);

  const load = useCallback(
    (which: number) => {
      setLoading(true);
      setError(null);
      return getAuditLog({
        limit: PAGE_SIZE,
        page: which,
        action: activityType === ALL ? undefined : activityType,
        status: status === ALL ? undefined : (status as "SUCCESS" | "FAILED"),
      })
        .then((result) => {
          setEntries(result.entries);
          setTotal(result.total);
        })
        .catch((e: unknown) =>
          setError(e instanceof Error ? e.message : "Could not load the activity log."),
        )
        .finally(() => setLoading(false));
    },
    [status, activityType],
  );

  // A filter change resets to page one: staying on page 4 of a set that no
  // longer has four pages shows an empty table and looks broken.
  useEffect(() => {
    setPage(1);
    void load(1);
  }, [load]);


  // Search is client-side and deliberately narrow: it filters the page you are
  // looking at, not the whole log. Anything wider belongs in the query above.
  const term = search.trim().toLowerCase();
  const visible = term
    ? entries.filter((entry) =>
        [entry.actorName, entry.actorEmail, entry.action, entry.label, entry.summary]
          .filter(Boolean)
          .join(" ")
          .toLowerCase()
          .includes(term),
      )
    : entries;

  return (
    <div className="space-y-4 sm:space-y-6">
      <section className={`${CARD_BARE} space-y-3 p-5 sm:p-6`}>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <SearchInput
            value={search}
            onChange={setSearch}
            placeholder="Search this page by person, action, or summary"
            className="sm:flex-1"
            inputClassName="h-11"
          />

          <Select value={activityType} onValueChange={setActivityType}>
            <SelectTrigger className={`${CONTROL} sm:w-44`} aria-label="Activity type">
              <SelectValue placeholder="All activity" />
            </SelectTrigger>
            <SelectContent>
              {ACTIVITY_TYPES.map((type) => (
                <SelectItem key={type.value} value={type.value}>
                  {type.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select value={status} onValueChange={setStatus}>
            <SelectTrigger className={`${CONTROL} sm:w-44`} aria-label="Status">
              <SelectValue placeholder="Any outcome" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>Any outcome</SelectItem>
              <SelectItem value="SUCCESS">Success</SelectItem>
              <SelectItem value="FAILED">Failed</SelectItem>
            </SelectContent>
          </Select>

        </div>
      </section>

      {error ? (
        <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-4 text-sm font-medium text-destructive">
          {error}
        </section>
      ) : null}

      <section className={CARD_BARE}>
        {loading ? (
          <table className="w-full text-sm">
            <tbody>
              <SkeletonRows rows={6} widths={["w-32", "w-40", "w-24", "w-20"]} />
            </tbody>
          </table>
        ) : visible.length === 0 ? (
          <div className="p-8 text-center">
            <p className="text-lg font-bold text-foreground">Nothing recorded yet.</p>
            <p className="mx-auto mt-2 max-w-md text-sm text-muted-foreground">
              Changing settings, adding an account or project, editing people, or connecting Xero
              will appear here as it happens.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[880px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["When", "Actor", "Action", "Summary", "Status"].map((column) => (
                    <th
                      key={column}
                      className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground first:pl-6 last:pr-6"
                    >
                      {column}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {visible.map((entry) => {
                  const detail = parseMetadata(entry.metadata);
                  const open = openId === entry.id;
                  return (
                  <Fragment key={entry.id}>
                  <tr
                    onClick={() => detail && setOpenId(open ? null : entry.id)}
                    className={`border-b border-border/60 align-top ${
                      detail ? "cursor-pointer hover:bg-muted/40" : ""
                    } ${open ? "bg-primary/5" : ""}`}
                  >
                    <td className="whitespace-nowrap p-4 pl-6 text-muted-foreground">
                      <time dateTime={entry.createdAt} title={new Date(entry.createdAt).toString()}>
                        {formatWhen(entry.createdAt)}
                      </time>
                    </td>
                    <td className="p-4">
                      <p className="font-semibold text-foreground">{entry.actorName}</p>
                      <p className="text-xs text-muted-foreground">{entry.actorEmail}</p>
                    </td>
                    <td className="p-4">
                      <p className="text-foreground">{entry.label}</p>
                      {/* The raw code, small and monospaced, so it stays
                          greppable long after the label has been reworded. */}
                      <p className="mt-0.5 font-mono text-[10px] text-muted-foreground/70">
                        {entry.action}
                      </p>
                    </td>
                    <td className="p-4">
                      <p className="text-foreground">{entry.summary}</p>
                      {entry.errorReason ? (
                        <p className="mt-1 text-xs text-destructive">{entry.errorReason}</p>
                      ) : null}
                    </td>
                    <td className="p-4 pr-6">
                      {/* Outlined, not filled. Almost every row succeeds, so a
                          solid badge on each one is a wall of colour that stops
                          the rare failure standing out — which is the only time
                          this column is worth reading. */}
                      <span
                        className={`inline-flex items-center rounded-full border px-3 py-1 text-[10px] font-bold uppercase tracking-[0.14em] ${
                          entry.status === "SUCCESS"
                            ? "border-transparent bg-secondary text-secondary-foreground"
                            : "border-destructive/40 bg-destructive/10 text-destructive"
                        }`}
                      >
                        {entry.status === "SUCCESS" ? "Success" : "Failed"}
                      </span>
                    </td>
                  </tr>

                  {/* What actually changed. The summary says "role Employee →
                      Supervisor"; this is where the rest of it lives, including
                      the fields whose before-values were captured but that the
                      one-line summary has no room for. */}
                  {open && detail ? (
                    <tr className="border-b border-border/60 bg-primary/5">
                      <td colSpan={5} className="px-6 pb-5 pt-1">
                        {/* An inset card, so the details read as belonging to
                            the row above rather than as a second, unruled table.
                            Label over value: the old inline "LABEL value" pairs
                            ran together, and all-caps labels at 11px were harder
                            to read than the numbers they introduced. */}
                        <div className="rounded-2xl border border-border/60 bg-card px-5 py-4">
                          <p className="mb-3 text-xs font-semibold text-muted-foreground">Details</p>
                          <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
                            {detail.map(({ label, from, to, value, kind }) => (
                              <div key={label} className="min-w-0">
                                <dt className="text-xs text-muted-foreground">{label}</dt>
                                <dd
                                  className={`mt-0.5 break-words ${
                                    kind === "id"
                                      ? "font-mono text-[11px] text-muted-foreground"
                                      : "text-sm font-medium text-foreground tabular-nums"
                                  }`}
                                >
                                  {from !== undefined ? (
                                    <>
                                      <span className="font-normal text-muted-foreground line-through">
                                        {from}
                                      </span>
                                      <span className="mx-1.5 text-muted-foreground">→</span>
                                      <span>{to}</span>
                                    </>
                                  ) : (
                                    value
                                  )}
                                </dd>
                              </div>
                            ))}
                          </dl>
                          {entry.ipAddress ? (
                            <p className="mt-4 border-t border-border/50 pt-3 text-xs text-muted-foreground">
                              Recorded from {entry.ipAddress} · Entry #{entry.seq}
                            </p>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ) : null}
                  </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {!loading ? (
          <PaginationControls
            className="flex flex-col gap-3 border-t border-border/60 px-6 py-4 sm:flex-row sm:items-center sm:justify-between"
            currentPage={page}
            totalItems={total}
            itemNoun="events"
            onPageChange={(next) => {
              setPage(next);
              void load(next);
            }}
          />
        ) : null}
      </section>
    </div>
  );
}

// Relative for anything recent, absolute once "3 days ago" stops being the
// useful framing. The full timestamp is on the title attribute either way.
function formatWhen(iso: string) {
  // The API serialises without a trailing Z; read it as UTC rather than letting
  // the browser assume local time and shift every entry by the offset.
  const at = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`);
  const minutes = Math.round((Date.now() - at.getTime()) / 60000);

  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;
  if (minutes < 60 * 24) return `${Math.round(minutes / 60)}h ago`;
  if (minutes < 60 * 24 * 7) return `${Math.round(minutes / (60 * 24))}d ago`;

  return at.toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
}

// Flattens an entry's metadata into rows the detail panel can render.
//
// A value shaped { From, To } is a before → after pair and gets rendered as
// one; anything else is shown as-is. That shape is a convention the writers
// follow, not a contract — an action carrying something else still displays,
// just without the arrow.
type DetailRow = {
  label: string;
  from?: string;
  to?: string;
  value?: string;
  // An internal id — shown small and muted: rarely read, occasionally copied.
  kind?: "id";
};

function parseMetadata(raw: string | null): DetailRow[] | null {
  if (!raw) return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    // Never throw on a bad blob: a malformed metadata value must not be able to
    // take the whole feed down with it.
    return [{ label: "Detail", value: raw }];
  }

  if (typeof parsed !== "object" || parsed === null) return null;

  const entries = Object.entries(parsed as Record<string, unknown>);
  const rows: DetailRow[] = [];

  // PeriodYear + PeriodMonth are one fact — "September 2026" — not two rows.
  const year = entries.find(([k]) => k.toLowerCase() === "periodyear")?.[1];
  const month = entries.find(([k]) => k.toLowerCase() === "periodmonth")?.[1];
  const period =
    typeof year === "number" && typeof month === "number" && month >= 1 && month <= 12
      ? new Date(year, month - 1, 1).toLocaleDateString("en-MY", { month: "long", year: "numeric" })
      : null;
  if (period) rows.push({ label: "Period", value: period });

  for (const [key, value] of entries) {
    if (period && /^period(year|month)$/i.test(key)) continue;
    const label = humanizeKey(key);

    if (isBeforeAfter(value)) {
      const from = show(value.From ?? value.from, key);
      const to = show(value.To ?? value.to, key);
      // A "change" that changed nothing is noise — the writers record both
      // sides unconditionally, so filtering happens here.
      if (from !== to) rows.push({ label, from, to });
      continue;
    }

    const text = show(value, key);
    if (text !== "—") rows.push({ label, value: text, kind: isId(key, value) ? "id" : undefined });
  }

  return rows.length > 0 ? rows : null;
}

function isBeforeAfter(value: unknown): value is Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const keys = Object.keys(value).map((k) => k.toLowerCase());
  return keys.includes("from") && keys.includes("to");
}

// Money-shaped keys — totals, gross, net, amounts. "Count" keys are never
// money even when they start with "total" (TotalCount, PayslipCount).
const MONEY_KEY = /(gross|net|cost|amount|salary|pay$|payment|pcb|epf|socso|eis|deduction|total)/i;
const COUNT_KEY = /(count|days|hours|minutes|number|seq|year|month|index)/i;

function show(value: unknown, key = ""): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (typeof value === "number") {
    // "119500" and "106225.3" read as raw data; "RM 119,500.00" reads as pay.
    if (MONEY_KEY.test(key) && !COUNT_KEY.test(key)) {
      return `RM ${value.toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    }
    return /year/i.test(key) ? String(value) : value.toLocaleString("en-MY");
  }
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

// "PayrollRunId", "ClaimId"… or a bare GUID.
function isId(key: string, value: unknown): boolean {
  return (
    /id$/i.test(key) ||
    (typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value))
  );
}

// "ClaimRunCutoffDay" / "claimRunCutoffDay" → "Claim run cutoff day"
function humanizeKey(key: string) {
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
