using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Claims.Dtos;

// Filters for the claims summary export. All optional — an unfiltered call
// exports every claim the caller may see.
//
// Unlike production (which defaults to the current month so an accidental click
// can't dump years of data), this defaults to everything: the tenant filter
// already bounds it to one org, and an admin exporting "the claims" and getting
// only this month is the more surprising outcome of the two.
public class ClaimsExportQueryDto
{
    // Inclusive, matched against SpentAt (when the money was spent) unless
    // DateField says otherwise.
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    // "spent" (default) or "submitted" — finance reconciles on spend date,
    // payroll on submission date.
    [MaxLength(20)]
    public string? DateField { get; set; }

    public ClaimStatus? Status { get; set; }

    // PERSONAL = the employee is out of pocket and the org owes them; COMPANY =
    // the money already left a company account. A reimbursement run is only ever
    // the PERSONAL half, so the export has to be able to say which.
    public PaymentType? PaymentType { get; set; }

    [MaxLength(40)]
    public string? EmployeeId { get; set; }

    [MaxLength(40)]
    public string? ProjectId { get; set; }
}
