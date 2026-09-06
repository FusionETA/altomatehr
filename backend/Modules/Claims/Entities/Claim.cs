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

    // Transient — whether the CALLER can decide this claim right now. The team
    // view returns a supervisor's whole team, including claims already settled
    // and ones sitting with a different step's approver, so the client needs to
    // know which rows earn Approve/Reject buttons rather than guessing from
    // status alone.
    [NotMapped]
    public bool CanAct { get; set; }

    // Transient — who the claim is waiting on, for the team view. A PENDING
    // claim that has cleared one layer looks identical to a brand new one
    // unless the row can say whose desk it is on now.
    [NotMapped]
    public List<string> AwaitingApprovers { get; set; } = new();

    // ---- Xero ----
    // An approved claim becomes an accounts-payable bill in Xero. The id and
    // reference come back from Xero and are what let an admin find the bill
    // there; the error is kept so a failed sync explains itself instead of
    // just refusing to advance.
    public XeroSyncStatus XeroSyncStatus { get; set; } = XeroSyncStatus.NOT_SYNCED;

    [MaxLength(60)]
    public string? XeroBillId { get; set; }

    [MaxLength(60)]
    public string? XeroBillRef { get; set; }

    public string? XeroSyncError { get; set; }

    public DateTime? XeroSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
