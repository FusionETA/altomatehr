using System.Text.Json;
using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Fah;

// Policy and admin changes FAH makes in AltomateHR itself (not through the
// MCP server, which has no such tools), through the same services as the
// screens: create a policy or an admin (in the signed-in company, and in
// others the admin owns), and — for an Owner of several companies — copy a
// policy or add an existing admin across them. Another company's part runs as the Owner there.
//
// Like the employee import: the tool call is answered with what the card
// shows; nothing changes until the admin presses Confirm.
public interface IFahCompanyChanges
{
    public const string CreatePolicyTool = "create_policy";
    public const string CreateAdminTool = "create_admin";
    public const string CopyPolicyTool = "copy_policy_to_companies";
    public const string AddAdminTool = "add_admin_to_companies";

    static bool Handles(string tool) => tool is CreatePolicyTool or CreateAdminTool or CopyPolicyTool or AddAdminTool;

    // Create a policy or an admin, for someone who owns a company; copy and
    // add-admin too for someone who owns two or more.
    IReadOnlyList<FahTool> Tools(IReadOnlyList<UserOrgDto> companies);

    // What would happen in each company, or why the request can't be done.
    Task<(FahCompanyPlan? Plan, string? Error)> PrepareAsync(
        string tool, JsonElement input, IReadOnlyList<UserOrgDto> companies);

    // Confirm: each company's change, re-checked first. Text says what
    // happened in each; Made is whether anything was.
    Task<(string Text, bool Made)> RunAsync(FahCompanyPlan plan);
}
