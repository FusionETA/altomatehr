using System.Text.Json;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
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
    private readonly FakeAttachments _attachments = new();
    private readonly FakeImport _import = new();
    private readonly FakeCompanyChanges _companyChanges = new();
    private readonly StubAuth _auth = new();

    public FahServiceTests()
    {
        _keyRepo.Members[("org-1", "usr-admin")] = new ActingMember("Admin", "admin@x.test");
    }

    private FahService Service() => new(
        _conversations,
        _attachments,
        _import,
        _companyChanges,
        _auth,
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
        _import.Allowed = false;   // MCP tools only
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
        _import.Allowed = false;   // MCP tools only
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

    // ─── Attached files ─────────────────────────────────────────────────

    private static byte[] Png(int width = 800, int height = 600)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        return b;
    }

    private async Task<string> Upload(string name, byte[] content)
    {
        var result = await Service().UploadAsync(name, content, default);
        return Assert.IsType<FahAttachmentDto>(result.Attachment).Id;
    }

    [Fact]
    public async Task AnImage_IsStoredWithTheModel_AndGoesBeforeTheQuestion()
    {
        var id = await Upload("form.png", Png());
        _model.Script.Enqueue(Turn(text: "It's a leave form."));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "What's this?", AttachmentIds = [id] }, null, default);

        Assert.True(result.Ok);
        Assert.Equal(["form.png"], _model.Uploaded);
        var content = _model.Seen[0][^1].GetProperty("content");
        Assert.Equal("[Attached file: form.png]", content[0].GetProperty("text").GetString());
        Assert.Equal("image", content[1].GetProperty("type").GetString());
        Assert.Equal("file_1", content[1].GetProperty("source").GetProperty("file_id").GetString());
        Assert.EndsWith("What's this?", content[2].GetProperty("text").GetString());

        // Sent: tied to the chat, counted, and the chat stops before the file is deleted.
        var chat = _conversations.Rows.Single();
        Assert.Equal(chat.Id, _attachments.Rows.Single().ConversationId);
        Assert.Equal(1, chat.AttachmentCount);
        Assert.True(chat.FilesExpireAt < DateTime.UtcNow + FahFiles.KeepFor);

        var shown = (await Service().GetAsync(chat.Id))!.Messages[0];
        Assert.Equal(["form.png"], shown.Attachments);
        Assert.Equal("What's this?", shown.Text);
    }

    [Fact]
    public async Task ASpreadsheet_IsReadIntoText_AndNeverStoredWithTheModel()
    {
        var id = await Upload("staff.csv", System.Text.Encoding.UTF8.GetBytes("Name,IC\nAisyah,900101015555\n"));
        _model.Script.Enqueue(Turn(text: "One person."));

        await Service().ChatAsync(new FahChatRequestDto { Message = "", AttachmentIds = [id] }, null, default);

        Assert.Empty(_model.Uploaded);
        var document = _model.Seen[0][^1].GetProperty("content")[1];
        Assert.Equal("document", document.GetProperty("type").GetString());
        Assert.Equal("staff.csv", document.GetProperty("title").GetString());
        Assert.Contains("Aisyah,900101015555", document.GetProperty("source").GetProperty("data").GetString());
        // Files without a message still make a chat, named after them — and a
        // chat with no image or PDF never expires.
        var chat = _conversations.Rows.Single();
        Assert.Equal("Files: staff.csv", chat.Title);
        Assert.Null(chat.FilesExpireAt);
    }

    [Fact]
    public async Task AStoredMessageWithFiles_ReplaysThroughTheSdk()
    {
        var image = await Upload("form.png", Png());
        var sheet = await Upload("staff.csv", "Name\nAisyah\n"u8.ToArray());
        _model.Script.Enqueue(Turn(text: "Done."));
        await Service().ChatAsync(new FahChatRequestDto { Message = "Check", AttachmentIds = [image, sheet] }, null, default);

        var param = JsonSerializer.Deserialize<BetaMessageParam>(_model.Seen[0][^1].GetRawText())!;
        using var back = JsonDocument.Parse(JsonSerializer.Serialize(param));

        var content = back.RootElement.GetProperty("content");
        Assert.Equal("file_1", content[1].GetProperty("source").GetProperty("file_id").GetString());
        Assert.Equal("text", content[3].GetProperty("source").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SomeoneElsesFile_OrOneAlreadySent_IsRefused_BeforeAnythingHappens()
    {
        _attachments.Rows.Add(new FahAttachment { Id = "a-other", UserId = "usr-someone-else", FileName = "x.png", CreatedAt = DateTime.UtcNow });

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Hi", AttachmentIds = ["a-other"] }, null, default);

        Assert.False(result.Ok);
        Assert.True(result.NotSent);
        Assert.Empty(_model.Seen);
        Assert.Empty(_keyRepo.Keys);
    }

    [Fact]
    public async Task AChat_TakesAtMostTenFiles()
    {
        _conversations.Rows.Add(new FahConversation
        {
            Id = "c-1", UserId = "usr-admin", OrganizationId = "org-1", AttachmentCount = FahFiles.MaxPerChat,
        });
        var id = await Upload("one-more.png", Png());

        var result = await Service().ChatAsync(new FahChatRequestDto { ConversationId = "c-1", Message = "And this", AttachmentIds = [id] }, null, default);

        Assert.True(result.NotSent);
        Assert.Contains("Start a new chat", result.Error);
        Assert.Null(_attachments.Rows.Single().ConversationId);
    }

    [Fact]
    public async Task AChatWhoseFilesWereDeleted_CannotContinue()
    {
        _conversations.Rows.Add(new FahConversation
        {
            Id = "c-1", UserId = "usr-admin", OrganizationId = "org-1", FilesExpireAt = DateTime.UtcNow.AddMinutes(-1),
        });

        var result = await Service().ChatAsync(new FahChatRequestDto { ConversationId = "c-1", Message = "Hi again" }, null, default);

        Assert.Contains("30 days", result.Error);
        Assert.Empty(_model.Seen);
        Assert.True((await Service().GetAsync("c-1"))!.FilesExpired);
    }

    // A file the model won't take would break every later message in the
    // chat, so the message comes back out and its files stay unsent.
    [Fact]
    public async Task AMessageTheModelRejects_IsTakenBackOut_WithItsFiles()
    {
        _model.Script.Enqueue(Turn(text: "First answer"));
        var first = await Service().ChatAsync(new FahChatRequestDto { Message = "First" }, null, default);
        var id = await Upload("odd.png", Png());
        _model.FailNext = new FahModelException("bad request", rejected: true);

        var result = await Service().ChatAsync(
            new FahChatRequestDto { ConversationId = first.Response!.ConversationId, Message = "What's this?", AttachmentIds = [id] }, null, default);

        Assert.True(result.NotSent);
        var chat = _conversations.Rows.Single();
        Assert.Equal(2, JsonSerializer.Deserialize<List<JsonElement>>(chat.MessagesJson)!.Count);
        Assert.Equal(0, chat.AttachmentCount);
        Assert.Null(chat.FilesExpireAt);
        Assert.Null(chat.TurnStartedAt);
        Assert.Null(_attachments.Rows.Single().ConversationId);
    }

    [Fact]
    public async Task ANewChatWhoseOnlyMessageIsRejected_IsNotKept()
    {
        var id = await Upload("odd.png", Png());
        _model.FailNext = new FahModelException("bad request", rejected: true);

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "What's this?", AttachmentIds = [id] }, null, default);

        Assert.True(result.NotSent);
        Assert.Empty(_conversations.Rows);
    }

    [Fact]
    public async Task RemovingAnUnsentFile_DeletesItFromTheModel()
    {
        var id = await Upload("form.png", Png());

        Assert.True(await Service().RemoveAttachmentAsync(id));

        Assert.Equal(["file_1"], _model.Deleted);
        Assert.Empty(_attachments.Rows);
    }

    [Fact]
    public async Task AFileFahCantRead_IsRefusedAtUpload()
    {
        var result = await Service().UploadAsync("notes.pages", [1, 2, 3], default);

        Assert.Null(result.Attachment);
        Assert.Contains("can't read notes.pages", result.Error);
        Assert.Empty(_model.Uploaded);
    }

    // ─── Importing a spreadsheet ────────────────────────────────────────

    private async Task<FahResult> ImportTurn()
    {
        var id = await Upload("staff.csv", "Email,Name\na@x.test,A\nb@x.test,\n"u8.ToArray());
        _model.Script.Enqueue(Turn(tools: [("t1", IFahImportService.ToolName, """{"file":"staff.csv","columns":{"email":"Email"}}""")]));
        _model.Script.Enqueue(Turn(text: "1 to add; row 3 needs a name."));
        return await Service().ChatAsync(new FahChatRequestDto { Message = "Add these", AttachmentIds = [id] }, null, default);
    }

    // From the first message, so FAH can say up front that a file of people
    // goes in with one Confirm — not promise to add them one by one.
    [Fact]
    public async Task TheImport_IsOfferedFromTheStart_AndReadsTheSpreadsheetSentWithTheMessage()
    {
        _model.Script.Enqueue(Turn(text: "Hi"));
        await Service().ChatAsync(new FahChatRequestDto { Message = "Hello" }, null, default);
        Assert.Contains(_model.OfferedTools, t => t.Name == IFahImportService.ToolName);

        await ImportTurn();
        Assert.Contains(_model.OfferedTools, t => t.Name == IFahImportService.ToolName);
        // The spreadsheet sent with this very message is the one it reads.
        Assert.Equal("staff.csv", Assert.Single(Assert.Single(_import.Prepared)).FileName);
    }

    [Fact]
    public async Task AnAdminWhoCantImport_IsNotOfferedIt()
    {
        _import.Allowed = false;
        var id = await Upload("staff.csv", "Email,Name\na@x.test,A\n"u8.ToArray());
        _model.Script.Enqueue(Turn(text: "I can't import here."));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Add these", AttachmentIds = [id] }, null, default);

        Assert.DoesNotContain(_model.OfferedTools, t => t.Name == IFahImportService.ToolName);
    }

    // FAH hears the check at once, so it can explain the card; the import
    // itself waits for Confirm.
    [Fact]
    public async Task ThePreparedImport_IsHeldAsOneCard_AndExplainedStraightAway()
    {
        var result = await ImportTurn();

        Assert.Equal("1 to add; row 3 needs a name.", result.Response!.Reply);
        var card = result.Response.PendingAction!;
        Assert.Equal("Import employees", card.Title);
        Assert.Equal(1, card.Import!.Adds);
        Assert.Equal(1, card.Import.Problems);
        Assert.Equal(["Name", "Email"], card.Import.Columns);
        var answer = _model.Seen[1][^1].GetProperty("content")[0];
        Assert.Equal("tool_result", answer.GetProperty("type").GetString());
        Assert.Contains("Row 3: Name is required.", answer.GetProperty("content").GetString());
        Assert.Empty(_import.Ran);
    }

    [Fact]
    public async Task ConfirmingTheCard_RunsTheImport_AndTellsFah()
    {
        var held = await ImportTurn();
        _model.Script.Enqueue(Turn(text: "Done — 1 added."));

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        }, null, default);

        Assert.Equal("made", result.Response!.ChangeOutcome);
        Assert.Single(_import.Ran);
        var told = _model.Seen[^1][^1];
        Assert.Equal("user", told.GetProperty("role").GetString());
        Assert.Contains("pressed Confirm on the import card", told.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task CancellingTheCard_ImportsNothing()
    {
        var held = await ImportTurn();
        _model.Script.Enqueue(Turn(text: "OK, nothing imported."));

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = false,
        }, null, default);

        Assert.Equal("cancelled", result.Response!.ChangeOutcome);
        Assert.Empty(_import.Ran);
    }

    [Fact]
    public async Task MovingOnFromTheCard_DropsIt_AndSaysSo()
    {
        var held = await ImportTurn();
        _model.Script.Enqueue(Turn(text: "Sure."));

        await Service().ChatAsync(new FahChatRequestDto { ConversationId = held.Response!.ConversationId, Message = "Actually wait" }, null, default);

        Assert.Null(_conversations.Rows.Single().PendingJson);
        var text = _model.Seen[^1][^1].GetProperty("content")[0].GetProperty("text").GetString();
        Assert.StartsWith("(The admin sent a new message instead of confirming the card.", text);
        // The note is FAH's, not something the admin typed.
        var shown = (await Service().GetAsync(held.Response.ConversationId))!.Messages.Last(m => m.Role == "user");
        Assert.Equal("Actually wait", shown.Text);
    }

    [Fact]
    public async Task TheImportFile_CanBeDownloaded_WhileTheCardWaits()
    {
        var held = await ImportTurn();

        var file = await Service().ImportFileAsync(held.Response!.ConversationId, held.Response.PendingAction!.Id);

        Assert.Equal("employees-import-staff.xlsx", file!.FileName);
        Assert.Null(await Service().ImportFileAsync(held.Response.ConversationId, "some-other-card"));
    }

    // ─── Across companies ───────────────────────────────────────────────

    private void TwoMoreCompanies()
    {
        _auth.Orgs.AddRange([
            new UserOrgDto("org-2", "Second Co", "Owner"),
            new UserOrgDto("org-3", "Third Co", "Admin"),
            new UserOrgDto("org-4", "Staff Only Co", "Employee"),
            new UserOrgDto("org-5", "Left Co", "Admin", IsFormer: true),
        ]);
        _keyRepo.Members[("org-2", "usr-admin")] = new ActingMember("Owner", "admin@x.test");
        _keyRepo.Members[("org-3", "usr-admin")] = new ActingMember("Admin", "admin@x.test");
    }

    [Fact]
    public async Task WithOneCompany_NothingAboutCompaniesIsOffered()
    {
        _model.Script.Enqueue(Turn(text: "Hi"));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Hello" }, null, default);

        Assert.All(_model.OfferedTools, t => Assert.False(
            t.InputSchema.TryGetProperty("properties", out var p) && p.TryGetProperty("company", out _)));
        Assert.DoesNotContain("companies on AltomateHR", _model.Systems.Single());
    }

    [Fact]
    public async Task ALookupInAnotherCompany_UsesAKeyForThatCompany_AndSaysWhichItIs()
    {
        TwoMoreCompanies();
        _model.Script.Enqueue(Turn(tools: [("t1", "who_is_on_leave", """{"company":"second co"}""")]));
        _model.Script.Enqueue(Turn(text: "Nobody."));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Who's away at Second Co?" }, null, default);

        // Offered with a company to pick, and told which ones are theirs.
        Assert.True(_model.OfferedTools.Single(t => t.Name == "who_is_on_leave")
            .InputSchema.GetProperty("properties").TryGetProperty("company", out _));
        var system = _model.Systems[0];
        Assert.Contains("- Second Co: Owner", system);
        Assert.Contains("- Third Co: Admin", system);
        Assert.DoesNotContain("Staff Only Co", system);   // only staff there
        Assert.DoesNotContain("Left Co", system);         // a company they've left

        var call = Assert.Single(_mcp.Calls);
        var key = _keyRepo.Keys.Single(k => ApiTokenGenerator.HashToken(call.Token) == k.TokenHash);
        Assert.Equal("org-2", key.OrganizationId);
        Assert.False(call.Arguments.TryGetProperty("company", out _));   // FAH's, never sent on
        Assert.StartsWith("[Second Co]", _model.Seen[1][^1].GetProperty("content")[0].GetProperty("content").GetString());
        // Every key the turn made is ended with it.
        Assert.All(_keyRepo.Keys, k => Assert.False(k.Active));
    }

    [Theory]
    [InlineData("Staff Only Co")]
    [InlineData("Left Co")]
    [InlineData("Someone Else's Co")]
    public async Task ACompanyThatIsntTheirsToAdminister_IsRefused(string company)
    {
        TwoMoreCompanies();
        _model.Script.Enqueue(Turn(tools: [("t1", "who_is_on_leave", $$"""{"company":"{{company}}"}""")]));
        _model.Script.Enqueue(Turn(text: "Can't."));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Who's away?" }, null, default);

        Assert.Empty(_mcp.Calls);
        var answer = _model.Seen[1][^1].GetProperty("content")[0];
        Assert.True(answer.GetProperty("is_error").GetBoolean());
        Assert.Contains("isn't one of the admin's companies", answer.GetProperty("content").GetString());
    }

    [Fact]
    public async Task TwoCompaniesWithTheSameName_AreToldApartById_NeverGuessed()
    {
        TwoMoreCompanies();
        _auth.Orgs.Add(new UserOrgDto("org-6", "Second Co", "Admin"));
        _keyRepo.Members[("org-6", "usr-admin")] = new ActingMember("Admin", "admin@x.test");
        _model.Script.Enqueue(Turn(tools: [
            ("t1", "who_is_on_leave", """{"company":"Second Co"}"""),
            ("t2", "who_is_on_leave", """{"company":"org-6"}"""),
        ]));
        _model.Script.Enqueue(Turn(text: "Done."));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Who's away at Second Co?" }, null, default);

        Assert.Contains("- Second Co (id org-6): Admin", _model.Systems[0]);
        var answers = _model.Seen[1][^1].GetProperty("content");
        Assert.True(answers[0].GetProperty("is_error").GetBoolean());   // by the shared name: which one?
        var call = Assert.Single(_mcp.Calls);                             // by id: that one
        Assert.Equal("org-6", _keyRepo.Keys.Single(k => ApiTokenGenerator.HashToken(call.Token) == k.TokenHash).OrganizationId);
    }

    [Fact]
    public async Task AChangeInAnotherCompany_IsRefused_NothingPreviewed()
    {
        TwoMoreCompanies();
        _model.Script.Enqueue(Turn(tools: [("t1", "update_employee", """{"employee":"Fay","jobTitle":"Lead","dryRun":false,"confirmToken":"tok","company":"Second Co"}""")]));
        _model.Script.Enqueue(Turn(text: "Switch first."));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Make Fay a lead at Second Co" }, null, default);

        Assert.Null(result.Response!.PendingAction);
        Assert.Empty(_mcp.Calls);
        Assert.Contains("switch to Second Co", _model.Seen[1][^1].GetProperty("content")[0].GetProperty("content").GetString());
    }

    // ─── Changes across companies ───────────────────────────────────────

    private void OwnerOfTwo()
    {
        TwoMoreCompanies();
        _auth.Orgs[0] = _auth.Orgs[0] with { Role = "Owner" };
        _keyRepo.Members[("org-1", "usr-admin")] = new ActingMember("Owner", "admin@x.test");
    }

    private async Task<FahResult> CopyTurn(string input = """{"policy":"Full-time","to":["all"]}""")
    {
        _model.Script.Enqueue(Turn(tools: [("t1", IFahCompanyChanges.CopyPolicyTool, input)]));
        _model.Script.Enqueue(Turn(text: "Second Co gets it; Third Co is skipped."));
        return await Service().ChatAsync(new FahChatRequestDto { Message = "Copy Full-time to my other companies" }, null, default);
    }

    [Fact]
    public async Task AnOwnerOfSeveralCompanies_IsOfferedTheChangesAcrossThem()
    {
        OwnerOfTwo();
        _model.Script.Enqueue(Turn(text: "Hi"));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Hello" }, null, default);

        Assert.Contains(_model.OfferedTools, t => t.Name == IFahCompanyChanges.CopyPolicyTool);
        Assert.Contains(_model.OfferedTools, t => t.Name == IFahCompanyChanges.AddAdminTool);
        Assert.Contains("The exception: four changes can be made across the companies they own", _model.Systems[0]);
    }

    [Fact]
    public async Task SomeoneWhoOwnsOnlyOne_IsNotOfferedThem()
    {
        TwoMoreCompanies();   // owns Second Co; only an admin in First Co
        _model.Script.Enqueue(Turn(text: "Hi"));

        await Service().ChatAsync(new FahChatRequestDto { Message = "Hello" }, null, default);

        Assert.DoesNotContain(_model.OfferedTools, t => IFahCompanyChanges.Handles(t.Name));
        Assert.DoesNotContain("The exception", _model.Systems[0]);
    }

    // One card for every company — what happens in each, and which are
    // skipped — and FAH hears the same at once, so it can explain it.
    [Fact]
    public async Task AChangeAcrossCompanies_IsHeldAsOneCard_ListingEachCompany()
    {
        OwnerOfTwo();

        var result = await CopyTurn();

        var card = result.Response!.PendingAction!;
        Assert.Equal("Copy policy \"Full-time\"", card.Title);
        Assert.Equal("From First Co, in 1 company (1 skipped).", card.Preview);
        Assert.Collection(card.Companies!,
            r => Assert.Equal(("Second Co", false), (r.Company, r.Skip)),
            r => Assert.Equal(("Third Co", true, "You're Admin here, not the owner."), (r.Company, r.Skip, r.Detail)));
        var answer = _model.Seen[1][^1].GetProperty("content")[0];
        Assert.False(answer.GetProperty("is_error").GetBoolean());
        Assert.Contains("- Third Co: skipped — You're Admin here, not the owner.", answer.GetProperty("content").GetString());
        Assert.Empty(_companyChanges.Ran);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task ConfirmingIt_MakesTheChange_AndTellsFahWhatHappenedInEach()
    {
        OwnerOfTwo();
        var held = await CopyTurn();
        _model.Script.Enqueue(Turn(text: "Done in Second Co."));

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = true,
        }, null, default);

        Assert.Equal("made", result.Response!.ChangeOutcome);
        Assert.Equal("org-2", Assert.Single(_companyChanges.Ran).Steps[0].OrganizationId);
        var told = _model.Seen[^1][^1].GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("pressed Confirm on the card", told);
        Assert.Contains("- Second Co: done.", told);
    }

    [Fact]
    public async Task CancellingIt_ChangesNothing()
    {
        OwnerOfTwo();
        var held = await CopyTurn();
        _model.Script.Enqueue(Turn(text: "OK."));

        var result = await Service().ConfirmAsync(new FahConfirmRequestDto
        {
            ConversationId = held.Response!.ConversationId, ActionId = held.Response.PendingAction!.Id, Approve = false,
        }, null, default);

        Assert.Equal("cancelled", result.Response!.ChangeOutcome);
        Assert.Empty(_companyChanges.Ran);
    }

    [Fact]
    public async Task ARequestThatCantBeDone_IsExplainedToFah_WithNoCard()
    {
        OwnerOfTwo();

        var result = await CopyTurn("""{"policy":"Nope","to":["all"]}""");

        Assert.Null(result.Response!.PendingAction);
        var answer = _model.Seen[1][^1].GetProperty("content")[0];
        Assert.True(answer.GetProperty("is_error").GetBoolean());
        Assert.Contains("no policy named", answer.GetProperty("content").GetString());
    }

    // Two cards can't wait at once: the second is refused, the first kept.
    [Fact]
    public async Task OneCardAtATime_WhateverKind()
    {
        OwnerOfTwo();
        _model.Script.Enqueue(Turn(tools: [
            ("t1", IFahCompanyChanges.CopyPolicyTool, """{"policy":"Full-time","to":["all"]}"""),
            ("t2", IFahCompanyChanges.AddAdminTool, """{"email":"fay@x.test","to":["all"]}"""),
        ]));
        _model.Script.Enqueue(Turn(text: "The policy first."));

        var result = await Service().ChatAsync(new FahChatRequestDto { Message = "Copy it and add Fay" }, null, default);

        Assert.Equal(IFahCompanyChanges.CopyPolicyTool, result.Response!.PendingAction!.Tool);
        var answers = _model.Seen[1][^1].GetProperty("content");
        Assert.True(answers[1].GetProperty("is_error").GetBoolean());
        Assert.Contains("one change at a time", answers[1].GetProperty("content").GetString());
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

        public List<string> Systems { get; } = [];

        public Task<FahModelTurn> NextAsync(string system, IReadOnlyList<FahTool> tools, IReadOnlyList<JsonElement> messages, CancellationToken ct)
        {
            OfferedTools = tools;
            Systems.Add(system);
            Seen.Add([.. messages]);
            if (FailNext is { } failure)
            {
                FailNext = null;
                throw failure;
            }
            return Task.FromResult(Script.Dequeue());
        }

        public List<string> Uploaded { get; } = [];
        public List<string> Deleted { get; } = [];

        public Task<string> UploadFileAsync(string fileName, string mediaType, byte[] content, TimeSpan keepFor, CancellationToken ct)
        {
            Uploaded.Add(fileName);
            return Task.FromResult($"file_{Uploaded.Count}");
        }

        public Task DeleteFileAsync(string fileId, CancellationToken ct)
        {
            Deleted.Add(fileId);
            return Task.CompletedTask;
        }
    }

    // The import's own behaviour is FahImportServiceTests'; here only how FAH
    // offers it, holds its card and hands over Confirm.
    // The companies the admin can switch into: the signed-in one, plus any a test adds.
    private sealed class StubAuth : IAuthService
    {
        public List<UserOrgDto> Orgs { get; } = [new("org-1", "First Co", "Admin")];

        public Task<IReadOnlyList<UserOrgDto>> GetOrgsAsync(string userId) => Task.FromResult<IReadOnlyList<UserOrgDto>>(Orgs);

        public Task<AuthResult?> EnterSupportAsync(string userId, string organizationId, bool sso = false) => throw new NotSupportedException();
        public Task<AuthResult?> ExitSupportAsync(string userId, bool sso = false) => throw new NotSupportedException();
        public Task<AuthResult?> SwitchOrgAsync(string userId, string organizationId, bool sso = false) => throw new NotSupportedException();
        public Task<AltomateHR.Api.Modules.Auth.Entities.User?> VerifyPasswordAsync(string email, string password, string? clientIp = null) => throw new NotSupportedException();
        public Task<AuthResult?> LoginAsync(string email, string password, string? clientIp = null) => throw new NotSupportedException();
        public Task<AuthResult?> RefreshAsync(string refreshToken) => throw new NotSupportedException();
        public Task<LeaveOrgResult> LeaveFormerOrgAsync(string userId, string organizationId, string? activeOrganizationId, bool sso = false) => throw new NotSupportedException();
        public Task LogoutAsync(string refreshToken) => throw new NotSupportedException();
        public Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> ResetPasswordAsync(string email, string otp, string newPassword) => throw new NotSupportedException();
        public Task<string?> ChangePasswordAsync(string userId, string current, string next, bool viaSso = false) => throw new NotSupportedException();
    }

    private sealed class FakeCompanyChanges : IFahCompanyChanges
    {
        public List<FahCompanyPlan> Ran { get; } = [];

        // Offered, like the real one, to an owner of two companies or more.
        public IReadOnlyList<FahTool> Tools(IReadOnlyList<UserOrgDto> companies) =>
            companies.Count(c => c.Role == "Owner") < 2 ? [] :
            [
                new FahTool(IFahCompanyChanges.CopyPolicyTool, "copy", JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()),
                new FahTool(IFahCompanyChanges.AddAdminTool, "add", JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()),
            ];

        public Task<(FahCompanyPlan? Plan, string? Error)> PrepareAsync(
            string tool, JsonElement input, IReadOnlyList<UserOrgDto> companies) =>
            Task.FromResult<(FahCompanyPlan?, string?)>(input.TryGetProperty("policy", out var p) && p.GetString() == "Nope"
                ? (null, "First Co has no policy named \"Nope\".")
                : (new FahCompanyPlan(tool, "Copy policy \"Full-time\"", "org-1", "First Co",
                [
                    new FahCompanyStep("org-2", "Second Co", false, "New policy."),
                    new FahCompanyStep("org-3", "Third Co", true, "You're Admin here, not the owner."),
                ]), null));

        public Task<(string Text, bool Made)> RunAsync(FahCompanyPlan plan)
        {
            Ran.Add(plan);
            return Task.FromResult(("Copy policy \"Full-time\", from First Co:\n- Second Co: done.", true));
        }
    }

    private sealed class FakeImport : IFahImportService
    {
        public bool Allowed { get; set; } = true;
        public List<IReadOnlyList<FahAttachment>> Prepared { get; } = [];
        public List<FahImportPlan> Ran { get; } = [];

        public Task<FahTool?> ToolAsync() => Task.FromResult<FahTool?>(Allowed
            ? new FahTool(IFahImportService.ToolName, "import", JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone())
            : null);

        public Task<(FahImportPlan? Plan, string? Error)> PrepareAsync(IReadOnlyList<FahAttachment> sheets, JsonElement input)
        {
            Prepared.Add(sheets);
            return Task.FromResult<(FahImportPlan?, string?)>((new FahImportPlan(
                "staff.csv",
                ["email", "name"],
                [
                    new FahImportRow(2, new() { ["email"] = "a@x.test", ["name"] = "A" }, FahImportRowStatus.Add),
                    new FahImportRow(3, new() { ["email"] = "b@x.test" }, FahImportRowStatus.Problem, "Name is required."),
                ],
                1, 0), null));
        }

        public Task<(string Text, bool Saved)> RunAsync(FahImportPlan plan)
        {
            Ran.Add(plan);
            return Task.FromResult(("Imported from staff.csv: 1 added, 0 updated.", true));
        }

        public Task<TabularExportResult> BuildFileAsync(FahImportPlan plan) =>
            Task.FromResult(new TabularExportResult([1, 2, 3], "employees-import-staff.xlsx", "application/xlsx"));
    }

    private sealed class FakeAttachments : IFahAttachmentRepository
    {
        public List<FahAttachment> Rows { get; } = [];

        public Task AddAsync(FahAttachment attachment)
        {
            attachment.OrganizationId = "org-1";
            Rows.Add(attachment);
            return Task.CompletedTask;
        }

        public Task<List<FahAttachment>> GetUnsentAsync(IReadOnlyCollection<string> ids, string userId) =>
            Task.FromResult(Rows.Where(a => ids.Contains(a.Id) && a.UserId == userId && a.ConversationId == null).ToList());

        public Task<List<FahAttachment>> GetSpreadsheetsAsync(string conversationId, string userId) =>
            Task.FromResult(Rows.Where(a => a.ConversationId == conversationId && a.UserId == userId && a.TableJson != null).ToList());

        public Task MarkSentAsync(IReadOnlyCollection<string> ids, string conversationId)
        {
            foreach (var a in Rows.Where(a => ids.Contains(a.Id) && a.ConversationId == null)) a.ConversationId = conversationId;
            return Task.CompletedTask;
        }

        public Task<FahAttachment?> RemoveUnsentAsync(string id, string userId)
        {
            var row = Rows.FirstOrDefault(a => a.Id == id && a.UserId == userId && a.ConversationId == null);
            if (row is not null) Rows.Remove(row);
            return Task.FromResult(row);
        }

        public Task<List<FahAttachment>> PruneUnsentAsync(string userId, int keep, DateTime olderThan)
        {
            var gone = Rows.Where(a => a.UserId == userId && a.ConversationId == null)
                .OrderByDescending(a => a.CreatedAt)
                .Where((a, i) => i >= keep || a.CreatedAt < olderThan)
                .ToList();
            foreach (var a in gone) Rows.Remove(a);
            return Task.FromResult(gone);
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
