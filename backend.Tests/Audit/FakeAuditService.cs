using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Dtos;

namespace AltomateHR.Api.Tests.Audit;

// Records what it was asked to write instead of persisting it, so a test can
// assert that an action was audited without standing up a database.
internal sealed class FakeAuditService : IAuditService
{
    public List<AuditEvent> Written { get; } = [];

    public Task WriteAsync(AuditEvent entry)
    {
        Written.Add(entry);
        return Task.CompletedTask;
    }

    // Only the count, by action — enough for callers asking "has this happened before".
    public Task<AuditPageDto> ListAsync(AuditQueryDto query) =>
        Task.FromResult(new AuditPageDto
        {
            Total = Written.Count(e => query.Action is null || e.Action == query.Action),
        });

    public Task<AuditVerificationDto> VerifyAsync() =>
        Task.FromResult(new AuditVerificationDto { Ok = true });

    public bool Recorded(string action) => Written.Any(e => e.Action == action);
}
