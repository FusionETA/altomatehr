using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AltomateHR.Api.Modules.Fah;

// Which of the MCP server's tools FAH offers the model. An allow-list: a tool
// the MCP server adds later stays hidden until someone decides FAH should
// have it. Submitting, approving and rejecting payroll are never on it — and
// the API refuses those routes to FAH keys anyway (ActingKeyWriteAllowlistMiddleware).
public static partial class FahTools
{
    // Look things up; nothing changes.
    public static readonly IReadOnlySet<string> Reads = new HashSet<string>(StringComparer.Ordinal)
    {
        "list_pending", "whoami",
        "list_employees", "get_employee", "list_admins", "list_policies",
        "list_claims", "get_claim",
        "list_payroll_runs", "get_payroll_run", "get_payroll_readiness",
        "list_adjustment_categories", "list_loans", "get_salary_history",
        "list_leave_applications", "get_leave_balances", "list_leave_balances", "who_is_on_leave",
        "get_attendance_summary", "who_is_clocked_in",
    };

    // Make changes. Each works in two calls: a preview (the default — nothing
    // is written) and the real thing, `dryRun: false` with the preview's
    // confirmToken. FAH runs previews freely and holds every real call until
    // the admin presses Confirm on the preview card.
    public static readonly IReadOnlyDictionary<string, string> Writes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["create_employee"] = "Add an employee",
        ["update_employee"] = "Update an employee",
        ["create_payroll_run"] = "Start a payroll run",
        ["run_payroll"] = "Recalculate payroll",
        ["add_payroll_adjustment"] = "Add a payroll adjustment",
        ["change_payroll_adjustment"] = "Change a payroll adjustment",
    };

    // Arguments FAH never sends. get_employee's `reveal` would put full IC,
    // SOCSO and bank numbers into the conversation; the masked form is enough
    // to answer questions, and anyone who needs the numbers has the screen.
    private static readonly IReadOnlyDictionary<string, string[]> Hidden = new Dictionary<string, string[]>
    {
        ["get_employee"] = ["reveal"],
    };

    public static IReadOnlyList<FahTool> Offer(IEnumerable<McpTool> tools) =>
        [.. tools
            .Where(t => IsOffered(t.Name))
            .OrderBy(t => t.Name, StringComparer.Ordinal)   // a stable list keeps the prompt cacheable
            .Select(t => new FahTool(t.Name, t.Description, WithoutHidden(t.Name, t.InputSchema)))];

    public static bool IsOffered(string name) => Reads.Contains(name) || Writes.ContainsKey(name);

    // The real call of a write tool — anything else (a read, or a preview) runs at once.
    public static bool IsCommit(string name, JsonElement arguments) =>
        Writes.ContainsKey(name)
        && arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty("dryRun", out var dryRun)
        && dryRun.ValueKind == JsonValueKind.False;

    // The same call as a preview: what the admin is shown is the server's own
    // preview of exactly these details, not the model's description of them.
    public static JsonElement AsPreview(JsonElement arguments)
    {
        var node = JsonNode.Parse(arguments.GetRawText())!.AsObject();
        node.Remove("confirmToken");
        node["dryRun"] = true;
        return JsonSerializer.SerializeToElement(node);
    }

    // A preview as the admin reads it: without the "PREVIEW ONLY" banner (the
    // card says that) and without the lines addressed to the model about how
    // to confirm — the confirm token included.
    public static string ForCard(string preview)
    {
        var text = PreviewBanner().Replace(preview.Trim(), string.Empty);
        var paragraphs = text.Split("\n\n")
            .Select(p => string.Join('\n', p.Split('\n').Where(l => !TokenLine().IsMatch(l.Trim()))))
            .Select(p => ForModel().IsMatch(p)
                ? string.Join(' ', Sentences().Split(p).Where(s => !ForModel().IsMatch(s)))
                : p)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);
        return string.Join("\n\n", paragraphs);
    }

    [GeneratedRegex(@"^PREVIEW ONLY\s*[—-]\s*nothing has been \w+\.\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewBanner();

    [GeneratedRegex(@"^\d{10,}\.[A-Za-z0-9_-]{20,}$")]
    private static partial Regex TokenLine();

    [GeneratedRegex(@"dryRun|confirmToken|call (this tool )?again|show the user|ask them to confirm", RegexOptions.IgnoreCase)]
    private static partial Regex ForModel();

    [GeneratedRegex(@"(?<=[.!?:])\s+")]
    private static partial Regex Sentences();

    // The arguments as sent, minus anything hidden — whatever the model wrote.
    public static JsonElement Clean(string name, JsonElement arguments)
    {
        if (!Hidden.TryGetValue(name, out var hidden) || arguments.ValueKind != JsonValueKind.Object)
            return arguments;
        var node = JsonNode.Parse(arguments.GetRawText())!.AsObject();
        foreach (var key in hidden) node.Remove(key);
        return JsonSerializer.SerializeToElement(node);
    }

    private static JsonElement WithoutHidden(string name, JsonElement schema)
    {
        if (!Hidden.TryGetValue(name, out var hidden) || schema.ValueKind != JsonValueKind.Object)
            return schema;
        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        if (node["properties"] is JsonObject properties)
            foreach (var key in hidden) properties.Remove(key);
        if (node["required"] is JsonArray required)
            foreach (var item in required.Where(r => hidden.Contains(r?.GetValue<string>())).ToList())
                required.Remove(item);
        return JsonSerializer.SerializeToElement(node);
    }
}
