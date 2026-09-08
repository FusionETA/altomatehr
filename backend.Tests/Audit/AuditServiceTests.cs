using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Audit.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace AltomateHR.Api.Tests.Audit;

// The plumbing around the chain. The guarantee that matters most here is the
// one that is invisible when it works: writing an audit row must never be able
// to fail the action that triggered it.
public class AuditServiceTests
{
    private static AuditService Service(
        IAuditRepository? repo = null,
        ICurrentUser? currentUser = null) =>
        new(repo ?? new InMemoryAuditRepository(),
            currentUser ?? new StubCurrentUser(),
            NullLogger<AuditService>.Instance);

    // ---- Never throws ----

    [Fact]
    public async Task ARepositoryFailureDoesNotReachTheCaller()
    {
        // The whole point. An audit log that can fail a save turns an
        // observability feature into a new way for the app to break — and the
        // first time it happens will be mid-incident.
        var service = Service(new ThrowingAuditRepository());

        var write = () => service.WriteAsync(new AuditEvent("settings.org.update", "Updated"));

        await write();   // no assertion needed: the test fails if this throws
    }

    [Fact]
    public async Task AnEventWithNoOrganizationIsDroppedRatherThanThrowing()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo, new StubCurrentUser { OrganizationId = null });

        await service.WriteAsync(new AuditEvent("settings.org.update", "Updated"));

        Assert.Empty(repo.Rows);
    }

    // ---- Actor resolution ----

    [Fact]
    public async Task TheActorComesFromTheSignedInUser()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo, new StubCurrentUser
        {
            UserId = "usr-admin",
            Email = "admin@altomate.com",
            Role = "Admin",
            IpAddress = "10.0.0.9",
        });

        await service.WriteAsync(new AuditEvent("project.create", "Created project X"));

        var row = Assert.Single(repo.Rows);
        Assert.Equal("usr-admin", row.ActorUserId);
        Assert.Equal("admin@altomate.com", row.ActorEmail);
        // The address, not the GUID: an id nobody can match to a person six
        // months from now is not an actor.
        Assert.Equal("admin@altomate.com", row.ActorName);
        Assert.Equal("Admin", row.ActorRole);
        Assert.Equal("10.0.0.9", row.IpAddress);
    }

    [Fact]
    public async Task AnUnauthenticatedEventCarriesItsOwnOrgAndActor()
    {
        // A failed sign-in has no session to infer either from, so both are
        // supplied by the caller.
        var repo = new InMemoryAuditRepository();
        var service = Service(repo, new StubCurrentUser { UserId = null, Email = null, OrganizationId = null });

        await service.WriteAsync(new AuditEvent(
            "auth.login.failed",
            "Failed sign-in for someone@x.com",
            Status: AuditStatuses.Failed,
            OrganizationId: "org-7",
            ActorEmail: "someone@x.com",
            ActorName: "someone@x.com"));

        var row = Assert.Single(repo.Rows);
        Assert.Equal("org-7", row.OrganizationId);
        Assert.Equal("someone@x.com", row.ActorEmail);
        Assert.Equal(AuditStatuses.Failed, row.Status);
        Assert.Null(row.ActorUserId);
    }

    [Fact]
    public async Task TheTimestampIsTruncatedToWhatTheColumnCanHold()
    {
        // Set here rather than by the database, and truncated, so the value
        // that comes back out is the one that was hashed. See AuditChain.
        var repo = new InMemoryAuditRepository();

        await Service(repo).WriteAsync(new AuditEvent("project.create", "Created"));

        var row = Assert.Single(repo.Rows);
        Assert.Equal(0, row.CreatedAt.Ticks % (TimeSpan.TicksPerMillisecond / 1000));
        Assert.NotEqual(default, row.CreatedAt);
    }

    [Fact]
    public async Task MetadataIsStoredAsJson()
    {
        var repo = new InMemoryAuditRepository();

        await Service(repo).WriteAsync(new AuditEvent(
            "coa.update", "Updated", Metadata: new { Code = "6100", Limit = 2000 }));

        var row = Assert.Single(repo.Rows);
        Assert.Contains("\"Code\":\"6100\"", row.Metadata);
        Assert.Contains("\"Limit\":2000", row.Metadata);
    }

    // ---- Reading ----

    [Fact]
    public async Task TheTotalDescribesTheWholeFilteredSetNotThePage()
    {
        // The pager renders "1-2 of 5" from this, so the count has to survive
        // being sliced.
        var repo = new InMemoryAuditRepository();
        var service = Service(repo);
        for (var i = 0; i < 5; i++)
            await service.WriteAsync(new AuditEvent("project.create", $"Created {i}"));

        var first = await service.ListAsync(new AuditQueryDto { Limit = 2, Page = 1 });

        Assert.Equal(2, first.Entries.Count);
        Assert.Equal(5, first.Total);
    }

    [Fact]
    public async Task PagesDoNotOverlapOrSkip()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo);
        for (var i = 0; i < 5; i++)
            await service.WriteAsync(new AuditEvent("project.create", $"Created {i}"));

        var first = await service.ListAsync(new AuditQueryDto { Limit = 2, Page = 1 });
        var second = await service.ListAsync(new AuditQueryDto { Limit = 2, Page = 2 });
        var third = await service.ListAsync(new AuditQueryDto { Limit = 2, Page = 3 });

        Assert.Equal([5, 4], first.Entries.Select(e => e.Seq));
        Assert.Equal([3, 2], second.Entries.Select(e => e.Seq));
        Assert.Equal([1], third.Entries.Select(e => e.Seq));
    }

    [Fact]
    public async Task TheTotalCountsOnlyWhatTheFilterMatched()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo);
        await service.WriteAsync(new AuditEvent("settings.org.update", "Config"));
        await service.WriteAsync(new AuditEvent("xero.connect", "Connected"));

        // A total that ignored the filter would offer a page with nothing on it.
        var page = await service.ListAsync(new AuditQueryDto { Action = "settings" });

        Assert.Single(page.Entries);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task EntriesComeBackNewestFirstWithTheirLabelResolved()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo);
        await service.WriteAsync(new AuditEvent("project.create", "First"));
        await service.WriteAsync(new AuditEvent("xero.connect", "Second"));

        var page = await service.ListAsync(new AuditQueryDto());

        Assert.Equal("xero.connect", page.Entries[0].Action);
        // The row keeps the code for forensics; the UI reads the sentence.
        Assert.Equal("Xero connected", page.Entries[0].Label);
        Assert.True(page.Entries[0].Seq > page.Entries[1].Seq);
    }


    // ---- Verification ----

    [Fact]
    public async Task AnIntactLogVerifiesAndOffersAnAnchorableHead()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo);
        for (var i = 0; i < 3; i++)
            await service.WriteAsync(new AuditEvent("project.create", $"Created {i}"));

        var result = await service.VerifyAsync();

        Assert.True(result.Ok);
        Assert.Equal(3, result.EntriesChecked);
        // The line worth publishing outside the database — without an external
        // anchor the chain only proves nobody edited it carelessly.
        Assert.Contains("seq=3", result.Head);
        Assert.Contains($"format={AuditChain.FormatVersion}", result.Head);
    }

    [Fact]
    public async Task AnEditedRowIsReportedInPlainLanguage()
    {
        var repo = new InMemoryAuditRepository();
        var service = Service(repo);
        for (var i = 0; i < 3; i++)
            await service.WriteAsync(new AuditEvent("project.create", $"Created {i}"));

        repo.Rows[1].Summary = "Something else entirely";

        var result = await service.VerifyAsync();

        Assert.False(result.Ok);
        Assert.Equal(2, result.BrokenAtSeq);
        Assert.Equal("A row's contents were edited after it was written.", result.Reason);
        Assert.Null(result.Head);
    }
}

