using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Xero;

namespace AltomateHR.Api.Modules.Payroll;

// Turns an admin's description of a split into `PayrollJournalSplitRule`s.
//
// AI only TRANSLATES here: it never sees an amount and never writes a journal
// line. What it returns is parsed into rules, and `PayrollJournalSplit` —
// plain code — applies and checks them against a real run. The rules are
// saved with the settings; posting applies them without calling AI again.
public class PayrollJournalSplitService : IPayrollJournalSplitService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IPayrollXeroSyncService _sync;
    private readonly IXeroService _xero;
    private readonly IAiTextClient _ai;
    private readonly ILogger<PayrollJournalSplitService> _log;

    public PayrollJournalSplitService(
        IPayrollXeroSyncService sync, IXeroService xero, IAiTextClient ai,
        ILogger<PayrollJournalSplitService> log)
    {
        _sync = sync;
        _xero = xero;
        _ai = ai;
        _log = log;
    }

    public async Task<XeroSplitGenerateResult> GenerateAsync(GenerateXeroSplitDto dto, CancellationToken ct = default)
    {
        if (!await _xero.IsConnectedAsync())
            return XeroSplitGenerateResult.Refused("Connect Xero first — the split posts to Xero accounts.");

        var org = await _sync.GetSplitOrgContextAsync();

        AiTextResult answer;
        try
        {
            answer = await _ai.GenerateJsonAsync(
                SystemPrompt, UserText(dto.Prompt, dto.Conversation, dto.CurrentRules, org), ct);
        }
        catch (AiConfigurationException ex)
        {
            return XeroSplitGenerateResult.Refused(ex.Message);
        }
        catch (AiProviderException ex)
        {
            _log.LogWarning(ex, "AI split generation failed.");
            return XeroSplitGenerateResult.Refused("The AI service couldn't answer just now. Try again in a moment.");
        }

        Reply reply;
        try
        {
            reply = Parse(answer.Text);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "AI split reply was not the expected JSON ({Provider}).", answer.Provider);
            return XeroSplitGenerateResult.Refused(
                "The AI's answer couldn't be read as split rules. Try rewording the prompt.");
        }

        var rules = Normalise(reply.Rules);
        var preview = await _sync.PreviewSplitAsync(dto.RunId, rules);

        return new XeroSplitGenerateResult(
            preview.Ok, null, rules, reply.Summary, reply.Questions, answer.Provider, preview);
    }

    public async Task<XeroSplitGenerateResult> CheckAsync(CheckXeroSplitDto dto)
    {
        var rules = Normalise(dto.Rules);
        var preview = await _sync.PreviewSplitAsync(dto.RunId, rules);
        return new XeroSplitGenerateResult(preview.Ok, null, rules, null, [], null, preview);
    }

    // ─── Prompt ─────────────────────────────────────────────────────────

    private const string SystemPrompt = """
        You turn an HR admin's plain-language description of how to split a Malaysian payroll
        journal into JSON rules. Code applies the rules; you never calculate amounts.

        The standard journal debits each expense to one account (e.g. all salary to the default
        salary account). Rules re-divide those debit totals across other accounts. Credits
        (deductions, EPF/SOCSO/EIS/PCB payables, net pay) are never split.

        Each rule:
          source      which debit it splits, one of: salary, overtime, allowance, epfEmployer,
                      socsoEmployer, eisEmployer, hrdfEmployer, taxBorneByEmployer
          basis       DEPARTMENT  the actual amounts of employees in the departments in "match"
                      PROJECT     the actual amounts of employees in a team under the projects in
                                  "match" (their whole pay follows the project)
                      EMPLOYEE    the actual amounts of the employees whose ids are in "match"
                      FIXED       a fixed RM amount in "value"
                      PERCENT     "value" percent (40 = 40%) of what DEPARTMENT/EMPLOYEE rules left
                      REMAINDER   whatever is left (exactly one per source when shares don't cover 100%)
          match       department names, project names or employee ids, exactly as listed in the
                      context; else []
          value       number for FIXED/PERCENT, else null
          accountCode an account code from the context, exactly as listed
          tracking    up to 2 {"category","option"} tags, names exactly as listed; else []
          label       2-4 words for the journal line, e.g. "Sales"

        Order of application per source: DEPARTMENT/PROJECT/EMPLOYEE rules in order (an employee
        is taken by the first match), then FIXED and PERCENT share what is left, then REMAINDER.
        "HQ projects", "staff on project X" and similar mean a PROJECT rule when X is a listed
        project; prefer it over naming the staff one by one.

        Rules:
          - Use only accounts, tracking categories/options, departments, projects and employee ids that
            appear in the context. Never invent one. If the admin names something that isn't
            there, leave that rule out and say so in "questions".
          - Only split the sources the admin asked about.
          - "Tag each line with the X tracking option of the same name" means a tag whose
            category is X and whose option matches the line's label/department.
          - If the admin refers to the saved rules ("last month's rule", "same as before"),
            start from savedRules.
          - When something is unclear or missing, ask. Each question is short and specific, and
            "options" lists the likely answers you can see in the context (account codes with
            names, employee names, department names), at most 6, each a complete answer the
            admin could click. Use [] when no option fits.
          - There may be a conversation after the instructions: your earlier questions and the
            admin's answers. Treat the answers as part of the instructions, keep the rules that
            were already right, and only ask what is still unclear. Never repeat a question that
            has been answered.

        Reply with one JSON object and nothing else:
        {"rules":[...], "summary":"one or two sentences restating the split",
         "questions":[{"question":"...","options":["...","..."]}]}
        """;

    private static string UserText(
        string prompt, IReadOnlyList<XeroSplitTurnDto> conversation,
        IReadOnlyList<PayrollJournalSplitRule> currentRules, PayrollXeroSplitOrgContext org)
    {
        var context = new
        {
            sources = PayrollJournalSplitSources.All.Select(s => new
            {
                source = s,
                label = PayrollJournalSplitSources.Label(s),
                standardAccountCode = org.StandardAccounts.GetValueOrDefault(s),
            }),
            accounts = org.Accounts.Select(a => new { code = a.Code, name = a.Name, type = a.Type }),
            trackingCategories = org.Context.TrackingCategories
                .Select(c => new { category = c.Key, options = c.Value.OrderBy(o => o) }),
            departments = org.Context.Departments.OrderBy(d => d),
            projects = org.Context.Projects.OrderBy(p => p),
            employees = org.Context.Employees
                .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                .Select(e => new
                {
                    id = e.Key,
                    name = e.Value,
                    projects = org.Context.EmployeeProjects.GetValueOrDefault(e.Key) ?? [],
                }),
            savedRules = org.SavedRules,
        };

        var text = "Context:\n" + JsonSerializer.Serialize(context, Json)
            + "\n\nThe admin's instructions:\n" + prompt.Trim();

        if (conversation.Count > 0)
        {
            text += "\n\nConversation since then (oldest first):\n" + string.Join("\n\n",
                conversation.Select(t => (t.Role == "ai" ? "You (AI):\n" : "Admin:\n") + t.Text.Trim()));
        }

        if (currentRules.Count > 0)
        {
            text += "\n\nThe rules you produced last (refine these):\n"
                + JsonSerializer.Serialize(currentRules, Json);
        }

        return text;
    }

    // ─── Reading the reply ──────────────────────────────────────────────

    private sealed record Reply(
        List<PayrollJournalSplitRule> Rules, string? Summary, List<XeroSplitQuestion> Questions);

    private sealed record RawReply(List<PayrollJournalSplitRule>? Rules, string? Summary);

    private static Reply Parse(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            text = firstNewline < 0 ? text : text[(firstNewline + 1)..];
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException("No JSON object in the reply.");

        var json = text[start..(end + 1)];
        var reply = JsonSerializer.Deserialize<RawReply>(json, Json) ?? throw new JsonException("Empty reply.");

        return new Reply(reply.Rules ?? [], reply.Summary, Questions(json));
    }

    // Each question as { question, options }; a plain string (the older
    // shape, or a model that ignored the schema) is a question with no options.
    private static List<XeroSplitQuestion> Questions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        // Case-insensitive, like the rest of the reply.
        var questions = doc.RootElement.EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, "questions", StringComparison.OrdinalIgnoreCase))
            .Value;
        if (questions.ValueKind != JsonValueKind.Array) return [];

        var list = new List<XeroSplitQuestion>();
        foreach (var q in questions.EnumerateArray())
        {
            if (q.ValueKind == JsonValueKind.String)
            {
                if (!string.IsNullOrWhiteSpace(q.GetString())) list.Add(new XeroSplitQuestion(q.GetString()!.Trim(), []));
                continue;
            }

            if (q.ValueKind != JsonValueKind.Object) continue;

            var question = (q.TryGetProperty("question", out var t) || q.TryGetProperty("text", out t))
                && t.ValueKind == JsonValueKind.String ? t.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(question)) continue;

            var options = q.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                    .Select(x => x.GetString()!.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .Take(6)
                    .ToList()
                : [];

            list.Add(new XeroSplitQuestion(question, options));
        }

        return list;
    }

    // Trim what came back, and drop what can never be valid (a blank
    // match entry, an empty tag), so the checks speak about real problems.
    private static List<PayrollJournalSplitRule> Normalise(IEnumerable<PayrollJournalSplitRule> rules) =>
    [
        .. rules.Take(50).Select(r => r with
        {
            Source = (r.Source ?? string.Empty).Trim(),
            AccountCode = (r.AccountCode ?? string.Empty).Trim(),
            Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim(),
            Match = [.. (r.Match ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())],
            Tracking = [.. (r.Tracking ?? [])
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Category) && !string.IsNullOrWhiteSpace(t.Option))
                .Select(t => new PayrollJournalSplitTag(t.Category.Trim(), t.Option.Trim()))],
            Value = r.Basis is SplitBasis.FIXED or SplitBasis.PERCENT ? r.Value : null,
        }),
    ];
}
