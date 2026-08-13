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
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Required]
    public DateTime? SpentAt { get; set; }                    // nullable + required → missing date is caught

    public ClaimType ClaimType { get; set; }                 // "EXPENSE" | "MILEAGE"
    public PaymentType PaymentType { get; set; }             // "PERSONAL" | "COMPANY"

    [MaxLength(40)]
    public string? ProjectId { get; set; }                   // optional → Projects settings

    [MaxLength(40)]
    public string? ChartOfAccountId { get; set; }            // optional → Chart of Accounts settings

    [MaxLength(1000)]
    public string? ReceiptUrl { get; set; }
}
