using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Realtime.Dtos;
using AltomateHR.Api.Tests.Common;

namespace AltomateHR.Api.Tests.Support;

// Fakes for the two collaborators that claims / attendance / leave ALL gained at
// once (live updates + the employee directory). Shared here rather than copied
// into each module's own doubles file, so adding a third gained dependency is
// one edit instead of three.

// Records what was published, so a test can assert on the fan-out instead of
// just tolerating it.
internal sealed class FakeRealtimeService : IRealtimeService
{
    public List<(string OrganizationId, List<string> UserIds, RealtimeEventDto Event)> Published { get; } = [];

    public int ConnectionCount => 0;

    public RealtimeConnection? Connect() => null;

    public Task PublishAsync(string organizationId, IEnumerable<string?> userIds, RealtimeEventDto evt)
    {
        Published.Add((
            organizationId,
            userIds.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToList(),
            evt));
        return Task.CompletedTask;
    }
}

internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly List<EmployeeIdentity> _members;

    public FakeEmployeeDirectory(params EmployeeIdentity[] members) => _members = members.ToList();

    public Task<EmployeeDirectorySnapshot> GetSnapshotAsync() =>
        Task.FromResult(EmployeeDirectoryTestFactory.Snapshot(_members));
}

// EmployeeDirectorySnapshot's constructor is internal to the API assembly, so
// tests build one the only way they can: through the real EmployeeDirectory,
// over main's TestDirectory helper.
internal static class EmployeeDirectoryTestFactory
{
    public static EmployeeDirectorySnapshot Snapshot(IEnumerable<EmployeeIdentity> members)
    {
        var list = members.ToList();
        var directory = new EmployeeDirectory(
            TestDirectory.Over(new StubMembershipRepository(list), new StubUserRepository(list)));
        return directory.GetSnapshotAsync().GetAwaiter().GetResult();
    }
}

internal sealed class FakeHoursSummaryService : IHoursSummaryService
{
    public HoursSummaryDto OrgSummary { get; set; } = new();

    public Task<HoursBucketsDto> GetMyHoursSummaryAsync(string employeeId, DateTime from, DateTime to) =>
        Task.FromResult(new HoursBucketsDto());

    public Task<HoursSummaryDto> GetOrgHoursSummaryAsync(DateTime from, DateTime to, string? teamId) =>
        Task.FromResult(OrgSummary);

    public Task<HoursBucketsDto?> GetEmployeeHoursSummaryAsync(
        string employeeId, DateTime from, DateTime to, string requestingUserId, string? requestingRole) =>
        Task.FromResult<HoursBucketsDto?>(new HoursBucketsDto());
}

internal sealed class FakeProjectServiceForExport : IProjectService
{
    private readonly List<ProjectDto> _projects;

    public FakeProjectServiceForExport(params ProjectDto[] projects) => _projects = projects.ToList();

    public Task<IEnumerable<ProjectDto>> GetAllAsync() => Task.FromResult(_projects.AsEnumerable());

    public Task<ProjectDto?> GetByIdAsync(string id) =>
        Task.FromResult(_projects.FirstOrDefault(p => p.Id == id));

    public Task<ProjectDto> CreateAsync(SaveProjectDto dto) => throw new NotImplementedException();
    public Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto) => throw new NotImplementedException();
    public Task<ProjectDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
}

internal sealed class StubMembershipRepository : IOrganizationMembershipRepository
{
    private readonly List<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership> _rows;

    public StubMembershipRepository(IEnumerable<EmployeeIdentity> members) =>
        _rows = members.Select(m => new AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership
        {
            OrganizationId = "org-1",
            UserId = m.Id,
            Role = m.Role,
        }).ToList();

    public Task<List<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership>> GetForCurrentOrgAsync() =>
        Task.FromResult(_rows.ToList());

    public Task<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
        Task.FromResult(_rows.FirstOrDefault(r => r.UserId == userId));

    public Task<List<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership>> GetByUserAsync(string userId) =>
        Task.FromResult(_rows.Where(r => r.UserId == userId).ToList());

    public Task<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership?> GetAsync(string organizationId, string userId) =>
        Task.FromResult(_rows.FirstOrDefault(r => r.UserId == userId));

    public Task<List<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership>> GetBySupervisorAsync(string supervisorId) =>
        Task.FromResult(new List<AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership>());

    public Task AddAsync(AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership membership) => Task.CompletedTask;
    public Task UpdateAsync(AltomateHR.Api.Modules.Employees.Entities.OrganizationMembership membership) => Task.CompletedTask;
    public Task<int> CountByShiftIdAsync(string shiftId) => Task.FromResult(0);
}

internal sealed class StubUserRepository : AltomateHR.Api.Modules.Auth.IUserRepository
{
    private readonly List<AltomateHR.Api.Modules.Auth.Entities.User> _users;

    public StubUserRepository(IEnumerable<EmployeeIdentity> members) =>
        _users = members.Select(m => new AltomateHR.Api.Modules.Auth.Entities.User
        {
            Id = m.Id,
            Email = m.Email,
            Name = m.Name,
        }).ToList();

    public Task<List<AltomateHR.Api.Modules.Auth.Entities.User>> GetAllAsync() => Task.FromResult(_users.ToList());
    public Task<AltomateHR.Api.Modules.Auth.Entities.User?> GetByEmailAsync(string email) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Email == email));
    public Task<AltomateHR.Api.Modules.Auth.Entities.User?> GetByIdAsync(string id) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
    public Task AddAsync(AltomateHR.Api.Modules.Auth.Entities.User user) => Task.CompletedTask;
    public Task UpdateAsync(AltomateHR.Api.Modules.Auth.Entities.User user) => Task.CompletedTask;
    public Task<bool> AnyAsync() => Task.FromResult(_users.Count > 0);
}
