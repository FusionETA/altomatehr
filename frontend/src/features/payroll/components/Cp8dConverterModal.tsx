import { useState } from "react";
import { Download, LoaderCircle, Plus, Trash2 } from "lucide-react";
import { convertCp8d, type Cp8dConvertRow } from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { BUTTON, BUTTON_GHOST, HINT, INPUT, LABEL } from "../lib/ui";
import { ModalPortal } from "./ModalPortal";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

// CP8D converter.
//
// The downloads on the Annual forms page build the M + P pair from real payroll.
// This builds the same pair from rows typed by hand, for the years this system
// did not run: a mid-year cutover, a one-off correction for a single employee,
// or a dry run against LHDN's upload portal before the first Jan–Dec cycle.
//
// The TXT is rendered SERVER-side, by the same Cp8dTxt the real downloads use.
// The reference implementation in ClaimGuard builds it in the browser and
// carries a comment asking whoever edits it to keep the columns in step with
// the server renderer — two copies of a byte-exact contract that a filing
// depends on. Posting the rows instead means there is only ever one.

type Row = Cp8dConvertRow & { id: string };

// Date.now()/Math.random() would give StrictMode's double mount duplicate keys.
let rowCounter = 0;
function newRow(): Row {
  rowCounter += 1;
  return {
    id: `cp8d-${rowCounter}`,
    name: "",
    taxRef: "",
    newIc: "",
    category: "1",
    taxBorneByEmployer: false,
    children: 0,
    childRelief: 0,
    annualGross: 0,
    epf: 0,
    pcb: 0,
  };
}

const CELL =
  "w-full rounded-lg border border-transparent bg-transparent px-2 py-1 text-xs text-foreground outline-none focus:border-border focus:bg-background";
// The shared trigger defaults to a full-height form control (h-12, rounded-2xl,
// solid background). In this grid it sits beside CELL text inputs, so it is cut
// down to match them — the dropdown itself is the standard one.
const CELL_SELECT =
  "h-auto w-full gap-1 rounded-lg border-transparent bg-transparent px-2 py-1 text-xs shadow-none hover:border-border data-[state=open]:ring-0";
const TH = "px-2 py-2 text-left text-[11px] font-bold uppercase tracking-[0.12em]";

