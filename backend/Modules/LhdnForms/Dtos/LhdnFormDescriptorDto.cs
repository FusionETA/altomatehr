namespace AltomateHR.Api.Modules.LhdnForms.Dtos;

// One card in the "LHDN Forms" grid — everything the UI needs to render the
// button without duplicating the availability/deadline business logic.
public class LhdnFormDescriptorDto
{
    public string Kind { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool NeedsYearPicker { get; set; }
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }
    /// CP22 only: "Due in N days" / "Overdue by N days" / "Overdue — file late".
    public string? Badge { get; set; }
    public string? BadgeVariant { get; set; }
}
