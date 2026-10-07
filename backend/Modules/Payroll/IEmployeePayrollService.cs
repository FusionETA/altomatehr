using AltomateHR.Api.Modules.Payroll.Dtos;

namespace AltomateHR.Api.Modules.Payroll;

// An employee's view of their own pay.
//
// Every read here is scoped to the CALLER, resolved from their token — never
// from an id in the request. An employee asking for someone else's payslip by
// guessing an id gets a 404, not a 403, because confirming a payslip exists is
// already more than they should learn.
public interface IEmployeePayrollService
{
    // The caller's payslips, newest first. Only SUBMITTED runs: a draft is
    // still being edited, and showing a figure that may still move is worse
    // than showing nothing yet.
    Task<IReadOnlyList<EmployeePayslipSummaryDto>> GetMyPayslipsAsync();

    // Null when it does not exist, is not the caller's, or its run has not
    // been submitted — the three cases are deliberately indistinguishable.
    Task<PayslipDto?> GetMyPayslipAsync(string payslipId);

    // The same PDF the admin can download for them.
    Task<StatutoryFileResult> RenderMyPayslipPdfAsync(string payslipId);

    // The caller's own Borang PCB/TP1 for that payslip's month — "employee
    // also can print and save these TP Form" (MTD Spec 2026, item 16).
    Task<StatutoryFileResult> RenderMyTp1FormAsync(string payslipId);

    // The years the caller was paid in, newest first, each saying whether
    // that year's Form EA can be downloaded yet.
    Task<IReadOnlyList<EmployeeEaFormDto>> GetMyEaFormsAsync();

    // The caller's own Form EA for the year — their page of the admin's bulk
    // EA. Refused until all twelve months of the year are approved (EaYear).
    Task<StatutoryFileResult> RenderMyEaFormAsync(int year);
}
