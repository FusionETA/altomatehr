using System.Net;
using System.Text.Json;
using AltomateHR.Api.Modules.TeamChat;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.TeamChat;

// Sign-ins and new companies go to the team's Bitrix group. No real Bitrix is
// called: the client is pointed at a recording handler.
public class TeamChatTests
{
    private static BitrixClient Client(RecordingHandler handler, string webhook = "https://x.bitrix24.com/rest/1/abc/", string dialog = "chat42") =>
        Client(handler, new TeamChatOptions { WebhookUrl = webhook, DialogId = dialog });

    private static BitrixClient Client(RecordingHandler handler, TeamChatOptions options) =>
        new(new HttpClient(handler), Options.Create(options));

    [Fact]
    public async Task AMessage_GoesToImMessageAdd_ForTheGroup()
    {
        var handler = new RecordingHandler("""{"result":123}""");

        await Client(handler).PostAsync(TeamChatChannel.Action, "hello");

        Assert.Equal("https://x.bitrix24.com/rest/1/abc/im.message.add.json", handler.Url);
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("chat42", body.GetProperty("DIALOG_ID").GetString());
        Assert.Equal("hello", body.GetProperty("MESSAGE").GetString());
    }

    [Theory]
    [InlineData("", "chat42")]
    [InlineData("https://x.bitrix24.com/rest/1/abc/", "")]
    public async Task WithoutAWebhookAndAChat_NothingIsSent(string webhook, string dialog)
    {
        var handler = new RecordingHandler("{}");

        await Client(handler, webhook, dialog).PostAsync(TeamChatChannel.Action, "hello");

        Assert.Null(handler.Url);
    }

    // Each channel has its own chat; one not set falls back to the old single chat.
    [Fact]
    public async Task EachChannel_PostsToItsOwnChat_OrTheFallback()
    {
        var options = new TeamChatOptions
        {
            WebhookUrl = "https://x.bitrix24.com/rest/1/abc/",
            DialogId = "chat1",
            AlertDialogId = "chat2",
            DailyDialogId = " chat3 ",
        };

        Assert.Equal("chat1", options.DialogFor(TeamChatChannel.Action));
        Assert.Equal("chat2", options.DialogFor(TeamChatChannel.Alert));
        Assert.Equal("chat3", options.DialogFor(TeamChatChannel.Daily));

        var handler = new RecordingHandler("""{"result":1}""");
        await Client(handler, options).PostAsync(TeamChatChannel.Alert, "hi");
        Assert.Equal("chat2", JsonDocument.Parse(handler.Body!).RootElement.GetProperty("DIALOG_ID").GetString());
    }

    [Fact]
    public async Task AChannelWithNoChat_PostsNothing()
    {
        var options = new TeamChatOptions { WebhookUrl = "https://x.bitrix24.com/rest/1/abc/", AlertDialogId = "chat2" };
        var handler = new RecordingHandler("{}");

        await Client(handler, options).PostAsync(TeamChatChannel.Daily, "hi");

        Assert.Null(handler.Url);
        Assert.False(options.IsConfigured(TeamChatChannel.Action));
        Assert.True(options.IsConfigured(TeamChatChannel.Alert));
    }

