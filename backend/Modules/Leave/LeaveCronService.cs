using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Modules.Leave;

public class LeaveCronService : ILeaveCronService
{
    // Production picks the year in Malaysian time, not UTC, so a midnight-MYT
    // firing on 1 Jan lands in the NEW year (the UTC clock still reads 31 Dec at
    // that instant). Same reasoning here.
    private static readonly TimeZoneInfo Myt =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IDirectoryService _directory;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly ILeaveTypeRepository _types;
    private readonly ILeaveApplicationRepository _apps;
    private readonly IPolicyLeaveEntitlementRepository _policyEntitlements;

    public LeaveCronService(
        ILeaveEntitlementRepository entitlements,
        ILeaveTypeRepository types,
        ILeaveApplicationRepository apps,
        IPolicyLeaveEntitlementRepository policyEntitlements,
        IDirectoryService directory)
    {
        _entitlements = entitlements;
        _types = types;
        _apps = apps;
        _policyEntitlements = policyEntitlements;
        _directory = directory;
    }

    // ── monthly accrual + carry expiry sweep ────────────────────────────────
    public async Task<AccrualResult> RunMonthlyAccrualAsync(DateTime now)
    {
        var year = YearInMyt(now);
        var ctx = await LoadContextAsync(year);
        var rows = await _entitlements.GetByYearAsync(year);

        var accrued = 0;
        foreach (var row in rows)
        {
            if (!ctx.TypesById.TryGetValue(row.LeaveTypeId, out var type) || type.IsArchived)
                continue;
            if (ctx.Resolve(row, type) != LeaveAccrualMethod.PRO_RATED) continue;

            var next = LeaveAccrualMath.NextAccruedDays(row.EntitledDays, row.AccruedDays);
            if (Math.Abs(next - row.AccruedDays) < Epsilon) continue;   // already capped

            row.AccruedDays = next;
            row.UpdatedAt = now;
            accrued++;
        }

        // Sweep lapsed carry-forward. Only the UNUSED portion is forfeited —
        // the current-year bucket is assumed spent first — and the forfeited
        // amount is recorded so an audit (or a future cash-out) can answer
        // "how many days did she lose, and when" long afterwards.
        var expired = 0;
        foreach (var row in await _entitlements.GetCarryExpiringAsync(now))
        {
            if (!ctx.TypesById.TryGetValue(row.LeaveTypeId, out var type)) continue;

            var method = ctx.Resolve(row, type);
            var used = ctx.UsedDays(row.EmployeeId, row.LeaveTypeId);
            var unused = LeaveAccrualMath.UnusedCarriedAtExpiry(
                method, row.EntitledDays, row.AccruedDays, row.CarriedDays, used);

            row.CarriedExpiredDays = unused;
            row.CarriedDays -= unused;          // what's left is the portion already spent
            row.CarriedExpired = true;
            row.CarriedExpiredAt = now;
            row.UpdatedAt = now;
            expired++;
        }

        if (accrued > 0 || expired > 0) await _entitlements.SaveAsync();
        return new AccrualResult(true, accrued, expired, year);
    }

