using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Tests.Ai;

// The model's answer is a suggestion, not a fact. Everything here is about what
// the service refuses to pass through: an account the employee couldn't pick, a
// guess it isn't confident about, an id it invented, a currency that isn't one.
public class ReceiptOcrServiceTests
{
    private const string SelectableAccountId = "acc-travel";
    private const string ArchivedAccountId = "acc-archived";

    [Fact]
    public async Task EmptyFile_IsRejectedBeforeCallingTheModel()
    {
        var gemini = new FakeGeminiClient("{}");
        var service = CreateService(gemini);

        await Assert.ThrowsAsync<AiProviderException>(
            () => service.AnalyzeAsync([], "image/png"));

        Assert.Equal(0, gemini.Calls);
    }

    [Fact]
    public async Task AConfidentSuggestionForASelectableAccount_IsPassedThrough()
    {
        var service = CreateService(new FakeGeminiClient(Json(
            accountId: SelectableAccountId, confidence: 0.95, currency: "MYR", total: 42.50m)));

        var result = await service.AnalyzeAsync([1, 2, 3], "image/png");

        Assert.Equal(SelectableAccountId, result.SuggestedAccountId);
        Assert.Equal(42.50m, result.Total);
    }

    [Fact]
    public async Task ASuggestionBelowTheConfidenceThreshold_IsDropped()
    {
        // The threshold is 0.8; 0.79 must not survive.
        var service = CreateService(new FakeGeminiClient(Json(
            accountId: SelectableAccountId, confidence: 0.79)));

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Null(result.SuggestedAccountId);
        // The confidence itself is still reported, so the UI can explain itself.
        Assert.Equal(0.79, result.SuggestedAccountConfidence, 3);
    }

    [Fact]
    public async Task AnInventedAccountId_IsDropped()
    {
        // A hallucinated id would otherwise be pre-filled into the claim form.
        var service = CreateService(new FakeGeminiClient(Json(
            accountId: "acc-does-not-exist", confidence: 1.0)));

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Null(result.SuggestedAccountId);
    }

    [Fact]
    public async Task AnArchivedAccount_IsNeverOfferedAndNeverAccepted()
    {
        // Archived accounts aren't in the candidate list, so even a confident
        // suggestion for one has to fail the membership check.
        var gemini = new FakeGeminiClient(Json(accountId: ArchivedAccountId, confidence: 1.0));
        var service = CreateService(gemini);

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Null(result.SuggestedAccountId);
        Assert.DoesNotContain(ArchivedAccountId, gemini.LastPrompt);
    }

    [Fact]
    public async Task ADetectedCurrencyThatLooksLikeACode_IsUsedAsIs()
    {
        var service = CreateService(new FakeGeminiClient(Json(currency: "SGD")));

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Equal("SGD", result.DetectedCurrency);
        Assert.Equal("SGD", result.ResolvedCurrency);
        Assert.False(result.CurrencyWasOverridden);
    }

    [Theory]
    [InlineData("ringgit")]
    [InlineData("$")]
    [InlineData("")]
    public async Task ACurrencyThatIsNotACode_FallsBackToTheOrgDefault(string detected)
    {
        var service = CreateService(new FakeGeminiClient(Json(currency: detected)));

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Equal("MYR", result.ResolvedCurrency);
        Assert.True(result.CurrencyWasOverridden);
    }

    [Fact]
    public async Task WithNoCurrentOrg_ThereIsNothingToFallBackTo()
    {
        var service = CreateService(new FakeGeminiClient(Json(currency: "not-a-code")), orgId: null);

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Null(result.ResolvedCurrency);
        Assert.False(result.CurrencyWasOverridden);
    }

    [Fact]
    public async Task AFencedJsonResponse_IsStillParsed()
    {
        // Models like wrapping JSON in ```json fences; the parser strips them.
        var fenced = "```json\n" + Json(supplier: "Kopitiam", total: 12.30m) + "\n```";
        var service = CreateService(new FakeGeminiClient(fenced));

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Equal("Kopitiam", result.Supplier);
        Assert.Equal(12.30m, result.Total);
    }

