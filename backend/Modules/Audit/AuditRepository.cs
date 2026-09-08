using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Audit;

// The ONLY place that touches the database for audit rows.
public class AuditRepository : IAuditRepository
{
    // Two writers racing for the same Seq means one insert loses the unique
    // index and has to re-read the head. Bounded so a genuinely broken write
    // fails loudly instead of spinning.
    private const int MaxAppendAttempts = 5;

    private readonly AppDbContext _db;

    public AuditRepository(AppDbContext db) => _db = db;

    public async Task<AuditLog> AppendAsync(AuditLog entry, string organizationId)
    {
        entry.OrganizationId = organizationId;

        for (var attempt = 1; ; attempt++)
        {
            // IgnoreQueryFilters: the tenant filter reads the CURRENT user's org,
            // and this write may be for one nobody is signed in to (a failed
            // sign-in). The org is scoped explicitly instead.
            var head = await _db.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => a.OrganizationId == organizationId)
                .OrderByDescending(a => a.Seq)
                .FirstOrDefaultAsync();

            entry.Seq = (head?.Seq ?? 0) + 1;
            entry.PrevHash = head?.Hash;
            entry.Hash = AuditChain.ComputeHash(entry, entry.PrevHash);

            _db.AuditLogs.Add(entry);
            try
            {
                await _db.SaveChangesAsync();
                return entry;
            }
            catch (DbUpdateException) when (attempt < MaxAppendAttempts)
            {
                // Lost the race for this Seq. Detach and read the new head.
                _db.Entry(entry).State = EntityState.Detached;
            }
        }
    }

    public async Task<(List<AuditLog> Rows, int Total)> QueryAsync(
        AuditQueryDto query, string organizationId)
    {
        var rows = _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            // "xero.connect" matches exactly; "xero" matches the namespace. The
            // trailing dot stops "leave" from also matching "leaver.*".
            var prefix = query.Action.Trim();
            rows = rows.Where(a => a.Action == prefix || a.Action.StartsWith(prefix + "."));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
            rows = rows.Where(a => a.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.ActorUserId))
            rows = rows.Where(a => a.ActorUserId == query.ActorUserId);

        if (query.From is { } from) rows = rows.Where(a => a.CreatedAt >= from.Date);
        // Exclusive end on the NEXT day, so a "to" of the 8th includes the 8th.
        if (query.To is { } to) rows = rows.Where(a => a.CreatedAt < to.Date.AddDays(1));

        // Counted before paging, so the total describes the whole filtered set
        // rather than the slice being returned.
        var total = await rows.CountAsync();

        var limit = Math.Clamp(query.Limit, 1, 200);
        var page = Math.Max(1, query.Page);

        // Ordered by Seq, which is monotonic per org — so this is the same order
        // as newest-first without needing a compound (CreatedAt, Id) comparison.
        var slice = await rows
            .OrderByDescending(a => a.Seq)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        return (slice, total);
    }

    public Task<List<AuditLog>> GetChainAsync(string organizationId) =>
        _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.Seq)
            .ToListAsync();
}
