namespace AltomateHR.Api.Modules.Leave.Entities;

// Mirrors the Prisma LeaveStatus enum. Stored as a string in the DB and
// serialized as a string in JSON.
public enum LeaveStatus
{
    PENDING,
    APPROVED,
    REJECTED,
    CANCELLED,
}

// How a year's entitlement becomes available.
//   LUMP_SUM  — the whole entitlement from day one of the year.
//   PRO_RATED — EntitledDays/12 accrues each month (the monthly cron).
// Resolved narrowest-wins: LeaveEntitlement → PolicyLeaveEntitlement → LeaveType.
public enum LeaveAccrualMethod
{
    LUMP_SUM,
    PRO_RATED,
}

// Half-day leave. MORNING/AFTERNOON must start and end on the same date and
// count as 0.5 days.
public enum LeaveDuration
{
    FULL_DAY,
    MORNING,
    AFTERNOON,
}
