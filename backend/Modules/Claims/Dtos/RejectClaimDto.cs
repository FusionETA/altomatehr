using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Claims.Dtos;

public class RejectClaimDto
{
    [MaxLength(2000)]
    public string? ReviewNotes { get; set; }
}
