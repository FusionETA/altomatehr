using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Data;

// Seeds the demo tenant + users (hashed passwords) on startup, and backfills any
// pre-tenancy rows so they belong to the demo org. Runs from Program.cs after build,
// where there is no request context (so the global query filter is a no-op).
public static class DbSeeder
{
    private const string DemoOrgId = "org-altomate";

    public static async Task SeedAsync(
        IOrganizationRepository organizations,
        IUserRepository users,
        IClaimsRepository claims,
        ILeaveTypeRepository leaveTypes,
        IEmployeePolicyRepository policies)
    {
        await SeedOrganizationAsync(organizations);
        await EnsureUserAsync(users, "usr-admin", "admin@altomate.com", "Admin");
        await EnsureUserAsync(users, "usr-super", "supervisor@altomate.com", "Supervisor");
        await EnsureUserAsync(users, "usr-emp", "employee@altomate.com", "Employee");
        await AssignSupervisorAsync(users, "usr-emp", "usr-super");
        await BackfillClaimsAsync(claims);
        await SeedLeaveTypesAsync(leaveTypes);
        await SeedPolicyAsync(policies);
    }

    private static async Task SeedPolicyAsync(IEmployeePolicyRepository policies)
    {
        if ((await policies.GetAllAsync()).Count > 0) return;

        var now = DateTime.UtcNow;
        await policies.AddAsync(new EmployeePolicy
        {
            OrganizationId = DemoOrgId,   // set explicitly — no request context during seeding
            Name = "Full-time",
            Description = "Default policy: geofenced attendance, standard leave.",
            IsDefault = true,
            RequireGeofence = true,
            SalaryType = SalaryType.MONTHLY,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    // Point an employee at their approving supervisor (idempotent).
    private static async Task AssignSupervisorAsync(IUserRepository users, string employeeId, string supervisorId)
    {
        var employee = await users.GetByIdAsync(employeeId);
        if (employee is null || employee.SupervisorId == supervisorId) return;

        employee.SupervisorId = supervisorId;
        await users.UpdateAsync(employee);
    }

    private static async Task SeedLeaveTypesAsync(ILeaveTypeRepository leaveTypes)
    {
        if ((await leaveTypes.GetAllAsync()).Count > 0) return;

        var now = DateTime.UtcNow;
        LeaveType make(string code, string name, bool paid, double days) => new()
        {
            OrganizationId = DemoOrgId,   // set explicitly — no request context during seeding
            Code = code,
            Name = name,
            Paid = paid,
            DefaultDays = days,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await leaveTypes.AddAsync(make("AL", "Annual Leave", true, 14));
        await leaveTypes.AddAsync(make("MC", "Medical Leave", true, 14));
        await leaveTypes.AddAsync(make("UL", "Unpaid Leave", false, 0));
    }

    private static async Task SeedOrganizationAsync(IOrganizationRepository organizations)
    {
        if (await organizations.GetByIdAsync(DemoOrgId) is not null) return;

        await organizations.AddAsync(new Organization
        {
            Id = DemoOrgId,
            Name = "AltomateHR Demo Co",
            DefaultCurrency = "MYR",
            DefaultMileageRate = 0.60m,
            GeofenceRadiusMeters = 200,
            CreatedAt = DateTime.UtcNow,
        });
    }

    // Create the user if missing, or backfill its org if it predates tenancy.
    private static async Task EnsureUserAsync(IUserRepository users, string id, string email, string role)
    {
        var existing = await users.GetByEmailAsync(email);
        if (existing is null)
        {
            await users.AddAsync(new User
            {
                Id = id,
                Email = email,
                OrganizationId = DemoOrgId,
                PasswordHash = BC.HashPassword("password123"),   // hashed at seed time, never stored plain
                Role = role,
                CreatedAt = DateTime.UtcNow,
            });
            return;
        }

        if (string.IsNullOrEmpty(existing.OrganizationId))
        {
            existing.OrganizationId = DemoOrgId;
            await users.UpdateAsync(existing);
        }
    }

    // Point old demo claims at the real user ids + the demo org.
    private static async Task BackfillClaimsAsync(IClaimsRepository claims)
    {
        var allClaims = await claims.GetAllAsync();
        foreach (var claim in allClaims)
        {
            var changed = false;

            var updatedEmployeeId = claim.EmployeeId switch
            {
                "employee@altomate.com" => "usr-emp",
                "admin@altomate.com" => "usr-admin",
                _ => claim.EmployeeId,
            };
            if (updatedEmployeeId != claim.EmployeeId)
            {
                claim.EmployeeId = updatedEmployeeId;
                changed = true;
            }

            if (string.IsNullOrEmpty(claim.OrganizationId))
            {
                claim.OrganizationId = DemoOrgId;
                changed = true;
            }

            if (changed) await claims.UpdateAsync(claim);
        }
    }
}
