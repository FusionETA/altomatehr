// Which bulk-payroll upload file a company's disbursement bank takes.
//
// Mirrors the backend's PayrollDisbursement — the `value`s are stored verbatim
// on PayrollSettings.payrollBankName and matched there, so the two lists have
// to stay in step. The server is still the authority: it refuses rather than
// rendering another bank's layout, and this only decides what the UI offers
// and asks for.
export type PayrollFileFormat =
  | "PbEcpXlsx"
  | "MbbM2eTxt"
  | "CimbBizChannelTxt"
  | "HlbConnect";

// Stored when the company banks somewhere no upload file is generated for.
// Deliberately distinct from null: null means nobody has configured this yet,
// while "Other" records that an admin looked at the list and none applied.
export const OTHER_BANK = "Other";

// One option per FORMAT, not per legal entity: a bank's Islamic arm files
// identically to its parent, so offering both would ask an admin to choose
// between two answers that produce the same file.
export const DISBURSEMENT_BANKS: {
  value: string;
  label: string;
  format: PayrollFileFormat | null;
}[] = [
  {
    value: "Malayan Banking Berhad",
    label: "Maybank (incl. Maybank Islamic)",
    format: "MbbM2eTxt",
  },
  {
    value: "CIMB Bank Berhad",
    label: "CIMB (incl. CIMB Islamic)",
    format: "CimbBizChannelTxt",
  },
  {
    value: "Public Bank Berhad",
    label: "Public Bank (incl. Public Islamic)",
    format: "PbEcpXlsx",
  },
  {
    value: "Hong Leong Bank Berhad",
    label: "Hong Leong Bank (incl. Hong Leong Islamic)",
    format: "HlbConnect",
  },
  { value: OTHER_BANK, label: "Other bank (no upload file)", format: null },
];

// Null for a bank with no format — including "Other", and including a name
// stored before this list existed. Matched case-insensitively because an
// earlier build of the picker stored "OTHER".
export function formatFor(bankName: string | null | undefined) {
  const name = bankName?.trim().toLowerCase();
  if (!name) return null;
  return (
    DISBURSEMENT_BANKS.find((bank) => bank.value.toLowerCase() === name)?.format ?? null
  );
}
