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

    // Detaches the claim from `runId`. A claim can only ever be on one run, but
    // the route names the run, so a claim held by a DIFFERENT run is "not found"
    // rather than silently taken off whichever run actually has it.
    Task<PayrollRunClaimDetachResult> DetachAsync(string runId, string claimId);
}
