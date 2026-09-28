namespace AltomateHR.Api.Modules.Payroll;

// Borang PCB/TP1 for one employee and month, and the month's list of TP1 / TP3
// claimants — the two things LHDN's MTD Spec 2026 (Section E item 16) requires
// the system to let the employer and employee print and save. Both refuse a
// run that is not approved yet; a missing run is Ok = false with no error.
public interface ITp1FormService
{
    Task<StatutoryFileResult> RenderFormAsync(string runId, string employeeProfileId);

    Task<StatutoryFileResult> RenderClaimsListAsync(string runId);
}
