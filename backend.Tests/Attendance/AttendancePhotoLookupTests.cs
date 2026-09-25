using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Attendance;

// The day record mirrors only the FIRST shift's clock-in photo and the LAST
// shift's clock-out photo. A second shift's clock-in photo lived only on its
// session, so the lookup behind GET /attendance/photos found nothing and the
// photo 404'd for everyone, admin included (seen on a real two-shift day).
public class AttendancePhotoLookupTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AttendanceRepository _repo;

    public AttendancePhotoLookupTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"attendance-photos-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, new StubCurrentUser());
        _repo = new AttendanceRepository(_db);

        _db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = "rec-1",
            EmployeeId = "emp-1",
            Date = new DateTime(2026, 9, 25),
            ClockInPhotoUrl = null,                           // shift 1 had none
            ClockOutPhotoUrl = "/attendance/photos/out-2.jpg", // shift 2's
            Status = AttendanceStatus.CLOCKED_OUT,
        });
        _db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = "s-1", AttendanceRecordId = "rec-1", EmployeeId = "emp-1" },
            new AttendanceSession
            {
                Id = "s-2",
                AttendanceRecordId = "rec-1",
                EmployeeId = "emp-1",
                ClockInPhotoUrl = "/attendance/photos/in-2.jpg",
                ClockOutPhotoUrl = "/attendance/photos/out-2.jpg",
            });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_later_shifts_clock_in_photo_resolves_to_its_day()
    {
        var record = await _repo.GetByPhotoUrlAsync("/attendance/photos/in-2.jpg");

        Assert.Equal("rec-1", record?.Id);
    }

    [Fact]
    public async Task A_photo_on_the_record_still_resolves()
    {
        var record = await _repo.GetByPhotoUrlAsync("/attendance/photos/out-2.jpg");

        Assert.Equal("rec-1", record?.Id);
    }

    [Fact]
    public async Task An_unknown_photo_resolves_to_nothing()
    {
        Assert.Null(await _repo.GetByPhotoUrlAsync("/attendance/photos/nope.jpg"));
    }
}
