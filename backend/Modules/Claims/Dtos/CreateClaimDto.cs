using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Claims.Dtos;

// What the client sends to create/update a claim.
// The server controls: Id, ClaimNumber, Status, SubmittedAt, CreatedAt, UpdatedAt.
public class CreateClaimDto
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    public ClaimCategory Category { get; set; }               // enum (sent as "MEAL", etc.)

    [Range(0.01, 1_000_000, ErrorMessage = "Amount must be greater than 0.")]
    public decimal? Amount { get; set; }                         // EXPENSE only; MILEAGE is calculated

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Required]
    public DateTime? SpentAt { get; set; }                    // nullable + required → missing date is caught

    public ClaimType ClaimType { get; set; }                 // "EXPENSE" | "MILEAGE"
    public PaymentType PaymentType { get; set; }             // "PERSONAL" | "COMPANY"

    [MaxLength(40)]
    public string? PayViaAccountId { get; set; }              // COMPANY only

    [MaxLength(200)]
    public string? SpendingWith { get; set; }

    [MaxLength(200)]
    public string? SpendingAt { get; set; }                   // COMPANY requires merchant/vendor

    [MaxLength(40)]
    public string? ProjectId { get; set; }                   // optional → Projects settings

    [Required, MaxLength(40)]
    public string? ChartOfAccountId { get; set; }            // required → Chart of Accounts settings

    [Range(0.01, 1_000_000, ErrorMessage = "Distance must be greater than 0.")]
    public decimal? Distance { get; set; }                   // MILEAGE only

    public string? MileageOriginAddress { get; set; }        // MILEAGE only
    public string? MileageDestinationAddress { get; set; }   // MILEAGE only

    [MaxLength(1000)]
    public string? ReceiptUrl { get; set; }
}
