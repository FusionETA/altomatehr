using System.Text;
using System.Text.Json;
using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Modules.Documents;

// Builds the instructions from the merge-field registry and the letter markup,
// calls Gemini, and checks what comes back before the editor sees it.
//
// No employee data goes to the AI: a template holds placeholders, so the model
// only ever sees the admin's request, the field list and the draft itself.
public class TemplateAiService : ITemplateAiService
{
    // Well under the 20,000-character template limit; a letter is a page or two.
    private const int MaxBodyChars = 20000;
    private const int MaxNameChars = 160;

    private readonly IGeminiClient _gemini;

    public TemplateAiService(IGeminiClient gemini) => _gemini = gemini;

    public async Task<TemplateAiResultDto> DraftAsync(TemplateAiRequestDto request, CancellationToken cancellationToken = default)
    {
        var revising = !string.IsNullOrWhiteSpace(request.CurrentBody);
        var turns = request.Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => new GeminiTurn(m.Role == "assistant" ? "model" : "user", m.Text.Trim()))
            .ToList();
        if (turns.Count == 0 || turns[^1].Role != "user")
            throw new ArgumentException("Say what the letter should be.");

        var raw = await _gemini.GenerateTextAsync(
            BuildInstructions(request, revising),
            turns,
            // A little variety makes the two drafts genuinely different; a
            // revision should change what was asked and little else.
            new GeminiTextOptions(MaxOutputTokens: 8192, Temperature: revising ? 0.3 : 0.7),
            cancellationToken);

        return Parse(raw, revising ? 1 : 2, request);
    }

    // ─── Instructions ────────────────────────────────────────────────────

    public static string BuildInstructions(TemplateAiRequestDto request, bool revising)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            You write HR letter TEMPLATES for AltomateHR, a Malaysian HR and payroll system. The person
            talking to you is a company's HR admin. A template is reused for many employees: every
            personal or company detail is a merge field that AltomateHR fills in when a letter is made.

            LANGUAGE
            - Write the letter in the language the admin asks for. If they don't say, use the language
              of their message (English or Bahasa Melayu). Use the formal register of a Malaysian
              employer's letter. Write your reply to the admin in the same language.

            WHAT THE PDF ALREADY PRINTS — do NOT write these in the body
            - The company letterhead (name, address, phone, email) at the top.
            - The letter's date.
            - The sign-off: "Yours sincerely, for and on behalf of <company>", the signature line, and
              the signatory's name and position. End the body with the last paragraph of the letter.

            FORMAT (the only markup supported)
            - Plain text. A blank line ends a paragraph; a single line break stays inside it.
            - "# " starts a heading (the letter title), "## " a sub-heading.
            - "- " starts a bullet point. No numbered lists, tables, italics, links or HTML.
            - **bold** for emphasis.
            - Merge fields are written exactly as {{key}}, e.g. {{employee.name}}.

            MERGE FIELDS you may use (key — meaning):
            """);
        foreach (var f in MergeFields.All)
            sb.AppendLine($"- {{{{{f.Key}}}}} — {f.Label}: {f.Description} ({f.Kind})");
        sb.AppendLine("""

            For anything the letter needs that is NOT in that list (a last working day, a reporting
            manager, a new salary, an effective date, a reason, a deadline …), use a field typed in for
            each letter: {{input.camelCaseName}}, e.g. {{input.lastWorkingDay}}, {{input.newSalary}},
            {{input.reportingManager}}. Name dates ending in "Date" or "Day" and money ending in
            "Amount" or "Salary". Never invent any other {{…}} key, never write blanks like "____" or
            "[Name]", and never make up names, figures or dates — use a field.

            Keep to standard Malaysian employment practice and the Employment Act 1955 where it
            applies, but don't quote section numbers you are not sure of. Keep a body under 6,000
            characters.

            CATEGORY — one of: OFFER, CONFIRMATION, PROMOTION, RESIGNATION, TERMINATION, WARNING, OTHER.
            """);

        if (revising)
        {
            sb.AppendLine("""
                TASK
                The admin is editing the draft below. Apply the change they ask for in their latest
                message and keep everything else as it is. Return exactly ONE draft.
                """);
            if (!string.IsNullOrWhiteSpace(request.CurrentName)) sb.AppendLine($"Current name: {request.CurrentName}");
            if (request.CurrentCategory is { } cat) sb.AppendLine($"Current category: {cat}");
            sb.AppendLine("CURRENT DRAFT:");
            sb.AppendLine("<<<");
            sb.AppendLine(request.CurrentBody);
            sb.AppendLine(">>>");
        }
        else
        {
            sb.AppendLine("""
                TASK
                Write TWO complete drafts of the letter the admin describes, for them to choose from.
                Make them genuinely different — e.g. one concise and direct, one fuller and more
                formal — but both complete and ready to use. If the admin asks for changes later in
                the conversation, write two new drafts that include them.
                """);
        }

        sb.AppendLine($$"""

            OUTPUT — only this JSON, nothing else:
            {"reply": "one or two plain sentences to the admin about what you wrote",
             "drafts": [{"name": "short template name, under 60 characters, in the letter's language",
                         "category": "OFFER", "body": "the template body"}]}
            "drafts" has exactly {{(revising ? 1 : 2)}} item{{(revising ? "" : "s")}}.
            """);
        return sb.ToString();
    }

    // ─── Parsing ─────────────────────────────────────────────────────────

    public static TemplateAiResultDto Parse(string raw, int expected, TemplateAiRequestDto request)
    {
        var text = StripCodeFences(raw).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start) text = text[start..(end + 1)];

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new AiProviderException($"The AI's answer wasn't readable: {ex.Message}");
        }

        var result = new TemplateAiResultDto { Reply = Str(root, "reply").Trim() };
        if (root.TryGetProperty("drafts", out var drafts) && drafts.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in drafts.EnumerateArray())
            {
                if (result.Drafts.Count == expected) break;
                var body = Str(d, "body").Replace("\r\n", "\n").Trim();
                if (body.Length == 0) continue;

                var draft = new TemplateAiDraftDto
                {
                    Name = Clip(Str(d, "name").Trim(), MaxNameChars),
                    Category = Enum.TryParse<DocumentCategory>(Str(d, "category").Trim(), ignoreCase: true, out var c)
                        ? c
                        : request.CurrentCategory ?? DocumentCategory.OTHER,
                };
                if (draft.Name.Length == 0) draft.Name = request.CurrentName?.Trim() is { Length: > 0 } n ? n : "Untitled letter";
                if (body.Length > MaxBodyChars)
                {
                    body = body[..MaxBodyChars];
                    draft.Warnings.Add("The draft was longer than a template can be, so its end was cut off.");
                }
                draft.Body = body;
                draft.UnknownFields = [.. DocxTemplateImport.UnknownPlaceholders(body)];
                result.Drafts.Add(draft);
            }
        }

        if (result.Drafts.Count == 0)
            throw new AiProviderException("The AI didn't return a draft.");
        return result;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static string StripCodeFences(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return trimmed;
        trimmed = trimmed[(firstNewline + 1)..];
        var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? trimmed[..closing] : trimmed;
    }
}
