using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// An employee's own payslips.
//
// Every read is scoped to the caller's own profile, resolved from their token.
// There is deliberately no id parameter for WHOSE payslips these are — the
// admin surfaces under /payroll/runs are where someone else's pay is read,
// and they are role-gated.
[ApiController]
[Route("payslips")]
[Authorize]
public class PayslipsController : ControllerBase
{
    private readonly IEmployeePayrollService _payroll;

    public PayslipsController(IEmployeePayrollService payroll) => _payroll = payroll;

    [HttpGet]
    public async Task<IActionResult> GetMine() => Ok(await _payroll.GetMyPayslipsAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var payslip = await _payroll.GetMyPayslipAsync(id);
        return payslip is null ? NotFound() : Ok(payslip);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> Pdf(string id)
    {
        var result = await _payroll.RenderMyPayslipPdfAsync(id);

        if (!result.Ok) return result.Error is null ? NotFound() : Conflict(new { error = result.Error });

        return File(result.Content!, result.ContentType!, result.FileName);
    }
}
