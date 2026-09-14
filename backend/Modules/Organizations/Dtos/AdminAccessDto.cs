namespace AltomateHR.Api.Modules.Organizations.Dtos;

// One admin the Owner can control access for, with their current module grant.
public class AdminAccessDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    // The admin's module grant: null = full access (everything the org's plan
    // enables); a list narrows them to those modules; an empty list locks them
    // out. This is OrganizationMembership.Modules, surfaced for the Owner UI.
    public List<string>? Modules { get; set; }
}

// The Owner's "Manage access" save. Null modules = full access; a list narrows;
// an empty list locks out. Each entry is validated against the known module keys.
public class SetAdminAccessDto
{
    public List<string>? Modules { get; set; }
}

// What the module picker (and the nav) need: every grantable module key, and the
// caller's own effective enabled set (plan ceiling ∩ their grant).
public class ModuleAccessDto
{
    public IReadOnlyList<string> All { get; set; } = [];
    public IReadOnlyCollection<string> Enabled { get; set; } = [];
}
