using System.Reflection;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Policies;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Tests.Leave;

// The employee's own apply flow — apply, edit, the day-count preview and the
// attachment upload — sits behind the same gates: a person (no API key) whose
// policy grants the Leave module. Editing once lacked the policy gate, so an
// employee whose policy hid Leave could still change a pending request.
public class LeaveControllerGateTests
{
    [Theory]
    [InlineData(nameof(LeaveController.Apply))]
    [InlineData(nameof(LeaveController.Edit))]
    [InlineData(nameof(LeaveController.PreviewDays))]
    [InlineData(nameof(LeaveController.UploadAttachment))]
    public void Apply_flow_endpoints_are_human_only_and_policy_gated(string action)
    {
        var method = typeof(LeaveController).GetMethod(action)!;

        Assert.NotNull(method.GetCustomAttribute<HumanOnlyAttribute>());
        var gate = Assert.Single(method.CustomAttributes,
            a => a.AttributeType == typeof(RequirePolicyModuleAttribute));
        Assert.Equal(PolicyModules.Leave, gate.ConstructorArguments[0].Value);
    }

    [Fact]
    public void Preview_is_a_GET_on_preview_days()
    {
        var get = typeof(LeaveController).GetMethod(nameof(LeaveController.PreviewDays))!
            .GetCustomAttribute<HttpGetAttribute>();
        Assert.Equal("preview-days", get?.Template);
    }
}
