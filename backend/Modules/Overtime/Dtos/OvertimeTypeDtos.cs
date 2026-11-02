using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Modules.Overtime.Dtos;

// One of the company's overtime types, with its company-wide multiplier.
public class OvertimeTypeDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public OvertimeTypeKind Kind { get; set; }
    public decimal Multiplier { get; set; }
    public bool IsArchived { get; set; }
}

// Add or edit a type. A built-in type's name and multiplier can change; its
// kind never does.
public class SaveOvertimeTypeDto
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 99.99)]
    public decimal Multiplier { get; set; }
}

// A custom type an employee may pick on an overtime request, at their
// policy's multiplier.
public class PickableOvertimeTypeDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Multiplier { get; set; }
}
