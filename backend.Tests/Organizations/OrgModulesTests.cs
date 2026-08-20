using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Tests.Organizations;

public class OrgModulesTests
{
    [Fact]
    public void FreeTier_GetsBaseModulesOnly_AddonsIgnored()
    {
        // Even if addons are present, DIY+FREE never unlocks the paid modules.
        var modules = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.DIY, OrgPlanTier.FREE, new[] { "expense_claim", "clock" });

        Assert.Contains(OrgModules.Leave, modules);         // base
        Assert.Contains(OrgModules.Employees, modules);     // base
        Assert.DoesNotContain(OrgModules.Claims, modules);  // gated
        Assert.DoesNotContain(OrgModules.Attendance, modules);
    }

    [Fact]
    public void PaidTier_WithAddons_UnlocksClaimsAndAttendance()
    {
        var modules = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.DIY, OrgPlanTier.PAID, new[] { "expense_claim", "clock" });

        Assert.Contains(OrgModules.Claims, modules);
        Assert.Contains(OrgModules.Attendance, modules);
        Assert.Contains(OrgModules.Leave, modules);         // base still there
    }

    [Fact]
    public void PaidTier_WithoutAddons_HasNoPaidModules()
    {
        var modules = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.DIY, OrgPlanTier.PAID, Array.Empty<string>());

        Assert.DoesNotContain(OrgModules.Claims, modules);
        Assert.Contains(OrgModules.Leave, modules);
    }

    [Fact]
    public void Expert_BehavesLikePaid_HonoursAddons()
    {
        var modules = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.EXPERT, null, new[] { "expense_claim" });

        Assert.Contains(OrgModules.Claims, modules);
        Assert.DoesNotContain(OrgModules.Attendance, modules); // no clock addon
    }

    [Fact]
    public void Effective_NullGrant_IsFullCeiling()
    {
        var ceiling = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.DIY, OrgPlanTier.PAID, new[] { "expense_claim" });

        var effective = OrgModules.Effective(ceiling, adminGrant: null);

        Assert.Equal(ceiling.Count, effective.Count);   // owner / unrestricted → everything
        Assert.Contains(OrgModules.Claims, effective);
    }

    [Fact]
    public void Effective_GrantNarrowsBelowCeiling()
    {
        var ceiling = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.DIY, OrgPlanTier.PAID, new[] { "expense_claim" });

        // Admin granted only leave → sees leave, not claims (even though the org has claims).
        var effective = OrgModules.Effective(ceiling, new[] { OrgModules.Leave });

        Assert.Contains(OrgModules.Leave, effective);
        Assert.DoesNotContain(OrgModules.Claims, effective);
    }

    [Fact]
    public void Effective_GrantCannotExceedCeiling()
    {
        // Org is FREE (no claims). Even if an admin is "granted" claims, they can't have it.
        var ceiling = OrgModules.DeriveOrgEnabledModules(
            OrgPlan.DIY, OrgPlanTier.FREE, Array.Empty<string>());

        var effective = OrgModules.Effective(ceiling, new[] { OrgModules.Claims, OrgModules.Leave });

        Assert.DoesNotContain(OrgModules.Claims, effective);   // ceiling wins
        Assert.Contains(OrgModules.Leave, effective);
    }
}
