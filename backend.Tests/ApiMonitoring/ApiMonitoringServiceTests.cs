using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiMonitoring;
using AltomateHR.Api.Modules.ApiMonitoring.Dtos;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.ApiMonitoring;

// Against a real EF context for the repository (the module convention). What
// the service owns is the grouping, the 4xx/5xx split, the newest-first error
// list and its filters, and the retention cutoff.
public class ApiMonitoringServiceTests : IDisposable
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly AppDbContext _db;
    private readonly ApiMonitoringService _service;

    public ApiMonitoringServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"api-monitoring-{Guid.NewGuid()}")
            .Options;
        // Superadmin reads span every company, so no current org.
        _db = new AppDbContext(options, new StubCurrentUser { OrganizationId = null });
        _service = new ApiMonitoringService(
            new ApiRequestLogRepository(_db), new StubOrganizations(), Options.Create(new ApiMonitoringOptions()));
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(params ApiRequestLog[] rows)
    {
        _db.ApiRequestLogs.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    private static ApiRequestLog Row(
        string route, int status, int ms = 100, string? org = "org-1", string method = "GET",
        DateTime? at = null, string? message = null) => new()
    {
        Method = method,
        Route = route,
        StatusCode = status,
        DurationMs = ms,
        OrganizationId = org,
        CreatedAt = at ?? Now.AddMinutes(-10),
        ErrorMessage = message,
    };

    [Fact]
    public async Task SummaryGroupsPerEndpointAndKeeps4xxAnd5xxApart()
    {
        await SeedAsync(
            Row("employees", 200, ms: 100),
            Row("employees", 200, ms: 300),
            Row("employees", 409, ms: 200),
            Row("payroll/runs/{id}/generate", 500, ms: 900, method: "POST"));

        var summary = await _service.GetSummaryAsync(null, null, null);

        Assert.Equal(4, summary.TotalCalls);
        Assert.Equal(1, summary.TotalClientErrors);
        Assert.Equal(1, summary.TotalServerErrors);

        // The one throwing 500s comes first, however rarely it is called.
        var generate = summary.Endpoints[0];
        Assert.Equal("payroll/runs/{id}/generate", generate.Route);
        Assert.Equal("POST", generate.Method);
        Assert.Equal(1, generate.ServerErrors);

        var employees = summary.Endpoints[1];
        Assert.Equal(3, employees.Calls);
        Assert.Equal(1, employees.ClientErrors);
        Assert.Equal(0, employees.ServerErrors);
        Assert.Equal(200, employees.AverageMs);
        Assert.Equal(300, employees.SlowestMs);
        Assert.Equal(1.0 / 3, employees.ErrorRate, precision: 6);
    }

    [Fact]
    public async Task SummaryDefaultsToTheLast24Hours()
    {
        await SeedAsync(Row("employees", 200), Row("employees", 200, at: Now.AddDays(-2)));

        var summary = await _service.GetSummaryAsync(null, null, null);

        Assert.Equal(1, summary.TotalCalls);
    }

    [Fact]
    public async Task SummaryCanBeNarrowedToOneCompany()
    {
        await SeedAsync(Row("employees", 200, org: "org-1"), Row("employees", 200, org: "org-2"));

        var summary = await _service.GetSummaryAsync(null, null, "org-2");

        Assert.Equal(1, summary.TotalCalls);
    }

    // The list the company filter picks from: so it must not shrink to the one
    // company already picked.
    [Fact]
    public async Task SummaryListsEveryCallingCompanyWhateverIsFiltered()
    {
        await SeedAsync(
            Row("employees", 200, org: "org-1"),
            Row("employees", 200, org: "org-2"),
            Row("employees", 200, org: "org-2"),
            Row("auth/login", 200, org: null));

        var summary = await _service.GetSummaryAsync(null, null, "org-1");

        Assert.Equal(1, summary.TotalCalls);
        Assert.Equal(["Beta Sdn Bhd", "Acme Sdn Bhd"], summary.Companies.Select(c => c.OrganizationName));
        Assert.Equal([2, 1], summary.Companies.Select(c => c.Calls));
    }

    [Fact]
    public async Task ErrorsAreFailuresOnlyNewestFirstWithTheCompanyName()
    {
        await SeedAsync(
            Row("employees", 200),
            Row("claims", 409, at: Now.AddMinutes(-30), message: "older"),
            Row("claims", 500, at: Now.AddMinutes(-5), message: "newer"));

        var errors = await _service.GetRecentErrorsAsync(new ApiRequestErrorQuery());

        Assert.Equal(["newer", "older"], errors.Select(e => e.ErrorMessage));
        Assert.All(errors, e => Assert.Equal("Acme Sdn Bhd", e.OrganizationName));
    }

    [Fact]
    public async Task ErrorsFilterByRouteStatusAndCompany()
    {
        await SeedAsync(
            Row("claims", 409, org: "org-1"),
            Row("claims", 500, org: "org-1"),
            Row("claims", 500, org: "org-2"),
            Row("leave", 500, org: "org-1"));

        var errors = await _service.GetRecentErrorsAsync(new ApiRequestErrorQuery
        {
            Route = "claims", Status = 500, OrganizationId = "org-1",
        });

        var only = Assert.Single(errors);
        Assert.Equal("claims", only.Route);
        Assert.Equal(500, only.StatusCode);
        Assert.Equal("org-1", only.OrganizationId);
    }

    // One endpoint row is a method AND a route; GET and DELETE on the same
    // path must not share an error list.
    [Fact]
    public async Task ErrorsFilterByMethodToo()
    {
        await SeedAsync(
            Row("employees/{id}", 500, method: "GET"),
            Row("employees/{id}", 500, method: "DELETE"));

        var errors = await _service.GetRecentErrorsAsync(new ApiRequestErrorQuery
        {
            Method = "get", Route = "employees/{id}",
        });

        Assert.Equal("GET", Assert.Single(errors).Method);
    }

    [Fact]
    public async Task ErrorListIsCapped()
    {
        await SeedAsync(Enumerable.Range(0, 5).Select(_ => Row("claims", 500)).ToArray());

        var errors = await _service.GetRecentErrorsAsync(new ApiRequestErrorQuery { Limit = 2 });

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public async Task PurgeDeletesRowsOlderThanTheRetentionWindow()
    {
        var repo = new RecordingRepository();
        var service = new ApiMonitoringService(
            repo, new StubOrganizations(), Options.Create(new ApiMonitoringOptions { RetentionDays = 30 }));

        await service.PurgeExpiredAsync();

        var expected = DateTime.UtcNow.AddDays(-30);
        Assert.InRange(repo.Cutoff!.Value, expected.AddMinutes(-1), expected.AddMinutes(1));
    }

    // A misconfigured 0 must not empty the table every night.
    [Fact]
    public async Task PurgeNeverKeepsLessThanADay()
    {
        var repo = new RecordingRepository();
        var service = new ApiMonitoringService(
            repo, new StubOrganizations(), Options.Create(new ApiMonitoringOptions { RetentionDays = 0 }));

        await service.PurgeExpiredAsync();

        Assert.True(repo.Cutoff <= DateTime.UtcNow.AddHours(-23));
    }

    // ---- fakes ----

    private sealed class StubOrganizations : IOrganizationRepository
    {
        public Task<List<Organization>> GetAllAsync() => Task.FromResult(new List<Organization>
        {
            new() { Id = "org-1", Name = "Acme Sdn Bhd" },
            new() { Id = "org-2", Name = "Beta Sdn Bhd" },
        });

        public Task<Organization?> GetByIdAsync(string id) => throw new NotSupportedException();
        public Task<Organization?> GetFirstAsync() => throw new NotSupportedException();
        public Task AddAsync(Organization organization) => throw new NotSupportedException();
        public Task UpdateAsync(Organization organization) => throw new NotSupportedException();
        public Task<bool> AnyAsync() => throw new NotSupportedException();
    }

    // InMemory can't run ExecuteDeleteAsync, so the purge is checked at the
    // cutoff it asks the repository for.
    private sealed class RecordingRepository : IApiRequestLogRepository
    {
        public DateTime? Cutoff { get; private set; }

        public Task<int> DeleteOlderThanAsync(DateTime cutoff)
        {
            Cutoff = cutoff;
            return Task.FromResult(0);
        }

        public Task AddRangeAsync(IReadOnlyList<ApiRequestLog> rows) => throw new NotSupportedException();
        public Task<List<ApiOrganizationCalls>> GetCallsPerOrganizationAsync(DateTime from, DateTime to) =>
            throw new NotSupportedException();
        public Task<List<ApiEndpointStats>> GetEndpointStatsAsync(DateTime from, DateTime to, string? organizationId) =>
            throw new NotSupportedException();
        public Task<List<ApiRequestLog>> GetErrorsAsync(
            DateTime from, DateTime to, string? organizationId, string? method, string? route, int? status, int limit) =>
            throw new NotSupportedException();
    }
}
