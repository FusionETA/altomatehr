import { useEffect, useState } from "react";
import { Info } from "lucide-react";
import { getOvertimeRate, type OtRateResolution } from "../api";
import { describeRate, isPremiumDay, otDayTypeLabels } from "../lib/ot-rate";

type Props = {
  /** Plain YYYY-MM-DD. The day type is derived from this. */
  workDate: string;
  projectId?: string;
};

// Shows which OT rate the chosen date attracts — read-only, on purpose.
//
// The day type is a fact about the date (is it in the employee's working days?
// is it in the holiday calendar?), so asking the employee to pick it would let
// them choose a 3x public-holiday rate for an ordinary Tuesday, and would create
// a disagreement with payroll that has no correct resolution. So: shown, never
// asked. It also explains WHY, which a dropdown never could.
export function OtRatePreview({ workDate, projectId }: Props) {
  const [rate, setRate] = useState<OtRateResolution | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!workDate) return;

    // The date can change while a request is in flight; ignore stale replies so
    // a slow lookup for an old date can't overwrite a newer one.
    let active = true;
    setFailed(false);

    getOvertimeRate(workDate, projectId)
      .then((result) => {
        if (active) setRate(result);
      })
      .catch(() => {
        if (active) {
          setRate(null);
          setFailed(true);
        }
      });

    return () => {
      active = false;
    };
  }, [workDate, projectId]);

  // Nothing useful to say yet, and a failed lookup must not block submitting —
  // the server resolves the rate again on approval regardless.
  if (failed || !rate) return null;

  const summary = describeRate(rate);
  const premium = isPremiumDay(rate.dayType);

  return (
    <div
      className={`grid gap-1 rounded-2xl border px-4 py-3 ${
        premium ? "border-primary/40 bg-primary/5" : "border-border bg-muted/40"
      }`}
    >
      <p className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <Info className={`h-4 w-4 shrink-0 ${premium ? "text-primary" : "text-muted-foreground"}`} />
        {otDayTypeLabels[rate.dayType]}
        {summary ? (
          <span className="ml-auto font-mono text-xs font-medium tabular-nums text-muted-foreground">
            {summary}
          </span>
        ) : null}
      </p>
      <p className="pl-6 text-xs text-muted-foreground">{rate.reason}</p>
    </div>
  );
}
