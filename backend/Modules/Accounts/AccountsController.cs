using AltomateHR.Api.Modules.Accounts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Accounts;

[ApiController]
[Route("[controller]")]        // → /accounts
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly IChartOfAccountService _accounts;

    public AccountsController(IChartOfAccountService accounts) => _accounts = accounts;

    // GET /accounts — any authenticated user (employees pick a selectable account when filing a claim).
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _accounts.GetAllAsync());

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveChartOfAccountDto dto) =>
        Ok(await _accounts.CreateAsync(dto));

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveChartOfAccountDto dto)
    {
        var account = await _accounts.UpdateAsync(id, dto);
        return account is null ? NotFound() : Ok(account);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var account = await _accounts.SetArchivedAsync(id, true);
        return account is null ? NotFound() : Ok(account);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var account = await _accounts.SetArchivedAsync(id, false);
        return account is null ? NotFound() : Ok(account);
    }
}
