import { useCallback, useEffect, useState } from "react";
import { Coffee, LoaderCircle } from "lucide-react";
import {
  approveBreak,
  getTeamBreakApprovals,
  rejectBreak,
  type AttendanceApprovalRequest,
} from "../api";

const CARD = "rounded-[26px] border border-border/70 bg-card/90 shadow-ambient";

// Breaks awaiting the signed-in supervisor as current-step approver.
//
// Separate from the record queue on purpose: breaks decide through their own
// endpoints, because /attendance/{id}/approve only accepts CLOCK_IN and
// CLOCK_OUT kinds. Without this screen every recorded break sits PENDING
// forever, which is where they were until now.
export function BreakApprovals() {
  const [requests, setRequests] = useState<AttendanceApprovalRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<AttendanceApprovalRequest | null>(null);
  const [notes, setNotes] = useState("");

  const load = useCallback(() => {
    getTeamBreakApprovals()
      .then(setRequests)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(load, [load]);

  async function decide(request: AttendanceApprovalRequest, approve: boolean) {
    if (!approve && !notes.trim()) {
      setError("Remark is required when rejecting a break.");
      return;
    }

    setBusyId(request.id);
    setError(null);
    try {
      if (approve) await approveBreak(request.id);
      else await rejectBreak(request.id, notes.trim());
      setRequests((current) => current.filter((r) => r.id !== request.id));
      setRejecting(null);
      setNotes("");
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Could not record that decision.");
    } finally {
      setBusyId(null);
    }
  }

  if (loading) {
    return <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading break approvals...</section>;
  }

  if (requests.length === 0 && !error) {
    return (
      <section className={`${CARD} p-6 text-center`}>
        <Coffee className="mx-auto h-5 w-5 text-muted-foreground" />
        <p className="mt-3 text-sm font-bold text-foreground">No break approvals waiting.</p>
      </section>
    );
  }

  return (
    <div className="space-y-3">
      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {requests.map((request) => (
        <article key={request.id} className={`${CARD} p-4`}>
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0">
              <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                {request.kind === "BREAK_START" ? "Break start" : "Break end"}
              </p>
              <p className="mt-1 truncate text-base font-black text-foreground">
                {request.employeeEmail ?? request.employeeId}
              </p>
              <p className="text-sm text-muted-foreground">{fmtDateTime(request.eventAt)}</p>
              {request.reason ? (
                <p className="mt-1 text-xs text-muted-foreground">&ldquo;{request.reason}&rdquo;</p>
              ) : null}
            </div>
          </div>

          {rejecting?.id === request.id ? (
            <div className="mt-3 grid gap-2 border-t border-border/50 pt-3">
              <textarea
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
                rows={2}
                placeholder="Why is this break being rejected?"
                className="resize-none rounded-2xl border border-border bg-card px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
              <div className="grid gap-2 sm:grid-cols-2">
                <button
                  type="button"
                  onClick={() => void decide(request, false)}
                  disabled={busyId === request.id}
                  className="flex h-10 items-center justify-center gap-2 rounded-full bg-destructive text-sm font-bold text-destructive-foreground transition hover:opacity-90 disabled:opacity-60"
                >
                  {busyId === request.id ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                  Confirm reject
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setRejecting(null);
                    setNotes("");
                  }}
                  className="h-10 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
                >
                  Cancel
                </button>
              </div>
            </div>
          ) : (
            <div className="mt-3 grid gap-2 border-t border-border/50 pt-3 sm:grid-cols-2">
              <button
                type="button"
                onClick={() => void decide(request, true)}
                disabled={busyId === request.id}
                className="flex h-10 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
              >
                {busyId === request.id ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Approve
              </button>
              <button
                type="button"
                onClick={() => {
                  setRejecting(request);
                  setNotes("");
                  setError(null);
                }}
                className="h-10 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
              >
                Reject
              </button>
            </div>
          )}
        </article>
      ))}
    </div>
  );
}

function fmtDateTime(iso: string) {
  return new Date(iso).toLocaleString("en-US", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}
