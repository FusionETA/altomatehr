using System.Text;
using AltomateHR.Api.Modules.Xero;

namespace AltomateHR.Api.Tests.Xero;

// Which Xero org a sign-in connects. It used to be whichever org the app had
// been connected to FIRST — so an admin who chose their own company's Xero got
// the test org another company had connected weeks earlier.
public class XeroTenantChoiceTests
{
    private static XeroTenantResponse Tenant(string id, string name, string? authEvent) =>
        new($"conn-{id}", id, name, "ORGANISATION", authEvent);

    private static readonly XeroTenantResponse TestOrg = Tenant("t-test", "AltomateHR-V2", "evt-old");
    private static readonly XeroTenantResponse Globe = Tenant("t-globe", "GLOBE ENGINEERING SDN. BHD.", "evt-now");

    // An app shared with another product re-approves every org it has at once.
    // On a reconnect, the org the company is connected to now is the answer.
    [Fact]
    public void AReconnectKeepsTheCurrentOrgAmongSeveralReapproved()
    {
        var result = XeroTenantChoice.Choose(
            [Tenant("t-a", "Ayu Borneo (AP)", "evt-now"), Tenant("t-s14", "Ayu Borneo (Section 14)", "evt-now")],
            "evt-now", new HashSet<string>(), currentTenantId: "t-s14");

        Assert.Equal("t-s14", result.Tenant!.TenantId);
    }

    // No current org: the admin picks — from the ones not already used by
    // another company, in name order.
    [Fact]
    public void SeveralWithNothingToGoOn_OffersThemToPick()
    {
        var result = XeroTenantChoice.Choose(
            [Tenant("t-z", "Zeta", "evt-now"), Tenant("t-used", "Used", "evt-now"), Tenant("t-a", "Alpha", "evt-now")],
            "evt-now", new HashSet<string> { "t-used" });

        Assert.Null(result.Tenant);
        Assert.Equal(XeroTenantChoice.Refusal.SeveralAuthorised, result.Refusal);
        Assert.Equal(["Alpha", "Zeta"], result.Candidates!.Select(t => t.TenantName));
    }

    // The exact bug: the test org is listed first, Globe was chosen just now.
    [Fact]
    public void PicksTheOrgAuthorisedInThisSignInNotTheFirstListed()
    {
        var result = XeroTenantChoice.Choose([TestOrg, Globe], "evt-now", new HashSet<string>());

        Assert.Equal("t-globe", result.Tenant!.TenantId);
    }

    [Fact]
    public void RefusesAnOrgAlreadyConnectedToAnotherCompany()
    {
        var result = XeroTenantChoice.Choose(
            [Tenant("t-test", "AltomateHR-V2", "evt-now")], "evt-now", new HashSet<string> { "t-test" });

        Assert.Null(result.Tenant);
        Assert.Equal(XeroTenantChoice.Refusal.InUseElsewhere, result.Refusal);
        Assert.Equal("AltomateHR-V2", result.TenantName);
    }

    // No readable event id: every listed org is a candidate, the ones owned by
    // other companies drop out, and the one left is connected.
    [Fact]
    public void WithoutAnEventIdFallsBackToTheOrgsNotTakenElsewhere()
    {
        var result = XeroTenantChoice.Choose([TestOrg, Globe], null, new HashSet<string> { "t-test" });

        Assert.Equal("t-globe", result.Tenant!.TenantId);
    }

    // Several orgs ticked in one sign-in: refused rather than guessed.
    [Fact]
    public void RefusesToGuessBetweenSeveralOrgsAuthorisedTogether()
    {
        var other = Tenant("t-other", "Other Sdn Bhd", "evt-now");

        var result = XeroTenantChoice.Choose([Globe, other], "evt-now", new HashSet<string>());

        Assert.Null(result.Tenant);
        Assert.Equal(XeroTenantChoice.Refusal.SeveralAuthorised, result.Refusal);
    }

    // Re-authorising the org this company already has is not "in use
    // elsewhere" — the lookup only counts OTHER companies.
    [Fact]
    public void ReconnectingTheSameOrgIsAllowed()
    {
        var result = XeroTenantChoice.Choose([Globe], "evt-now", new HashSet<string>());

        Assert.Equal("t-globe", result.Tenant!.TenantId);
    }

    [Fact]
    public void RefusesWhenXeroReturnedNothing()
    {
        Assert.Equal(
            XeroTenantChoice.Refusal.NothingAuthorised,
            XeroTenantChoice.Choose([], "evt-now", new HashSet<string>()).Refusal);
    }

    [Fact]
    public void ReadsTheEventIdFromTheAccessToken()
    {
        static string B64(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = $"{B64("{\"alg\":\"RS256\"}")}.{B64("{\"authentication_event_id\":\"evt-now\",\"sub\":\"x\"}")}.sig";

        Assert.Equal("evt-now", XeroTenantChoice.AuthEventIdOf(jwt));
        Assert.Null(XeroTenantChoice.AuthEventIdOf("not-a-jwt"));
    }
}
