import { useState } from "react";
import { ArrowRight, CircleAlert, LoaderCircle } from "lucide-react";
import {
  approvePayrollRun,
  deletePayrollRun,
  generatePayrollRun,
  getRevertImpact,
  rejectPayrollRun,
  revertPayrollRun,
  submitPayrollRun,
  type PayrollRun,
  type SkippedEmployee,
} from "../api";
import { BUTTON, BUTTON_DANGER, BUTTON_GHOST, LABEL, LINK_BUTTON, TEXTAREA, WARN_PANEL } from "../lib/ui";

// WARN_PANEL's colours at a single line's weight — a reason next to a button,
// not a panel of its own.
const BLOCKER_STRIP =
  "flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3.5 py-2.5 text-sm text-amber-800 dark:text-amber-300";

// What can be done to a run, given where it is.
//
//   DRAFT ──submit──▶ PENDING_APPROVAL ──approve──▶ SUBMITTED
//     ▲                      │                          │
//     └───────reject─────────┘                          │
//     └──────────────────revert──────────────────────────┘
//
// Only the transitions the run can actually make are rendered. Showing a
// disabled "Approve" on a draft invites the admin to hunt for why it is
// greyed out; not showing it says the same thing without the hunt.
export function PayrollRunActions({
  run,
  payslipCount,
  blockingCount,
  onChanged,
  onSkipped,
  onDeleted,
  onViewAttention,
}: {
  run: PayrollRun;
  payslipCount: number;
  // Fields the readiness guard would refuse the submission over. Used to
  // say WHICH problem is in the way, rather than greying a button out and
  // leaving the admin to hunt.
  blockingCount: number;
  onChanged: () => void;
  onSkipped: (skipped: SkippedEmployee[]) => void;
  onDeleted: () => void;
  // Jumps to the Needs attention tab. A disabled Send for approval used to
  // explain itself only in a hover title — invisible on the touch devices
  // this is opened on, and easy to miss even with a mouse — so the same
  // reason now sits in view next to the button, with a way straight to the
  // list of what is actually blocking it.
  onViewAttention: () => void;
}) {
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The cascade a revert will cause, fetched before it happens so the admin
  // sees which later months go back to draft with it.
  const [revertImpact, setRevertImpact] = useState<string[] | null>(null);

  // Rejecting opens an inline form rather than window.prompt. The reason is
  // kept on the run and shown to whoever picks it back up, so it deserves a
  // field that is themed, resizable and cancellable — not a native dialog
  // that cannot be styled and reads as a browser error.
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState("");

  async function run_<T>(key: string, action: () => Promise<T>) {
    setBusy(key);
    setError(null);
    try {
      return await action();
    } catch (err) {
      // Every refusal from this endpoint group is a business rule with a
      // written reason — stale figures, someone on zero net, a prior month
      // still open. Showing it verbatim is more use than a generic failure.
      setError(err instanceof Error ? err.message : "That action did not go through.");
      return null;
    } finally {
      setBusy(null);
    }
  }

  async function handleGenerate() {
    const result = await run_("generate", () => generatePayrollRun(run.id));
    if (!result) return;

    onSkipped(result.skippedEmployees);
    onChanged();
  }

  async function askRevert() {
    const impact = await run_("revert-impact", () => getRevertImpact(run.id));
    if (impact) setRevertImpact(impact.alsoReverted);
  }

  async function confirmRevert() {
    const result = await run_("revert", () => revertPayrollRun(run.id));
    setRevertImpact(null);
    if (result) onChanged();
  }

  const spinner = (key: string) =>
    busy === key ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null;

  // What is stopping a draft from being sent for approval, in words. Null
  // when nothing is — or when there are no payslips yet, because then the
  // button is not offered at all.
  const submitBlocker =
    run.status !== "DRAFT" || payslipCount === 0
      ? null
      : run.isStale
        ? "Something this run uses changed after payroll was run. Re-run payroll before sending for approval."
        : blockingCount > 0
          ? `${blockingCount === 1 ? "1 required field needs" : `${blockingCount} required fields need`} fixing before this can be sent for approval.`
          : null;

  return (
    <div className="space-y-3">
      {/* Delete sits at the far end, away from the others. It destroys the
          month's work, and a destructive button shoulder-to-shoulder with
          the one you press every time gets hit by muscle memory. */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        {run.status === "DRAFT" ? (
          <>
            {/* On a phone the row stacks: every button goes full width so the
                column reads as one set, and Delete drops to the bottom — the
                same distance-from-the-others rule, in the other direction. */}
            <button
              type="button"
              className={`${BUTTON_DANGER} max-sm:order-last max-sm:w-full`}
              disabled={busy !== null}
              onClick={() =>
                void run_("delete", () => deletePayrollRun(run.id)).then(() => onDeleted())
              }
            >
              {spinner("delete")}
              Delete draft
            </button>

            <div className="flex flex-wrap items-center gap-3 max-sm:w-full max-sm:flex-col max-sm:items-stretch">
              <button
                type="button"
                className={BUTTON_GHOST}
                disabled={busy !== null}
                onClick={() => void handleGenerate()}
              >
                {spinner("generate")}
                {/* The previous system's words: the admin runs payroll, and
                    re-runs it after something changes. */}
                {busy === "generate"
                  ? "Running payroll…"
                  : run.generatedAt
                    ? "Re-run payroll"
                    : "Run payroll"}
              </button>

              {/* Only offered once there is something to submit. A button
                  that cannot possibly apply yet is noise, not guidance. */}
              {payslipCount > 0 ? (
                <button
                  type="button"
                  className={BUTTON}
                  disabled={busy !== null || submitBlocker !== null}
                  aria-describedby={submitBlocker ? "submit-blocker" : undefined}
                  onClick={() =>
                    void run_("submit", () => submitPayrollRun(run.id)).then(
                      (r) => r && onChanged(),
                    )
                  }
                >
                  {spinner("submit")}
                  Send for approval
                </button>
              ) : null}
            </div>
          </>
        ) : null}

        {run.status === "PENDING_APPROVAL" ? (
          <>
            <button
              type="button"
              className={BUTTON_GHOST}
              disabled={busy !== null}
              onClick={() => setRejecting(true)}
            >
              Send back to draft
            </button>

            <button
              type="button"
              className={BUTTON}
              disabled={busy !== null}
              onClick={() =>
                void run_("approve", () => approvePayrollRun(run.id)).then(
                  (r) => r && onChanged(),
                )
              }
            >
              {spinner("approve")}
              Approve and file
            </button>
          </>
        ) : null}

        {run.status === "SUBMITTED" ? (
          <button
            type="button"
            className={BUTTON_DANGER}
            disabled={busy !== null}
            onClick={() => void askRevert()}
          >
            {spinner("revert-impact")}
            Revert to draft
          </button>
        ) : null}
      </div>

      {/* Why Send for approval is greyed out, in view rather than in a hover
          title (invisible on a touch screen, easy to miss with a mouse). It
          sits on its own line under the buttons: tucked beneath the button
          itself, it pushed that button out of line with its neighbours. A
          missing field links straight to the tab that lists them. */}
      {submitBlocker ? (
        <div id="submit-blocker" role="status" className={BLOCKER_STRIP}>
          <CircleAlert className="size-4 shrink-0" aria-hidden />
          {/* A floor on the message's width, so on a narrow screen the link
              drops to its own line instead of crushing the sentence into a
              one-word column. */}
          <span className="min-w-[14rem] flex-1">{submitBlocker}</span>
          {!run.isStale ? (
            <button type="button" className={LINK_BUTTON} onClick={onViewAttention}>
              See what's missing
              <ArrowRight className="size-3.5" aria-hidden />
            </button>
          ) : null}
        </div>
      ) : null}

      {/* An empty reason is allowed — "send it back" is sometimes said in
          person — so the field is optional rather than validated. */}
      {rejecting ? (
        <div className={WARN_PANEL}>
          <label className={LABEL} htmlFor="rejectReason">
            Send {run.periodLabel} back to draft
          </label>
          <p className="mt-1 mb-2 text-xs">
            Whoever picks this run back up sees this note on it. Optional.
          </p>
          <textarea
            id="rejectReason"
            rows={2}
            maxLength={500}
            className={TEXTAREA}
            placeholder="What needs fixing?"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />

          <div className="mt-3 flex flex-wrap gap-3">
            <button
              type="button"
              className={BUTTON}
              disabled={busy !== null}
              onClick={() =>
                void run_("reject", () =>
                  rejectPayrollRun(run.id, reason.trim() || null),
                ).then((r) => {
                  if (!r) return;
                  setRejecting(false);
                  setReason("");
                  onChanged();
                })
              }
            >
              {spinner("reject")}
              Send it back
            </button>
            <button
              type="button"
              className={BUTTON_GHOST}
              onClick={() => {
                setRejecting(false);
                setReason("");
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}

      {/* Reverting a month takes every later submitted month in the same year
          with it, because their year-to-date figures were computed off it.
          Naming them before the admin commits is the whole point. */}
      {revertImpact !== null ? (
        <div className={WARN_PANEL}>
          <p className="font-semibold">
            Revert {run.periodLabel} back to draft?
          </p>

          {revertImpact.length > 0 ? (
            <p className="mt-1">
              These later months were calculated from this one's year-to-date figures and will
              go back to draft as well: <strong>{revertImpact.join(", ")}</strong>. Their
              payslips are kept, so payroll can be re-run and re-approved.
            </p>
          ) : (
            <p className="mt-1">
              No later months depend on this one. Its payslips are kept, so it can be fixed and
              re-approved.
            </p>
          )}

          <div className="mt-3 flex flex-wrap gap-3">
            <button
              type="button"
              className={BUTTON_DANGER}
              disabled={busy !== null}
              onClick={() => void confirmRevert()}
            >
              {spinner("revert")}
              Yes, revert
            </button>
            <button
              type="button"
              className={BUTTON_GHOST}
              onClick={() => setRevertImpact(null)}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
    </div>
  );
}
