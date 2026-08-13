using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Attendance.Dtos;

// What the client sends to clock in. All optional for this slice — geofence
// (lat/lng/distance) and selfies get added when those concerns are migrated.
public class ClockInDto
{
    [MaxLength(40)]
    public string? ProjectId { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(1000)]
    public string? Remark { get; set; }

    // Employee GPS at clock-in (from the browser). Optional — null when the
    // user didn't grant location.
    [Range(-90, 90)]
    public double? Lat { get; set; }

    [Range(-180, 180)]
    public double? Lng { get; set; }

    // URL from POST /attendance/photo. Required (with a remark) when off-site.
    [MaxLength(1000)]
    public string? PhotoUrl { get; set; }
}
