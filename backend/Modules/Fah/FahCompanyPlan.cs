using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies.Dtos;

namespace AltomateHR.Api.Modules.Fah;

// One change the Owner asked FAH to make in several of their companies at
// once, held with its card until they press Confirm. Steps says, company by
// company, what will happen there — or why it's skipped — as checked when
// the card was made; Confirm checks each again before making it.
//
// Exactly one of Policy / Admin is set, by Tool. Settings: what's being
// created (a new policy's every setting, a new admin's access), shown once
// above the companies.
public sealed record FahCompanyPlan(
    string Tool,
    string Title,
    string SourceOrganizationId,
    string SourceCompany,
    IReadOnlyList<FahCompanyStep> Steps,
    FahPolicyCopy? Policy = null,
    FahAdminCopy? Admin = null,
    string? Settings = null)
{
    public int Doing => Steps.Count(s => !s.Skip);
}

public sealed record FahCompanyStep(string OrganizationId, string Company, bool Skip, string Detail);

// The policy as it is in the source company (or as the admin described a
// new one). Leave days go by leave type NAME: each company has its own leave
// types, matched on Confirm. A type not listed keeps its own default days.
public sealed record FahPolicyCopy(SavePolicyDto Policy, IReadOnlyList<FahLeaveDays> LeaveDays);

public sealed record FahLeaveDays(string LeaveType, double Days);

// The admin's access as it is in the source company (or as the Owner
// described it for a new admin). Their employee limit goes by policy NAME,
// matched in each company on Confirm; a name a company doesn't have narrows
// them there, never widens them. DateOfBirth: for a brand-new account only,
// whose first password is the house rule (email + birthday as MMDD).
public sealed record FahAdminCopy(
    string Email,
    string Name,
    Dictionary<string, ModuleLevel>? Levels,
    IReadOnlyList<string>? PolicyNames,
    bool CanChangeSettings,
    DateTime? DateOfBirth = null);
