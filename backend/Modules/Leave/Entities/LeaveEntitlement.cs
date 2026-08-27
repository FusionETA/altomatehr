using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Leave.Entities;

// One employee's entitlement for ONE leave type in ONE year.
//
// This is the STATEFUL half of leave. Balances used to be derived on the fly
// (type default − approved days), which is correct only for LUMP_SUM types with
// no carry-forward. Accrual and carry-forward are path-dependent — how much has
// accrued depends on months elapsed, and this year's carried days depend on last
// year's closing balance — so they have to be stored and advanced by the crons
// (monthly accrual + year rollover) rather than recomputed from constants.
//
// Rows are created lazily: a year nobody has touched has no rows, which means
// "not yet opened", NOT "zero entitlement". Mirrors production's behaviour.
public class LeaveEntitlement : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string LeaveTypeId { get; set; } = string.Empty;

    public int Year { get; set; }

    // The full year's entitlement, resolved at creation from the policy override
    // or the type default. Frozen on the row so later config changes don't
    // silently rewrite history.
    public double EntitledDays { get; set; }

    // PRO_RATED: accumulates EntitledDays/12 each month, capped at EntitledDays.
    // LUMP_SUM: equals EntitledDays from the start.
    public double AccruedDays { get; set; }

    // Unused days carried in from LAST year, capped by LeaveType.MaxCarryForwardDays.
    public double CarriedDays { get; set; }

    // When the carried days lapse (from LeaveType.CarryExpiryMonth). Null = never.
    public DateTime? CarriedExpiresAt { get; set; }

    // Set by the monthly accrual sweep once CarriedExpiresAt has passed.
    public bool CarriedExpired { get; set; }
    public DateTime? CarriedExpiredAt { get; set; }

    // How many days were forfeited at the sweep. Kept indefinitely so a
    // cash-out or audit can answer "she lost X days on date Y" years later.
    public double? CarriedExpiredDays { get; set; }

    // Per-employee override of the accrual method. Null = inherit from the
    // policy, then the type. (type → policy → employee, narrowest wins)
    public LeaveAccrualMethod? AccrualMethod { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
