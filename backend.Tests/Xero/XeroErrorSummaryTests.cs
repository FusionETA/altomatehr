using AltomateHR.Api.Modules.Xero;

namespace AltomateHR.Api.Tests.Xero;

// Xero answers a rejected document by echoing the whole payload back, with the
// real reason buried near the end. These pin down that we surface the reason and
// nothing else — the raw body used to reach the admin screen verbatim.
public class XeroErrorSummaryTests
{
    [Fact]
    public void PullsTheValidationMessageOutOfXerosEchoedPayload()
    {
        // Trimmed from a real response: the useful sentence is the last thing in
        // it, after the whole invoice is echoed back.
        const string body = """
        {"ErrorNumber":10,"Type":"ValidationException","Message":"A validation exception occurred",
         "Elements":[{"Type":"ACCPAY","InvoiceID":"00000000-0000-0000-0000-000000000000",
         "Reference":"CLM-20260907-89C029","Contact":{"Name":"Evan Employee"},
         "LineItems":[{"Description":"COFFEE SHOP","UnitAmount":17.00}],
         "CurrencyCode":"USD",
         "ValidationErrors":[{"Message":"Organisation is not subscribed to currency USD"}]}]}
        """;

        Assert.Equal(
            "Organisation is not subscribed to currency USD.",
            XeroErrorSummary.Describe(body, 400));
    }

    [Fact]
    public void JoinsSeveralValidationMessages()
    {
        const string body = """
        {"Elements":[{"ValidationErrors":[
          {"Message":"Account code 'ZZZ' is not valid"},
          {"Message":"Date is required"}]}]}
        """;

        Assert.Equal(
            "Account code 'ZZZ' is not valid. Date is required.",
            XeroErrorSummary.Describe(body, 400));
    }

    [Fact]
    public void DoesNotRepeatTheSameMessageTwice()
    {
        // Two rejected line items usually fail for the identical reason.
        const string body = """
        {"Elements":[
          {"ValidationErrors":[{"Message":"Account code is not valid"}]},
          {"ValidationErrors":[{"Message":"Account code is not valid"}]}]}
        """;

        Assert.Equal("Account code is not valid.", XeroErrorSummary.Describe(body, 400));
    }

    [Fact]
    public void FallsBackToTheEnvelopeWhenThereAreNoValidationErrors()
    {
        Assert.Equal(
            "The resource you're looking for cannot be found.",
            XeroErrorSummary.Describe(
                """{"Title":"Not Found","Detail":"The resource you're looking for cannot be found"}""",
                404));
    }

    [Fact]
    public void UnderstandsTheOAuthErrorShape()
    {
        Assert.Equal(
            "The refresh token has expired.",
            XeroErrorSummary.Describe(
                """{"error":"invalid_grant","error_description":"The refresh token has expired"}""",
                400));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SaysSomethingUsefulWhenTheBodyIsEmpty(string? body)
    {
        Assert.Equal("Xero returned 503.", XeroErrorSummary.Describe(body, 503));
    }

    [Fact]
    public void NeverThrowsOnMalformedJson()
    {
        // An error path that can itself fail would lose the original failure.
        var summary = XeroErrorSummary.Describe("<html>502 Bad Gateway</html>", 502);
        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    [Fact]
    public void TrimsAnUnreadableBodyRatherThanDumpingIt()
    {
        var summary = XeroErrorSummary.Describe(new string('x', 5000), 400);
        Assert.True(summary.Length < 220, $"summary was {summary.Length} chars");
    }
}
