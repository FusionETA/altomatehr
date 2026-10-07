using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Employees.Entities;

// A scheduled move of one employee from this org to another org the same admin
// runs (the monolith's EmployeeTransfer). Created from the "Transfer" button on
// the employee screen; executed on its effective date — inline when that date
// is today, otherwise by ExecuteDueTransfersBackgroundService.
//
// On execute the source profile is archived (leave date = the day before) and
// the person gains a membership + profile at the target, carrying their
// personal details and — when CopyPayrollInfo — their statutory, bank and
// salary setup plus this year's YTD as "previous employment", so PCB at the
// target picks up where the source left off. Teams, shift, leave balances,
// claims and documents are never copied: they are a fresh hire there for those.
//
// Tenant-scoped to the SOURCE org — the one whose admin scheduled it and whose
// employee screen shows the pending banner. At most one PENDING row per
// (org, user); CANCELLED / EXECUTED / FAILED rows stay as history.
public class EmployeeTransfer : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant = source org

    // The person moving. Keyed on the user, not the profile: a member may not
    // have a saved profile yet, and the target side is found by user too.
    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string TargetOrganizationId { get; set; } = string.Empty;

    // Their policy at the target. Must belong to the target org.
    [MaxLength(40)]
    public string TargetPolicyId { get; set; } = string.Empty;

    // First day at the target (a UTC-midnight day key, as leave dates are).
    public DateTime EffectiveDate { get; set; }

    // True: statutory numbers, bank, salary, allowances and YTD go across.
    // False: only personal / identity details — the target admin sets payroll up fresh.
    public bool CopyPayrollInfo { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public EmployeeTransferStatus Status { get; set; } = EmployeeTransferStatus.PENDING;

    [MaxLength(40)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }

    // A fingerprint of the previous-employment figures this transfer wrote on
    // the target profile. The old company's later payroll submits re-carry
    // them only while the target still holds exactly these — a figure HR
    // corrected by hand is theirs, and is left alone.
    [MaxLength(64)]
    public string? CarriedPrevFingerprint { get; set; }

    // Why the last attempt failed, for the operator. The daily job retries a
    // FAILED row until it executes or someone cancels it.
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
}

public enum EmployeeTransferStatus
{
    PENDING,
    EXECUTED,
    CANCELLED,
    FAILED,
}
