import { useState } from "react";
import { createPortal } from "react-dom";
import { Copy, LoaderCircle } from "lucide-react";
import { duplicateEmployee, type TransferTarget } from "../api";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";

// Add the same person to another company the admin runs, while they KEEP
// working here — someone employed by two group companies at once.
//
// Unlike a transfer nothing ends here. They sign in with the same login and
// switch between the two companies. The new company gets their personal
// details and, by default, their own statutory numbers and bank account
// (those are the person's, not the employer's). Salary does not copy — each
// company pays its own — and neither does this year's pay: concurrent jobs
// are taxed by each employer separately, so there is no "previous employer".

const input =
  "mt-1 h-11 w-full rounded-xl border border-border/70 bg-background px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60";

function localToday(): string {
  const d = new Date();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${mm}-${dd}`;
}

export function DuplicateDialog({
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
  onDone: (targetName: string) => void;
}) {
  useBodyScrollLock();

  const open = targets.filter((t) => !t.employeeActiveHere);
  const [targetId, setTargetId] = useState(open.length === 1 ? open[0].id : "");
  const [policyId, setPolicyId] = useState(open.length === 1 ? (open[0].policies[0]?.id ?? "") : "");
  const [joinDate, setJoinDate] = useState(localToday());
  const [copyStatutoryAndBank, setCopyStatutoryAndBank] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const target = targets.find((t) => t.id === targetId);
  const canSubmit = !!targetId && !!policyId && !!joinDate && !busy;

  function pickTarget(id: string) {
    setTargetId(id);
    setPolicyId(targets.find((t) => t.id === id)?.policies[0]?.id ?? "");
  }

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const result = await duplicateEmployee(employeeId, {
        targetOrganizationId: targetId,
        targetPolicyId: policyId,
        joinDate,
        copyStatutoryAndBank,
      });
      onDone(result.targetOrganizationName);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not add them to that company.");
      setBusy(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-lg overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="flex items-center gap-2 text-xl font-black text-foreground">
          <Copy className="h-5 w-5 text-primary" />
          Add to another company
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          <span className="font-semibold text-foreground">{employeeName}</span> keeps working here and
          also joins the company you pick — same login, switching between the two.
        </p>

        <label className="mt-4 block">
          <span className="text-xs font-semibold text-muted-foreground">Other company</span>
          <select value={targetId} onChange={(e) => pickTarget(e.target.value)} className={input}>
            <option value="">Select company…</option>
            {targets.map((t) => (
              <option key={t.id} value={t.id} disabled={t.employeeActiveHere}>
                {t.name}
                {t.employeeActiveHere ? " (already works here)" : ""}
              </option>
            ))}
          </select>
        </label>

        <label className="mt-3 block">
          <span className="text-xs font-semibold text-muted-foreground">Policy there</span>
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
          <span className="text-xs font-semibold text-muted-foreground">Join date there</span>
          <input
            type="date"
            value={joinDate}
            onChange={(e) => setJoinDate(e.target.value)}
            className={input}
          />
        </label>

        <div className="mt-3 rounded-2xl border border-border/60 bg-surface-low p-3">
          <p className="text-xs font-semibold text-muted-foreground">Copied over</p>
          <p className="mt-1 text-sm text-foreground">
            Personal details — contact, identity, address, family and emergency contact — plus job
            title and staff number.
          </p>
          <label className="mt-3 flex items-start gap-2">
            <input
              type="checkbox"
              checked={copyStatutoryAndBank}
              onChange={(e) => setCopyStatutoryAndBank(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-primary"
            />
            <span className="text-sm">
              <span className="font-semibold text-foreground">Include statutory numbers and bank</span>
              <span className="mt-0.5 block text-xs text-muted-foreground">
                EPF, SOCSO, EIS and income tax registration, and their bank account — these are the
                person's own, so normally the same at both companies.
              </span>
            </span>
          </label>
          <p className="mt-3 text-xs text-muted-foreground">
            Not copied: salary and allowances (each company sets its own), this year's earnings
            (each employer taxes its own pay), teams, shift, leave balances and documents.
          </p>
        </div>

        {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => void submit()}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Add to company
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
