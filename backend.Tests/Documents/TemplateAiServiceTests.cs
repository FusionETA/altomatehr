using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.Documents;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Tests.Documents;

// "Write with AI": what the model is told, and what reaches the editor from
// whatever it answers. The model itself is faked — these pin the contract.
public class TemplateAiServiceTests
{
    private static TemplateAiRequestDto Ask(string text, string? currentBody = null) => new()
    {
        Messages = [new TemplateAiMessageDto { Role = "user", Text = text }],
        CurrentBody = currentBody,
        CurrentName = currentBody is null ? null : "Offer Letter",
        CurrentCategory = currentBody is null ? null : DocumentCategory.OFFER,
    };

    private const string TwoDrafts = """
        {"reply": "Here are two offer letters.",
         "drafts": [
           {"name": "Offer Letter (Concise)", "category": "offer",
            "body": "# Letter of Offer\n\nDear {{employee.name}},\n\nWe offer you the post of {{employee.jobTitle}} from {{employee.joinDate}}."},
           {"name": "Offer Letter (Formal)", "category": "OFFER",
            "body": "# Letter of Offer\n\nDear {{Employee Name}},\n\nReporting to {{input.reportingManager}}."}
         ]}
        """;

    [Fact]
    public async Task ANewTemplate_ComesBackAsTwoDrafts_WithMadeUpFieldsFlagged()
    {
        var gemini = new FakeGemini(TwoDrafts);
        var result = await new TemplateAiService(gemini).DraftAsync(Ask("An offer letter for a site engineer"));

        Assert.Equal("Here are two offer letters.", result.Reply);
        Assert.Equal(2, result.Drafts.Count);
        Assert.All(result.Drafts, d => Assert.Equal(DocumentCategory.OFFER, d.Category));
        Assert.Empty(result.Drafts[0].UnknownFields);
        // A loose placeholder goes to the editor's "Replace with…" picker; a
        // typed-per-letter {{input.xxx}} is a real field.
        Assert.Equal(["Employee Name"], result.Drafts[1].UnknownFields);
        Assert.Equal(0.7, gemini.LastOptions!.Temperature);
    }

    [Fact]
    public async Task Revising_SendsTheCurrentDraft_AndReturnsOneVersion()
    {
        var gemini = new FakeGemini(TwoDrafts);
        var result = await new TemplateAiService(gemini)
            .DraftAsync(Ask("Make it more formal", currentBody: "Dear {{employee.name}}, welcome aboard."));

        Assert.Single(result.Drafts);
        Assert.Contains("Dear {{employee.name}}, welcome aboard.", gemini.LastInstruction);
        Assert.Contains("Return exactly ONE draft", gemini.LastInstruction);
        Assert.Equal(0.3, gemini.LastOptions!.Temperature);
    }

    [Fact]
    public void TheInstructions_ListEveryMergeField_AndTheLanguageRule()
    {
        var text = TemplateAiService.BuildInstructions(Ask("Surat tawaran"), revising: false);

        foreach (var f in MergeFields.All) Assert.Contains("{{" + f.Key + "}}", text);
        Assert.Contains("Bahasa Melayu", text);
        Assert.Contains("Write TWO complete drafts", text);
        // The PDF prints these, so the body mustn't repeat them.
        Assert.Contains("Yours sincerely", text);
    }

    [Fact]
    public async Task EarlierReplies_GoBackToTheModelAsItsOwnTurns()
    {
        var gemini = new FakeGemini(TwoDrafts);
        await new TemplateAiService(gemini).DraftAsync(new TemplateAiRequestDto
        {
            Messages =
            [
                new() { Role = "user", Text = "A warning letter for lateness" },
                new() { Role = "assistant", Text = "Here are two warning letters." },
                new() { Role = "user", Text = "Mention that it's the second warning" },
            ],
        });

        Assert.Equal(["user", "model", "user"], gemini.LastTurns!.Select(t => t.Role));
    }

    [Fact]
    public async Task ACodeFencedAnswer_IsStillRead_AndAnUnknownCategoryBecomesOther()
    {
        var gemini = new FakeGemini("```json\n{\"reply\":\"ok\",\"drafts\":[{\"name\":\"Memo\",\"category\":\"MEMO\",\"body\":\"Hello {{employee.name}}\"}]}\n```");
        var result = await new TemplateAiService(gemini).DraftAsync(Ask("A memo"));

        var draft = Assert.Single(result.Drafts);
        Assert.Equal(DocumentCategory.OTHER, draft.Category);
        Assert.Equal("Hello {{employee.name}}", draft.Body);
    }

    [Fact]
    public async Task ALongReply_IsCut_SoItStillFitsWhenSentBackNextTurn()
    {
        var reply = new string('x', 5000);
        var gemini = new FakeGemini($$"""{"reply":"{{reply}}","drafts":[{"name":"Memo","category":"OTHER","body":"Hi"}]}""");
        var result = await new TemplateAiService(gemini).DraftAsync(Ask("A memo"));

        // TemplateAiMessageDto caps an assistant turn at 4000.
        Assert.True(result.Reply.Length <= 1000);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"reply\":\"sorry\",\"drafts\":[]}")]
    [InlineData("{\"reply\":\"sorry\",\"drafts\":[{\"name\":\"x\",\"body\":\"   \"}]}")]
    public async Task NoUsableDraft_IsAProviderError(string answer)
    {
        await Assert.ThrowsAsync<AiProviderException>(() =>
            new TemplateAiService(new FakeGemini(answer)).DraftAsync(Ask("Anything")));
    }

    [Fact]
    public async Task TheLastMessageMustBeTheAdmins()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TemplateAiService(new FakeGemini(TwoDrafts)).DraftAsync(new TemplateAiRequestDto
            {
                Messages = [new() { Role = "assistant", Text = "Here you go" }],
            }));
    }

    private sealed class FakeGemini(string answer) : IGeminiClient
    {
        public string? LastInstruction { get; private set; }
        public IReadOnlyList<GeminiTurn>? LastTurns { get; private set; }
        public GeminiTextOptions? LastOptions { get; private set; }

        public Task<string> GenerateTextAsync(string systemInstruction, IReadOnlyList<GeminiTurn> turns,
            GeminiTextOptions options, CancellationToken cancellationToken = default)
        {
            LastInstruction = systemInstruction;
            LastTurns = turns;
            LastOptions = options;
            return Task.FromResult(answer);
        }

        public Task<string> GenerateFromImageAsync(string prompt, byte[] fileBytes, string mimeType,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
