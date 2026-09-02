namespace AltomateHR.Api.Modules.Leave.Dtos;

// Per-type leave balance for one employee in one year.
//
// RemainingDays is what they may actually apply for now — for LUMP_SUM that's
// the full entitlement, for PRO_RATED only what has accrued so far — plus any
// unexpired carry-forward, minus days taken. Pending is reported separately so
// requests awaiting approval are visible without reducing the balance.
//
// IsOpened=false means no entitlement row exists for this year yet (the rollover
// hasn't run). The figures are then the projection the rollover WOULD create,
// not stored state.
public class LeaveBalanceDto
{
    public string LeaveTypeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public double EntitlementDays { get; set; }

    // How much of the entitlement is available so far. LUMP_SUM: the whole
    // thing. PRO_RATED: what the monthly cron has drip-fed.
    public double AccruedDays { get; set; }

    // Unused days rolled in from last year, and when they lapse.
    public double CarriedDays { get; set; }
    public DateTime? CarriedExpiresAt { get; set; }
    public bool CarriedExpired { get; set; }

    public string AccrualMethod { get; set; } = "LUMP_SUM";
    public int Year { get; set; }
    public bool IsOpened { get; set; }
    public double TakenDays { get; set; }
    public double PendingDays { get; set; }
    public double RemainingDays { get; set; }
}
