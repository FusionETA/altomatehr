using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Audit.Entities;

namespace AltomateHR.Api.Modules.Audit;

public interface IAuditRepository
{
    // Appends one row, allocating its Seq and hashing it onto the end of the
    // org's chain. Returns the persisted row.
    //
    // `organizationId` is explicit rather than left to the tenant stamp: a
    // failed sign-in has no authenticated org yet, and that is exactly an event
    // worth recording.
    Task<AuditLog> AppendAsync(AuditLog entry, string organizationId);

    // A page of the feed, newest first, plus how many rows match the filters in
    // total. Both come from one method so the count can never be taken with a
    // different filter than the page it labels.
    Task<(List<AuditLog> Rows, int Total)> QueryAsync(AuditQueryDto query, string organizationId);

    // The whole chain in Seq order, for verification. Reads every row on
    // purpose — a partial read cannot prove anything about the chain.
    Task<List<AuditLog>> GetChainAsync(string organizationId);
}
