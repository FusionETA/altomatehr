using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Shifts.Dtos;

public class ShiftDto
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string? WorkingDays { get; set; }
    public int LunchBreakMinutes { get; set; }
    public bool IsDefault { get; set; }
}

// Create a shift. ProjectId is set once here and is immutable afterwards
// (matching the reference app — editing a shift never moves it to another project).
public class CreateShiftDto
{
    [Required, MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:MM (24-hour) format")]
    public string StartTime { get; set; } = string.Empty;

    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:MM (24-hour) format")]
    public string EndTime { get; set; } = string.Empty;

    [RegularExpression(@"^[1-7](,[1-7])*$", ErrorMessage = "Comma-separated weekday numbers 1-7 (Mon-Sun)")]
    public string? WorkingDays { get; set; }

    [Range(0, 240)]
    public int LunchBreakMinutes { get; set; } = 60;
}

public class UpdateShiftDto
{
    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:MM (24-hour) format")]
    public string StartTime { get; set; } = string.Empty;

    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:MM (24-hour) format")]
    public string EndTime { get; set; } = string.Empty;

    [RegularExpression(@"^[1-7](,[1-7])*$", ErrorMessage = "Comma-separated weekday numbers 1-7 (Mon-Sun)")]
    public string? WorkingDays { get; set; }

    [Range(0, 240)]
    public int LunchBreakMinutes { get; set; } = 60;
}
