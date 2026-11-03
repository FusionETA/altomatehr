using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Organizations;

// One person's Manage access in a NAMED org, resolved with NO request context:
// nobody is signed in, there is no active tenant, and the answer is about a
// company the caller may not belong to.
//
// This is what lets a companion app (ABPay) honour Manage access. It holds the
// company's own wp_live_ key, which sees the WHOLE company — so without this
// it would show a limited admin every employee's pay. The resolution is read
// live on every call, with no cache, because an Owner who narrows an admin's
// access expects it to bite on the companion's next request.
//
// Master-key callers only — PartnerApiController gates it. A company key must
// never be able to read another person's access.
public interface IAdminAccessResolver
{
    // Null = no such user, not an admin or owner here, or the company is
    // deactivated — the caller turns all three into one 404, so nobody can
    // use it to learn which accounts exist.
    Task<AdminAccessSnapshot?> ResolveAsync(string organizationId, string userId);

    // The membership-level half of the answer, without resolving WHICH
    // employees — the flags /auth/verify and /auth/organizations carry per
    // company, where listing people would be a query per org for nothing.
    Task<AdminAccessFlags> FlagsAsync(string organizationId, Employees.Entities.OrganizationMembership membership);
}

// `Employees` is null when `AllEmployees` — an unrestricted admin or an owner
// covers everyone, and sending thousands of ids to say so helps nobody.
public sealed record AdminAccessSnapshot(
    string Role,
    bool AllEmployees,
    bool CanRunPayroll,
    bool CanChangeSettings,
    IReadOnlyDictionary<string, ModuleLevel> Modules,
    IReadOnlyList<ScopedEmployee>? Employees);

public sealed record AdminAccessFlags(bool AllEmployees, bool CanRunPayroll);
