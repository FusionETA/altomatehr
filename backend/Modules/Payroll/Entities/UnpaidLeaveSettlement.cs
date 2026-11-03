using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// Which payslip settled which unpaid leave, for which month, and how many
// days: docked in its own month, docked late on a later run, or refunded after
// it was cancelled (negative Days). One row per (payslip, application, month).
//
// Written when a run is generated with Final = false; the run's approval makes
// them Final, and a revert to draft makes them provisional again. Only Final
// rows count as settled, so drafts never see each other's settlements. See
// UnpaidLeaveCarryForward.
//
// The leave's type and dates are copied at the time: a settled application
// that is still approved with the same type and dates is never recounted, so
// a later change to the leave type, the work week or the holidays can't
// re-price a filed month.
public class UnpaidLeaveSettlement : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)] public string OrganizationId { get; set; } = string.Empty;   // tenant
    [MaxLength(40)] public string PayrollRunId { get; set; } = string.Empty;
    [MaxLength(40)] public string PayslipId { get; set; } = string.Empty;
    [MaxLength(40)] public string EmployeeProfileId { get; set; } = string.Empty;
    [MaxLength(40)] public string LeaveApplicationId { get; set; } = string.Empty;

    // The month the leave days fall in — not necessarily the run's.
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }

    [Precision(6, 2)] public decimal Days { get; set; }

    [MaxLength(40)] public string LeaveTypeId { get; set; } = string.Empty;
    public DateTime LeaveStartDate { get; set; }
    public DateTime LeaveEndDate { get; set; }

    public bool Final { get; set; }
    public DateTime CreatedAt { get; set; }
}
