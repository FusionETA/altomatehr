using System.Text.Json;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Fah;
using AltomateHR.Api.Modules.Fah.Dtos;
using AltomateHR.Api.Modules.Fah.Entities;
using AltomateHR.Api.Tests.ApiKeys;
using AltomateHR.Api.Tests.Audit;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.Fah;

// FAH's turn loop: the model picks tools, FAH runs them through the MCP server
// with a key made for the turn, and only the tools FAH offers ever run.
public class FahServiceTests
{
    private readonly FakeApiKeyRepository _keyRepo = new();
    private readonly FakeMcp _mcp = new();
    private readonly ScriptedModel _model = new();
    private readonly FakeConversations _conversations = new();

    public FahServiceTests()
    {
        _keyRepo.Members[("org-1", "usr-admin")] = new ActingMember("Admin", "admin@x.test");
    }

    private FahService Service() => new(
        _conversations,
        new ActingKeyService(_keyRepo, new StubCurrentUser()),
        _mcp,
        _model,
        new StubCurrentUser(),
        Options.Create(new FahOptions { MaxToolRounds = 3 }),
        NullLogger<FahService>.Instance);

    [Fact]
    public async Task ALookup_RunsThroughTheMcpServer_AsTheAdmin_ThenAnswers()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "who_is_on_leave", "{}")]));
        _model.Script.Enqueue(Turn(text: "Aisyah is on leave today."));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Who's on leave?" }, null, default);

        Assert.True(result.Ok);
        Assert.Equal("Aisyah is on leave today.", result.Response!.Reply);
        Assert.Equal("who_is_on_leave", Assert.Single(result.Response.Steps).Tool);

        // With this turn's FAH key, which is ended once the turn is over.
        var call = Assert.Single(_mcp.Calls);
        var key = Assert.Single(_keyRepo.Keys);
        Assert.Equal(ApiTokenGenerator.HashToken(call.Token), key.TokenHash);
        Assert.Equal("usr-admin", key.ActingUserId);
        Assert.False(key.Active);

        // The tool's answer went back to the model with its call's id.
        var resultMessage = _model.Seen[1][^1];
        var block = resultMessage.GetProperty("content")[0];
        Assert.Equal("tool_result", block.GetProperty("type").GetString());
        Assert.Equal("t1", block.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task ApprovingSubmittingAndSso_AreNeverOffered()
    {
        _mcp.Tools = ["who_is_on_leave", "approve_payroll_run", "submit_payroll_run", "reject_payroll_run", "add_payroll_adjustment", "get_admin_sso_link"];
        _model.Script.Enqueue(Turn(text: "Hi"));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Hello" }, null, default);

        Assert.Equal(["add_payroll_adjustment", "who_is_on_leave"], _model.OfferedTools.Select(t => t.Name));
    }

    // ─── Changes wait for Confirm ───────────────────────────────────────

    private const string Change = """{"employee":"Fay Staff","jobTitle":"Lead","dryRun":false,"confirmToken":"tok-1"}""";

    // A preview changes nothing, so it runs at once like a lookup.
    [Fact]
    public async Task APreview_RunsAtOnce()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", """{"employee":"Fay Staff","jobTitle":"Lead"}""")]));
        _model.Script.Enqueue(Turn(text: "Shall I?"));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);

        Assert.Null(result.Response!.PendingAction);
        Assert.Equal("update_employee", Assert.Single(_mcp.Calls).Name);
    }

    [Fact]
    public async Task TheRealChange_IsHeld_AndShownAsAltomateHRsOwnPreview()
    {
        _model.Script.Enqueue(Turn(text: "Here it is.", tools: [("t1", "update_employee", Change)]));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);

        var card = result.Response!.PendingAction!;
        Assert.Equal("Update an employee", card.Title);
        Assert.Equal("Fay Staff (DT401):\n  Job title: Outlet Staff → Lead", card.Preview);
        // Only the preview reached the server — as a preview, without the token.
        var call = Assert.Single(_mcp.Calls);
        Assert.True(call.Arguments.GetProperty("dryRun").GetBoolean());
        Assert.False(call.Arguments.TryGetProperty("confirmToken", out _));
        Assert.NotNull(_conversations.Rows.Single().PendingJson);
        Assert.Single(_model.Seen);   // FAH stops and waits; no further model call
    }

    [Fact]
    public async Task Confirm_MakesTheChangeWithTheModelsDetails_ThenFahSaysWhatHappened()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _model.Script.Enqueue(Turn(text: "Done — Fay is now a Lead."));

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        }, null, default);

        Assert.Equal("Done — Fay is now a Lead.", result.Response!.Reply);
        var commit = _mcp.Calls[^1];
        Assert.False(commit.Arguments.GetProperty("dryRun").GetBoolean());
        Assert.Equal("tok-1", commit.Arguments.GetProperty("confirmToken").GetString());
        Assert.Null(_conversations.Rows.Single().PendingJson);
        var answer = _model.Seen[^1][^1].GetProperty("content")[0];
        Assert.Equal("t1", answer.GetProperty("tool_use_id").GetString());
        // A key for each turn, all ended.
        Assert.Equal(2, _keyRepo.Keys.Count);
        Assert.DoesNotContain(_keyRepo.Keys, k => k.Active);
    }

    [Fact]
    public async Task Cancel_ChangesNothing()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _model.Script.Enqueue(Turn(text: "OK, left as it was."));

        await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = false,
        }, null, default);

        Assert.Single(_mcp.Calls);   // the preview only
        var answer = _model.Seen[^1][^1].GetProperty("content")[0];
        Assert.Contains("Cancel", answer.GetProperty("content").GetString());
    }

    // A double click, or a retry, must not make the change twice.
    [Fact]
    public async Task ConfirmingTwice_MakesTheChangeOnce()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _model.Script.Enqueue(Turn(text: "Done."));
        var confirm = new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        };

        var first = await Service().ConfirmAsync(confirm, null, default);
        var second = await Service().ConfirmAsync(confirm, null, default);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Single(_mcp.Calls, c => c.Arguments.TryGetProperty("dryRun", out var d) && d.ValueKind == JsonValueKind.False);
    }

    [Fact]
    public async Task Progress_SaysWhatFahIsChecking()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "who_is_on_leave", "{}")]));
        _model.Script.Enqueue(Turn(text: "Nobody."));
        var events = new List<FahProgressEvent>();

        await Service().ChatAsync(new FahChatRequestDto { Message = "Who's away?" }, e => { events.Add(e); return Task.CompletedTask; }, default);

        Assert.Equal([new FahProgressEvent("started", "who_is_on_leave"), new FahProgressEvent("done", "who_is_on_leave", true)], events);
    }

    // The panel shows the chat, not FAH's working: no tool calls or results,
    // and not the date FAH adds to each message for the model.
    [Fact]
    public async Task AChat_IsShownAsMessagesOnly()
    {
        _model.Script.Enqueue(Turn(text: "Let me look.", tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);

        var chat = (await Service().GetAsync(held.Response!.ConversationId))!;

        Assert.Equal(
            [("user", "Make Fay a lead"), ("assistant", "Let me look.")],
            chat.Messages.Select(m => (m.Role, m.Text)));
        Assert.Equal(held.Response.PendingAction!.Id, chat.PendingAction!.Id);
        Assert.Single(await Service().ListAsync());
    }

    // An old card, or one already answered, can't make a change.
    [Fact]
    public async Task AStaleCard_DoesNothing()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = "some-other-card", Approve = true,
        }, null, default);

        Assert.False(result.Ok);
        Assert.Single(_mcp.Calls);
        Assert.NotNull(_conversations.Rows.Single().PendingJson);
    }

    [Fact]
    public async Task ANewMessage_DropsTheHeldChange()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _model.Script.Enqueue(Turn(text: "Sure."));

        await Service().ChatAsync(new FahChatRequestDto { ConversationId = held.Response!.ConversationId, Message = "Actually, never mind" }, null, default);

        Assert.Single(_mcp.Calls);
        Assert.Null(_conversations.Rows.Single().PendingJson);
        var seen = _model.Seen[^1];
        Assert.Equal("tool_result", seen[^2].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("user", seen[^1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task OneChangeAtATime()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change), ("t2", "update_employee", Change)]));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Two changes" }, null, default);

        Assert.NotNull(result.Response!.PendingAction);
        var pending = JsonSerializer.Deserialize<FahPendingAction>(_conversations.Rows.Single().PendingJson!)!;
        Assert.Equal("t1", pending.ToolUseId);
        Assert.True(Assert.Single(pending.Results).IsError);   // t2 refused, answered with t1 later
    }

    // If AltomateHR stops answering part-way, every call still gets an answer,
    // so the saved chat can carry on.
    [Fact]
    public async Task AFailurePartWay_StillAnswersEveryToolCall()
    {
        _mcp.FailCallsAfter = 1;
        _model.Script.Enqueue(Turn(tools: [("t1", "who_is_on_leave", "{}"), ("t2", "who_is_on_leave", "{}")]));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Who's away?" }, null, default);

        Assert.True(result.Unavailable);
        var saved = JsonSerializer.Deserialize<List<JsonElement>>(_conversations.Rows.Single().MessagesJson)!;
        var answered = saved[^1].GetProperty("content").EnumerateArray().Select(b => b.GetProperty("tool_use_id").GetString());
        Assert.Equal(["t1", "t2"], answered);
    }

    // Not just the expected failures: anything that ends a turn early still
    // leaves a chat that can carry on.
    [Fact]
    public async Task AnUnexpectedFailure_StillSavesAChatThatCanCarryOn()
    {
        _mcp.FailCallsAfter = 0;
        _mcp.Failure = new TaskCanceledException("timed out");
        _model.Script.Enqueue(Turn(tools: [("t1", "who_is_on_leave", "{}")]));

        var first = await Service().ChatAsync(new FahChatRequestDto { Message = "Who's away?" }, null, default);

        Assert.True(first.Unavailable);
        var row = _conversations.Rows.Single();
        Assert.Null(row.TurnStartedAt);
        var saved = JsonSerializer.Deserialize<List<JsonElement>>(row.MessagesJson)!;
        Assert.Equal("tool_result", saved[^1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.DoesNotContain(_keyRepo.Keys, k => k.Active);
    }

    // The change went through; only FAH's reply after it failed. The card
    // must still say it was made.
    [Fact]
    public async Task AChangeMade_IsReported_EvenWhenTheReplyAfterItFails()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _model.FailNext = new FahModelException("busy");

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        }, null, default);

        Assert.True(result.Ok);
        Assert.Equal("made", result.Response!.ChangeOutcome);
        Assert.StartsWith("The change was made.", result.Response.Reply);
    }

    // AltomateHR didn't answer the real call: it may have landed. Say so.
    [Fact]
    public async Task AChangeWhoseCallFailed_IsReportedAsUnknown()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _mcp.FailCallsAfter = 1;   // the preview already ran
        _mcp.Failure = new InvalidOperationException("boom");

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        }, null, default);

        Assert.Equal("unknown", result.Response!.ChangeOutcome);
        var saved = JsonSerializer.Deserialize<List<JsonElement>>(_conversations.Rows.Single().MessagesJson)!;
        Assert.Contains("not known", saved[^1].GetProperty("content")[0].GetProperty("content").GetString());
        Assert.Null(_conversations.Rows.Single().TurnStartedAt);
    }

    // Another tab, or a retry after a dropped connection, waits its turn.
    [Fact]
    public async Task OneTurnAtATime_PerChat()
    {
        _model.Script.Enqueue(Turn(text: "First"));
        var first = await Service().ChatAsync(new FahChatRequestDto { Message = "First" }, null, default);
        _conversations.Rows.Single().TurnStartedAt = DateTime.UtcNow;   // a turn still running

        var second = await Service().ChatAsync(
            new FahChatRequestDto { ConversationId = first.Response!.ConversationId, Message = "Second" }, null, default);

        Assert.False(second.Ok);
        Assert.Contains("still answering", second.Error);
    }

    // No key (say the person was just made an employee): the held change
    // stays held, untouched.
    [Fact]
    public async Task ARefusedKey_LeavesTheHeldChangeAlone()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", Change)]));
        var held = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead" }, null, default);
        _keyRepo.Members[("org-1", "usr-admin")] = new ActingMember("Employee", "admin@x.test");

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        }, null, default);

        Assert.False(result.Ok);
        var row = _conversations.Rows.Single();
        Assert.NotNull(row.PendingJson);
        Assert.Null(row.TurnStartedAt);
    }

    [Fact]
    public void TheCard_DropsTheBannerAndTheLinesMeantForTheModel()
    {
        const string preview = """
            PREVIEW ONLY — nothing has been created. New payroll run: November 2026.

            Would include 3 employees:
              • Outlet Staff: 3

            Confirming creates the draft and runs payroll for it. If the user agrees, call again with the same details, dryRun false and confirmToken:
            1793153709610.Zx5GZnG_mSaM3w4K2V3Ca6y2Mjt7G9Ln67WlDukgE00
            """;

        Assert.Equal(
            "New payroll run: November 2026.\n\nWould include 3 employees:\n  • Outlet Staff: 3\n\nConfirming creates the draft and runs payroll for it.",
            FahTools.ForCard(preview));
    }

    // Whatever the model writes, a tool FAH doesn't offer never reaches the server.
    [Fact]
    public async Task AToolThatIsntOffered_IsRefusedWithoutCallingTheServer()
    {
        _model.Script.Enqueue(Turn(tools: [("t1", "approve_payroll_run", """{"period":"latest"}""")]));
        _model.Script.Enqueue(Turn(text: "I can't approve payroll."));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Approve payroll" }, null, default);

        Assert.Empty(_mcp.Calls);
        Assert.False(Assert.Single(result.Response!.Steps).Ok);
    }

    // Full IC, SOCSO and bank numbers never enter the conversation.
    [Fact]
    public async Task Reveal_IsRemovedFromGetEmployee_InTheSchemaAndTheCall()
    {
        _mcp.Tools = ["get_employee"];
        _model.Script.Enqueue(Turn(tools: [("t1", "get_employee", """{"employee":"Aisyah","reveal":true}""")]));
        _model.Script.Enqueue(Turn(text: "Here she is."));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Show Aisyah's IC" }, null, default);

        var schema = Assert.Single(_model.OfferedTools).InputSchema;
        Assert.False(schema.GetProperty("properties").TryGetProperty("reveal", out _));
        var args = Assert.Single(_mcp.Calls).Arguments;
        Assert.False(args.TryGetProperty("reveal", out _));
        Assert.Equal("Aisyah", args.GetProperty("employee").GetString());
    }

    [Fact]
    public async Task TheConversation_IsSaved_AndContinues()
    {
        _model.Script.Enqueue(Turn(text: "First answer"));
        var first = await Service().ChatAsync(new FahChatRequestDto { Message = "First" }, null, default);
        _model.Script.Enqueue(Turn(text: "Second answer"));

        var second = await Service().ChatAsync(
            new FahChatRequestDto { ConversationId = first.Response!.ConversationId, Message = "Second" }, null, default);

        Assert.Equal(first.Response.ConversationId, second.Response!.ConversationId);
        var saved = Assert.Single(_conversations.Rows);
        Assert.Equal("First", saved.Title);
        // user, assistant, user, assistant — exactly as exchanged.
        Assert.Equal(4, JsonSerializer.Deserialize<List<JsonElement>>(saved.MessagesJson)!.Count);
        Assert.Equal(3, _model.Seen[1].Count);   // the second turn saw the first in full
    }

    [Fact]
    public async Task SomeoneElsesConversation_IsNotFound()
    {
        _conversations.Rows.Add(new FahConversation { Id = "c-other", UserId = "usr-someone-else", OrganizationId = "org-1" });

        var result = await Service().ChatAsync(new FahChatRequestDto { ConversationId = "c-other", Message = "Hi" }, null, default);

        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task AModelThatKeepsLookingThingsUp_IsStopped()
    {
        for (var i = 0; i < 10; i++) _model.Script.Enqueue(Turn(tools: [($"t{i}", "who_is_on_leave", "{}")]));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Loop" }, null, default);

        Assert.True(result.Ok);
        Assert.Contains("narrow", result.Response!.Reply);
        Assert.Equal(4, _model.Seen.Count);   // MaxToolRounds = 3, plus the first
        // Every tool call has its answer, so the next turn is a valid conversation.
        var saved = JsonSerializer.Deserialize<List<JsonElement>>(_conversations.Rows.Single().MessagesJson)!;
        Assert.Equal("tool_result", saved[^1].GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task WhenTheMcpServerIsDown_TheTurnFails_AndTheKeyIsStillEnded()
    {
        _mcp.Down = true;

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Who's on leave?" }, null, default);

        Assert.True(result.Unavailable);
        Assert.False(Assert.Single(_keyRepo.Keys).Active);
    }

    // Stored messages must replay through the SDK byte-for-byte in meaning:
    // signed thinking blocks are rejected if anything about them changes.
    [Fact]
    public void AStoredAssistantMessage_RoundTripsThroughTheSdk()
    {
        const string stored = """
            {"role":"assistant","content":[
              {"type":"thinking","thinking":"","signature":"sig-abc"},
              {"type":"text","text":"Let me check."},
              {"type":"tool_use","id":"toolu_1","name":"who_is_on_leave","input":{"date":"2026-10-28"}}
            ]}
            """;

        var param = JsonSerializer.Deserialize<BetaMessageParam>(stored)!;
        using var back = JsonDocument.Parse(JsonSerializer.Serialize(param));

        var content = back.RootElement.GetProperty("content");
        Assert.Equal("assistant", back.RootElement.GetProperty("role").GetString());
        Assert.Equal("sig-abc", content[0].GetProperty("signature").GetString());
        Assert.Equal("toolu_1", content[2].GetProperty("id").GetString());
        Assert.Equal("2026-10-28", content[2].GetProperty("input").GetProperty("date").GetString());
    }

    [Fact]
    public void AnMcpToolSchema_KeepsItsShapeAsAClaudeToolSchema()
    {
        const string schema = """
            {"type":"object","properties":{"employee":{"type":"string"}},"required":["employee"],"additionalProperties":false}
            """;

        var parsed = JsonSerializer.Deserialize<InputSchema>(schema)!;
        using var back = JsonDocument.Parse(JsonSerializer.Serialize(parsed));

        Assert.Equal("employee", back.RootElement.GetProperty("required")[0].GetString());
        Assert.False(back.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    // ─── Fakes ──────────────────────────────────────────────────────────

    private static FahModelTurn Turn(string text = "", (string Id, string Name, string Input)[]? tools = null)
    {
        var uses = (tools ?? []).Select(t => new FahToolUse(t.Id, t.Name, JsonDocument.Parse(t.Input).RootElement.Clone())).ToList();
        var content = new List<object>();
        if (text.Length > 0) content.Add(new { type = "text", text });
        content.AddRange(uses.Select(u => (object)new { type = "tool_use", id = u.Id, name = u.Name, input = u.Input }));
        return new FahModelTurn(
            JsonSerializer.SerializeToElement(new { role = "assistant", content }),
            uses.Count > 0 ? "tool_use" : "end_turn",
            uses,
            text,
            100,
            20);
    }

    private sealed class ScriptedModel : IFahModel
    {
        public bool IsConfigured => true;
        public Queue<FahModelTurn> Script { get; } = new();
        // Thrown on the next call instead of answering, once.
        public Exception? FailNext { get; set; }
        public List<List<JsonElement>> Seen { get; } = [];
        public IReadOnlyList<FahTool> OfferedTools { get; private set; } = [];

        public Task<FahModelTurn> NextAsync(string system, IReadOnlyList<FahTool> tools, IReadOnlyList<JsonElement> messages, CancellationToken ct)
        {
            OfferedTools = tools;
            Seen.Add([.. messages]);
            if (FailNext is { } failure)
            {
                FailNext = null;
                throw failure;
            }
            return Task.FromResult(Script.Dequeue());
        }
    }

    private sealed class FakeMcp : IMcpClient
    {
        public string[] Tools { get; set; } = ["who_is_on_leave", "get_employee", "update_employee"];
        public bool Down { get; set; }
        public int FailCallsAfter { get; set; } = int.MaxValue;
        // What the failing call throws: an MCP outage by default.
        public Exception Failure { get; set; } = new McpUnavailableException("down");
        public List<(string Token, string Name, JsonElement Arguments)> Calls { get; } = [];

        public Task<string> GetInstructionsAsync(string token, CancellationToken ct) =>
            Down ? throw new McpUnavailableException("down") : Task.FromResult("Use the tools.");

        public Task<IReadOnlyList<McpTool>> ListToolsAsync(string token, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<McpTool>>([.. Tools.Select(n => new McpTool(n, $"{n} tool", JsonDocument.Parse(
                n == "get_employee"
                    ? """{"type":"object","properties":{"employee":{"type":"string"},"reveal":{"type":"boolean"}}}"""
                    : """{"type":"object","properties":{}}""").RootElement.Clone()))]);

        public Task<McpToolResult> CallToolAsync(string token, string name, JsonElement arguments, CancellationToken ct)
        {
            if (Calls.Count >= FailCallsAfter) throw Failure;
            Calls.Add((token, name, arguments));
            var preview = name == "update_employee" && !(arguments.TryGetProperty("dryRun", out var d) && d.ValueKind == JsonValueKind.False);
            return Task.FromResult(new McpToolResult(
                preview
                    ? "PREVIEW ONLY — nothing has been changed. Fay Staff (DT401):\n  Job title: Outlet Staff → Lead\n\nShow the user this and ask them to confirm. If they agree, call again with the same details, dryRun false and the confirmToken (pass it silently):\n1793153709514.eUgBitb4qFy4NP_ZY8fpILyRVBu6O84w7aOjBZxgyaM"
                    : $"{name} result",
                false));
        }
    }

    private sealed class FakeConversations : IFahRepository
    {
        public List<FahConversation> Rows { get; } = [];

        public Task<FahConversation?> GetAsync(string id, string userId) =>
            Task.FromResult(Rows.FirstOrDefault(c => c.Id == id && c.UserId == userId));

        public Task<List<FahConversation>> ListAsync(string userId, int take) =>
            Task.FromResult(Rows.Where(c => c.UserId == userId).OrderByDescending(c => c.UpdatedAt).Take(take).ToList());

        public Task<bool> TryStartTurnAsync(string id, string userId)
        {
            var row = Rows.FirstOrDefault(c => c.Id == id && c.UserId == userId && c.TurnStartedAt == null);
            if (row is null) return Task.FromResult(false);
            row.TurnStartedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task EndTurnAsync(string id)
        {
            foreach (var row in Rows.Where(c => c.Id == id)) row.TurnStartedAt = null;
            return Task.CompletedTask;
        }

        // Like the real one: takes the held change only if it is still there.
        public Task<bool> TryClaimPendingAsync(string id, string userId, string actionId)
        {
            var row = Rows.FirstOrDefault(c => c.Id == id && c.UserId == userId && c.PendingJson?.Contains(actionId) == true);
            if (row is null) return Task.FromResult(false);
            row.PendingJson = null;
            return Task.FromResult(true);
        }

        public Task AddAsync(FahConversation conversation)
        {
            conversation.OrganizationId = "org-1";
            Rows.Add(conversation);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(FahConversation conversation) => Task.CompletedTask;
    }
}
