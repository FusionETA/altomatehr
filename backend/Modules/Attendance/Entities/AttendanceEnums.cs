namespace AltomateHR.Api.Modules.Attendance.Entities;

// Mirrors the Prisma AttendanceStatus enum. Stored as a string in the DB
// (configured in AppDbContext) and serialized as a string in JSON.
//
// This first slice only uses MISSING / CLOCKED_IN / CLOCKED_OUT. ON_TIME,
// LATE and ON_LEAVE become meaningful once shift policy (working-hours start)
// and the leave module land — the values exist now so that migration is purely
// additive.
public enum AttendanceStatus
{
    ON_TIME,
    LATE,
    MISSING,
    CLOCKED_IN,
    CLOCKED_OUT,
    ON_LEAVE,
}

public enum AttendanceApprovalStatus
{
    PENDING,
    APPROVED,
    REJECTED,
}

// Which submittable event an AttendanceApprovalRequest covers. Each kind gets
// its own row, never overwriting another kind's decision — this is what fixes
// the old bug where clock-out silently reset clock-in's already-decided
// approval (and break-end reset break-start's).
public enum AttendanceApprovalKind
{
    CLOCK_IN,
    CLOCK_OUT,
    BREAK_START,
    BREAK_END,
}