    // ── year rollover ───────────────────────────────────────────────────────
    public async Task<RolloverResult> RunYearRolloverAsync(int targetYear, DateTime now)
    {
        var prevYear = targetYear - 1;
        var ctx = await LoadContextAsync(prevYear);

        var members = await _directory.GetMembershipsForCurrentOrgAsync();
        var prevRows = (await _entitlements.GetByYearAsync(prevYear))
            .ToDictionary(r => (r.EmployeeId, r.LeaveTypeId));
        var existing = (await _entitlements.GetByYearAsync(targetYear))
            .Select(r => (r.EmployeeId, r.LeaveTypeId))
            .ToHashSet();

        var created = 0;
        var skipped = 0;

        foreach (var member in members)
        {
            // Only types belonging to THIS member's org — the cron spans tenants.
            foreach (var type in ctx.TypesById.Values.Where(
                         t => !t.IsArchived && t.OrganizationId == member.OrganizationId))
            {
                if (existing.Contains((member.UserId, type.Id)))
                {
                    skipped++;                  // already opened — idempotent re-run
                    continue;
                }

                prevRows.TryGetValue((member.UserId, type.Id), out var prev);

                var entitledDays = ctx.EntitledDaysFor(member.UserId, type);
                // The employee-layer override rides along to the new row so an
                // explicit per-person customisation survives the rollover.
                var employeeMethod = prev?.AccrualMethod;
                var method = ctx.Resolve(member.UserId, type, employeeMethod);

                var (carriedDays, carriedExpiresAt) = CarryForward(type, prev, method, ctx, targetYear);

                await _entitlements.AddAsync(new LeaveEntitlement
                {
                    OrganizationId = member.OrganizationId,   // no auto-stamp without a tenant context
                    EmployeeId = member.UserId,
                    LeaveTypeId = type.Id,
                    Year = targetYear,
                    EntitledDays = entitledDays,
                    // PRO_RATED starts empty and fills monthly; LUMP_SUM is available at once.
                    AccruedDays = method == LeaveAccrualMethod.PRO_RATED ? 0 : entitledDays,
                    CarriedDays = carriedDays,
                    CarriedExpiresAt = carriedExpiresAt,
                    AccrualMethod = employeeMethod,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                created++;
            }
        }

        if (created > 0) await _entitlements.SaveAsync();
        return new RolloverResult(true, created, skipped, targetYear);
    }

    private static (double Days, DateTime? ExpiresAt) CarryForward(
        LeaveType type, LeaveEntitlement? prev, LeaveAccrualMethod method,
        CronContext ctx, int targetYear)
    {
        if (!type.CarryForward || prev is null) return (0, null);

        var days = LeaveAccrualMath.CarryForwardAmount(
            method,
            prev.EntitledDays,
            prev.AccruedDays,
            prev.CarriedExpired ? 0 : prev.CarriedDays,   // already-lapsed days don't roll again
            ctx.UsedDays(prev.EmployeeId, prev.LeaveTypeId),
            type.MaxCarryForwardDays);

        if (days <= 0 || type.CarryExpiryMonth is null) return (days, null);

        // Lapses at the start of the configured month, in the target year.
        return (days, new DateTime(targetYear, type.CarryExpiryMonth.Value, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private const double Epsilon = 0.0001;

    private static int YearInMyt(DateTime now) =>
        TimeZoneInfo.ConvertTimeFromUtc(now.ToUniversalTime(), Myt).Year;

    // Everything the loops need, loaded ONCE so neither cron issues a query per row.
    private async Task<CronContext> LoadContextAsync(int usageYear)
    {
        var types = (await _types.GetAllAsync()).ToDictionary(t => t.Id);
        var policyByEmployee = (await _directory.GetMembershipsForCurrentOrgAsync())
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, g => g.First().PolicyId);
        // ONE read covers both layers: the day count and the method override.
        var policyRows = await _policyEntitlements.GetAllAsync();
        var policyMethods = policyRows.ToDictionary(e => (e.PolicyId, e.LeaveTypeId), e => e.AccrualMethod);
        var policyDays = policyRows.ToDictionary(e => (e.PolicyId, e.LeaveTypeId), e => e.DefaultDays);

        // Used days come from APPROVED applications rather than a denormalised
        // column, so they can never drift out of step with the applications.
        var used = (await _apps.GetAllAsync())
            .Where(a => a.Status == LeaveStatus.APPROVED && a.StartDate.Year == usageYear)
            .GroupBy(a => (a.EmployeeId, a.LeaveTypeId))
            .ToDictionary(g => g.Key, g => g.Sum(a => a.TotalDays));

        return new CronContext(types, policyByEmployee, policyMethods, policyDays, used);
    }

    private sealed record CronContext(
        Dictionary<string, LeaveType> TypesById,
        Dictionary<string, string?> PolicyByEmployee,
        Dictionary<(string, string), LeaveAccrualMethod?> PolicyMethods,
        Dictionary<(string, string), double> PolicyDays,
        Dictionary<(string, string), double> Used)
    {
        public double UsedDays(string employeeId, string leaveTypeId) =>
            Used.GetValueOrDefault((employeeId, leaveTypeId));

        // Narrowest wins: the employee's row, then their policy, then the type.
        public LeaveAccrualMethod Resolve(LeaveEntitlement row, LeaveType type) =>
            Resolve(row.EmployeeId, type, row.AccrualMethod);

        public LeaveAccrualMethod Resolve(string employeeId, LeaveType type, LeaveAccrualMethod? employeeMethod)
        {
            if (employeeMethod is not null) return employeeMethod.Value;
            var policyId = PolicyByEmployee.GetValueOrDefault(employeeId);
            if (policyId is not null
                && PolicyMethods.TryGetValue((policyId, type.Id), out var m)
                && m is not null)
                return m.Value;
            return type.AccrualMethod;
        }

        // Same layering for the day count: policy override, else the type default.
        public double EntitledDaysFor(string employeeId, LeaveType type)
        {
            var policyId = PolicyByEmployee.GetValueOrDefault(employeeId);
            return policyId is not null && PolicyDays.TryGetValue((policyId, type.Id), out var d)
                ? d
                : type.DefaultDays;
        }
    }
}
