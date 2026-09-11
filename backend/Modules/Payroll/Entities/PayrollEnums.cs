namespace AltomateHR.Api.Modules.Payroll.Entities;

// How many days the org treats a month as having, for the ÷ divisor on the
// ordinary rate of pay (Employment Act s.60I). Set per-org on PayrollSettings.
//
// This is NOT the divisor for an incomplete month — s.18A overrides that with
// calendar days regardless of what is chosen here. See PayPeriod.
public enum WorkingDaysRule
{
    // Actual calendar days in the month (28–31).
    CALENDAR,

    // The fixed 26-day Malaysian convention.
    TWENTY_SIX,
}

// Where a run is in its life. Phase 3 only ever creates DRAFT runs; the
// transitions (and the locking that goes with them) land in phase 5. The full
// set is declared now so the stored string never has to be migrated.
public enum PayrollRunStatus
{
    // Editable. Payslips can be regenerated from scratch.
    DRAFT,

    // Submitted for approval — locked pending an approver's decision.
    PENDING_APPROVAL,

    // Approved and final. Feeds YTD for every later run in the same year.
    SUBMITTED,
}

// Whether the numbers on a run came from the calc engine or were typed in.
public enum PayrollRunSource
{
    // Produced by PayslipCalculator — the normal path.
    COMPUTED,

    // Seeded from a YTD migration upload (phase 8). Payslip figures are taken
    // as-entered and never recomputed.
    IMPORTED,
}

// What a payslip line does to the money.
//
//   ALLOWANCE     — adds to gross (or, when the category is non-cash, only to
//                   the PCB base).
//   DEDUCTION     — comes off take-home, or off gross for lost earnings.
//   REIMBURSEMENT — adds to gross but is not wage, so no statutory contribution
//                   is computed on it.
public enum PayslipLineKind
{
    ALLOWANCE,
    DEDUCTION,
    REIMBURSEMENT,
}
