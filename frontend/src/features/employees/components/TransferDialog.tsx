import { useState } from "react";
import { createPortal } from "react-dom";
import { ArrowRightLeft, LoaderCircle } from "lucide-react";
import { createTransfer, type EmployeeTransfer, type TransferTarget } from "../api";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";

// Move an employee to another company the admin also runs — the previous
// system's Transfer wizard.
//
// On the effective date the profile here is archived (last day = the day
// before) and the person gets a profile at the target. Personal details always
// go across; "Include payroll settings" also carries statutory numbers, bank,
// salary, allowances and this year's YTD (as previous employment, so PCB at
// the target continues rather than restarting). Teams, shift, leave balances,
// claims and documents never move — they are a new joiner there for those.

const input =
  "mt-1 h-11 w-full rounded-xl border border-border/70 bg-background px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";

// The browser's calendar day, which is what the admin means by "today".
function localToday(): string {
  const d = new Date();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${mm}-${dd}`;
}

export function TransferDialog({
  employeeId,
  employeeName,
  targets,
  onClose,
  onDone,
}: {
  employeeId: string;
  employeeName: string;
  targets: TransferTarget[];
  onClose: () => void;
  onDone: (transfer: EmployeeTransfer, executedImmediately: boolean) => void;
}) {
  useBodyScrollLock();

  const today = localToday();
  const [targetId, setTargetId] = useState(targets.length === 1 ? targets[0].id : "");
  const [policyId, setPolicyId] = useState(
    targets.length === 1 ? (targets[0].policies[0]?.id ?? "") : "",
  );
  const [effectiveDate, setEffectiveDate] = useState(today);
  const [copyPayrollInfo, setCopyPayrollInfo] = useState(true);
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const target = targets.find((t) => t.id === targetId);
  const runsNow = effectiveDate === today;
  const canSubmit = !!targetId && !!policyId && effectiveDate >= today && !busy;

  // A new company's policies are its own — preselect its default (listed first).
  function pickTarget(id: string) {
    setTargetId(id);
    setPolicyId(targets.find((t) => t.id === id)?.policies[0]?.id ?? "");
  }

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const result = await createTransfer(employeeId, {
        targetOrganizationId: targetId,
        targetPolicyId: policyId,
        effectiveDate,
        copyPayrollInfo,
        notes: notes.trim() || null,
      });
      onDone(result.transfer, result.executedImmediately);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not schedule the transfer.");
      setBusy(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-lg overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="flex items-center gap-2 text-xl font-black text-foreground">
          <ArrowRightLeft className="h-5 w-5 text-primary" />
          Transfer to another company
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Moves <span className="font-semibold text-foreground">{employeeName}</span> on the
          effective date: their profile here is archived and one is set up at the target company.
        </p>

        <label className="mt-4 block">
          <span className="text-xs font-semibold text-muted-foreground">Target company</span>
          <select value={targetId} onChange={(e) => pickTarget(e.target.value)} className={input}>
            <option value="">Select company…</option>
            {targets.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </label>

        <label className="mt-3 block">
          <span className="text-xs font-semibold text-muted-foreground">Policy at the target</span>
          <select
            value={policyId}
            onChange={(e) => setPolicyId(e.target.value)}
            disabled={!target}
            className={input}
          >
            {!target ? <option value="">Pick a company first</option> : null}
            {target?.policies.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
                {p.isDefault ? " (default)" : ""}
              </option>
            ))}
          </select>
        </label>

        <label className="mt-3 block">
          <span className="text-xs font-semibold text-muted-foreground">Effective date</span>
          <input
            type="date"
            value={effectiveDate}
            min={today}
            onChange={(e) => setEffectiveDate(e.target.value)}
            className={input}
          />
          <span className="mt-1 block text-xs text-muted-foreground">
            Their first day at the target. Today moves them now; a later date queues the transfer
            and it runs automatically that day.
          </span>
        </label>

        <div className="mt-3 rounded-2xl border border-border/60 bg-surface-low p-3">
          <p className="text-xs font-semibold text-muted-foreground">Carried over</p>
          <p className="mt-1 text-sm text-foreground">
            Personal details — contact, identity, address, family and emergency contact — always go
            across, along with job title and staff number.
          </p>
          <label className="mt-3 flex items-start gap-2">
            <input
              type="checkbox"
              checked={copyPayrollInfo}
              onChange={(e) => setCopyPayrollInfo(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-primary"
            />
            <span className="text-sm">
              <span className="font-semibold text-foreground">Include payroll settings</span>
              <span className="mt-0.5 block text-xs text-muted-foreground">
                Salary, allowances, bank account and statutory setup (EPF / SOCSO / EIS / PCB), plus
                this year's earnings so far as previous employment — so PCB at the new company
                continues instead of starting over. Untick to have the target set payroll up from
                scratch.
              </span>
            </span>
          </label>
          <p className="mt-3 text-xs text-muted-foreground">
            Not carried: teams, shift, leave balances (the target policy's entitlement starts from
            the effective date), claims and documents.
          </p>
        </div>

        <label className="mt-3 block">
          <span className="text-xs font-semibold text-muted-foreground">Notes (optional)</span>
          <input
            value={notes}
            maxLength={500}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Why the transfer — kept with the record"
            className={input}
          />
        </label>

        {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => void submit()}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            {runsNow ? "Transfer now" : "Schedule transfer"}
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-60"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
