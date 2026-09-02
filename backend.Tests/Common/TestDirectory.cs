using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Tests.Common;

// Wraps an existing membership/user fake in the REAL DirectoryService, so tests
// that used to hand a service its repository can keep their fakes unchanged and
// still exercise the production seam rather than a test-only stand-in.
public static class TestDirectory
{
    public static IDirectoryService Over(
        IOrganizationMembershipRepository memberships, IUserRepository? users = null) =>
        new DirectoryService(memberships, users ?? new EmptyUserRepository());

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
}
