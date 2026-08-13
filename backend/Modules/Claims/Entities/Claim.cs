using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Claims.Entities;

// EF Core entity → the "Claims" table. Core fields, aligned with the real
// AltomateHR schema. Xero / mileage / approval / relational fields get added
// later as we migrate those concerns (strangler-fig style).
public class Claim : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();   // string PK (real app uses cuid)

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;    // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string ClaimNumber { get; set; } = string.Empty;       // server-generated, unique

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;       // long text

    public ClaimCategory Category { get; set; }

    [Precision(12, 2)]
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    public DateTime SpentAt { get; set; }
    public DateTime SubmittedAt { get; set; }

    public ClaimStatus Status { get; set; } = ClaimStatus.PENDING;
    public ClaimType ClaimType { get; set; } = ClaimType.EXPENSE;
    public PaymentType PaymentType { get; set; } = PaymentType.PERSONAL;

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;        // FK (id only, for now)

    [MaxLength(40)]
    public string? ProjectId { get; set; }                        // FK → Projects (settings)

    [MaxLength(40)]
    public string? ChartOfAccountId { get; set; }                 // FK → ChartOfAccounts (settings)

    public bool ExceedsLimit { get; set; }                        // amount blew past the account's spend limit

    [MaxLength(1000)]
    public string? ReceiptUrl { get; set; }

    public string? ReviewNotes { get; set; }                      // long text, nullable

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
