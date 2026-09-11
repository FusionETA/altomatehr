using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Tests.Common;

// Wraps an existing membership/user fake in the REAL DirectoryService, so tests
// that used to hand a service its repository can keep their fakes unchanged and
// still exercise the production seam rather than a test-only stand-in.
public static class TestDirectory
{
    public static IDirectoryService Over(
        IOrganizationMembershipRepository memberships,
        IUserRepository? users = null,
        IEmployeeProfileRepository? profiles = null) =>
        new DirectoryService(
            memberships,
            profiles ?? new EmptyProfileRepository(),
            users ?? new EmptyUserRepository());

    // For the many services that never touch users — a directory still needs one.
    private sealed class EmptyUserRepository : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(null);
        public Task<User?> GetByIdAsync(string id) => Task.FromResult<User?>(null);
        public Task<List<User>> GetAllAsync() => Task.FromResult(new List<User>());
        public Task AddAsync(User user) => Task.CompletedTask;
        public Task UpdateAsync(User user) => Task.CompletedTask;
        public Task<bool> AnyAsync() => Task.FromResult(false);
    }

    // Likewise for the modules that never look at an employment record.
    private sealed class EmptyProfileRepository : IEmployeeProfileRepository
    {
        public Task<EmployeeProfile?> GetByUserAsync(string userId) =>
            Task.FromResult<EmployeeProfile?>(null);

        public Task<List<EmployeeProfile>> GetAllForCurrentOrgAsync() =>
            Task.FromResult(new List<EmployeeProfile>());

        public Task<EmployeeProfile> AddAsync(EmployeeProfile profile) =>
            Task.FromResult(profile);

        public Task UpdateAsync(EmployeeProfile profile) => Task.CompletedTask;
    
    // The archive sweep does not run through this stand-in.
    public Task<List<EmployeeProfile>> GetUnarchivedPastLeaversAsync(DateTime before, int max) =>
        Task.FromResult(new List<EmployeeProfile>());
}
}
