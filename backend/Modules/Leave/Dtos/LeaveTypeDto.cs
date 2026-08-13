using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Leave.Dtos;

public class LeaveTypeDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public double DefaultDays { get; set; }
    public bool IsArchived { get; set; }
}

// Used for both create and update. (Id / IsArchived / org are server-controlled.)
public class SaveLeaveTypeDto
{
    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public bool Paid { get; set; } = true;

    [Range(0, 365)]
    public double DefaultDays { get; set; }
}
