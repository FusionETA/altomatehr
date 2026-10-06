import { useEffect, useState } from "react";
import { Check, LoaderCircle } from "lucide-react";
import { setPayrollRunLhdnReceipts, type PayrollRun } from "../api";
import { BUTTON_SM, CARD, ERROR_PANEL, HINT, INPUT_SM, LABEL } from "../lib/ui";

// LHDN's receipts for the month's MTD (CP39) and CP38 payments.
//
// Paperwork about money already paid, so it appears only on an approved month
// and changes no figure. Each employee's PCB 2(II) prints these beside their
// deduction for the month.
export function PayrollRunLhdnReceipts({
  run,
  onSaved,
}: {
  run: PayrollRun;
  onSaved: () => void;
}) {
  const [form, setForm] = useState(() => fromRun(run));
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Follow the run when it reloads (another save, a revert and re-approve).
  useEffect(() => setForm(fromRun(run)), [run]);

  if (run.status !== "SUBMITTED") return null;

  // CP38 is a direct order against particular employees; most months have
  // none, and two empty fields for it would only invite a wrong entry.
  const hasCp38 = run.totalCp38 > 0 || Boolean(run.cp38ReceiptNo);
  const dirty = JSON.stringify(form) !== JSON.stringify(fromRun(run));

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => {
    setSaved(false);
    setForm((f) => ({ ...f, [key]: e.target.value }));
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      await setPayrollRunLhdnReceipts(run.id, {
        pcbReceiptNo: form.pcbReceiptNo.trim() || null,
        pcbReceiptDate: form.pcbReceiptDate || null,
        cp38ReceiptNo: form.cp38ReceiptNo.trim() || null,
        cp38ReceiptDate: form.cp38ReceiptDate || null,
      });
      setSaved(true);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Couldn't save the receipts.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className={CARD}>
      <header className="mb-4">
        <h2 className="text-base font-semibold text-foreground">LHDN payment receipts</h2>
        <p className={HINT}>
          The receipt LHDN issued when this month's PCB was paid. Printed on each employee's PCB
          2(II) statement.
        </p>
      </header>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="pcbReceiptNo" label="MTD receipt / bank slip / transaction no.">
          <input
            id="pcbReceiptNo"
            className={INPUT_SM}
            maxLength={60}
            value={form.pcbReceiptNo}
            onChange={set("pcbReceiptNo")}
          />
        </Field>
        <Field id="pcbReceiptDate" label="MTD receipt / transaction date">
          <input
            id="pcbReceiptDate"
            type="date"
            className={INPUT_SM}
            value={form.pcbReceiptDate}
            onChange={set("pcbReceiptDate")}
          />
        </Field>

        {hasCp38 ? (
          <>
            <Field id="cp38ReceiptNo" label="CP38 receipt / bank slip / transaction no.">
              <input
                id="cp38ReceiptNo"
                className={INPUT_SM}
                maxLength={60}
                value={form.cp38ReceiptNo}
                onChange={set("cp38ReceiptNo")}
              />
            </Field>
            <Field id="cp38ReceiptDate" label="CP38 receipt / transaction date">
              <input
                id="cp38ReceiptDate"
                type="date"
                className={INPUT_SM}
                value={form.cp38ReceiptDate}
                onChange={set("cp38ReceiptDate")}
              />
            </Field>
          </>
        ) : null}
      </div>

      {error ? <p className={`${ERROR_PANEL} mt-4`}>{error}</p> : null}

      <div className="mt-4 flex items-center gap-3">
        <button
          type="button"
          className={BUTTON_SM}
          disabled={!dirty || saving}
          onClick={() => void save()}
        >
          {saving ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
          Save receipts
        </button>
        {saved && !dirty ? (
          <span className="inline-flex items-center gap-1 text-sm font-medium text-muted-foreground">
            <Check className="size-4" aria-hidden />
            Saved
          </span>
        ) : null}
      </div>
    </section>
  );
}

function Field({ id, label, children }: { id: string; label: string; children: React.ReactNode }) {
  return (
    <div>
      <label htmlFor={id} className={LABEL}>
        {label}
      </label>
      {children}
    </div>
  );
}

// The API sends dates as ISO timestamps; a date input wants yyyy-mm-dd.
function fromRun(run: PayrollRun) {
  return {
    pcbReceiptNo: run.pcbReceiptNo ?? "",
    pcbReceiptDate: run.pcbReceiptDate?.slice(0, 10) ?? "",
    cp38ReceiptNo: run.cp38ReceiptNo ?? "",
    cp38ReceiptDate: run.cp38ReceiptDate?.slice(0, 10) ?? "",
  };
}
