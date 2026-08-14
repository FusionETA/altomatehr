using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Claims;

// Business logic: DTO → entity mapping + business rules.
public class ClaimsService : IClaimsService
{
    private const ApprovalModule Module = ApprovalModule.CLAIMS;

    private readonly IClaimsRepository _repo;
    private readonly IClaimReceiptStorage _receiptStorage;
    private readonly IChartOfAccountService _accounts;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;

    public ClaimsService(
        IClaimsRepository repo,
        IClaimReceiptStorage receiptStorage,
        IChartOfAccountService accounts,
        ISupervisionService supervision,
        IApprovalRouter router)
    {
        _repo = repo;
        _receiptStorage = receiptStorage;
        _accounts = accounts;
        _supervision = supervision;
        _router = router;
    }

    // The caller's own claims.
    public async Task<IEnumerable<Claim>> GetMineAsync(string userId) =>
        await _repo.GetByEmployeeIdAsync(userId);

    // Claims the caller can act on: an org approver sees the whole org; a
    // supervisor sees only their direct reports. Each row is labelled with the
    // applicant's email so the approver knows who filed it.
    public async Task<IEnumerable<Claim>> GetTeamAsync(string userId, string? role)
    {
        var all = await _repo.GetAllAsync();
        List<Claim> claims;
        if (_router.IsOrgApprover(role))
        {
            claims = all;
        }
        else
        {
            claims = [];
            foreach (var c in all.Where(c => c.Status == ClaimStatus.PENDING))
            {
                var approvers = await _router.CurrentApproversAsync(Module, c.EmployeeId, c.CurrentStep);
                if (approvers.Contains(userId)) claims.Add(c);
            }
        }

        var emails = await _supervision.GetEmailsAsync(claims.Select(c => c.EmployeeId).Distinct());
        foreach (var c in claims)
            c.EmployeeEmail = emails.GetValueOrDefault(c.EmployeeId);
        return claims;
    }

    public Task<Claim?> GetByIdAsync(string id) => _repo.GetByIdAsync(id);

    public async Task<Claim?> GetVisibleByIdAsync(string id, string userId, bool isAdmin)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null) return null;
        return isAdmin || claim.EmployeeId == userId ? claim : null;
    }

    public async Task<Claim> CreateAsync(CreateClaimDto dto, string employeeId)
    {
        var now = DateTime.UtcNow;

        // If filed against an account with a spend limit, flag over-limit claims.
        // They are still saved (employees may submit over-limit); admins see the flag.
        var exceedsLimit = false;
        if (!string.IsNullOrEmpty(dto.ChartOfAccountId))
        {
            var account = await _accounts.GetByIdAsync(dto.ChartOfAccountId);
            if (account?.LimitAmount is decimal limit && dto.Amount > limit)
                exceedsLimit = true;
        }

        var claim = new Claim
        {
            ClaimNumber = GenerateClaimNumber(),   // server-generated
            Title = dto.Title,
            Description = dto.Description,
            Category = dto.Category,
            Amount = dto.Amount,
            Currency = dto.Currency,
            SpentAt = dto.SpentAt!.Value,
            SubmittedAt = now,
            Status = ClaimStatus.PENDING,          // business rule: new claims start PENDING
            ClaimType = dto.ClaimType,
            PaymentType = dto.PaymentType,
            EmployeeId = employeeId,
            ProjectId = dto.ProjectId,
            ChartOfAccountId = dto.ChartOfAccountId,
            ExceedsLimit = exceedsLimit,
            ReceiptUrl = dto.ReceiptUrl,
            CreatedAt = now,
            UpdatedAt = now,
        };
        return await _repo.AddAsync(claim);
    }

    public async Task<bool> UpdateAsync(string id, CreateClaimDto dto, string userId, bool isAdmin)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null) return false;
        if (!isAdmin && claim.EmployeeId != userId) return false;

        claim.Title = dto.Title;
        claim.Description = dto.Description;
        claim.Category = dto.Category;
        claim.Amount = dto.Amount;
        claim.Currency = dto.Currency;
        claim.SpentAt = dto.SpentAt!.Value;
        claim.ClaimType = dto.ClaimType;
        claim.PaymentType = dto.PaymentType;
        claim.ReceiptUrl = dto.ReceiptUrl;
        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return true;
    }

    public Task<bool> DeleteAsync(string id) => _repo.DeleteAsync(id);

    public async Task<ClaimStatusTransitionResult> ApproveAsync(string id, string approverId, string? role)
    {
        var (claim, error) = await AuthorizeAsync(id, approverId, role);
        if (error is not null) return error;

        var stepCount = await _router.StepCountAsync(Module, claim!.EmployeeId);
        var isFinal = _router.IsOrgApprover(role) || claim.CurrentStep + 1 >= stepCount;
        if (isFinal)
            claim.Status = ClaimStatus.APPROVED;
        else
            claim.CurrentStep += 1;   // advance to the next step; stays PENDING

        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return new ClaimStatusTransitionResult(true, true, claim);
    }

    public async Task<ClaimStatusTransitionResult> RejectAsync(string id, string approverId, string? role, string? reviewNotes)
    {
        var (claim, error) = await AuthorizeAsync(id, approverId, role);
        if (error is not null) return error;

        claim!.Status = ClaimStatus.REJECTED;
        claim.ReviewNotes = reviewNotes;
        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return new ClaimStatusTransitionResult(true, true, claim);
    }

    public Task<ClaimReceiptUploadResult> StoreReceiptAsync(ClaimReceiptUpload upload) =>
        _receiptStorage.StoreAsync(upload);

    public async Task<ClaimReceiptFileResult?> GetReceiptForUserAsync(
        string fileName,
        string userId,
        bool isAdmin)
    {
        var receiptUrl = $"/claims/receipts/{fileName}";
        var claim = await _repo.GetByReceiptUrlAsync(receiptUrl);
        if (claim is null)
            return null;

        if (!isAdmin && claim.EmployeeId != userId)
            return null;

        return await _receiptStorage.GetAsync(fileName);
    }

    private static string GenerateClaimNumber() =>
        $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

    // Loads the claim and checks the caller may act at its current step. Returns
    // an error result (to return as-is) on any failure; otherwise the claim.
    private async Task<(Claim? Claim, ClaimStatusTransitionResult? Error)> AuthorizeAsync(
        string id, string approverId, string? role)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null)
            return (null, new ClaimStatusTransitionResult(false, false, null));

        // Only a current-step approver (or an org approver) may act; others are
        // treated as not-found so the claim stays hidden.
        if (!_router.IsOrgApprover(role))
        {
            var approvers = await _router.CurrentApproversAsync(Module, claim.EmployeeId, claim.CurrentStep);
            if (!approvers.Contains(approverId))
                return (null, new ClaimStatusTransitionResult(false, false, null));
        }

        if (claim.Status != ClaimStatus.PENDING)
            return (claim, new ClaimStatusTransitionResult(true, false, claim,
                "Only pending claims can be approved or rejected."));

        return (claim, null);
    }
}
