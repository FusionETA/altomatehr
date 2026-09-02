using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Realtime.Dtos;

namespace AltomateHR.Api.Modules.Realtime;

public class RealtimeService : IRealtimeService
{
    private readonly IRealtimeHub _hub;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RealtimeService> _logger;

    public RealtimeService(IRealtimeHub hub, ICurrentUser currentUser, ILogger<RealtimeService> logger)
    {
        _hub = hub;
        _currentUser = currentUser;
        _logger = logger;
    }

    public int ConnectionCount => _hub.ConnectionCount;

    public RealtimeConnection? Connect()
    {
        var userId = _currentUser.UserId;
        var organizationId = _currentUser.OrganizationId;

        // Both halves are required: an org-less token can't be scoped to a tenant,
        // and subscribing it would put it in a bucket nothing ever publishes to.
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(organizationId)) return null;

        return _hub.Connect(organizationId, userId);
    }

    public Task PublishAsync(string organizationId, IEnumerable<string?> userIds, RealtimeEventDto evt)
    {
        try
        {
            if (string.IsNullOrEmpty(organizationId)) return Task.CompletedTask;

            // Deduped because approval chains overlap: the same admin can be both
            // the applicant's next-step approver and an org-wide approver, and
            // they only need one nudge.
            var targets = userIds
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (targets.Count > 0) _hub.Publish(organizationId, targets, evt);
        }
        catch (Exception ex)
        {
            // The caller has already committed a claim / leave / attendance
            // decision. Losing the nudge costs the user one manual refresh;
            // rethrowing here would cost them the decision.
            _logger.LogWarning(ex, "Realtime publish failed for {Scope}/{Action}", evt.Scope, evt.Action);
        }

        // Synchronous today (in-process fan-out). The Task-returning signature is
        // what lets a Redis backplane slot in behind IRealtimeHub without
        // touching a single call site.
        return Task.CompletedTask;
    }
}
