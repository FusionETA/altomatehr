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
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
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
        ITeamService? teams = null,
        IChartOfAccountService? accounts = null,
        IClaimReceiptStorage? receiptStorage = null,
        IOrganizationService? organizations = null,
        ICurrentUser? currentUser = null,
        IRealtimeService? realtime = null,
        IEmployeeRowResolver? employees = null,
        IProjectService? projects = null,
        IXeroService? xero = null) =>
        new(
            new FakeClaimsRepository(claims),
            receiptStorage ?? new FakeClaimReceiptStorage(),
            accounts ?? new FakeChartOfAccountService(),
            supervision ?? new FakeSupervisionService(),
            router ?? new FakeApprovalRouter(),
            teams ?? new FakeTeamService(),
            organizations ?? new FakeOrganizationService(),
            currentUser ?? new FakeCurrentUser(),
            realtime ?? new FakeRealtimeService(),
            new FakeNotificationService(),
            employees ?? new FakeEmployeeDirectory(),
            projects ?? new FakeProjectServiceForExport(),
            xero ?? new FakeXeroBillService());

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
                    // Spends address the bank by its Xero id, so an account
                    // that was never synced cannot be spent from.
                    XeroAccountId = "xero-bank-1",
                    IsSelectable = false,
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
    // Claims settings read through here now, so this one actually works rather
    // than throwing: ClaimsService.GetSettingsAsync calls GetByIdAsync.
    public Task<OrganizationDto?> SetClaimSettingsAsync(
        string organizationId, int cutoffDay, ClaimSettlement settlementRoute, XeroBillStatus xeroBillStage)
    {
        _organization.ClaimRunCutoffDay = cutoffDay;
        _organization.ClaimSettlementRoute = settlementRoute.ToString();
        _organization.XeroBillStage = xeroBillStage.ToString();
        return Task.FromResult<OrganizationDto?>(_organization);
    }

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
    public string? Email => "test@altomate.com";
    public string? UserId { get; init; } = "usr-emp";
    public string? OrganizationId { get; init; } = "org-demo";
    public string? Role { get; init; } = "Employee";
    public string? IpAddress { get; init; }
    public bool IsAdmin => Role is "Admin" or "Owner";
    public bool IsAuthenticated => UserId is not null;
}

// Org-approver check + email lookup only — routing and "who are my reports"
// both come from ITeamService now (see FakeTeamService below).
internal sealed class FakeSupervisionService : ISupervisionService
{
    private readonly Dictionary<string, string> _emails;

    public FakeSupervisionService(Dictionary<string, string>? emails = null) => _emails = emails ?? new();

    public bool IsOrgApprover(string? role) => role is "Admin" or "Owner";

    // Tests that need an admin excluded from routing pass them here.
    public HashSet<string> AdministrativeUserIds { get; init; } = [];

    public Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
        Task.FromResult<IReadOnlySet<string>>(AdministrativeUserIds);

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

    public Task<IReadOnlyList<string>> CurrentApproversAsync(
        ApprovalModule module, string applicantId, int currentStep, string? projectId = null)
    {
        var steps = _chains.GetValueOrDefault(applicantId) ?? [];
        return Task.FromResult<IReadOnlyList<string>>(
            currentStep >= 0 && currentStep < steps.Count ? steps[currentStep] : []);
    }

    public Task<int> StepCountAsync(ApprovalModule module, string applicantId, string? projectId = null) =>
        Task.FromResult((_chains.GetValueOrDefault(applicantId) ?? []).Count);
}

// Minimal ITeamService double. `reportsOf` maps a supervisor id to the flat
// list of employee ids GetReportEmployeeIdsAsync should return for them —
// enough for the "team view" visibility tests, without a real Team/layer
// model behind it.
internal sealed class FakeTeamService : ITeamService
{
    private readonly Dictionary<string, List<string>> _reportsOf;

