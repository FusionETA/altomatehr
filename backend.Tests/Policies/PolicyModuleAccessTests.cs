using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Tests.Policies;

// Which modules an employee's policy lets them use.
//
// The policy editor has carried these checkboxes all along and nothing ever
// read them: a part-time policy with Claims and Leave unticked saved happily,
// and the employee kept full use of both. These pin the rule that fixes it.
public class PolicyModuleAccessTests
{
    [Fact]
    public void AllowsOnlyTheModulesTheFlagsName()
    {
        var access = new PolicyModuleAccess(Attendance: true, Claims: false, Leave: false);

        Assert.True(access.Allows(PolicyModules.Attendance));
        Assert.False(access.Allows(PolicyModules.Claims));
        Assert.False(access.Allows(PolicyModules.Leave));
    }

    // An org that never configured a policy, or an employee not assigned one,
    // gets everything. Locking someone out of the whole app because config is
    // missing is the worse failure.
    [Fact]
    public void MissingConfigurationMeansFullAccess()
    {
        Assert.True(PolicyModuleAccess.All.Allows(PolicyModules.Attendance));
        Assert.True(PolicyModuleAccess.All.Allows(PolicyModules.Claims));
        Assert.True(PolicyModuleAccess.All.Allows(PolicyModules.Leave));
    }

    // The gate only knows the three modules a policy actually governs. Anything
    // else is not a policy question, so it is not this gate's to refuse.
    [Fact]
    public void AModuleNoPolicyGovernsIsNotRefused()
    {
        var nothing = new PolicyModuleAccess(false, false, false);

        Assert.True(nothing.Allows("Payslips"));
    }
}
