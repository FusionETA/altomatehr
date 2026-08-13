using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Attendance.Dtos;

public class ClockOutDto
{
    [MaxLength(1000)]
    public string? Remark { get; set; }

    [Range(-90, 90)]
    public double? Lat { get; set; }

    [Range(-180, 180)]
    public double? Lng { get; set; }

    [MaxLength(1000)]
    public string? PhotoUrl { get; set; }
}
