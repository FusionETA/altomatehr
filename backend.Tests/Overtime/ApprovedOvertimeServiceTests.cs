using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Overtime;

// What payroll pays as overtime by default. The two things that must hold:
// only APPROVED hours in the month count, and each lands in the bucket for the
// kind of day it was worked on — classified by the same code the OT rate uses.
public class ApprovedOvertimeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly StubDayTypes _dayTypes = new();
    private readonly ApprovedOvertimeService _service;

    private static readonly DateTime From = new(2026, 9, 1);
    private static readonly DateTime To = new(2026, 9, 30);

    public ApprovedOvertimeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"approved-ot-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _service = new ApprovedOvertimeService(new OvertimeRepository(_db), _dayTypes);
    }

    public void Dispose() => _db.Dispose();

    private void AddRequest(
        string employeeId, DateTime workDate, int minutes,
        OvertimeStatus status = OvertimeStatus.APPROVED, string? projectId = null)
    {
        _db.OvertimeRequests.Add(new OvertimeRequest
        {
            OrganizationId = "org-1",
            EmployeeId = employeeId,
            ProjectId = projectId,
            WorkDate = workDate,
            StartAt = workDate.AddHours(18),
            EndAt = workDate.AddHours(18).AddMinutes(minutes),
            RequestedMinutes = minutes,
            Reason = "Site deadline",
            BeforePhotoUrl = "x",
            Status = status,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task SplitsApprovedMinutesByTheDayTheyWereWorkedOn()
    {
        _dayTypes.Set(new DateTime(2026, 9, 5), OtDayType.REST_DAY);
        _dayTypes.Set(new DateTime(2026, 9, 16), OtDayType.PUBLIC_HOLIDAY);
        AddRequest("usr-1", new DateTime(2026, 9, 2), 90);
        AddRequest("usr-1", new DateTime(2026, 9, 3), 30);
        AddRequest("usr-1", new DateTime(2026, 9, 5), 120);
        AddRequest("usr-1", new DateTime(2026, 9, 16), 60);

        var ot = (await _service.GetApprovedMinutesAsync(From, To))["usr-1"];

        Assert.Equal(120, ot.NormalDayMin);
        Assert.Equal(120, ot.RestDayMin);
        Assert.Equal(60, ot.PublicHolidayMin);
    }

    // Pending, rejected and cancelled requests are not payable.
    [Theory]
    [InlineData(OvertimeStatus.PENDING)]
    [InlineData(OvertimeStatus.REJECTED)]
    [InlineData(OvertimeStatus.CANCELLED)]
    public async Task IgnoresRequestsThatAreNotApproved(OvertimeStatus status)
    {
        AddRequest("usr-1", new DateTime(2026, 9, 2), 90, status);

        Assert.Empty(await _service.GetApprovedMinutesAsync(From, To));
    }

    // The work date decides the month, both ends inclusive — OT on the 30th is
    // September's, OT on 1 October is not.
    [Fact]
    public async Task CountsOnlyWorkDatesInsideThePeriod()
    {
        AddRequest("usr-1", new DateTime(2026, 8, 31), 60);
        AddRequest("usr-1", new DateTime(2026, 9, 1), 30);
        AddRequest("usr-1", new DateTime(2026, 9, 30), 45);
        AddRequest("usr-1", new DateTime(2026, 10, 1), 60);

        var ot = (await _service.GetApprovedMinutesAsync(From, To))["usr-1"];

        Assert.Equal(75, ot.TotalMin);
    }

    // OT on two projects is classified per project (their holiday calendars
    // can differ) and summed for the employee.
    [Fact]
    public async Task SumsOneEmployeesOvertimeAcrossProjects()
    {
        AddRequest("usr-1", new DateTime(2026, 9, 2), 60, projectId: "prj-a");
        AddRequest("usr-1", new DateTime(2026, 9, 3), 30, projectId: "prj-b");

        var ot = (await _service.GetApprovedMinutesAsync(From, To))["usr-1"];

        Assert.Equal(90, ot.NormalDayMin);
        Assert.Contains("prj-a", _dayTypes.ProjectsAsked);
        Assert.Contains("prj-b", _dayTypes.ProjectsAsked);
    }

    [Fact]
    public async Task KeepsEachEmployeesOvertimeApart()
    {
        AddRequest("usr-1", new DateTime(2026, 9, 2), 60);
        AddRequest("usr-2", new DateTime(2026, 9, 2), 30);

        var result = await _service.GetApprovedMinutesAsync(From, To);

        Assert.Equal(60, result["usr-1"].TotalMin);
        Assert.Equal(30, result["usr-2"].TotalMin);
    }

    [Fact]
    public async Task CanBeAskedAboutOneEmployee()
    {
        AddRequest("usr-1", new DateTime(2026, 9, 2), 60);
        AddRequest("usr-2", new DateTime(2026, 9, 2), 30);

        var result = await _service.GetApprovedMinutesAsync(From, To, "usr-2");

        Assert.Equal(["usr-2"], result.Keys);
    }

    // Every date is a normal day unless set; records which projects were asked.
    private sealed class StubDayTypes : IOtRateService
    {
        private readonly Dictionary<DateTime, OtDayType> _types = [];
        public List<string?> ProjectsAsked { get; } = [];

        public void Set(DateTime date, OtDayType type) => _types[date.Date] = type;

        public Task<IReadOnlyDictionary<DateTime, OtDayType>> ResolveDayTypesAsync(
            string employeeId, DateTime from, DateTime to, string? projectId)
        {
            ProjectsAsked.Add(projectId);
            return Task.FromResult<IReadOnlyDictionary<DateTime, OtDayType>>(
                new Dictionary<DateTime, OtDayType>(_types));
        }

        public Task<OtRateResolution> ResolveAsync(string employeeId, DateTime date, string? projectId) =>
            throw new NotSupportedException();
        public Task<OtDayType> ResolveDayTypeAsync(string employeeId, DateTime date, string? projectId) =>
            throw new NotSupportedException();
    }
}
