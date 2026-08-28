using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Holidays.Dtos;

public class HolidayDto
{
    public string Id { get; set; } = string.Empty;
    public string? ProjectId { get; set; }        // null = org-wide
    public string Date { get; set; } = string.Empty;   // yyyy-MM-dd
    public string Name { get; set; } = string.Empty;
}

// Create/update a holiday. Id / org are server-controlled.
public class SaveHolidayDto
{
    // Null = org-wide; set = only that project observes it.
    [MaxLength(40)]
    public string? ProjectId { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;
}
