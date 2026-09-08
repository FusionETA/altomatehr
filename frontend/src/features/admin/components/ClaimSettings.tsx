import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import {
  claimSettlementHints,
  claimSettlementLabels,
  getClaimSettings,
  updateClaimSettings,
  xeroBillStageHints,
  xeroBillStageLabels,
  type ClaimSettings as ClaimSettingsValues,
  type ClaimSettlement,
  type XeroBillStage,
} from "@/features/claims/api";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";

const ROUTES: ClaimSettlement[] = ["XERO_BILL", "PAYROLL"];
const STAGES: XeroBillStage[] = ["AwaitingPayment", "Draft"];

// The org-level rules the claims module runs on: where approved money goes, and
// when the month's run closes.
//
// One form over both cards, with a single save. They are two settings on one
// screen, and a save button per card reads as if the other card's edits were
// lost when you pressed it.
export function ClaimSettings() {
  const [loaded, setLoaded] = useState(false);
  const [cutoffDay, setCutoffDay] = useState(25);
  const [route, setRoute] = useState<ClaimSettlement>("XERO_BILL");
  const [stage, setStage] = useState<XeroBillStage>("AwaitingPayment");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    getClaimSettings()
      .then((settings: ClaimSettingsValues) => {
        setCutoffDay(settings.claimRunCutoffDay);
        setRoute(settings.settlementRoute);
        setStage(settings.xeroBillStage);
        setLoaded(true);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await updateClaimSettings({
        claimRunCutoffDay: cutoffDay,
        settlementRoute: route,
        xeroBillStage: stage,
      });
      setCutoffDay(updated.claimRunCutoffDay);
      setRoute(updated.settlementRoute);
      setStage(updated.xeroBillStage);
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the claim settings.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <div className={`${CARD} text-sm text-muted-foreground`}>Loading claim settings…</div>;
  }

  if (!loaded) {
    return (
      <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        {error ?? "Could not load the claim settings."}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-5">
      <section className={`${CARD} space-y-4`}>
        <div>
          <h2 className="text-lg font-black text-foreground">How approved claims are paid</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            One route for the whole company.
          </p>
        </div>

        <div role="radiogroup" aria-label="How approved claims are paid" className="space-y-2">
          {ROUTES.map((option) => {
            const active = route === option;
            // The Xero option owns the stage choice, so it is a container with a
            // label inside rather than a label itself — nesting a second radio
            // group inside a <label> would make every stage click also re-pick
            // the parent option.
            return (
              <div
                key={option}
                className={`overflow-hidden rounded-2xl border transition ${
                  active ? "border-primary/40 bg-primary/5" : "border-border/60 bg-card hover:border-border"
                } ${saving ? "opacity-60" : ""}`}
              >
                <label
                  className={`flex items-start gap-3 p-3.5 ${
                    saving ? "cursor-not-allowed" : "cursor-pointer"
                  }`}
                >
                  <input
                    type="radio"
                    name="claim-settlement-route"
                    value={option}
                    checked={active}
                    disabled={saving}
                    onChange={() => {
                      setSaved(false);
                      setRoute(option);
                    }}
                    className="mt-0.5 h-4 w-4 shrink-0 accent-primary"
                  />
                  <span className="min-w-0">
                    <span
                      className={`block text-sm font-bold ${active ? "text-primary" : "text-foreground"}`}
                    >
                      {claimSettlementLabels[option]}
                    </span>
                    <span className="mt-0.5 block text-xs leading-snug text-muted-foreground">
                      {claimSettlementHints[option]}
                    </span>
                  </span>
                </label>

                {/* Inside the option it belongs to, and only while that option is
                    chosen. It used to sit below BOTH routes as a sibling, which
                    read as though the stage applied to payroll too — it does not;
                    nothing reaches Xero on that route. */}
                {option === "XERO_BILL" && active ? (
                  <div className="border-t border-primary/20 px-3.5 pb-3.5 pt-3">
                    <p className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                      Which stage in Xero
                    </p>

                    <div
                      role="radiogroup"
                      aria-label="Which stage in Xero"
                      className="mt-2.5 space-y-2"
                    >
                      {STAGES.map((choice) => {
                        const chosen = stage === choice;
                        return (
                          <label
                            key={choice}
                            className={`flex items-start gap-3 rounded-xl border bg-card p-3 transition ${
                              chosen ? "border-primary/40" : "border-border/60 hover:border-border"
                            } ${saving ? "cursor-not-allowed" : "cursor-pointer"}`}
                          >
                            <input
                              type="radio"
                              name="xero-bill-stage"
                              value={choice}
                              checked={chosen}
                              disabled={saving}
                              onChange={() => {
                                setSaved(false);
                                setStage(choice);
                              }}
                              className="mt-0.5 h-4 w-4 shrink-0 accent-primary"
                            />
                            <span className="min-w-0">
                              <span
                                className={`block text-sm font-bold ${
                                  chosen ? "text-primary" : "text-foreground"
                                }`}
                              >
                                {xeroBillStageLabels[choice]}
                              </span>
                              <span className="mt-0.5 block text-xs leading-snug text-muted-foreground">
                                {xeroBillStageHints[choice]}
                              </span>
                            </span>
                          </label>
                        );
                      })}
                    </div>
                  </div>
                ) : null}
              </div>
            );
          })}
        </div>

        {/* The caveat that matters, kept as a footnote rather than the four-line
            paragraph that used to open the card — it explains a consequence of
            changing the setting, which is not what you need before reading the
            options themselves. */}
        <p className="text-xs leading-snug text-muted-foreground">
          Each claim keeps the route it was created under, so changing this never re-routes existing
          claims — including any already billed to Xero, which would otherwise be paid twice.
        </p>
      </section>

      <section className={`${CARD} space-y-4`}>
        <div>
          <h2 className="text-lg font-black text-foreground">Claim run cutoff</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Claims submitted on or before this day stay in the current month's claims run. Claims
            submitted after the cutoff still go through, but they roll into the next run.
          </p>
        </div>

        <div className="sm:w-48">
          <label className={LABEL} htmlFor="claim-run-cutoff-day">
            Cutoff day of month
          </label>
          <input
            id="claim-run-cutoff-day"
            type="number"
            inputMode="numeric"
            min={1}
            max={28}
            required
            disabled={saving}
            value={cutoffDay}
            onChange={(event) => {
              setSaved(false);
              // An empty input parses to NaN, which would submit a blank body and
              // come back a 400. Hold the last good value instead.
              const next = Number(event.target.value);
              if (Number.isFinite(next)) setCutoffDay(next);
            }}
            className={`mt-1.5 ${INPUT}`}
          />
        </div>

        {/* Capped at 28 rather than 31 — a cutoff of the 30th would not exist in
            February, and a month-end process that skips a month is worse than
            one that closes three days early. */}
        <p className="text-xs text-muted-foreground">
          Any day from 1 to 28. Every month has these days, so the run always closes.
        </p>
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="submit"
          disabled={saving}
          className="inline-flex h-12 items-center justify-center gap-2 rounded-2xl bg-primary px-6 text-sm font-bold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
        >
          {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
          Save claim settings
        </button>

        {error ? <p className="text-sm font-semibold text-destructive">{error}</p> : null}
        {saved ? (
          <p className="text-sm font-semibold text-primary">
            Saved — approved claims go to {claimSettlementLabels[route].toLowerCase()}
            {route === "XERO_BILL" ? ` as ${xeroBillStageLabels[stage].toLowerCase()}` : ""}, and the
            run closes on day {cutoffDay} of each month.
          </p>
        ) : null}
      </div>
    </form>
  );
}
