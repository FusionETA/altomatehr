using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Tests.Auth;

public class EmployeeServiceTests
{
    [Fact]
    public async Task UpdateAsync_SetsRoleAndSupervisor_AndResolvesSupervisorEmail()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync(
            "usr-emp",
            new UpdateEmployeeDto { Role = "Employee", SupervisorId = "usr-super" });

        Assert.True(result.Ok);
        Assert.Equal("usr-super", result.Employee!.SupervisorId);
        Assert.Equal("supervisor@altomate.com", result.Employee.SupervisorEmail);
    }

    [Fact]
    public async Task UpdateAsync_RejectsSelfSupervisor()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync(
            "usr-emp",
            new UpdateEmployeeDto { Role = "Employee", SupervisorId = "usr-emp" });

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnknownRole()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync("usr-emp", new UpdateEmployeeDto { Role = "Wizard" });

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_RejectsSupervisorNotInOrg()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync(
            "usr-emp",
            new UpdateEmployeeDto { Role = "Employee", SupervisorId = "ghost" });

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNotFoundResultForNonMember()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync("ghost", new UpdateEmployeeDto { Role = "Employee" });

        Assert.False(result.Ok);
        Assert.Null(result.Error);   // no error message → controller maps to 404
    }

    [Fact]
    public async Task UpdateAsync_ClearsSupervisorWhenNull()
    {
        var service = MakeService(out var memberships);
        memberships.Single(m => m.UserId == "usr-emp").SupervisorId = "usr-super";

        var result = await service.UpdateAsync("usr-emp", new UpdateEmployeeDto { Role = "Employee", SupervisorId = null });

        Assert.True(result.Ok);
        Assert.Null(result.Employee!.SupervisorId);
    }

    // --- helpers ---

    private static EmployeeService MakeService(out List<OrganizationMembership> memberships)
    {
        var users = new List<User>
        {
            User("usr-admin", "admin@altomate.com"),
            User("usr-super", "supervisor@altomate.com"),
            User("usr-emp", "employee@altomate.com"),
        };
        memberships =
        [
            Membership("usr-admin", "Admin"),
            Membership("usr-super", "Supervisor"),
            Membership("usr-emp", "Employee"),
        ];
        return new EmployeeService(new FakeMembershipRepository(memberships), new FakeUserRepository(users));
    }

    private static User User(string id, string email) => new()
    {
        Id = id,
        Email = email,
        CreatedAt = DateTime.UtcNow,
    };

    private static OrganizationMembership Membership(string userId, string role) => new()
    {
        OrganizationId = "org-1",
        UserId = userId,
        Role = role,
    };

    private sealed class FakeMembershipRepository : IOrganizationMembershipRepository
    {
        private readonly List<OrganizationMembership> _m;
        public FakeMembershipRepository(List<OrganizationMembership> m) => _m = m;

        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) =>
            Task.FromResult(_m.Where(x => x.UserId == userId).ToList());
        public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) =>
            Task.FromResult(_m.FirstOrDefault(x => x.OrganizationId == organizationId && x.UserId == userId));
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() => Task.FromResult(_m.ToList());
        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
            Task.FromResult(_m.FirstOrDefault(x => x.UserId == userId));
        public Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId) =>
            Task.FromResult(_m.Where(x => x.SupervisorId == supervisorId).ToList());
        public Task AddAsync(OrganizationMembership m) { _m.Add(m); return Task.CompletedTask; }
        public Task UpdateAsync(OrganizationMembership m) => Task.CompletedTask;   // service mutates in place
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users;
        public FakeUserRepository(List<User> users) => _users = users;
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_users.FirstOrDefault(u => u.Email == email));
        public Task<User?> GetByIdAsync(string id) => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
        public Task<List<User>> GetAllAsync() => Task.FromResult(_users.ToList());
        public Task AddAsync(User user) { _users.Add(user); return Task.CompletedTask; }
        public Task UpdateAsync(User user) => Task.CompletedTask;
        public Task<bool> AnyAsync() => Task.FromResult(_users.Count > 0);
    }
}
