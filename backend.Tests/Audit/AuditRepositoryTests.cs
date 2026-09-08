using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Audit;

// The repository against a real EF context: seq allocation, per-org chains, and
// the filters the feed relies on.
//
// NOT covered here: the concurrent-append retry. It depends on the unique
// (OrganizationId, Seq) index rejecting the loser, and the in-memory provider
// does not enforce unique indexes — a test would pass while proving nothing.
// That path needs a real MySQL harness.
public class AuditRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuditRepository _repo;

    public AuditRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, new StubCurrentUser());
        _repo = new AuditRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private static AuditLog Entry(string action = "project.create", string? status = null) => new()
    {
        Action = action,
        Status = status ?? AuditStatuses.Success,
        Summary = $"did {action}",
        ActorEmail = "admin@altomate.com",
        ActorName = "admin@altomate.com",
        CreatedAt = AuditChain.TruncateToStorablePrecision(DateTime.UtcNow),
    };

    [Fact]
    public async Task SeqStartsAtOneAndCountsUp()
    {
        for (var i = 0; i < 3; i++) await _repo.AppendAsync(Entry(), "org-1");

        var rows = await _repo.GetChainAsync("org-1");
        Assert.Equal([1, 2, 3], rows.Select(r => r.Seq));
    }

    [Fact]
    public async Task EachRowLinksToTheOneBeforeIt()
    {
        for (var i = 0; i < 3; i++) await _repo.AppendAsync(Entry(), "org-1");

        var rows = await _repo.GetChainAsync("org-1");
        Assert.Null(rows[0].PrevHash);              // genesis
        Assert.Equal(rows[0].Hash, rows[1].PrevHash);
        Assert.Equal(rows[1].Hash, rows[2].PrevHash);
    }

    [Fact]
    public async Task AChainBuiltThroughTheRepositoryVerifies()
    {
        // The end-to-end check: what the repository writes is what the chain
        // can prove. Everything else here is a detail of that.
        for (var i = 0; i < 5; i++) await _repo.AppendAsync(Entry(), "org-1");

        var result = AuditChain.Verify(await _repo.GetChainAsync("org-1"));

        Assert.True(result.Ok, $"chain broke at {result.BrokenAtSeq}: {result.Reason}");
        Assert.Equal(5, result.EntriesChecked);
    }

    [Fact]
    public async Task TwoOrganizationsKeepIndependentChains()
    {
        // Per-org chains: two tenants must not block each other, and one org's
        // rows must never appear in another's log.
        await _repo.AppendAsync(Entry(), "org-1");
        await _repo.AppendAsync(Entry(), "org-2");
        await _repo.AppendAsync(Entry(), "org-1");

        Assert.Equal([1, 2], (await _repo.GetChainAsync("org-1")).Select(r => r.Seq));
        Assert.Equal([1], (await _repo.GetChainAsync("org-2")).Select(r => r.Seq));
        Assert.True(AuditChain.Verify(await _repo.GetChainAsync("org-1")).Ok);
        Assert.True(AuditChain.Verify(await _repo.GetChainAsync("org-2")).Ok);
    }

    [Fact]
    public async Task AQueryNeverReachesAnotherOrganizationsRows()
    {
        await _repo.AppendAsync(Entry(), "org-1");
        await _repo.AppendAsync(Entry(), "org-2");

        var (rows, _) = await _repo.QueryAsync(new AuditQueryDto(), "org-1");

        Assert.All(rows, r => Assert.Equal("org-1", r.OrganizationId));
    }

    [Fact]
    public async Task TheFeedReadsNewestFirst()
    {
        for (var i = 0; i < 3; i++) await _repo.AppendAsync(Entry(), "org-1");

        var (rows, _) = await _repo.QueryAsync(new AuditQueryDto(), "org-1");

        Assert.Equal([3, 2, 1], rows.Select(r => r.Seq));
    }

    [Fact]
    public async Task PagesWalkBackwardsWithoutRepeatingOrSkipping()
    {
        // Offset paging is stable here because the table is append-only — no row
        // a reader has already passed can shift underneath them.
        for (var i = 0; i < 5; i++) await _repo.AppendAsync(Entry(), "org-1");

        var (first, total) = await _repo.QueryAsync(new AuditQueryDto { Limit = 2, Page = 1 }, "org-1");
        var (second, _) = await _repo.QueryAsync(new AuditQueryDto { Limit = 2, Page = 2 }, "org-1");
        var (third, _) = await _repo.QueryAsync(new AuditQueryDto { Limit = 2, Page = 3 }, "org-1");

        Assert.Equal(5, total);
        Assert.Equal([5, 4], first.Select(r => r.Seq));
        Assert.Equal([3, 2], second.Select(r => r.Seq));
        Assert.Equal([1], third.Select(r => r.Seq));
    }

    [Fact]
    public async Task TheTotalIgnoresPagingButRespectsFilters()
    {
        await _repo.AppendAsync(Entry("settings.org.update"), "org-1");
        await _repo.AppendAsync(Entry("settings.claims.update"), "org-1");
        await _repo.AppendAsync(Entry("project.create"), "org-1");

        var (rows, total) = await _repo.QueryAsync(
            new AuditQueryDto { Action = "settings", Limit = 1 }, "org-1");

        Assert.Single(rows);
        // Two matched the filter even though only one was returned — a total
        // that counted the page would page to nowhere.
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task AnActionPrefixMatchesItsNamespaceOnly()
    {
        await _repo.AppendAsync(Entry("xero.connect"), "org-1");
        await _repo.AppendAsync(Entry("xero.accounts.sync"), "org-1");
        await _repo.AppendAsync(Entry("project.create"), "org-1");

        var namespaced = (await _repo.QueryAsync(new AuditQueryDto { Action = "xero" }, "org-1")).Rows;
        Assert.Equal(2, namespaced.Count);

        var exact = (await _repo.QueryAsync(new AuditQueryDto { Action = "xero.connect" }, "org-1")).Rows;
        Assert.Equal("xero.connect", Assert.Single(exact).Action);
    }

    [Fact]
    public async Task APrefixDoesNotMatchALongerWordThatStartsWithIt()
    {
        // "leave" must not also catch "leaver.*" — the trailing dot is what
        // stops the namespace filter being a plain string prefix.
        await _repo.AppendAsync(Entry("leave.approve"), "org-1");
        await _repo.AppendAsync(Entry("leaver.process"), "org-1");

        var rows = (await _repo.QueryAsync(
            new AuditQueryDto { Action = "leave" }, "org-1")).Rows;

        Assert.Equal("leave.approve", Assert.Single(rows).Action);
    }


    [Fact]
    public async Task StatusNarrowsToOneOutcome()
    {
        await _repo.AppendAsync(Entry(status: AuditStatuses.Success), "org-1");
        await _repo.AppendAsync(Entry("auth.login.failed", AuditStatuses.Failed), "org-1");

        var failed = (await _repo.QueryAsync(
            new AuditQueryDto { Status = AuditStatuses.Failed }, "org-1")).Rows;

        Assert.Equal(AuditStatuses.Failed, Assert.Single(failed).Status);
    }

    [Fact]
    public async Task TheEndOfADateRangeIncludesThatWholeDay()
    {
        // An exclusive end on the date itself would drop everything logged after
        // midnight on the last day someone asked for — which is all of it.
        var yesterday = Entry();
        yesterday.CreatedAt = DateTime.UtcNow.Date.AddDays(-1).AddHours(9);
        await _repo.AppendAsync(yesterday, "org-1");

        var today = Entry();
        today.CreatedAt = DateTime.UtcNow.Date.AddHours(23).AddMinutes(59);
        await _repo.AppendAsync(today, "org-1");

        var rows = (await _repo.QueryAsync(
            new AuditQueryDto { From = DateTime.UtcNow.Date, To = DateTime.UtcNow.Date }, "org-1")).Rows;

        Assert.Single(rows);
        Assert.Equal(today.Seq, rows[0].Seq);
    }

    [Fact]
    public async Task TheLimitIsClampedSoOneCallCannotDrainTheTable()
    {
        for (var i = 0; i < 5; i++) await _repo.AppendAsync(Entry(), "org-1");

        Assert.Single((await _repo.QueryAsync(new AuditQueryDto { Limit = 0 }, "org-1")).Rows);
        Assert.Equal(5, ((await _repo.QueryAsync(new AuditQueryDto { Limit = 9999 }, "org-1")).Rows).Count);
    }
}