    [Fact]
    public async Task MissingFields_ComeBackAsNullsRatherThanThrowing()
    {
        var service = CreateService(new FakeGeminiClient("{}"));

        var result = await service.AnalyzeAsync([1], "image/png");

        Assert.Null(result.Supplier);
        Assert.Null(result.Total);
        Assert.Null(result.Date);
        Assert.Null(result.SuggestedAccountId);
        Assert.Equal("gemini", result.Provider);
    }

    [Fact]
    public async Task AResponseThatIsNotJsonAtAll_SurfacesAsAProviderError()
    {
        var service = CreateService(new FakeGeminiClient("I'm sorry, I can't help with that."));

        await Assert.ThrowsAnyAsync<Exception>(() => service.AnalyzeAsync([1], "image/png"));
    }

    // --- helpers ---

    private static string Json(
        string? accountId = null,
        double? confidence = null,
        string? currency = null,
        decimal? total = null,
        string? supplier = null)
    {
        var fields = new List<string>();
        if (supplier is not null) fields.Add($"\"supplier\":\"{supplier}\"");
        if (total is not null) fields.Add($"\"total\":{total}");
        if (currency is not null) fields.Add($"\"currency\":\"{currency}\"");
        if (accountId is not null) fields.Add($"\"suggestedAccountId\":\"{accountId}\"");
        if (confidence is not null) fields.Add($"\"suggestedAccountConfidence\":{confidence}");
        return "{" + string.Join(",", fields) + "}";
    }

    private static ReceiptOcrService CreateService(
        FakeGeminiClient gemini, string? orgId = "org-1") =>
        new(gemini, new FakeChartOfAccountService(), new FakeOrganizationService(), new FakeCurrentUser(orgId));

    private sealed class FakeGeminiClient(string response) : IGeminiClient
    {
        public int Calls { get; private set; }
        public string LastPrompt { get; private set; } = "";

        public Task<string> GenerateFromImageAsync(
            string prompt, byte[] fileBytes, string mimeType, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeChartOfAccountService : IChartOfAccountService
    {
        // One pickable account and one archived, so the gate has something to
        // reject as well as something to accept.
        public Task<IEnumerable<ChartOfAccountDto>> GetAllAsync() =>
            Task.FromResult<IEnumerable<ChartOfAccountDto>>(
            [
                new ChartOfAccountDto
                {
                    Id = SelectableAccountId, Code = "6100", Name = "Travel",
                    Type = "EXPENSE", IsSelectable = true, IsArchived = false,
                },
                new ChartOfAccountDto
                {
                    Id = ArchivedAccountId, Code = "6200", Name = "Old Entertainment",
                    Type = "EXPENSE", IsSelectable = true, IsArchived = true,
                },
            ]);

        public Task<ChartOfAccountDto?> GetByIdAsync(string id) => throw new NotSupportedException();
        public Task<ChartOfAccountDto> CreateAsync(SaveChartOfAccountDto dto) => throw new NotSupportedException();
        public Task<ChartOfAccountDto?> UpdateAsync(string id, SaveChartOfAccountDto dto) =>
            throw new NotSupportedException();
        public Task<ChartOfAccountDto?> SetArchivedAsync(string id, bool archived) =>
            throw new NotSupportedException();
    }

    private sealed class FakeOrganizationService : IOrganizationService
    {
        public Task<OrganizationDto?> GetByIdAsync(string organizationId) =>
            Task.FromResult<OrganizationDto?>(new OrganizationDto
            {
                Id = organizationId, Name = "Fusioneta", DefaultCurrency = "MYR",
            });

        public Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId) =>
            throw new NotSupportedException();
        public Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto) =>
            throw new NotSupportedException();
        public Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCurrentUser(string? orgId) : ICurrentUser
    {
        public string? UserId => "usr-emp";
        public string? OrganizationId => orgId;
        public string? Role => "Employee";
        public bool IsAdmin => false;
        public bool IsAuthenticated => true;
        public string? IpAddress => null;
    }
}
