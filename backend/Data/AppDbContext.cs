using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
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
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<EmployeePolicy> EmployeePolicies => Set<EmployeePolicy>();
    public DbSet<PolicyLeaveEntitlement> PolicyLeaveEntitlements => Set<PolicyLeaveEntitlement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var claim = modelBuilder.Entity<Claim>();
        claim.HasIndex(c => c.ClaimNumber).IsUnique();
        claim.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.ClaimType).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.PaymentType).HasConversion<string>().HasMaxLength(20);
        claim.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);

        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.Token).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        var attendance = modelBuilder.Entity<AttendanceRecord>();
        attendance.HasIndex(r => new { r.EmployeeId, r.Date }).IsUnique();  // one row per employee per day
        attendance.HasIndex(r => new { r.Status, r.Date });
        attendance.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        modelBuilder.Entity<LeaveType>().HasIndex(t => new { t.OrganizationId, t.Code }).IsUnique();
        modelBuilder.Entity<LeaveApplication>()
            .Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<LeaveApplication>().HasIndex(a => a.EmployeeId);

        var policy = modelBuilder.Entity<EmployeePolicy>();
        policy.HasIndex(p => new { p.OrganizationId, p.Name }).IsUnique();
        policy.Property(p => p.SalaryType).HasConversion<string>().HasMaxLength(20);
        policy.Property(p => p.OtMethod).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<PolicyLeaveEntitlement>()
            .HasIndex(e => new { e.PolicyId, e.LeaveTypeId }).IsUnique();

        // ---- Multi-tenant global query filters ----
        // Every query on a tenant-scoped entity is auto-restricted to the current org.
        // When there's no current org (startup/seeding, or the unauthenticated login/refresh
        // calls), the filter is a no-op so those flows still work.
        modelBuilder.Entity<Claim>().HasQueryFilter(
            c => _currentUser.OrganizationId == null || c.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<User>().HasQueryFilter(
            u => _currentUser.OrganizationId == null || u.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<Project>().HasQueryFilter(
            p => _currentUser.OrganizationId == null || p.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<ChartOfAccount>().HasQueryFilter(
            a => _currentUser.OrganizationId == null || a.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<AttendanceRecord>().HasQueryFilter(
            r => _currentUser.OrganizationId == null || r.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<LeaveType>().HasQueryFilter(
            t => _currentUser.OrganizationId == null || t.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<LeaveApplication>().HasQueryFilter(
            a => _currentUser.OrganizationId == null || a.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<EmployeePolicy>().HasQueryFilter(
            p => _currentUser.OrganizationId == null || p.OrganizationId == _currentUser.OrganizationId);
        modelBuilder.Entity<PolicyLeaveEntitlement>().HasQueryFilter(
            e => _currentUser.OrganizationId == null || e.OrganizationId == _currentUser.OrganizationId);
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
