using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Entities;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.TeamChat;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.TeamChat;

// The daily summary's figures: yesterday's rows per company, read across
// every company (no tenant), deactivated companies left out.
public class TeamChatActivityTests
{
    // 11 Nov 2026 in Malaysia is 10 Nov 16:00 UTC to 11 Nov 16:00 UTC.
    private static readonly DateTime From = new(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddDays(1);
    private static readonly DateTime Day = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheDay_IsCountedPerCompany()
    {
        await using var db = Db();
        db.Organizations.AddRange(
            new Organization { Id = "org-a", Name = "Alpha" },
            new Organization { Id = "org-b", Name = "Beta" },
            new Organization { Id = "org-x", Name = "Gone", DeactivatedAt = From.AddDays(-30) });

        // Three sign-ins by two people; one the day before doesn't count.
        db.AuditLogs.AddRange(
            Login("org-a", "u1", From.AddHours(1)),
            Login("org-a", "u1", From.AddHours(5)),
            Login("org-a", "u2", From.AddHours(9)),
            Login("org-a", "u3", From.AddHours(-1)),
            Login("org-x", "u9", From.AddHours(2)));

        db.PayrollRuns.Add(new PayrollRun
        {
            OrganizationId = "org-b", PeriodYear = 2026, PeriodMonth = 10,
            Status = PayrollRunStatus.SUBMITTED, SubmittedAt = From.AddHours(3),
        });

        db.LeaveApplications.AddRange(
            new LeaveApplication { OrganizationId = "org-a", Status = LeaveStatus.PENDING, CreatedAt = From.AddHours(2) },
            new LeaveApplication
            {
                OrganizationId = "org-a", Status = LeaveStatus.APPROVED,
                CreatedAt = From.AddDays(-3), DecidedAt = From.AddHours(4),
            });

        db.Claims.Add(new Claim { OrganizationId = "org-b", Status = ClaimStatus.PENDING, SubmittedAt = From.AddHours(6) });

        db.AttendanceRecords.AddRange(
            new AttendanceRecord { OrganizationId = "org-a", EmployeeId = "u1", Date = Day, TimeIn = From.AddHours(1) },
            new AttendanceRecord { OrganizationId = "org-a", EmployeeId = "u2", Date = Day, TimeIn = From.AddHours(1) },
            new AttendanceRecord { OrganizationId = "org-a", EmployeeId = "u3", Date = Day },
            new AttendanceRecord { OrganizationId = "org-a", EmployeeId = "u1", Date = Day.AddDays(-1), TimeIn = From.AddHours(-20) });
        await db.SaveChangesAsync();

        var rows = await new TeamChatActivityRepository(db).GetAsync(From, To, Day);

        Assert.Equal(["Alpha", "Beta"], rows.Select(r => r.Company));
        var alpha = rows[0];
        Assert.Equal((3, 2), (alpha.SignIns, alpha.PeopleSignedIn));
        Assert.Equal((1, 1, 1), (alpha.LeaveApplied, alpha.LeaveApproved, alpha.LeaveWaiting));
        Assert.Equal(2, alpha.PeopleClockedIn);
        Assert.Equal(0, alpha.RunsApproved);

        var beta = rows[1];
        Assert.Equal(1, beta.RunsApproved);
        Assert.Equal((1, 1), (beta.ClaimsSubmitted, beta.ClaimsWaiting));
        Assert.Equal(0, beta.SignIns);
    }

    private static AuditLog Login(string org, string user, DateTime at) => new()
    {
        OrganizationId = org, ActorUserId = user, Action = AuditActions.AuthLogin, CreatedAt = at,
    };

    // A signed-in user of another company, to show the read ignores tenancy.
    private static AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"team-chat-{Guid.NewGuid()}")
                .Options,
            new StubCurrentUser { OrganizationId = "org-other" });
}
