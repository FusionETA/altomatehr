using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Fah;

// Naming the admin's companies to FAH and back.
public static class FahCompanyList
{
    // By id, or by name when only one company has it — two of theirs with
    // the same name are told apart by id (Label), never guessed.
    public static UserOrgDto? Find(IReadOnlyList<UserOrgDto> companies, string asked)
    {
        var byId = companies.FirstOrDefault(c => c.OrganizationId == asked.Trim());
        if (byId is not null) return byId;
        var byName = companies.Where(c => string.Equals(c.Name.Trim(), asked.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    // A company as FAH is told it: its name, and its id too when another of
    // theirs shares the name.
    public static string Label(IReadOnlyList<UserOrgDto> companies, UserOrgDto company) =>
        companies.Count(c => string.Equals(c.Name.Trim(), company.Name.Trim(), StringComparison.OrdinalIgnoreCase)) > 1
            ? $"{company.Name} (id {company.OrganizationId})"
            : company.Name;
}
