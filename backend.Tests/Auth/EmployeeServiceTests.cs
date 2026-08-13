using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;

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
    public async Task UpdateAsync_ReturnsNotFoundResultForMissingUser()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync("ghost", new UpdateEmployeeDto { Role = "Employee" });

        Assert.False(result.Ok);
        Assert.Null(result.Error);   // no error message → controller maps to 404
    }

    [Fact]
    public async Task UpdateAsync_ClearsSupervisorWhenNull()
    {
        var service = MakeService(out var users);
        users.Single(u => u.Id == "usr-emp").SupervisorId = "usr-super";

        var result = await service.UpdateAsync("usr-emp", new UpdateEmployeeDto { Role = "Employee", SupervisorId = null });

        Assert.True(result.Ok);
        Assert.Null(result.Employee!.SupervisorId);
    }

    private static EmployeeService MakeService(out List<User> users)
    {
        users =
        [
            User("usr-admin", "admin@altomate.com", "Admin"),
            User("usr-super", "supervisor@altomate.com", "Supervisor"),
            User("usr-emp", "employee@altomate.com", "Employee"),
        ];
        return new EmployeeService(new FakeUserRepository(users));
    }

    private static User User(string id, string email, string role) => new()
    {
        Id = id,
        Email = email,
        Role = role,
        OrganizationId = "org-1",
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users;
        public FakeUserRepository(List<User> users) => _users = users;
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_users.FirstOrDefault(u => u.Email == email));
        public Task<User?> GetByIdAsync(string id) => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
        public Task<List<User>> GetAllAsync() => Task.FromResult(_users.ToList());
        public Task<List<User>> GetBySupervisorAsync(string supervisorId) =>
            Task.FromResult(_users.Where(u => u.SupervisorId == supervisorId).ToList());
        public Task AddAsync(User user) { _users.Add(user); return Task.CompletedTask; }
        public Task UpdateAsync(User user) => Task.CompletedTask;   // service mutates in place
        public Task<bool> AnyAsync() => Task.FromResult(_users.Count > 0);
    }
}
