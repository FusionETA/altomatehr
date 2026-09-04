using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Claims;

// Shared fakes + factory for the ClaimsService unit tests. ClaimsService now
// depends on IChartOfAccountService (spend limits) and ISupervisionService
// (approval routing), so tests wire those too.
internal static class ClaimsTestFactory
{
    public static ClaimsService CreateService(
        IEnumerable<Claim> claims,
        IApprovalRouter? router = null,
        ISupervisionService? supervision = null,
        IChartOfAccountService? accounts = null,
        IClaimReceiptStorage? receiptStorage = null,
        IOrganizationService? organizations = null,
        ICurrentUser? currentUser = null,
        IRealtimeService? realtime = null,
        IEmployeeRowResolver? employees = null,
        IProjectService? projects = null) =>
        new(
            new FakeClaimsRepository(claims),
            receiptStorage ?? new FakeClaimReceiptStorage(),
            accounts ?? new FakeChartOfAccountService(),
            supervision ?? new FakeSupervisionService(),
            router ?? new FakeApprovalRouter(),
            organizations ?? new FakeOrganizationService(),
            currentUser ?? new FakeCurrentUser(),
            realtime ?? new FakeRealtimeService(),
            employees ?? new FakeEmployeeDirectory(),
            projects ?? new FakeProjectServiceForExport());

    public static Claim NewClaim(
        string id,
        string employeeId,
        ClaimStatus status = ClaimStatus.PENDING,
        string? receiptUrl = null) => new()
    {
        Id = id,
        ClaimNumber = $"CLM-{id}",
        Title = "Lunch",
        Description = "Team lunch",
        Category = ClaimCategory.MEAL,
        Amount = 12,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        SubmittedAt = DateTime.UtcNow,
        Status = status,
        EmployeeId = employeeId,
        ReceiptUrl = receiptUrl,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}

internal sealed class FakeClaimsRepository : IClaimsRepository
{
    private readonly List<Claim> _claims;

    public FakeClaimsRepository(IEnumerable<Claim> claims) => _claims = claims.ToList();

    public Task<List<Claim>> GetAllAsync() => Task.FromResult(_claims.ToList());

    public Task<List<Claim>> GetByEmployeeIdAsync(string employeeId) =>
        Task.FromResult(_claims.Where(c => c.EmployeeId == employeeId).ToList());

    public Task<Claim?> GetByIdAsync(string id) =>
        Task.FromResult(_claims.FirstOrDefault(c => c.Id == id));

    public Task<Claim?> GetByReceiptUrlAsync(string receiptUrl) =>
        Task.FromResult(_claims.FirstOrDefault(c =>
            c.ReceiptUrl == receiptUrl || c.SupportingDocumentUrls.Contains(receiptUrl)));

    public Task<Claim> AddAsync(Claim claim)
    {
        _claims.Add(claim);
        return Task.FromResult(claim);
    }

    public Task UpdateAsync(Claim claim) => Task.CompletedTask;

    public Task<bool> DeleteAsync(string id) =>
        Task.FromResult(_claims.RemoveAll(c => c.Id == id) > 0);
}

internal sealed class FakeClaimReceiptStorage : IClaimReceiptStorage
{
    public Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload) =>
        Task.FromResult(new ClaimReceiptUploadResult("/claims/receipts/receipt.pdf"));

    public Task<ClaimReceiptFileResult?> GetAsync(string fileName) =>
        Task.FromResult<ClaimReceiptFileResult?>(
            new ClaimReceiptFileResult(
                Path.Combine(Path.GetTempPath(), fileName),
                "application/pdf",
                fileName));
}

internal sealed class FakeChartOfAccountService : IChartOfAccountService
{
    private readonly Dictionary<string, ChartOfAccountDto> _accounts;

    public FakeChartOfAccountService(params ChartOfAccountDto[] accounts) =>
        _accounts = accounts.Length > 0
            ? accounts.ToDictionary(a => a.Id)
            : new[]
            {
                new ChartOfAccountDto
                {
                    Id = "acct-expense",
                    Code = "6100",
                    Name = "Travel Expenses",
                    Type = "EXPENSE",
                    IsSelectable = true,
                },
                new ChartOfAccountDto
                {
                    Id = "acct-mileage",
                    Code = "6200",
                    Name = "Mileage Claims",
                    Type = "EXPENSE",
                    IsSelectable = false,
                    AllowMileageClaim = true,
                    MileageRate = 0.8m,
                },
                new ChartOfAccountDto
                {
                    Id = "acct-bank",
                    Code = "1000",
                    Name = "Company Bank",
                    Type = "BANK",
                    IsSelectable = true,
                },
            }.ToDictionary(a => a.Id);

