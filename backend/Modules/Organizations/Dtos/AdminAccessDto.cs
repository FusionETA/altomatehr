using AltomateHR.Api.Modules.Organizations;

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
    // Entries are "key" (manage) or "key:view" — Levels below is the same, parsed.
    public List<string>? Modules { get; set; }

    // Per module: View or Manage. Null = every module at Manage (full access).
    // A module absent from a non-null map is Off.
    public Dictionary<string, ModuleLevel>? Levels { get; set; }

    // The policies whose employees this admin covers. Null = every employee.
    public List<string>? PolicyIds { get; set; }

    // May change the company's configuration (organisation, work schedule,
    // payroll settings, Xero). Always true for an Owner.
    public bool CanChangeSettings { get; set; } = true;
}

// The Owner's "Manage access" save — the three independent limits.
public class SetAdminAccessDto
{
    // Per module: View or Manage (None or absent = Off). Null = full access.
    public Dictionary<string, ModuleLevel>? Levels { get; set; }

    // Only employees on these policies. Null = every employee; empty = nobody.
    public List<string>? PolicyIds { get; set; }

    public bool CanChangeSettings { get; set; } = true;
}

// What the module picker (and the nav) need: every grantable module key, and the
// caller's own effective enabled set (plan ceiling ∩ their grant).
public class ModuleAccessDto
{
    public IReadOnlyList<string> All { get; set; } = [];
    public IReadOnlyCollection<string> Enabled { get; set; } = [];

    // The caller's own limits, so screens can hide what would be refused:
    // per enabled module View/Manage; whether they may change settings; whether
    // they see every employee (false = limited to some policies, which also
    // makes company-wide payroll view-only).
    public IReadOnlyDictionary<string, ModuleLevel> Levels { get; set; } = new Dictionary<string, ModuleLevel>();
    public bool CanChangeSettings { get; set; } = true;
    public bool AllEmployees { get; set; } = true;
}
