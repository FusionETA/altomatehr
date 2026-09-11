using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// Attaching approved expense claims to a payroll run, so they are reimbursed
// through the employee's pay instead of through Xero.
//
// The attachment is the durable thing; the REIMBURSEMENT line item it produces
// is rebuilt on every generation. Like adjustments, every mutation here marks
// the run stale.
public interface IPayrollRunClaimService
{
    Task<List<PayrollRunClaimDto>> GetForRunAsync(string runId);

    // Claims that could go on this run, including ones already attached
    // elsewhere (flagged, so the picker can explain itself rather than just
    // omitting them).
    Task<List<AttachableClaimDto>> GetAttachableAsync();

    Task<PayrollRunClaimAttachResult> AttachAsync(string runId, string claimId);

    // Detaches from whichever run holds the claim — the caller does not have to
    // know which, and a claim can only ever be on one.
    Task<PayrollRunClaimDetachResult> DetachAsync(string claimId);
}
