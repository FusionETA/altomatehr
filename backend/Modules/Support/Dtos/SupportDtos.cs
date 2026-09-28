using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Support.Dtos;

// One company on the Fusioneta support page.
public class SupportOrganizationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string? Tier { get; set; }
    public IReadOnlyList<string> Addons { get; set; } = [];

    // The first Owner, so support can tell companies with the same name apart.
    public string? OwnerName { get; set; }
    public string? OwnerEmail { get; set; }

    // Employees and supervisors — the people the company actually runs.
    public int EmployeeCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

// Provision a brand-new company and its Owner's sign-in, as the previous
// system's support page did.
public class CreateSupportCompanyDto
{
    [Required, MinLength(2), MaxLength(120)]
    public string OrgName { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string OwnerName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(160)]
    public string OwnerEmail { get; set; } = string.Empty;

    // Required only when the email has no account yet; an existing account
    // keeps its own password and is simply made Owner of the new company.
    [MinLength(8), MaxLength(128)]
    public string? Password { get; set; }

    public string Plan { get; set; } = "DIY";       // DIY | EXPERT
    public string? Tier { get; set; } = "PAID";     // FREE | PAID (DIY only)
    public bool Claims { get; set; } = true;
    public bool Attendance { get; set; } = true;
}

public class CreateSupportCompanyResultDto
{
    public string OrganizationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;

    // False when the owner email already had an account — their existing
    // password still applies.
    public bool OwnerCreated { get; set; }
}
