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
            answer = await _ai.GenerateJsonAsync(SystemPrompt, UserText(dto.Prompt, org), ct);
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

        var rules = Normalise(reply.Rules ?? []);
        var preview = await _sync.PreviewSplitAsync(dto.RunId, rules);

        return new XeroSplitGenerateResult(
            preview.Ok, null, rules, reply.Summary, reply.Questions ?? [], answer.Provider, preview);
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
                      EMPLOYEE    the actual amounts of the employees whose ids are in "match"
                      FIXED       a fixed RM amount in "value"
                      PERCENT     "value" percent (40 = 40%) of what DEPARTMENT/EMPLOYEE rules left
                      REMAINDER   whatever is left (exactly one per source when shares don't cover 100%)
          match       department names or employee ids, exactly as listed in the context; else []
          value       number for FIXED/PERCENT, else null
          accountCode an account code from the context, exactly as listed
          tracking    up to 2 {"category","option"} tags, names exactly as listed; else []
          label       2-4 words for the journal line, e.g. "Sales"

        Order of application per source: DEPARTMENT/EMPLOYEE rules in order (an employee is taken
        by the first match), then FIXED and PERCENT share what is left, then REMAINDER.

        Rules:
          - Use only accounts, tracking categories/options, departments and employee ids that
            appear in the context. Never invent one. If the admin names something that isn't
            there, leave that rule out and say so in "questions".
          - Only split the sources the admin asked about.
          - "Tag each line with the X tracking option of the same name" means a tag whose
            category is X and whose option matches the line's label/department.
          - If the admin refers to the saved rules ("last month's rule", "same as before"),
            start from savedRules.

        Reply with one JSON object and nothing else:
        {"rules":[...], "summary":"one or two sentences restating the split", "questions":["..."]}
        """;

    private static string UserText(string prompt, PayrollXeroSplitOrgContext org)
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
            employees = org.Context.Employees
                .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                .Select(e => new { id = e.Key, name = e.Value }),
            savedRules = org.SavedRules,
        };

        return "Context:\n" + JsonSerializer.Serialize(context, Json)
            + "\n\nThe admin's instructions:\n" + prompt.Trim();
    }

    // ─── Reading the reply ──────────────────────────────────────────────

    private sealed record Reply(
        List<PayrollJournalSplitRule>? Rules, string? Summary, List<string>? Questions);

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

        return JsonSerializer.Deserialize<Reply>(text[start..(end + 1)], Json)
            ?? throw new JsonException("Empty reply.");
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
