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
  getPayrollSettings,
  type PayrollRun,
} from "../api";
import { DISBURSEMENT_BANKS, formatFor } from "../lib/disbursement";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { saveFile } from "@/shared/lib/api-client";
import {
  BUTTON,
  BUTTON_GHOST,
  BUTTON_GHOST_SM,
  CARD,
  HINT,
  ICON_BUTTON,
  INPUT_SM,
  LABEL,
} from "../lib/ui";
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
  download: (runId: string, ctx: DownloadContext) => Promise<{ blob: Blob; fileName: string }>;
};

// What the bank files need beyond the run itself. Only Hong Leong reads the
// reference; the date reaches every format.
type DownloadContext = {
  paymentDate: string;
  recipientReference: string;
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
];

// The bank rows depend on the company's OWN bank, because the layouts are not
// interchangeable — a Maybank customer offered Public Bank's sheet downloads a
// file their portal rejects with nothing to say why. So the file on offer is
// derived from the setting, and when nothing is set the section says which
// setting to go and fill in.
//
// Hong Leong is the only bank with two rows: it publishes two upload portals
// taking different files, and nothing in payroll data says which one a company
// uses.
function bankItems(bankName: string | null | undefined): Item[] {
  const format = formatFor(bankName);
  const label = DISBURSEMENT_BANKS.find((b) => formatFor(b.value) === format)?.label;

  const refusedByServer =
    "Refused outright if a required setting is unset or an employee's bank name is unrecognised — silently dropping a row means someone is not paid and nobody notices.";

  switch (format) {
    case "PbEcpXlsx":
      return [
        {
          key: "bank",
          group: "BANK",
          title: "Public Bank ECP (Bulk Payroll)",
          description: `Bulk salary disbursement sheet, named the way PB's upload expects. ${refusedByServer}`,
          portal: "PB enterprise (Public Bank)",
          download: (runId, ctx) => downloadBankFile(runId, ctx.paymentDate),
        },
      ];

    case "MbbM2eTxt":
      return [
        {
          key: "bank",
          group: "BANK",
          title: "Maybank2E Universal Payment File",
          description: `Pipe-delimited bulk payment file. Maybank staff pay as an intra-bank book transfer, everyone else over IBG. ${refusedByServer}`,
          portal: "Maybank2E → Bulk Payment",
          download: (runId, ctx) => downloadBankFile(runId, ctx.paymentDate),
        },
      ];

    case "CimbBizChannelTxt":
      return [
        {
          key: "bank",
          group: "BANK",
          title: "BizChannel@CIMB Bulk Payroll",
          description: `Fixed-width file matching CIMB's BizConverter output, routed by BNM bank code. ${refusedByServer}`,
          portal: "BizChannel@CIMB → Bulk Payments",
          download: (runId, ctx) => downloadBankFile(runId, ctx.paymentDate),
        },
      ];

    case "HlbConnect":
      return [
        {
          key: "bank-hlb-first",
          group: "BANK",
          title: "HLB Connect First (Bulk Payroll)",
          description: `Fixed-width file for the Connect First portal. ${refusedByServer}`,
          portal: "HLB Connect First",
          download: (runId, ctx) =>
            downloadBankFile(runId, ctx.paymentDate, {
              channel: "ConnectFirst",
              recipientReference: ctx.recipientReference,
            }),
        },
        {
          key: "bank-hlb-biz",
          group: "BANK",
          title: "HLB ConnectBiz (CBIZ Bulk Payroll)",
          description: `HLB's own CBIZ template spreadsheet, for the ConnectBiz portal. Download whichever of the two your company submits through. ${refusedByServer}`,
          portal: "HLB ConnectBiz",
          download: (runId, ctx) =>
            downloadBankFile(runId, ctx.paymentDate, {
              channel: "ConnectBiz",
              recipientReference: ctx.recipientReference,
            }),
        },
      ];

    default:
      // "Other" and unset both produce no file. Saying so here beats a row
      // that downloads a 409 — but they are different problems, so the
      // section's own note tells them apart rather than this list.
      void label;
      return [];
  }
}

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
            {run.source === "IMPORTED"
              ? "Payslips for this run. This month came from your previous payroll "
                + "system, which owns its statutory filings and payments."
              : "Reports, statutory uploads, payslips and the bank file for this run."}
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

  // Hong Leong makes this mandatory and nothing in payroll data implies it —
  // it is what the employee sees on their own bank statement. Prefilled with
  // the period, which is what an admin types anyway, but still theirs to edit.
  const [recipientReference, setRecipientReference] = useState(
    () => `SALARY ${run.periodLabel}`.toUpperCase().slice(0, 20),
  );

  // Which bank file this org gets is a setting, so the rows are not known
  // until it loads. Cached, so reopening the modal doesn't re-fetch.
  const { data: settings } = useCachedQuery("payroll-settings", getPayrollSettings);
  const bankName = settings?.payrollBankName ?? null;

  // An imported month's figures were typed in from the previous system, not
  // calculated here. Its payslips render those figures faithfully, so they
  // stay; everything else would assert numbers this system never computed to
  // LHDN, KWSP, PERKESO or the bank, against filings and payments the old
  // system already made.
  //
  // Filtered rather than shown-and-refused: a row you can tick and download
  // only to be told no is a worse answer than not offering it. The service
  // refuses too — that is the enforcement, and it covers a typed URL; this
  // just stops the admin finding out by clicking.
  const imported = run.source === "IMPORTED";

  const items = useMemo(
    () => {
      const all = [...ITEMS, ...bankItems(bankName)];
      return imported ? all.filter((item) => item.group === "PAYSLIPS") : all;
    },
    [bankName, imported],
  );
  const needsReference = formatFor(bankName) === "HlbConnect";

  const grouped = useMemo(
    () =>
      (["REPORTS", "STATUTORY", "PAYSLIPS", "BANK"] as Group[])
        .map((group) => ({
          group,
          items: items.filter((item) => item.group === group),
        }))
        // A heading with nothing under it reads as something failing to load.
        // An imported run has only payslips, so the other three go entirely.
        .filter(({ items: rows }) => rows.length > 0),
    [items],
  );

  const context: DownloadContext = { paymentDate, recipientReference };

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
      saveFile(await item.download(run.id, context));
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

    for (const item of items.filter((entry) => picked.has(entry.key))) {
      try {
        saveFile(await item.download(run.id, context));
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

  const allPicked = picked.size > 0 && picked.size === items.length;

  return (
    <ModalPortal label={`Download files for ${run.periodLabel}`} onClose={onClose}>
      <header className="flex items-start justify-between gap-4 border-b border-border/60 p-6">
        <div>
          <h2 className="text-xl font-semibold text-foreground">
            Download files — {run.periodLabel}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {imported
              ? "Payslips for this run, rendered from the figures you imported."
              : "Reports, statutory uploads, and payslips for this run."}
          </p>
        </div>

        <button
          type="button"
          aria-label="Close"
          className={ICON_BUTTON}
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
                setPicked(allPicked ? new Set() : new Set(items.map((item) => item.key)))
              }
              ariaLabel="Select all files"
            />
            Select all ({items.length})
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
                  filename the portal parses, so it is chosen here rather than
                  silently defaulted. */}
              {group === "BANK" && items.length > ITEMS.length ? (
                <div className="flex flex-wrap items-end gap-3 rounded-xl border border-border/60 bg-muted/30 px-3 py-2">
                  <div className="w-44">
                    <label className={`${LABEL} text-xs`} htmlFor="paymentDate">
                      Payment date
                    </label>
                    <input
                      id="paymentDate"
                      type="date"
                      className={INPUT_SM}
                      value={paymentDate}
                      onChange={(e) => setPaymentDate(e.target.value)}
                    />
                  </div>

                  {/* Hong Leong refuses the file without one, so it is asked
                      for here rather than discovered as a 409. */}
                  {needsReference ? (
                    <div className="w-56">
                      <label className={`${LABEL} text-xs`} htmlFor="recipientReference">
                        Recipient reference
                      </label>
                      <input
                        id="recipientReference"
                        type="text"
                        maxLength={20}
                        className={INPUT_SM}
                        value={recipientReference}
                        onChange={(e) => setRecipientReference(e.target.value)}
                      />
                      <p className={`${HINT} text-[11px]`}>
                        What your employees see on their bank statement. Max 20 characters.
                      </p>
                    </div>
                  ) : null}
                </div>
              ) : null}

              {/* No bank file at all. Which of the two reasons it is matters:
                  one is a setting nobody filled in, the other is a decision
                  already taken. */}
              {group === "BANK" && items.length === ITEMS.length ? (
                <p className="rounded-xl border border-border/60 bg-muted/30 px-3 py-2.5 text-xs text-muted-foreground">
                  {bankName
                    ? "This company's payroll bank produces no bulk-upload file. Pay the salaries through your bank's own process — the Payment Schedule above lists every account and amount."
                    : "No payroll bank is set, so there is no upload file to generate. Choose one under Payroll Settings → Company Info — it decides which bank's file a run produces."}
                </p>
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
                      className={`${BUTTON_GHOST_SM} shrink-0`}
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
