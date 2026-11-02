using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Overtime.Entities;

// One kind of overtime a company pays, with its company-wide multiplier
// (× the hourly rate). A policy can override the multiplier, or switch a
// custom type off for its staff (PolicyOvertimeRate, and the three
// OtRate* columns on EmployeePolicy for the built-in kinds).
//
// Three are built in and always exist — normal day, rest day, public holiday
// — because approved overtime lands in them by the calendar. They can be
// renamed and re-rated, never archived. Anything else a company adds is
// CUSTOM: an employee picks it on their overtime request, or an admin types
// its hours on the payroll run.
public class OvertimeType : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public OvertimeTypeKind Kind { get; set; } = OvertimeTypeKind.CUSTOM;

    [Precision(4, 2)] public decimal Multiplier { get; set; }

    public int SortOrder { get; set; }

    // A custom type no longer offered. Payslips that paid it keep its name
    // and rate in their own snapshot.
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum OvertimeTypeKind
{
    NORMAL_DAY,
    REST_DAY,
    PUBLIC_HOLIDAY,
    CUSTOM,
}
