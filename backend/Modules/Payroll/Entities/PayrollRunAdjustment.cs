using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// What an admin typed for ONE employee on ONE run.
//
// This table exists for exactly one reason: GENERATION IS DESTRUCTIVE. Pressing
// Generate discards the run's payslips and line items and rebuilds them from
// the employees' current profiles, so anything hand-entered has to live
// somewhere the rebuild does not touch. Overtime hours, one-off allowances and
// deductions, and per-run tweaks to the profile's recurring rows all land here
// and are re-applied on every generation.
//
// At most one row per (run, employee) — the unique index enforces it. A row is
// created on first save and deleted outright when cleared; there is no "empty"
// row, so its absence and its emptiness are the same thing.
public class PayrollRunAdjustment : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string PayrollRunId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EmployeeProfileId { get; set; } = string.Empty;

    // ---- Overtime ----
    // Hours only. The multipliers come from the employee's policy at generation
    // time, and are NOT snapshotted here: a policy rate correction should reach
    // a draft run's next generation, and once the run is SUBMITTED the payslip
    // holds the resulting money anyway.
    [Precision(8, 2)] public decimal OtNormalHours { get; set; }
    [Precision(8, 2)] public decimal OtRestHours { get; set; }
    [Precision(8, 2)] public decimal OtPublicHours { get; set; }

    // ---- One-off rows ----
    // JSON array of `ManualLineItem`. Empty array, never null.
    public string ManualLineItemsJson { get; set; } = "[]";

    // JSON object of index → `FixedAllowanceOverride`. Empty object, never null.
    public string FixedAllowanceOverridesJson { get; set; } = "{}";

    // ---- Attendance overrides ----
    // Null = use the figure derived from attendance. For MONTHLY staff these
    // are display-only (pay is day-based); for HOURLY staff `WorkedHours` IS
    // the paid quantity, so an override here moves real money.
    [Precision(8, 2)] public decimal? WorkedHours { get; set; }
    [Precision(8, 2)] public decimal? ExpectedHours { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