    // Bitrix says no with a 200 and an error body for some refusals.
    [Fact]
    public async Task ARefusal_IsAnError_EvenWithA200()
    {
        var handler = new RecordingHandler("""{"error":"DIALOG_ID_EMPTY"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).PostAsync(TeamChatChannel.Action, "hello"));
    }

    [Fact]
    public void ASignIn_SaysWhoWhereAndHow()
    {
        var text = TeamChatAlerts.SignedInText("Siti Hajar", "siti@x.test", "Employee", "Demo Co", "password", "1.2.3.4");

        Assert.Equal("🔑 Sign-in: Siti Hajar (siti@x.test)\nCompany: Demo Co · Employee\nVia: password · IP 1.2.3.4", text);
    }

    [Fact]
    public void ANewCompany_NamesItsOwner_AndWhoMadeIt_WhenSomeoneElseDid()
    {
        Assert.Equal("🏢 New company: Demo Co\nOwner: Ali (ali@x.test)\nVia: AltomateHR",
            TeamChatAlerts.CompanyCreatedText("Demo Co", "Ali", "ali@x.test", "ALI@x.test", "AltomateHR"));
        Assert.Equal("🏢 New company: Demo Co\nOwner: Ali (ali@x.test)\nCreated by: support@x.test\nVia: AltomateHR",
            TeamChatAlerts.CompanyCreatedText("Demo Co", "Ali", "ali@x.test", "support@x.test", "AltomateHR"));
        Assert.Equal("🏢 New company: Demo Co\nVia: provisioning API",
            TeamChatAlerts.CompanyCreatedText("Demo Co", null, null, null, "provisioning API"));
    }

    [Fact]
    public void ADeactivation_SaysWhyAndWho_AndAReactivation_WhatItWasFor()
    {
        Assert.Equal("⛔ Company deactivated: Demo Co\nReason: Unpaid since Sep\nBy: support@x.test",
            TeamChatAlerts.CompanyStatusText("Demo Co", true, "Unpaid since Sep", "support@x.test"));
        Assert.Equal("✅ Company reactivated: Demo Co\nWas deactivated for: Unpaid since Sep\nBy: support@x.test",
            TeamChatAlerts.CompanyStatusText("Demo Co", false, "Unpaid since Sep", "support@x.test"));
    }

    // Customer-typed names can't turn into Bitrix formatting or links.
    [Fact]
    public void TypedNames_CantFormatTheMessage()
    {
        var text = TeamChatAlerts.CompanyCreatedText("[URL=https://evil.test]Click[/URL]\nCo", null, null, null, "AltomateHR");

        Assert.StartsWith("🏢 New company: (URL=https://evil.test)Click(/URL) Co\n", text);
    }

    // ---- Repeated wrong passwords ----

    [Fact]
    public void WrongPasswords_AreSaidOnceAtTheFifth_AndAgainOnlyAfterTheWindow()
    {
        var tracker = new SignInFailureTracker();
        var t = new DateTime(2026, 11, 12, 1, 0, 0, DateTimeKind.Utc);

        var said = Enumerable.Range(0, 7)
            .Select(i => tracker.RecordAndShouldAlert("usr-1", t.AddMinutes(i)))
            .ToList();
        Assert.Equal([false, false, false, false, true, false, false], said);

        // Another account counts on its own.
        Assert.False(tracker.RecordAndShouldAlert("usr-2", t.AddMinutes(6)));

        // A fresh run of five once the window has passed is said again.
        var later = t.AddMinutes(40);
        var again = Enumerable.Range(0, 5).Select(i => tracker.RecordAndShouldAlert("usr-1", later.AddMinutes(i))).ToList();
        Assert.Equal([false, false, false, false, true], again);
    }

    [Fact]
    public void FailuresSpreadOut_AreNeverSaid()
    {
        var tracker = new SignInFailureTracker();
        var t = new DateTime(2026, 11, 12, 1, 0, 0, DateTimeKind.Utc);

        Assert.All(
            Enumerable.Range(0, 10).Select(i => tracker.RecordAndShouldAlert("usr-1", t.AddMinutes(i * 16))),
            Assert.False);
    }

    // ---- The new messages ----

    [Fact]
    public void PayrollApproved_SaysTheRunAndItsMoney_AndCallsOutTheFirst()
    {
        Assert.Equal(
            "💰 Payroll approved: Demo Co\nRun: October 2026 · 12 people · net RM 45,210.50\nBy: boss@x.test",
            TeamChatAlerts.PayrollApprovedText("Demo Co", "October 2026", 12, 45210.5m, false, "boss@x.test"));
        Assert.StartsWith(
            "🎉 First payroll approved: Demo Co\nRun: October 2026 · 1 person",
            TeamChatAlerts.PayrollApprovedText("Demo Co", "October 2026", 1, 100m, true, null));
    }

    [Fact]
    public void ALeaveDecision_SaysWhoWhatAndWhen()
    {
        Assert.Equal(
            "🌴 Leave approved: Siti (siti@x.test)\nCompany: Demo Co\nAnnual Leave · 3 days · 3–5 Nov 2026\nBy: boss@x.test",
            TeamChatAlerts.LeaveDecidedText("Demo Co", "Siti", "siti@x.test", "Annual Leave", 3,
                new DateTime(2026, 11, 3), new DateTime(2026, 11, 5), true, false, "boss@x.test"));
        Assert.StartsWith(
            "🚫 Leave rejected: siti@x.test\nCompany: Demo Co\nMC · 0.5 days · 3 Nov 2026",
            TeamChatAlerts.LeaveDecidedText("Demo Co", null, "siti@x.test", "MC", 0.5,
                new DateTime(2026, 11, 3), new DateTime(2026, 11, 3), false, false, null));
        Assert.StartsWith(
            "🌴 Leave applied by admin:",
            TeamChatAlerts.LeaveDecidedText("Demo Co", null, "siti@x.test", "MC", 1,
                new DateTime(2026, 11, 30), new DateTime(2026, 12, 1), true, true, null));
    }

    [Fact]
    public void AClaimDecision_AndAnAttendanceRefusal_ReadPlainly()
    {
        Assert.Equal(
            "🧾 Claim approved: Ali (ali@x.test)\nCompany: Demo Co\nGrab to client · RM 42.00",
            TeamChatAlerts.ClaimDecidedText("Demo Co", "Ali", "ali@x.test", "Grab to client", "MYR", 42m, true, null));
        Assert.Equal(
            "🚫 Attendance rejected: Ali (ali@x.test)\nCompany: Demo Co\nClock-in at 12 Nov, 8:05 AM\nReason: Not at the outlet\nBy: sup@x.test",
            TeamChatAlerts.AttendanceRejectedText("Demo Co", "Ali", "ali@x.test", "Clock-in",
                new DateTime(2026, 11, 12, 0, 5, 0, DateTimeKind.Utc), "Not at the outlet", "sup@x.test"));
    }

    [Fact]
    public void PayslipFailures_AreGroupedByReason()
    {
        Assert.Equal(
            "📧 Payslip emails failed: Demo Co\nRun: October 2026 · 3 of 20 payslips not sent\n• 2 × The mail provider rejected the message.\n• 1 × Send failed unexpectedly.",
            TeamChatAlerts.PayslipEmailsFailedText("Demo Co", "October 2026", 20,
                ["The mail provider rejected the message.", "Send failed unexpectedly.", "The mail provider rejected the message."], null));
    }

    [Fact]
    public void ARevert_NamesTheRunsItDraggedBack()
    {
        Assert.Equal(
            "↩️ Approved payroll reverted to draft: Demo Co\nRun: September 2026\nAlso back to draft: October 2026\nBy: boss@x.test",
            TeamChatAlerts.PayrollRevertedText("Demo Co", "September 2026", ["October 2026"], "boss@x.test"));
    }

    // ---- Daily summary ----

    private static CompanyActivity Quiet(string name) => new("o-" + name, name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void TheDailySummary_ListsBusyCompanies_AndCountsTheQuietOnes()
    {
        var busy = Quiet("Ayu Borneo") with
        {
            SignIns = 34, PeopleSignedIn = 21, RunsApproved = 1, LeaveApplied = 5, LeaveApproved = 4,
            LeaveWaiting = 3, ClaimsSubmitted = 6, ClaimsWaiting = 9, PeopleClockedIn = 112, AttendanceWaiting = 2,
        };
        var messages = TeamChatDailySummaryService.SummaryMessages(
            new DateOnly(2026, 11, 11), [busy, Quiet("Idle Co"), Quiet("Sleepy Co") with { LeaveWaiting = 4 }]);

        var text = Assert.Single(messages);
        Assert.StartsWith("☀️ Daily summary · Wed 11 Nov 2026\n1 company active · 2 quiet", text);
        Assert.Contains(
            "🏢 Ayu Borneo\n• Sign-ins: 34 (21 people)\n• Payroll: 1 run approved\n• Leave: 5 applied · 4 approved · 3 waiting"
            + "\n• Claims: 6 submitted · 9 waiting\n• Attendance: 112 people clocked in · 2 waiting approval",
            text);
        Assert.DoesNotContain("Idle Co", text);
        Assert.DoesNotContain("Sleepy Co", text);
    }

    [Fact]
    public void AQuietDay_IsOneLine()
    {
        var text = Assert.Single(TeamChatDailySummaryService.SummaryMessages(new DateOnly(2026, 11, 11), [Quiet("A")]));
        Assert.Equal("☀️ Daily summary · Wed 11 Nov 2026\n0 companies active · 1 quiet", text);
    }

    [Fact]
    public void ALongSummary_IsSplit_WithoutCuttingACompanyInHalf()
    {
        var companies = Enumerable.Range(1, 80)
            .Select(i => Quiet($"Company {i:00}") with { SignIns = i, PeopleSignedIn = 1, PeopleClockedIn = i })
            .ToList();

        var messages = TeamChatDailySummaryService.SummaryMessages(new DateOnly(2026, 11, 11), companies);

        Assert.True(messages.Count > 1);
        Assert.All(messages, m => Assert.True(m.Length <= TeamChatDailySummaryService.MaxMessageLength));
        var all = string.Join("\n\n", messages);
        Assert.All(Enumerable.Range(1, 80), i => Assert.Contains($"🏢 Company {i:00}\n• Sign-ins: {i} (1 person)", all));
    }

    private sealed class RecordingHandler(string reply) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }
}
