using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Partners.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Shifts.Entities;
using AltomateHR.Api.Modules.Teams.Entities;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceBreak> AttendanceBreaks => Set<AttendanceBreak>();
    public DbSet<AttendanceApprovalRequest> AttendanceApprovalRequests => Set<AttendanceApprovalRequest>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<EmployeePolicy> EmployeePolicies => Set<EmployeePolicy>();
    public DbSet<PolicyLeaveEntitlement> PolicyLeaveEntitlements => Set<PolicyLeaveEntitlement>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<XeroConnection> XeroConnections => Set<XeroConnection>();
    public DbSet<XeroOAuthState> XeroOAuthStates => Set<XeroOAuthState>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiKeyAuditLog> ApiKeyAuditLogs => Set<ApiKeyAuditLog>();
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var claim = modelBuilder.Entity<Claim>();
        claim.HasIndex(c => c.ClaimNumber).IsUnique();
        claim.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.ClaimType).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.PaymentType).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.MileageUnitUsed).HasConversion<string>().HasMaxLength(20);

        modelBuilder.Entity<Organization>()
            .Property(o => o.MileageUnit)
            .HasConversion<string>()
            .HasMaxLength(20);
        modelBuilder.Entity<Organization>()
            .Property(o => o.Plan).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<Organization>()
            .Property(o => o.Tier).HasConversion<string>().HasMaxLength(20);

        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.Token).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<OrganizationMembership>().HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        modelBuilder.Entity<OrganizationMembership>().HasIndex(m => m.UserId);
        modelBuilder.Entity<ChartOfAccount>().HasIndex(a => new { a.OrganizationId, a.XeroAccountId });
        modelBuilder.Entity<Project>().HasIndex(p => new { p.OrganizationId, p.XeroProjectId });

        var attendance = modelBuilder.Entity<AttendanceRecord>();
        attendance.HasIndex(r => new { r.EmployeeId, r.Date }).IsUnique();  // one row per employee per day
        attendance.HasIndex(r => new { r.Status, r.Date });
        attendance.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        var attendanceSession = modelBuilder.Entity<AttendanceSession>();
        attendanceSession.HasIndex(s => s.AttendanceRecordId);
        attendanceSession.HasIndex(s => new { s.AttendanceRecordId, s.EndedAt });

        var attendanceBreak = modelBuilder.Entity<AttendanceBreak>();
        attendanceBreak.HasIndex(b => b.AttendanceSessionId);
        attendanceBreak.HasIndex(b => new { b.AttendanceSessionId, b.EndedAt });
        attendanceBreak.HasIndex(b => b.AttendanceRecordId);

        var approvalRequest = modelBuilder.Entity<AttendanceApprovalRequest>();
        approvalRequest.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        approvalRequest.Property(a => a.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
        approvalRequest.HasIndex(a => a.AttendanceRecordId);
        approvalRequest.HasIndex(a => a.AttendanceBreakId);
        approvalRequest.HasIndex(a => new { a.ApprovalStatus, a.Kind });
        approvalRequest.HasIndex(a => new { a.EmployeeId, a.SubmittedAt });

        modelBuilder.Entity<LeaveType>().HasIndex(t => new { t.OrganizationId, t.Code }).IsUnique();
        modelBuilder.Entity<LeaveApplication>()
            .Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<LeaveApplication>().HasIndex(a => a.EmployeeId);

        var overtime = modelBuilder.Entity<OvertimeRequest>();
        overtime.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        overtime.HasIndex(r => r.EmployeeId);
        overtime.HasIndex(r => new { r.Status, r.WorkDate });

        var policy = modelBuilder.Entity<EmployeePolicy>();
        policy.HasIndex(p => new { p.OrganizationId, p.Name }).IsUnique();
        policy.Property(p => p.SalaryType).HasConversion<string>().HasMaxLength(20);
        policy.Property(p => p.OtMethod).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<PolicyLeaveEntitlement>()
            .HasIndex(e => new { e.PolicyId, e.LeaveTypeId }).IsUnique();

        modelBuilder.Entity<Shift>().HasIndex(s => new { s.ProjectId, s.Name }).IsUnique();
        modelBuilder.Entity<Shift>().HasIndex(s => new { s.ProjectId, s.IsDefault });

        modelBuilder.Entity<Team>().HasIndex(t => new { t.ProjectId, t.Name }).IsUnique();
        modelBuilder.Entity<TeamMembership>().HasIndex(m => new { m.TeamId, m.EmployeeId }).IsUnique();
        modelBuilder.Entity<TeamMembership>().HasIndex(m => m.EmployeeId);

        modelBuilder.Entity<XeroConnection>().HasIndex(c => c.OrganizationId).IsUnique();
        modelBuilder.Entity<XeroConnection>().HasIndex(c => c.TenantId);
        modelBuilder.Entity<XeroOAuthState>().HasIndex(s => s.State).IsUnique();

        modelBuilder.Entity<ApiKey>().HasIndex(k => k.TokenHash).IsUnique();  // the per-request lookup
        modelBuilder.Entity<ApiKey>().HasIndex(k => k.OrganizationId);
        modelBuilder.Entity<ApiKeyAuditLog>().HasIndex(l => new { l.ApiKeyId, l.CreatedAt });

        // ApiClient (partner-app registry) is GLOBAL config — not tenant-scoped, so no
        // query filter. Name is the launch slug (unique); SecretHash is the per-request
        // client lookup.
        modelBuilder.Entity<ApiClient>().HasIndex(c => c.Name).IsUnique();
        modelBuilder.Entity<ApiClient>().HasIndex(c => c.SecretHash);

        // EmployeeProfile — one rich per-org record per user (the monolith's PayrollProfile).
        var profile = modelBuilder.Entity<EmployeeProfile>();
        profile.HasIndex(p => new { p.OrganizationId, p.UserId }).IsUnique();
        profile.Property(p => p.Gender).HasConversion<string>().HasMaxLength(20);
        profile.Property(p => p.IdType).HasConversion<string>().HasMaxLength(20);
        profile.Property(p => p.MaritalStatus).HasConversion<string>().HasMaxLength(20);
        profile.Property(p => p.SocsoScheme).HasConversion<string>().HasMaxLength(40);
        profile.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        profile.Property(p => p.SalaryType).HasConversion<string>().HasMaxLength(20);

        // ---- Multi-tenant global query filters ----
        // Every query on a tenant-scoped entity is auto-restricted to the current org.
        // When there's no current org (startup/seeding, or the unauthenticated login/refresh
        // calls), the filter is a no-op so those flows still work.
        modelBuilder.Entity<Claim>().HasQueryFilter(
            c => _currentUser.OrganizationId == null || c.OrganizationId == _currentUser.OrganizationId);
        // User is global (not tenant-scoped) — a login account reaches its orgs
        // through OrganizationMembership, which carries the org filter instead.
        modelBuilder.Entity<OrganizationMembership>().HasQueryFilter(
            m => _currentUser.OrganizationId == null || m.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<Project>().HasQueryFilter(
            p => _currentUser.OrganizationId == null || p.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<ChartOfAccount>().HasQueryFilter(
            a => _currentUser.OrganizationId == null || a.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<AttendanceRecord>().HasQueryFilter(
            r => _currentUser.OrganizationId == null || r.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<AttendanceSession>().HasQueryFilter(
            s => _currentUser.OrganizationId == null || s.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<AttendanceBreak>().HasQueryFilter(
            b => _currentUser.OrganizationId == null || b.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<AttendanceApprovalRequest>().HasQueryFilter(
            a => _currentUser.OrganizationId == null || a.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<LeaveType>().HasQueryFilter(
            t => _currentUser.OrganizationId == null || t.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<LeaveApplication>().HasQueryFilter(
            a => _currentUser.OrganizationId == null || a.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<OvertimeRequest>().HasQueryFilter(
            r => _currentUser.OrganizationId == null || r.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<EmployeePolicy>().HasQueryFilter(
            p => _currentUser.OrganizationId == null || p.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<PolicyLeaveEntitlement>().HasQueryFilter(
            e => _currentUser.OrganizationId == null || e.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<Shift>().HasQueryFilter(
            s => _currentUser.OrganizationId == null || s.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<Team>().HasQueryFilter(
            t => _currentUser.OrganizationId == null || t.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<TeamMembership>().HasQueryFilter(
            m => _currentUser.OrganizationId == null || m.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<XeroConnection>().HasQueryFilter(
            c => _currentUser.OrganizationId == null || c.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<XeroOAuthState>().HasQueryFilter(
            s => _currentUser.OrganizationId == null || s.OrganizationId == _currentUser.OrganizationId);
        // ApiKey is tenant-scoped for MANAGEMENT (an Owner only sees their org's keys). The
        // auth lookup by hash deliberately IgnoreQueryFilters() — it runs before any org is
        // known. ApiKeyAuditLog is NOT tenant-scoped (internal operational log).
        modelBuilder.Entity<ApiKey>().HasQueryFilter(
            k => _currentUser.OrganizationId == null || k.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<EmployeeProfile>().HasQueryFilter(
            p => _currentUser.OrganizationId == null || p.OrganizationId == _currentUser.OrganizationId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampTenant();
        return base.SaveChanges();
    }

    // Auto-STAMP OrganizationId on newly-added tenant-scoped entities, so services never
    // have to remember to set it. (Seeding sets it explicitly, before any request context.)
    private void StampTenant()
    {
        var org = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(org)) return;

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.OrganizationId))
                entry.Entity.OrganizationId = org;
        }
    }
}
