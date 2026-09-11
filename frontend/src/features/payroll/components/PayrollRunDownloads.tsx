import { useMemo, useState } from "react";
import { Download, FileText, LoaderCircle, X } from "lucide-react";
import {
  downloadAllPayslips,
  downloadBankFile,
  downloadEpfCsv,
  downloadPaymentSchedule,
  downloadPayrollSummary,
  downloadPcbDetails,
  downloadPcbTxt,
  downloadPerkesoTxt,
  type PayrollRun,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { BUTTON, BUTTON_GHOST, CARD, HINT, INPUT_SM, LABEL } from "../lib/ui";
import { CheckBox } from "./PayrollCheckbox";
import { ModalPortal } from "./ModalPortal";

// Everything a generated run produces, behind one button.
//
// A modal rather than a section on the page: these are needed once, at the
// end of the month, and eight cards permanently parked under the payslips
// pushed the run's actual figures off the screen.
//
// The groups are the audiences — reports get read, statutory files get
// uploaded to a named portal, payslips go to people, and the bank file moves
// money. Each statutory file is rejected outright if a field is missing,
// which is why a refusal here names the field rather than failing generically.
type Group = "REPORTS" | "STATUTORY" | "PAYSLIPS" | "BANK";

const GROUP_LABELS: Record<Group, string> = {
  REPORTS: "Reports",
  STATUTORY: "Statutory uploads",
  PAYSLIPS: "Payslips",
  BANK: "Bank",
};

type Item = {
  key: string;
  group: Group;
  title: string;
  description: string;
  // Where it is uploaded, when it is uploaded anywhere. Shown beside the
  // title so an admin can find the right file for the portal they have open.
  portal: string | null;
  download: (runId: string, paymentDate: string) => Promise<{ blob: Blob; fileName: string }>;
};

const ITEMS: Item[] = [
  {
    key: "summary",
    group: "REPORTS",
    title: "Payroll Summary",
    description:
      "Internal one-pager of run totals — gross, net, EPF, SOCSO, EIS, PCB, HRDF, headcount.",
    portal: null,
    download: (runId) => downloadPayrollSummary(runId),
  },
  {
    key: "schedule",
    group: "REPORTS",
    title: "Payment Schedule",
    description:
      "Per-employee net pay and statutory remittances. Shows each account in full, and flags anyone the bank file cannot pay.",
    portal: null,
    download: (runId) => downloadPaymentSchedule(runId),
  },
  {
    key: "pcb-details",
    group: "REPORTS",
    title: "PCB Calculation Details",
    description:
      "The LHDN MTD §E worksheet for each employee — numbered sections with LHDN's own variable names, audit-ready. One PDF covering the run.",
    portal: null,
    download: (runId) => downloadPcbDetails(runId),
  },
  {
    key: "epf",
    group: "STATUTORY",
    title: "EPF Contribution CSV",
    description:
      "Bulk-upload CSV with EPF number, IC, name, wage, and both contributions.",
    portal: "KWSP i-Akaun (Majikan)",
    download: (runId) => downloadEpfCsv(runId),
  },
  {
    key: "perkeso",
    group: "STATUTORY",
    title: "SOCSO + EIS Contribution TXT",
    description:
      "The combined PERKESO upload, 278-char fixed width. Which layout it uses is decided by the PERIOD — SKBBK from June 2026 — so an older month files under the rules it was paid under.",
    portal: "PERKESO ASSIST",
    download: (runId) => downloadPerkesoTxt(runId),
  },
  {
    key: "pcb",
    group: "STATUTORY",
    title: "PCB / MTD TXT",
    description: "Monthly tax deduction remittance, LHDN's CP39 fixed-width format.",
    portal: "LHDN e-PCB",
    download: (runId) => downloadPcbTxt(runId),
  },
  {
    key: "payslips",
    group: "PAYSLIPS",
    title: "Bulk Payslips (ZIP of individual PDFs)",
    description:
      "One PDF per employee, so payroll can forward each payslip individually without splitting a combined file.",
    portal: null,
    download: (runId) => downloadAllPayslips(runId),
  },
  {
    key: "bank",
    group: "BANK",
    title: "Public Bank ECP (Bulk Payroll)",
    description:
      "Bulk salary disbursement sheet. Named the way PB's upload expects, and refused outright if the payor account is unset or a bank name is unrecognised — silently dropping a row means someone is not paid and nobody notices.",
    portal: "PB enterprise (Public Bank)",
    download: (runId, paymentDate) => downloadBankFile(runId, paymentDate),
  },
];

export function PayrollRunDownloads({
  run,
  generated,
}: {
  run: PayrollRun;
  generated: boolean;
}) {
  const [open, setOpen] = useState(false);

  // Approval is the point the figures stop moving. Until then a regenerate
  // rebuilds every payslip, so anything cut from the run is a filing the org
  // cannot stand behind — the service refuses too, this just says so before
  // the click.
  const approved = run.status === "SUBMITTED";

  // Hidden outright on a draft rather than shown as a disabled card. The
  // run's own status and action buttons already say where it is; a panel
  // explaining why it is empty is noise on the screen an admin spends the
  // month in. It appears when approval makes the files real.
  if (!generated || !approved) return null;

  return (
    <>
      <section className={`${CARD} flex flex-wrap items-center justify-between gap-4`}>
        <div>
          <h2 className="text-base font-semibold text-foreground">Documents and files</h2>
          <p className={HINT}>
            Reports, statutory uploads, payslips and the bank file for this run.
          </p>
        </div>

        <button type="button" className={BUTTON} onClick={() => setOpen(true)}>
          <Download className="size-4" aria-hidden />
          Download files
        </button>
      </section>

      {open ? <DownloadsModal run={run} onClose={() => setOpen(false)} /> : null}
    </>
  );
}

function DownloadsModal({ run, onClose }: { run: PayrollRun; onClose: () => void }) {
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState<string | null>(null);
  // Keyed by item, because a batch half-succeeds routinely: the EPF file
  // comes down while the CP39 refuses for a missing tax number, and one
  // shared error line would hide which needs fixing.
  const [errors, setErrors] = useState<Record<string, string>>({});

  // The bank file embeds a value date, so it is picked rather than assumed.
  // Defaults to the last day of the period — the conventional pay date, and
  // what the server falls back to.
  const [paymentDate, setPaymentDate] = useState(() =>
    new Date(Date.UTC(run.periodYear, run.periodMonth, 0)).toISOString().slice(0, 10),
  );

  const grouped = useMemo(
    () =>
      (["REPORTS", "STATUTORY", "PAYSLIPS", "BANK"] as Group[]).map((group) => ({
        group,
        items: ITEMS.filter((item) => item.group === group),
      })),
    [],
  );

  function toggle(key: string) {
    setPicked((current) => {
      const next = new Set(current);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }

  async function get(item: Item) {
    setBusy(item.key);
    setErrors((current) => {
      const { [item.key]: _gone, ...rest } = current;
      return rest;
    });

    try {
      saveFile(await item.download(run.id, paymentDate));
    } catch (err) {
      setErrors((current) => ({
        ...current,
        [item.key]:
          err instanceof Error ? err.message : `Could not produce the ${item.title}.`,
      }));
    } finally {
      setBusy(null);
    }
  }

  // One at a time rather than in parallel: each is a server-side render, and
  // eight at once is how a month-end download times one of them out.
  async function getPicked() {
    setBusy("batch");
    setErrors({});

    for (const item of ITEMS.filter((entry) => picked.has(entry.key))) {
      try {
        saveFile(await item.download(run.id, paymentDate));
      } catch (err) {
        setErrors((current) => ({
          ...current,
          [item.key]:
            err instanceof Error ? err.message : `Could not produce the ${item.title}.`,
        }));
      }
    }

    setBusy(null);
  }

  const allPicked = picked.size === ITEMS.length;

  return (
    <ModalPortal label={`Download files for ${run.periodLabel}`} onClose={onClose}>
      <header className="flex items-start justify-between gap-4 border-b border-border/60 p-6">
        <div>
          <h2 className="text-xl font-semibold text-foreground">
            Download files — {run.periodLabel}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Reports, statutory uploads, and payslips for this run.
          </p>
        </div>

        <button
          type="button"
          aria-label="Close"
          className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
          onClick={onClose}
        >
          <X className="size-4" aria-hidden />
        </button>
      </header>

      <div className="flex-1 overflow-y-auto p-6">
        <div className="mb-5 flex items-center justify-between gap-2 rounded-xl border border-border/60 bg-muted/30 px-3 py-2 text-xs">
          <label className="flex cursor-pointer items-center gap-2 font-medium text-foreground">
            <CheckBox
              checked={allPicked}
              onChange={() =>
                setPicked(allPicked ? new Set() : new Set(ITEMS.map((item) => item.key)))
              }
              ariaLabel="Select all files"
            />
            Select all ({ITEMS.length})
          </label>
          <span className="text-muted-foreground">
            {picked.size > 0 ? `${picked.size} selected` : "none"}
          </span>
        </div>

        <div className="space-y-5">
          {grouped.map(({ group, items }) => (
            <section key={group} className="space-y-2">
              <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {GROUP_LABELS[group]}
              </h3>

              {/* The bank file writes this date into the sheet and into the
                  filename PB parses, so it is chosen here rather than
                  silently defaulted. */}
              {group === "BANK" ? (
                <div className="flex flex-wrap items-center gap-3 rounded-xl border border-border/60 bg-muted/30 px-3 py-2">
                  <label className={`${LABEL} mb-0 text-xs`} htmlFor="paymentDate">
                    Payment date
                  </label>
                  <div className="w-44">
                  <input
                    id="paymentDate"
                    type="date"
                    className={INPUT_SM}
                    value={paymentDate}
                    onChange={(e) => setPaymentDate(e.target.value)}
                  />
                  </div>
                </div>
              ) : null}

              <ul className="space-y-2">
                {items.map((item) => (
                  <li
                    key={item.key}
                    className="flex items-start justify-between gap-3 rounded-xl border border-border/60 bg-card/40 px-3 py-2.5"
                  >
                    <div className="flex min-w-0 items-start gap-3">
                      <span className="mt-0.5">
                        <CheckBox
                          checked={picked.has(item.key)}
                          onChange={() => toggle(item.key)}
                          ariaLabel={`Select ${item.title}`}
                        />
                      </span>
                      <FileText
                        className="mt-1 size-4 shrink-0 text-muted-foreground"
                        aria-hidden
                      />

                      <div className="min-w-0">
                        <p className="text-sm font-medium text-foreground">
                          {item.title}
                          {item.portal ? (
                            <span className="ml-2 text-xs font-normal text-muted-foreground">
                              → {item.portal}
                            </span>
                          ) : null}
                        </p>
                        <p className="mt-0.5 text-xs text-muted-foreground">
                          {item.description}
                        </p>
                        {errors[item.key] ? (
                          <p className="mt-1 text-xs font-medium text-destructive">
                            {errors[item.key]}
                          </p>
                        ) : null}
                      </div>
                    </div>

                    <button
                      type="button"
                      className={`${BUTTON_GHOST} h-8 shrink-0 gap-1.5 rounded-lg px-3 text-xs`}
                      disabled={busy !== null}
                      onClick={() => void get(item)}
                    >
                      {busy === item.key ? (
                        <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
                      ) : (
                        <Download className="size-3.5" aria-hidden />
                      )}
                      Download
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      </div>

      <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-border/60 p-6">
        <button
          type="button"
          className={BUTTON}
          disabled={picked.size === 0 || busy !== null}
          onClick={() => void getPicked()}
        >
          {busy === "batch" ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Download className="size-4" aria-hidden />
          )}
          {picked.size === 0 ? "Download selected" : `Download ${picked.size} selected`}
        </button>

        <button type="button" className={BUTTON_GHOST} onClick={onClose}>
          Close
        </button>
      </footer>
    </ModalPortal>
  );
}
