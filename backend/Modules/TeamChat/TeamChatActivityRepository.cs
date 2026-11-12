using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.TeamChat;

public class TeamChatActivityRepository : ITeamChatActivityRepository
{
    private readonly AppDbContext _db;

    // How LeaveService marks a row the leave import wrote (AppendTrail).
    private const string ImportedTrail = "\"IMPORTED\"";

    public TeamChatActivityRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CompanyActivity>> GetAsync(DateTime fromUtc, DateTime toUtc, DateTime localDay)
    {
        var companies = await _db.Organizations.AsNoTracking()
            .Where(o => o.DeactivatedAt == null)
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();

        // The day's rows are few enough to count here, which keeps the SQL
        // plain (no COUNT(DISTINCT) inside a GROUP BY for the provider to
        // translate).
        // Who signed in is the row's target: a sign-in has no session yet, so
        // its actor id is empty. (SSO sign-ins aren't audited as auth.login.)
        var signIns = await _db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Action == AuditActions.AuthLogin && a.CreatedAt >= fromUtc && a.CreatedAt < toUtc)
            .Select(a => new { a.OrganizationId, UserId = a.TargetId })
            .ToListAsync();

        // Imported months (YTD history) are filed the moment they're imported;
        // they're not anyone approving pay.
        var runsApproved = await _db.PayrollRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.SubmittedAt >= fromUtc && r.SubmittedAt < toUtc && r.Status == PayrollRunStatus.SUBMITTED
                        && r.Source == PayrollRunSource.COMPUTED)
            .Select(r => r.OrganizationId)
            .ToListAsync();
        var runsSent = await _db.PayrollRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.SubmittedForApprovalAt >= fromUtc && r.SubmittedForApprovalAt < toUtc)
            .Select(r => r.OrganizationId)
            .ToListAsync();

        // Imported leave history is left out: its trail says IMPORTED, and an
        // imported decided row was decided the moment it was created.
        var leaveApplied = await _db.LeaveApplications.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.CreatedAt >= fromUtc && l.CreatedAt < toUtc
                        && (l.Approvals == null || !l.Approvals.Contains(ImportedTrail)))
            .Select(l => l.OrganizationId)
            .ToListAsync();
        var leaveApproved = await _db.LeaveApplications.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Status == LeaveStatus.APPROVED && l.DecidedAt >= fromUtc && l.DecidedAt < toUtc
                        && l.DecidedAt != l.CreatedAt)
            .Select(l => l.OrganizationId)
            .ToListAsync();
        var leaveWaiting = await CountByOrgAsync(
            _db.LeaveApplications.IgnoreQueryFilters().Where(l => l.Status == LeaveStatus.PENDING)
                .Select(l => l.OrganizationId));

        var claimsSubmitted = await _db.Claims.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.SubmittedAt >= fromUtc && c.SubmittedAt < toUtc)
            .Select(c => c.OrganizationId)
            .ToListAsync();
        var claimsWaiting = await CountByOrgAsync(
            _db.Claims.IgnoreQueryFilters().Where(c => c.Status == ClaimStatus.PENDING)
                .Select(c => c.OrganizationId));

        var clockedIn = await _db.AttendanceRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Date == localDay && r.TimeIn != null)
            .Select(r => new { r.OrganizationId, r.EmployeeId })
            .ToListAsync();
        var attendanceWaiting = await CountByOrgAsync(
            _db.AttendanceApprovalRequests.IgnoreQueryFilters()
                .Where(a => a.ApprovalStatus == AttendanceApprovalStatus.PENDING)
                .Select(a => a.OrganizationId));

        static Dictionary<string, int> Tally(IEnumerable<string> orgIds) =>
            orgIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        var signInCount = Tally(signIns.Select(s => s.OrganizationId));
        var signInPeople = signIns.GroupBy(s => s.OrganizationId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.UserId).Distinct().Count());
        var approved = Tally(runsApproved);
        var sent = Tally(runsSent);
        var applied = Tally(leaveApplied);
        var leaveOk = Tally(leaveApproved);
        var claims = Tally(claimsSubmitted);
        var people = clockedIn.GroupBy(r => r.OrganizationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());

        static int Of(Dictionary<string, int> d, string id) => d.GetValueOrDefault(id);

        return companies.Select(c => new CompanyActivity(
            c.Id,
            c.Name,
            Of(signInCount, c.Id),
            Of(signInPeople, c.Id),
            Of(approved, c.Id),
            Of(sent, c.Id),
            Of(applied, c.Id),
            Of(leaveOk, c.Id),
            Of(leaveWaiting, c.Id),
            Of(claims, c.Id),
            Of(claimsWaiting, c.Id),
            Of(people, c.Id),
            Of(attendanceWaiting, c.Id))).ToList();
    }

    public async Task<bool> TryClaimDayAsync(DateOnly day, DateTime utcNow)
    {
        if (await _db.TeamChatDailyPosts.AnyAsync(p => p.Day == day)) return false;
        var row = new Entities.TeamChatDailyPost { Day = day, PostedAt = utcNow };
        _db.TeamChatDailyPosts.Add(row);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Another server claimed it between the check and the insert.
            _db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    private static async Task<Dictionary<string, int>> CountByOrgAsync(IQueryable<string> orgIds) =>
        await orgIds.GroupBy(id => id)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
}
