using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// Staff loans and salary advances.
//
// Admin-only: a loan decides a deduction from someone's pay, and the list
// covers the whole org. An employee sees their own repayments on their
// payslip, not here.
[ApiController]
[Route("payroll/loans")]
[Authorize(Roles = "Admin,Owner")]
public class PayrollLoansController : ControllerBase
{
    private readonly IEmployeeLoanService _loans;

    public PayrollLoansController(IEmployeeLoanService loans) => _loans = loans;

    // `employeeProfileId` narrows to one person — the employee detail page.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? employeeProfileId) =>
        Ok(await _loans.GetAllAsync(employeeProfileId));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var loan = await _loans.GetAsync(id);
        return loan is null ? NotFound() : Ok(loan);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveEmployeeLoanDto dto) =>
        await Guarded(async () => Ok(await _loans.CreateAsync(dto)));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveEmployeeLoanDto dto) =>
        await Guarded(async () =>
        {
            var loan = await _loans.UpdateAsync(id, dto);
            return loan is null ? NotFound() : Ok(loan);
        });

    // Cancelling stops the deductions from the next run while leaving the ones
    // already taken explained — which is why a started loan cannot be deleted.
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        var loan = await _loans.SetStatusAsync(id, LoanStatus.CANCELLED);
        return loan is null ? NotFound() : Ok(loan);
    }

    [HttpPost("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(string id)
    {
        var loan = await _loans.SetStatusAsync(id, LoanStatus.ACTIVE);
        return loan is null ? NotFound() : Ok(loan);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) =>
        await Guarded(async () => await _loans.DeleteAsync(id) ? NoContent() : NotFound());

    // Terms that cannot be repaid, and edits to a loan already deducting, are
    // the admin's input to fix — 400 with the message, not a 500.
    private static async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PayrollLoanException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }
    }
}
