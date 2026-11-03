namespace AltomateHR.Api.Modules.Auth;

// The AltomateHR AI's email(s) (AI_ACTOR_EMAILS, comma-separated): an
// automation that drives the MCP server with a company's own API key, and
// names itself as the acting admin there.
//
// It is NOT an account and NOT a superadmin: it has no password, can't sign
// in, appears in no company's admin or employee list on screen, and holds no
// permission of its own — what it can do in a company is exactly what that
// company's API key is scoped to. Its one effect is that every company's
// key-facing admin list (GET /admin/users) names it, so the MCP server
// accepts it as the acting admin anywhere. Read from config, so adding or
// dropping one is a config change.
public interface IAiActorRegistry
{
    IReadOnlyList<string> Emails { get; }
}

public class AiActorRegistry : IAiActorRegistry
{
    public const string DisplayName = "AltomateHR AI";

    public AiActorRegistry(IConfiguration config) =>
        Emails = (config["AI_ACTOR_EMAILS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .ToList();

    public IReadOnlyList<string> Emails { get; }
}
