using System.Text;
using System.Text.Json;

namespace AltomateHR.Api.Modules.Xero;

// Which Xero organisation an OAuth sign-in connects.
//
// Xero's /connections lists EVERY org this app has ever been connected to — not
// just the one the admin ticked on the consent screen. Taking the first of them
// connected whichever org happened to be authorised earliest: an admin who
// chose "GLOBE ENGINEERING" got the test org another company had connected
// weeks before. Two rules replace that, the second from the previous system:
//
//   1. Only the org(s) authorised in THIS sign-in. Xero stamps each connection
//      with the auth event that created or last re-authorised it, and the
//      access token carries that event's id.
//   2. Never an org already connected to a DIFFERENT AltomateHR company — one
//      Xero org on two companies would post one company's claims into the
//      other's books.
public static class XeroTenantChoice
{
    public enum Refusal { None, NothingAuthorised, SeveralAuthorised, InUseElsewhere }

    public sealed record Result(XeroTenantResponse? Tenant, Refusal Refusal, string? TenantName = null);

    public static Result Choose(
        IReadOnlyList<XeroTenantResponse> tenants,
        string? authEventId,
        IReadOnlySet<string> tenantIdsUsedElsewhere)
    {
        if (tenants.Count == 0) return new(null, Refusal.NothingAuthorised);

        // Rule 1. If the token carries no event id, or no connection matches it
        // (an older Xero response), fall back to every org — rule 2 still
        // narrows that, and several survivors are refused rather than guessed.
        var justAuthorised = authEventId is null
            ? tenants
            : tenants.Where(t => string.Equals(t.AuthEventId, authEventId, StringComparison.Ordinal)).ToList();
        if (justAuthorised.Count == 0) justAuthorised = tenants;

        // Rule 2.
        var connectable = justAuthorised.Where(t => !tenantIdsUsedElsewhere.Contains(t.TenantId)).ToList();

        if (connectable.Count == 0)
            return new(null, Refusal.InUseElsewhere, justAuthorised[0].TenantName);

        return connectable.Count == 1
            ? new(connectable[0], Refusal.None)
            : new(null, Refusal.SeveralAuthorised);
    }

    // The authentication_event_id claim of a Xero access token (a JWT). Only
    // read, never trusted for access — the token came straight from Xero's
    // token endpoint over TLS in this same request. Null if it can't be read.
    public static string? AuthEventIdOf(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("authentication_event_id", out var id)
                   && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
