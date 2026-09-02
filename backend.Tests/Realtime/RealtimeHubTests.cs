using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Realtime.Dtos;
using Microsoft.Extensions.Logging.Abstractions;

namespace AltomateHR.Api.Tests.Realtime;

// The fan-out registry behind /realtime/stream. Small surface, but two of its
// properties are load-bearing: tenant isolation, and never growing without
// bound when a client stops reading.
public class RealtimeHubTests
{
    private static RealtimeHub Hub() => new(NullLogger<RealtimeHub>.Instance);

    private static RealtimeEventDto Event() =>
        RealtimeEventDto.For(RealtimeScope.CLAIMS, RealtimeAction.APPROVED, "clm-1");

    [Fact]
    public void DeliversToTheTargetedUser()
    {
        var hub = Hub();
        using var connection = hub.Connect("org-1", "usr-1");

        hub.Publish("org-1", ["usr-1"], Event());

        Assert.True(connection.Events.TryRead(out var received));
        Assert.Equal(RealtimeScope.CLAIMS, received!.Scope);
        Assert.Equal("clm-1", received.EntityId);
    }

    [Fact]
    public void DoesNotDeliverToAnUntargetedUser()
    {
        var hub = Hub();
        using var mine = hub.Connect("org-1", "usr-1");
        using var theirs = hub.Connect("org-1", "usr-2");

        hub.Publish("org-1", ["usr-1"], Event());

        Assert.True(mine.Events.TryRead(out _));
        Assert.False(theirs.Events.TryRead(out _));
    }

    // Tenancy is a security boundary everywhere else in this codebase (see
    // AppDbContext's global filters); the live channel must not be the one place
    // a same-id user in another org can be reached.
    [Fact]
    public void DoesNotCrossTenants_EvenForTheSameUserId()
    {
        var hub = Hub();
        using var orgOne = hub.Connect("org-1", "usr-shared");
        using var orgTwo = hub.Connect("org-2", "usr-shared");

        hub.Publish("org-1", ["usr-shared"], Event());

        Assert.True(orgOne.Events.TryRead(out _));
        Assert.False(orgTwo.Events.TryRead(out _));
    }

    [Fact]
    public void DeliversToEveryTabTheSamePersonHasOpen()
    {
        var hub = Hub();
        using var tabOne = hub.Connect("org-1", "usr-1");
        using var tabTwo = hub.Connect("org-1", "usr-1");

        hub.Publish("org-1", ["usr-1"], Event());

        Assert.True(tabOne.Events.TryRead(out _));
        Assert.True(tabTwo.Events.TryRead(out _));
    }

    [Fact]
    public void PublishingToSomeoneWithNoOpenTabIsANoOp()
    {
        var hub = Hub();

        hub.Publish("org-1", ["nobody"], Event());   // must not throw

        Assert.Equal(0, hub.ConnectionCount);
    }

    [Fact]
    public void DisposeUnregistersTheConnection()
    {
        var hub = Hub();
        var connection = hub.Connect("org-1", "usr-1");
        Assert.Equal(1, hub.ConnectionCount);

        connection.Dispose();

        Assert.Equal(0, hub.ConnectionCount);
        hub.Publish("org-1", ["usr-1"], Event());     // nothing left to receive it
    }

    // A tab that stops reading (backgrounded, wedged, throttled) must not be
    // able to grow the server's heap. Events are "re-fetch" nudges, so the newest
    // supersedes the ones behind it and dropping the oldest loses nothing.
    [Fact]
    public void BoundsAQueueThatIsNeverRead_KeepingTheNewestEvents()
    {
        var hub = Hub();
        using var connection = hub.Connect("org-1", "usr-1");

        for (var i = 0; i < 200; i++)
            hub.Publish("org-1", ["usr-1"], RealtimeEventDto.For(
                RealtimeScope.LEAVE, RealtimeAction.SUBMITTED, $"app-{i}"));

        var drained = new List<string?>();
        while (connection.Events.TryRead(out var evt)) drained.Add(evt!.EntityId);

        Assert.Equal(32, drained.Count);              // the bounded depth
        Assert.Equal("app-199", drained[^1]);         // and it's the newest that survived
    }

    [Fact]
    public void SerializesEnumsAsStringsInCamelCase_MatchingTheRestEndpoints()
    {
        var json = RealtimeEventDto.For(RealtimeScope.ATTENDANCE, RealtimeAction.REJECTED, "req-9").ToJson();

        Assert.Contains("\"scope\":\"ATTENDANCE\"", json);
        Assert.Contains("\"action\":\"REJECTED\"", json);
        Assert.Contains("\"entityId\":\"req-9\"", json);
    }
}
