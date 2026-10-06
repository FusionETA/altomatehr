namespace AltomateHR.Api.Modules.Provisioning;

// Who administers a company that an integration created — the follow-up to
// POST /admin/organizations, which creates a company with nobody in it.
public interface IOrgAdminService
{
    // Make this email an administrator of the organization. Idempotent.
    Task<GrantAdminResult> GrantAsync(string organizationId, string email, string? name);

    // The organization's administrators, optionally narrowed to some roles.
    Task<IReadOnlyList<OrgAdminRow>> ListAsync(IReadOnlySet<string> roles, int? limit);
}

// `Created`: a new account was made. `Linked`: the person gained a seat they
// did not have. `Role`: the seat they now hold — Owner for the first person
// given one in a company that had nobody administering it, Admin otherwise.
public sealed record GrantAdminResult(string Email, bool Created, bool Linked, string Role);

public sealed record OrgAdminRow(string UserId, string Email, string? Name, string Role);
