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
        new(new HttpClient(handler), Options.Create(new TeamChatOptions { WebhookUrl = webhook, DialogId = dialog }));

    [Fact]
    public async Task AMessage_GoesToImMessageAdd_ForTheGroup()
    {
        var handler = new RecordingHandler("""{"result":123}""");

        await Client(handler).PostAsync("hello");

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

        await Client(handler, webhook, dialog).PostAsync("hello");

        Assert.Null(handler.Url);
    }

    // Bitrix says no with a 200 and an error body for some refusals.
    [Fact]
    public async Task ARefusal_IsAnError_EvenWithA200()
    {
        var handler = new RecordingHandler("""{"error":"DIALOG_ID_EMPTY"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).PostAsync("hello"));
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

    // Customer-typed names can't turn into Bitrix formatting or links.
    [Fact]
    public void TypedNames_CantFormatTheMessage()
    {
        var text = TeamChatAlerts.CompanyCreatedText("[URL=https://evil.test]Click[/URL]\nCo", null, null, null, "AltomateHR");

        Assert.StartsWith("🏢 New company: (URL=https://evil.test)Click(/URL) Co\n", text);
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