    public FakeTeamService(Dictionary<string, List<string>>? reportsOf = null) =>
        _reportsOf = reportsOf ?? new();

    public Task<IEnumerable<TeamDto>> GetAllAsync() => Task.FromResult<IEnumerable<TeamDto>>([]);
    public Task<TeamSaveResult> CreateAsync(CreateTeamDto dto) => throw new NotSupportedException();
    public Task<TeamSaveResult> UpdateAsync(string id, SaveTeamDto dto) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string id) => throw new NotSupportedException();
    public Task<TeamSaveResult> AddOrUpdateMemberAsync(string teamId, SaveMembershipDto dto) =>
        throw new NotSupportedException();
    public Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId) =>
        throw new NotSupportedException();
    public Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(
        string employeeId, ApprovalModule module, string? projectId = null) =>
        Task.FromResult<IEnumerable<ApprovalStepDto>>([]);
    public Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId) =>
        Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId) =>
        Task.FromResult<IReadOnlyList<SupervisedTeamDto>>([]);
    public Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId) =>
        Task.FromResult<IReadOnlyList<string>>(_reportsOf.GetValueOrDefault(supervisorId, []));
    public Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId) =>
        Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<LayerApproverOptionsDto>?> GetApproverOptionsAsync(string teamId, string employeeId) =>
        Task.FromResult<IReadOnlyList<LayerApproverOptionsDto>?>(null);
    public Task<ApproverOverrideResult> SetApproverOverrideAsync(
        string teamId, string employeeId, int layer, List<string> approverIds) =>
        throw new NotSupportedException();
    public Task<ApproverOverrideResult> ClearApproverOverrideAsync(string teamId, string employeeId, int layer) =>
        throw new NotSupportedException();
}


// Records the bills it was asked to create, so a test can assert on WHAT was
// pushed rather than only that something was. `Fail` makes it behave like a
// Xero outage.
internal sealed class FakeXeroBillService : IXeroService
{
    public Task<IReadOnlyList<XeroCurrencyResponse>> GetCurrenciesAsync() =>
        Task.FromResult<IReadOnlyList<XeroCurrencyResponse>>([new("MYR", "Malaysian Ringgit")]);

    private readonly bool _connected;
    private readonly string? _failWith;

    public FakeXeroBillService(bool connected = true, string? failWith = null)
    {
        _connected = connected;
        _failWith = failWith;
    }

    public List<XeroBillRequest> Created { get; } = [];
    public string NextBillId { get; set; } = "xero-bill-1";

    public Task<XeroBillResponse> CreateBillAsync(XeroBillRequest bill)
    {
        if (_failWith is not null) throw new XeroConnectionException(_failWith);

        Created.Add(bill);
        return Task.FromResult(new XeroBillResponse(NextBillId, "BILL-001"));
    }

    public List<XeroSpendRequest> Spends { get; } = [];

    public Task<XeroSpendResponse> CreateSpendAsync(XeroSpendRequest spend)
    {
        if (_failWith is not null) throw new XeroConnectionException(_failWith);

        Spends.Add(spend);
        return Task.FromResult(new XeroSpendResponse("xero-spend-1"));
    }

    public Task<bool> IsConnectedAsync() => Task.FromResult(_connected);

    public Task<XeroFileContent?> GetFileContentAsync(string fileId) =>
        Task.FromResult<XeroFileContent?>(null);

    public Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? r) => throw new NotImplementedException();
    public Task<string> CompleteCallbackAsync(string c, string s) => throw new NotImplementedException();
    public Task<XeroStatusDto> GetStatusAsync() => throw new NotImplementedException();
    public Task DisconnectAsync() => throw new NotImplementedException();
    public Task<XeroSyncAccountsResultDto> SyncAccountsAsync() => throw new NotImplementedException();
    public Task<XeroSyncProjectsResultDto> SyncProjectsAsync() => throw new NotImplementedException();
}