    public Task<IEnumerable<ChartOfAccountDto>> GetAllAsync() =>
        Task.FromResult<IEnumerable<ChartOfAccountDto>>(_accounts.Values.ToList());

    public Task<ChartOfAccountDto?> GetByIdAsync(string id) =>
        Task.FromResult(_accounts.GetValueOrDefault(id));

    public Task<ChartOfAccountDto> CreateAsync(SaveChartOfAccountDto dto) => throw new NotImplementedException();
    public Task<ChartOfAccountDto?> UpdateAsync(string id, SaveChartOfAccountDto dto) => throw new NotImplementedException();
    public Task<ChartOfAccountDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
}

internal sealed class FakeOrganizationService : IOrganizationService
{
    private readonly OrganizationDto _organization;

    public FakeOrganizationService(OrganizationDto? organization = null) =>
        _organization = organization ?? new OrganizationDto
        {
            Id = "org-demo",
            Name = "AltomateHR",
            DefaultCurrency = "MYR",
            DefaultMileageRate = 0.6m,
            MileageUnit = MileageUnit.KM,
            GeofenceRadiusMeters = 200,
        };

    public Task<OrganizationDto?> GetByIdAsync(string organizationId) =>
        Task.FromResult<OrganizationDto?>(_organization.Id == organizationId ? _organization : null);

    public Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId) =>
        throw new NotImplementedException();

    public Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto) =>
        throw new NotImplementedException();

    public Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto) =>
        throw new NotImplementedException();
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public string? UserId { get; init; } = "usr-emp";
    public string? OrganizationId { get; init; } = "org-demo";
    public string? Role { get; init; } = "Employee";
    public string? IpAddress { get; init; }
    public bool IsAdmin => Role is "Admin" or "Owner";
    public bool IsAuthenticated => UserId is not null;
}

// Mirrors the real routing logic: org approvers (Admin/Owner) may act on
// anything; otherwise only the applicant's assigned supervisor may.
internal sealed class FakeSupervisionService : ISupervisionService
{
    private readonly Dictionary<string, string> _supervisorOf;   // employeeId -> supervisorId
    private readonly Dictionary<string, string> _emails;

    public FakeSupervisionService(
        Dictionary<string, string>? supervisorOf = null,
        Dictionary<string, string>? emails = null)
    {
        _supervisorOf = supervisorOf ?? new();
        _emails = emails ?? new();
    }

    public Task<string?> GetSupervisorIdAsync(string employeeId) =>
        Task.FromResult(_supervisorOf.GetValueOrDefault(employeeId));

    public Task<IReadOnlyList<string>> GetReportIdsAsync(string supervisorId) =>
        Task.FromResult<IReadOnlyList<string>>(
            _supervisorOf.Where(kv => kv.Value == supervisorId).Select(kv => kv.Key).ToList());

    public bool IsOrgApprover(string? role) => role is "Admin" or "Owner";

    public async Task<bool> CanApproveAsync(string applicantId, string approverId, string? role)
    {
        if (IsOrgApprover(role)) return true;
        var supervisor = await GetSupervisorIdAsync(applicantId);
        return supervisor is not null && supervisor == approverId;
    }

    public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            userIds.Distinct().Where(_emails.ContainsKey).ToDictionary(id => id, id => _emails[id]));
}

// Configurable approval router. `chains` maps an applicant id to its ordered
// steps, each step being the approver ids at that step — e.g. a single
// supervisor is { ["usr-emp"] = [["usr-super"]] }; a two-step chain is
// { ["usr-emp"] = [["usr-super"], ["usr-admin"]] }.
internal sealed class FakeApprovalRouter : IApprovalRouter
{
    private readonly Dictionary<string, List<List<string>>> _chains;

    public FakeApprovalRouter(Dictionary<string, List<List<string>>>? chains = null) =>
        _chains = chains ?? new();

    public Task<IReadOnlyList<string>> CurrentApproversAsync(ApprovalModule module, string applicantId, int currentStep)
    {
        var steps = _chains.GetValueOrDefault(applicantId) ?? [];
        return Task.FromResult<IReadOnlyList<string>>(
            currentStep >= 0 && currentStep < steps.Count ? steps[currentStep] : []);
    }

    public Task<int> StepCountAsync(ApprovalModule module, string applicantId) =>
        Task.FromResult((_chains.GetValueOrDefault(applicantId) ?? []).Count);
}
