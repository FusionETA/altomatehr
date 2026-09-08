namespace AltomateHR.Api.Modules.Attendance.Dtos;

// How long each supervisor takes to decide, measured against the org's SLA.
//
// The point is not to rank people. It is to answer "is anything sitting
// unreviewed because of one person's queue" — which is why the slow count and
// the worst case are here alongside the average: an average of 40 minutes hides
// the one request that waited three days.
public class SupervisorPerformanceDto
{
    public string ReviewerId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public int TotalDecisions { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }

    // Decisions that took longer than the org's SupervisorSlaMinutes.
    public int SlowDecisionCount { get; set; }

    // Null when nothing in range had both a submitted and a decided time.
    public double? AvgDelayMinutes { get; set; }
    public double? MaxDelayMinutes { get; set; }
}

// One decided request, for the audit list: who decided what, and how long they
// took. Pending rows are included when asked for — "nobody has touched this in
// four days" is the more urgent half of the question.
public class ApprovalAuditEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime EventAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public string? ReviewNotes { get; set; }

    // Minutes from submission to decision, or to now while still pending —
    // so a stale pending row sorts and reads alongside a slow decided one.
    public double? DelayMinutes { get; set; }
}

// What the clock-in selfies are costing in storage.
//
// Sizes are read from disk rather than stored on the record: nothing writes a
// byte count today, and a stat() per photo is cheap next to being wrong about
// how much space an org is using.
public class SelfieStorageDto
{
    public int PhotoCount { get; set; }
    public long TotalBytes { get; set; }

    // Photos the record points at but that are no longer on disk. Not an error
    // — a pruned or manually deleted file — but worth surfacing, because it is
    // also what a broken upload path looks like.
    public int MissingCount { get; set; }
    public DateTime? OldestPhotoAt { get; set; }
}
