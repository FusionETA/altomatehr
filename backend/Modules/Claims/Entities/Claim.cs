using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // Current position in the approval chain (0-based). Advances as each step's
    // approver signs off; stays PENDING until the final step.
    public int CurrentStep { get; set; }

    public ClaimType ClaimType { get; set; } = ClaimType.EXPENSE;
    public PaymentType PaymentType { get; set; } = PaymentType.PERSONAL;

    [MaxLength(40)]
    public string? PayViaAccountId { get; set; }                 // COMPANY claims: bank account that paid

    [MaxLength(200)]
    public string? SpendingWith { get; set; }                    // optional client/vendor/team

    [MaxLength(200)]
    public string? SpendingAt { get; set; }                      // COMPANY claims: merchant/vendor

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;        // FK (id only, for now)

    [MaxLength(40)]
    public string? ProjectId { get; set; }                        // FK → Projects (settings)

    [MaxLength(40)]
    public string? ChartOfAccountId { get; set; }                 // FK → ChartOfAccounts (settings)

    public bool ExceedsLimit { get; set; }                        // amount blew past the account's spend limit

    [Precision(10, 2)]
    public decimal? Distance { get; set; }                        // MILEAGE only

    public string? MileageOriginAddress { get; set; }             // MILEAGE only
    public string? MileageDestinationAddress { get; set; }        // MILEAGE only

    [Precision(10, 4)]
    public decimal? MileageRateUsed { get; set; }                 // MILEAGE snapshot

    public MileageUnit? MileageUnitUsed { get; set; }             // MILEAGE snapshot

    [MaxLength(1000)]
    public string? ReceiptUrl { get; set; }

    [Column("SupportingDocumentUrls")]
    [JsonIgnore]
    public string? SupportingDocumentUrlsJson { get; set; }

    [NotMapped]
    public List<string> SupportingDocumentUrls
    {
        get
        {
            return string.IsNullOrWhiteSpace(SupportingDocumentUrlsJson)
                ? []
                : JsonSerializer.Deserialize<List<string>>(SupportingDocumentUrlsJson) ?? [];
        }
        set => SupportingDocumentUrlsJson = JsonSerializer.Serialize(
            value.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().ToList());
    }

    public string? ReviewNotes { get; set; }                      // long text, nullable

    // Transient (not a DB column) — the applicant's email, filled only for
    // supervisor/admin team views so an approver can see who filed the claim.
    [NotMapped]
    public string? EmployeeEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