export function Cp8dConverterModal({
  defaultEmployerNo,
  defaultEmployerName,
  defaultYear,
  onClose,
}: {
  defaultEmployerNo?: string;
  defaultEmployerName?: string;
  defaultYear: number;
  onClose: () => void;
}) {
  const [employerNo, setEmployerNo] = useState(defaultEmployerNo ?? "");
  const [employerName, setEmployerName] = useState(defaultEmployerName ?? "");
  const [year, setYear] = useState(defaultYear);
  const [rows, setRows] = useState<Row[]>([newRow()]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const patch = (id: string, next: Partial<Row>) =>
    setRows((current) => current.map((r) => (r.id === id ? { ...r, ...next } : r)));

  const removeRow = (id: string) =>
    setRows((current) => (current.length === 1 ? [newRow()] : current.filter((r) => r.id !== id)));

  // A row with neither a name nor a tax reference is an empty slot the admin
  // left behind, not an employee — dropped rather than rejected. One that has
  // identity but no money is kept: LHDN sometimes wants a zero-PCB row to
  // confirm the person was on payroll.
  const populated = rows.filter((r) => r.name.trim() || r.taxRef.trim());

  async function generate() {
    setError(null);

    if (populated.length === 0) {
      setError("Add at least one employee row.");
      return;
    }
    const incomplete = populated.findIndex((r) => !r.name.trim() || !r.taxRef.trim() || !r.newIc.trim());
    if (incomplete !== -1) {
      setError(`Row ${incomplete + 1} needs a name, tax reference and IC.`);
      return;
    }

    setBusy(true);
    try {
      saveFile(
        await convertCp8d({
          employerNo,
          employerName,
          year,
          // Strip the local row id — the server's DTO doesn't carry one.
          employees: populated.map(({ id: _id, ...row }) => row),
        }),
      );
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not build the CP8D files.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <ModalPortal label="CP8D converter" onClose={onClose}>
      <header className="border-b border-border/70 px-6 py-4">
        <h2 className="text-base font-semibold text-foreground">CP8D converter</h2>
        <p className={HINT}>
          Type the rows by hand and download the M (employer master) and P (employee particulars)
          pair as a ZIP, ready for LHDN&apos;s e-CP8D upload. For a mid-year cutover, a one-off
          correction, or testing the portal before a full Jan–Dec cycle.
        </p>
      </header>

      <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
        <section className="space-y-2">
          <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
            Employer master (M file)
          </h3>
          <div className="grid gap-3 sm:grid-cols-3">
            <div>
              <label className={LABEL} htmlFor="cp8dEmployerNo">
                Employer E-number
              </label>
              <input
                id="cp8dEmployerNo"
                className={INPUT}
                value={employerNo}
                onChange={(e) => setEmployerNo(e.target.value)}
                placeholder="e.g. 1234567890"
                inputMode="numeric"
              />
            </div>
            <div>
              <label className={LABEL} htmlFor="cp8dEmployerName">
                Employer name
              </label>
              <input
                id="cp8dEmployerName"
                className={INPUT}
                value={employerName}
                onChange={(e) => setEmployerName(e.target.value)}
                placeholder="DEMO SDN BHD"
              />
            </div>
            <div>
              <label className={LABEL} htmlFor="cp8dYear">
                Year of remuneration
              </label>
              <input
                id="cp8dYear"
                type="number"
                min={2000}
                max={2100}
                className={INPUT}
                value={year}
                onChange={(e) => setYear(Number(e.target.value))}
              />
            </div>
          </div>
        </section>

        <section className="space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
              Employee particulars (P file)
            </h3>
            <button
              type="button"
              onClick={() => setRows((c) => [...c, newRow()])}
              className="inline-flex items-center gap-1.5 rounded-xl border border-border/70 bg-card px-3 py-1.5 text-xs font-bold text-foreground hover:bg-muted"
            >
              <Plus className="size-3.5" aria-hidden />
              Add row
            </button>
          </div>

          <div className="overflow-x-auto rounded-2xl border border-border/60 bg-card">
            <table className="w-full min-w-[1100px] text-xs">
              <thead className="text-muted-foreground">
                <tr className="border-b border-border/60">
                  <th className={`${TH} w-10 text-center`}>#</th>
                  <th className={TH}>Name *</th>
                  <th className={TH}>Tax ref *</th>
                  <th className={TH}>New IC *</th>
                  <th className={`${TH} w-28`}>Category *</th>
                  <th className={`${TH} w-24`}>Tax borne</th>
                  <th className={`${TH} w-20 text-right`}>Children</th>
                  <th className={`${TH} text-right`}>Child relief</th>
                  <th className={`${TH} text-right`}>Gross *</th>
                  <th className={`${TH} text-right`}>EPF</th>
                  <th className={`${TH} text-right`}>PCB</th>
                  <th className={`${TH} w-10`}>
                    <span className="sr-only">Remove</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={row.id} className="border-b border-border/60 last:border-0">
                    <td className="px-2 py-1.5 text-center text-muted-foreground">{index + 1}</td>
                    <td className="px-2 py-1.5">
                      <input
                        className={CELL}
                        value={row.name}
                        onChange={(e) => patch(row.id, { name: e.target.value })}
                        placeholder="AHMAD BIN ALI"
                        aria-label={`Row ${index + 1} name`}
                      />
                    </td>
                    <td className="px-2 py-1.5">
                      <input
                        className={CELL}
                        value={row.taxRef}
                        onChange={(e) => patch(row.id, { taxRef: e.target.value })}
                        placeholder="SG12345678"
                        aria-label={`Row ${index + 1} tax reference`}
                      />
                    </td>
                    <td className="px-2 py-1.5">
                      <input
                        className={CELL}
                        value={row.newIc}
                        onChange={(e) => patch(row.id, { newIc: e.target.value })}
                        placeholder="12 digits, no dashes"
                        inputMode="numeric"
                        aria-label={`Row ${index + 1} IC`}
                      />
                    </td>
                    <td className="px-2 py-1.5">
                      <Select
                        value={row.category}
                        onValueChange={(next) =>
                          patch(row.id, { category: next as Row["category"] })
                        }
                      >
                        <SelectTrigger className={CELL_SELECT} aria-label={`Row ${index + 1} category`}>
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="1">1 — Single</SelectItem>
                          <SelectItem value="2">2 — Married, sole earner</SelectItem>
                          <SelectItem value="3">3 — Both working / other</SelectItem>
                        </SelectContent>
                      </Select>
                    </td>
                    <td className="px-2 py-1.5">
                      <Select
                        value={row.taxBorneByEmployer ? "1" : "2"}
                        onValueChange={(next) =>
                          patch(row.id, { taxBorneByEmployer: next === "1" })
                        }
                      >
                        <SelectTrigger
                          className={CELL_SELECT}
                          aria-label={`Row ${index + 1} tax borne by employer`}
                        >
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="2">No</SelectItem>
                          <SelectItem value="1">Yes</SelectItem>
                        </SelectContent>
                      </Select>
                    </td>
                    <NumberCell
                      label={`Row ${index + 1} children`}
                      value={row.children}
                      onChange={(v) => patch(row.id, { children: v })}
                    />
                    <NumberCell
                      label={`Row ${index + 1} child relief`}
                      value={row.childRelief}
                      onChange={(v) => patch(row.id, { childRelief: v })}
                    />
                    <NumberCell
                      label={`Row ${index + 1} gross`}
                      value={row.annualGross}
                      onChange={(v) => patch(row.id, { annualGross: v })}
                    />
                    <NumberCell
                      label={`Row ${index + 1} EPF`}
                      value={row.epf}
                      onChange={(v) => patch(row.id, { epf: v })}
                    />
                    <NumberCell
                      label={`Row ${index + 1} PCB`}
                      value={row.pcb}
                      onChange={(v) => patch(row.id, { pcb: v })}
                    />
                    <td className="px-2 py-1.5 text-center">
                      <button
                        type="button"
                        onClick={() => removeRow(row.id)}
                        aria-label={`Remove row ${index + 1}`}
                        className="rounded-lg p-1 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                      >
                        <Trash2 className="size-3.5" aria-hidden />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <p className="text-[11px] leading-snug text-muted-foreground">
            Category 1 = single · 2 = married, sole earner · 3 = both spouses working, divorced,
            widowed, or single with children. Amounts go out as whole ringgit except PCB, which
            keeps two decimals. Rows with no name and no tax reference are skipped.
          </p>
        </section>

        {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
      </div>

      <footer className="flex items-center justify-between gap-2 border-t border-border/70 px-6 py-4">
        <span className="text-xs text-muted-foreground">
          {populated.length} employee row{populated.length === 1 ? "" : "s"}
        </span>
        <div className="flex gap-2">
          <button type="button" className={BUTTON_GHOST} onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button type="button" className={BUTTON} onClick={() => void generate()} disabled={busy}>
            {busy ? (
              <LoaderCircle className="size-4 animate-spin" aria-hidden />
            ) : (
              <Download className="size-4" aria-hidden />
            )}
            {busy ? "Building…" : "Download ZIP"}
          </button>
        </div>
      </footer>
    </ModalPortal>
  );
}

// Empty reads as 0 rather than NaN — an admin clearing a cell means "nothing
// here", and NaN would post as null and fail validation with a vague message.
function NumberCell({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (next: number) => void;
}) {
  return (
    <td className="px-2 py-1.5">
      <input
        type="number"
        min={0}
        step="0.01"
        className={`${CELL} text-right tabular-nums`}
        value={value}
        onChange={(e) => onChange(Number(e.target.value) || 0)}
        aria-label={label}
      />
    </td>
  );
}
