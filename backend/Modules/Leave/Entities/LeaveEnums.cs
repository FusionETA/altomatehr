namespace AltomateHR.Api.Modules.Leave.Entities;

// Mirrors the Prisma LeaveStatus enum. Stored as a string in the DB and
// serialized as a string in JSON. (LeaveDuration / half-days deferred.)
public enum LeaveStatus
{
    PENDING,
    APPROVED,
    REJECTED,
    CANCELLED,
}