// ---- Doubles ----

// Appends the way the real repository does — allocating seq and hashing onto
// the end of the chain — so service tests exercise the same shape without a
// database.
internal sealed class InMemoryAuditRepository : IAuditRepository
{
    public List<AuditLog> Rows { get; } = [];

    public Task<AuditLog> AppendAsync(AuditLog entry, string organizationId)
    {
        entry.OrganizationId = organizationId;
        var head = Rows.Where(r => r.OrganizationId == organizationId)
            .OrderByDescending(r => r.Seq)
            .FirstOrDefault();

        entry.Seq = (head?.Seq ?? 0) + 1;
        entry.PrevHash = head?.Hash;
        entry.Hash = AuditChain.ComputeHash(entry, entry.PrevHash);
        Rows.Add(entry);
        return Task.FromResult(entry);
    }

    public Task<(List<AuditLog> Rows, int Total)> QueryAsync(
        AuditQueryDto query, string organizationId)
    {
        IEnumerable<AuditLog> rows = Rows.Where(r => r.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(query.Action))
            rows = rows.Where(r =>
                r.Action == query.Action || r.Action.StartsWith(query.Action + "."));
        if (!string.IsNullOrWhiteSpace(query.Status))
            rows = rows.Where(r => r.Status == query.Status);

        var matched = rows.ToList();
        var limit = Math.Clamp(query.Limit, 1, 200);
        var page = Math.Max(1, query.Page);

        return Task.FromResult((
            matched.OrderByDescending(r => r.Seq).Skip((page - 1) * limit).Take(limit).ToList(),
            matched.Count));
    }

    public Task<List<AuditLog>> GetChainAsync(string organizationId) =>
        Task.FromResult(Rows.Where(r => r.OrganizationId == organizationId)
            .OrderBy(r => r.Seq)
            .ToList());
}

internal sealed class ThrowingAuditRepository : IAuditRepository
{
    public Task<AuditLog> AppendAsync(AuditLog entry, string organizationId) =>
        throw new InvalidOperationException("the database is on fire");

    public Task<(List<AuditLog> Rows, int Total)> QueryAsync(
        AuditQueryDto query, string organizationId) =>
        throw new InvalidOperationException("the database is on fire");

    public Task<List<AuditLog>> GetChainAsync(string organizationId) =>
        throw new InvalidOperationException("the database is on fire");
}

internal sealed class StubCurrentUser : ICurrentUser
{
    public string? UserId { get; set; } = "usr-admin";
    public string? OrganizationId { get; set; } = "org-1";
    public string? Role { get; set; } = "Admin";
    public string? Email { get; set; } = "admin@altomate.com";
    public string? IpAddress { get; set; } = "127.0.0.1";
    public bool IsAdmin => Role is "Admin" or "Owner";
    public bool IsAuthenticated => UserId is not null;
}
