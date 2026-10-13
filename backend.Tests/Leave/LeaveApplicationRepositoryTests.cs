using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AltomateHR.Api.Tests.Leave;

// The employee's edit is saved by a guarded write: only the edited columns,
// and only while the stored row is still PENDING at step 0. An approval that
// lands between the edit's read and its save must win, not be overwritten
// back to PENDING by a whole-row update.
public class LeaveApplicationRepositoryTests
{
    private readonly string _dbName = $"leave-repo-{Guid.NewGuid()}";

    [Fact]
    public async Task An_untouched_request_takes_the_edit()
    {
        await SeedAsync();
        await using var db = Db();
        var repo = new LeaveApplicationRepository(db);

        var app = (await repo.GetByIdAsync("a1"))!;
        Edit(app);
        Assert.True(await repo.SaveEditIfUntouchedAsync(app));

        await using var check = Db();
        var row = await check.LeaveApplications.SingleAsync(a => a.Id == "a1");
        Assert.Equal(new DateTime(2026, 9, 2), row.StartDate);
        Assert.Equal(2, row.TotalDays);
        Assert.Equal("edited", row.Reason);
        Assert.Equal("xf-new", row.XeroFileId);
        Assert.Equal(LeaveStatus.PENDING, row.Status);
    }

    [Fact]
    public async Task An_approval_between_read_and_save_wins()
    {
        await SeedAsync();
        await using var db = Db();
        var repo = new LeaveApplicationRepository(db);
        var app = (await repo.GetByIdAsync("a1"))!;   // the edit's read

        // The approver's request lands now, in its own context.
        await using (var approver = Db())
        {
            var row = await approver.LeaveApplications.SingleAsync(a => a.Id == "a1");
            row.Status = LeaveStatus.APPROVED;
            row.CurrentStep = 1;
            await approver.SaveChangesAsync();
        }

        Edit(app);
        Assert.False(await repo.SaveEditIfUntouchedAsync(app));
        // A later save in the same request (a notification, say) must not
        // flush the refused edit either.
        await db.SaveChangesAsync();

        await using var check = Db();
        var stored = await check.LeaveApplications.SingleAsync(a => a.Id == "a1");
        Assert.Equal(LeaveStatus.APPROVED, stored.Status);
        Assert.Equal(1, stored.CurrentStep);
        Assert.Equal(new DateTime(2026, 9, 1), stored.StartDate);
        Assert.Equal(1, stored.TotalDays);
        Assert.Null(stored.Reason);
    }

    // On MySQL it is ONE UPDATE whose WHERE carries the guard and the tenant
    // filter, and whose SET touches only the edited columns. Pomelo
    // translates it here against no server at all: the connection open is
    // suppressed and each command captured instead of run.
    [Fact]
    public async Task On_mysql_the_edit_is_a_single_guarded_update_of_only_the_edited_columns()
    {
        var capture = new SqlCapture();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql("Server=127.0.0.1;Port=1;Database=none;User=none;Password=none",
                    new MySqlServerVersion(new Version(8, 0, 0)))
                .AddInterceptors(capture)
                .Options,
            new StubUser());
        var repo = new LeaveApplicationRepository(db);

        var app = Seeded();
        Edit(app);
        Assert.True(await repo.SaveEditIfUntouchedAsync(app));

        var sql = Assert.Single(capture.Commands, c => c.TrimStart().StartsWith("UPDATE", StringComparison.Ordinal));
        var parts = sql.Split("WHERE");
        var set = parts[0];
        var where = parts[1];

        Assert.Contains("`Status` = 'PENDING'", where);
        Assert.Contains("`CurrentStep` = 0", where);
        Assert.Contains("`OrganizationId` = ", where);   // the tenant filter applies

        foreach (var column in new[] { "LeaveTypeId", "StartDate", "EndDate", "Duration", "TotalDays",
                     "Reason", "XeroFileId", "AttachmentUrl", "AttachmentName", "UpdatedAt" })
            Assert.Contains($"`{column}` = ", set);
        foreach (var column in new[] { "Status", "CurrentStep", "Approvals", "EmployeeId", "DecidedAt" })
            Assert.DoesNotContain($"`{column}` = ", set);
    }

    private static void Edit(LeaveApplication app)
    {
        app.StartDate = new DateTime(2026, 9, 2);
        app.EndDate = new DateTime(2026, 9, 3);
        app.TotalDays = 2;
        app.Reason = "edited";
        app.XeroFileId = "xf-new";
        app.AttachmentUrl = "/leave/files/xf-new/content";
        app.AttachmentName = "new.pdf";
        app.UpdatedAt = DateTime.UtcNow;
    }

    private static LeaveApplication Seeded() => new()
    {
        Id = "a1",
        OrganizationId = "org-1",
        EmployeeId = "usr-emp",
        LeaveTypeId = "t-al",
        StartDate = new DateTime(2026, 9, 1),
        EndDate = new DateTime(2026, 9, 1),
        TotalDays = 1,
        Status = LeaveStatus.PENDING,
        CurrentStep = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private async Task SeedAsync()
    {
        await using var db = Db();
        db.LeaveApplications.Add(Seeded());
        await db.SaveChangesAsync();
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, new StubUser());

    private sealed class StubUser : ICurrentUser
    {
        public string? UserId => "usr-emp";
        public string? OrganizationId => "org-1";
        public string? Role => "Employee";
        public string? Email => "emp@acme.test";
        public string? IpAddress => null;
        public bool IsAdmin => false;
        public bool IsAuthenticated => true;
    }

    private sealed class SqlCapture : DbConnectionInterceptor, IDbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override InterceptionResult ConnectionOpening(
            System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }
}
