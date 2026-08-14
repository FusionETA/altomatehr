using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Claims.Dtos;

public class RejectClaimDto
{
    [Required]
    [MaxLength(2000)]
    public string? ReviewNotes { get; set; }
}
