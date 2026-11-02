using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Fah;

// Only an Owner, and only in companies they own — the source as well as
// each target. An Admin can't create policies (or, if limited, admins), and
// copying someone's access means reading the company's admins, which is the
// Owner's screen. Each company's part runs in that company (ICompanyScopes)
// through its own services, so the same checks, numbering and activity log
// apply as on its screens.
//
// A new policy in the signed-in company is made as the admin themselves
// (this request's own services), so the Policies screen's own rule decides:
// creating one is the Owner's.
public class FahCompanyChanges : IFahCompanyChanges
{
    private const string All = "all";

    private readonly ICompanyScopes _scopes;
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;
    private readonly IServiceProvider _services;
    private readonly IUserRepository _users;

    public FahCompanyChanges(
        ICompanyScopes scopes, IAuthService auth, ICurrentUser currentUser, IServiceProvider services, IUserRepository users)
    {
        _scopes = scopes;
        _auth = auth;
        _currentUser = currentUser;
        _services = services;
        _users = users;
    }

    public IReadOnlyList<FahTool> Tools(IReadOnlyList<UserOrgDto> companies)
    {
        if (_currentUser.IsSupport || _currentUser.IsFormer || Owned(companies).Count == 0) return [];
        var others = Owned(companies).Any(c => c.OrganizationId != _currentUser.OrganizationId);
        FahTool[] create = [CreateTool(others), CreateAdminTool(others)];
        if (Owned(companies).Count < 2) return create;

        var to = new
        {
            type = "array",
            items = new { type = "string" },
            description = "The companies to make it in, by name (or id, where two share a name) — or [\"all\"] for "
                + "every company the admin owns. Only companies they own.",
        };
        return
        [
            .. create,
            new FahTool(IFahCompanyChanges.CopyPolicyTool,
                "Copy one of the admin's policies (its rules, OT rates and leave days) into other companies they own, "
                + "as a new policy with the same name. A company that already has a policy with that name is skipped, "
                + "not changed. Leave days go to the leave type with the same name there. The admin sees one card "
                + "listing every company with Confirm and Cancel; nothing is copied until they press Confirm.",
                Schema(new Dictionary<string, object>
                {
                    ["policy"] = new { type = "string", description = "The policy's name in the company it's copied from." },
                    ["from"] = new { type = "string", description = "The company to copy it from. Default: the signed-in company." },
                    ["to"] = to,
                }, "policy", "to")),
            new FahTool(IFahCompanyChanges.AddAdminTool,
                "Make someone who is already an admin in one of the admin's companies an admin in other companies they own, "
                + "with the same access: the same modules at the same level, the same settings permission, and the same "
                + "employee limit matched by policy name (a policy a company doesn't have is left out, so they see fewer "
                + "people there, never more). No email is sent; they see the new companies in their company menu. "
                + "The admin sees one card listing every company with Confirm and Cancel; nothing changes until they "
                + "press Confirm.",
                Schema(new Dictionary<string, object>
                {
                    ["email"] = new { type = "string", description = "The person's login email." },
                    ["copyAccessFrom"] = new { type = "string", description = "The company where they're an admin now, to copy their access from. Default: the signed-in company." },
                    ["to"] = to,
                }, "email", "to")),
        ];
    }

    public async Task<(FahCompanyPlan? Plan, string? Error)> PrepareAsync(
        string tool, JsonElement input, IReadOnlyList<UserOrgDto> companies)
    {
        if (tool == IFahCompanyChanges.CreatePolicyTool) return await PrepareCreateAsync(input, companies);
        if (tool == IFahCompanyChanges.CreateAdminTool) return await PrepareCreateAdminAsync(input, companies);
        if (Owned(companies).Count < 2) return (null, "This needs the admin to own two companies or more.");

        var sourceAsked = Text(input, tool == IFahCompanyChanges.CopyPolicyTool ? "from" : "copyAccessFrom");
        var source = sourceAsked is null
            ? companies.FirstOrDefault(c => c.OrganizationId == _currentUser.OrganizationId)
            : FahCompanyList.Find(companies, sourceAsked);
        if (source is null) return (null, Unknown(companies, sourceAsked!));
        if (!IsOwner(source)) return (null, $"The admin isn't the owner of {source.Name}, so FAH can't copy from there.");

        var (targets, targetError) = Targets(input, companies, source);
        if (targetError is not null) return (null, targetError);

        return tool == IFahCompanyChanges.CopyPolicyTool
            ? await PreparePolicyAsync(input, companies, source, targets)
            : await PrepareAdminAsync(input, companies, source, targets);
    }

    // ─── Create a policy ─────────────────────────────────────────────────

    // The settings FAH may set, as the Policies screen names them (camelCase
    // SavePolicyDto properties). Name, description, salary type and leave
    // days have their own inputs.
    private static readonly Dictionary<string, (PropertyInfo Property, string Description)> Settings =
        new (string Key, string Description)[]
        {
            ("canAccessAttendance", "Staff on it use Attendance (clock in/out)."),
            ("canAccessClaims", "Staff on it make claims."),
            ("canAccessLeave", "Staff on it apply for leave."),
            ("geolocationEnabled", "Location is used at all."),
            ("requireGeofence", "Clock-in only inside the company's geofence."),
            ("captureLocationOnClockIn", "Record location at clock-in."),
            ("captureLocationOnClockOut", "Record location at clock-out."),
            ("captureLocationOnBreakStart", "Record location when a break starts."),
            ("captureLocationOnBreakEnd", "Record location when a break ends."),
            ("requireSelfie", "Selfie at clock-in."),
            ("requireClockOutSelfie", "Selfie at clock-out."),
            ("requireIpWhitelist", "Clock-in only from the company's allowed IP addresses."),
            ("autoClockOutEnabled", "Clock someone out automatically."),
            ("autoClockOutAfterMinutes", "After how many minutes (1–1440); needed with autoClockOutEnabled."),
            ("otEnabled", "Overtime is paid."),
            ("otDailyThresholdMinutes", "OT starts after this many minutes a day (0–1440)."),
            ("otMethod", "CASH (paid) or TIME_BANK (time off)."),
            ("otRateNormalDay", "OT rate on a normal day, × the hourly rate."),
            ("otRateRestDay", "OT rate on a rest day."),
            ("otRatePublicHoliday", "OT rate on a public holiday."),
            ("otRateRestDayInShift", "Rate for normal hours worked on a rest day."),
            ("otRatePublicHolidayInShift", "Rate for normal hours worked on a public holiday."),
            ("otSalaryThreshold", "Only staff earning up to this monthly salary get OT (RM); null = no limit."),
            ("temporary", "A temporary policy."),
        }.ToDictionary(
            s => s.Key,
            s => (typeof(SavePolicyDto).GetProperty(char.ToUpperInvariant(s.Key[0]) + s.Key[1..])!, s.Description),
            StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SettingJson = new() { Converters = { new JsonStringEnumConverter() } };

    private static FahTool CreateTool(bool otherCompanies) => new(IFahCompanyChanges.CreatePolicyTool,
        "Create a new policy. Either start from an existing policy (basedOn) and change only what the admin says, or "
        + "start from the Policies screen's defaults (geofence on, selfie off, OT on: cash after 8 hours, 1.5× normal "
        + "day, 2× rest day, 3× public holiday). Without basedOn, salaryType is needed: if the admin hasn't said "
        + "MONTHLY or HOURLY, ask them — never choose it for them. Leave types not given keep their own default days. "
        + (otherCompanies
            ? "It's made in the signed-in company unless `in` names others; another company only where the admin is the owner. "
            : "")
        + "The admin sees one card with every setting and each company, with Confirm and Cancel; nothing is created "
        + "until they press Confirm. A company that already has a policy with that name is skipped.",
        Schema(new Dictionary<string, object>
        {
            ["name"] = new { type = "string", description = "The new policy's name." },
            ["description"] = new { type = "string", description = "Optional." },
            ["salaryType"] = new { type = "string", @enum = new[] { "MONTHLY", "HOURLY" }, description = "Needed without basedOn; with it, taken from that policy unless the admin says otherwise." },
            ["basedOn"] = new { type = "string", description = "An existing policy to start from: its settings and leave days." },
            ["basedOnCompany"] = new { type = "string", description = "The company basedOn is in. Default: the signed-in company." },
            ["settings"] = new
            {
                type = "object",
                description = "Only the settings the admin asked to differ from the starting point.",
                properties = Settings.ToDictionary(s => s.Key, s => SettingSchema(s.Value.Property.PropertyType, s.Value.Description)),
                additionalProperties = false,
            },
            ["leaveDays"] = new
            {
                type = "array",
                description = "Days a year for these leave types, by name.",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["leaveType"] = new { type = "string" },
                        ["days"] = new { type = "number" },
                    },
                    required = new[] { "leaveType", "days" },
                },
            },
            ["in"] = new
            {
                type = "array",
                items = new { type = "string" },
                description = otherCompanies
                    ? "The companies to create it in, by name (or id, where two share a name) — or [\"all\"] for the "
                        + "signed-in company and every company the admin owns. Default: the signed-in company."
                    : "Leave out: the signed-in company.",
            },
        }, "name"));

    private static object SettingSchema(Type type, string description)
    {
        var inner = Nullable.GetUnderlyingType(type) ?? type;
        var nullable = inner != type;
        if (inner == typeof(OtMethod)) return new { type = "string", @enum = Enum.GetNames<OtMethod>(), description };
        var json = inner == typeof(bool) ? "boolean" : inner == typeof(int) ? "integer" : "number";
        return nullable ? new { type = new[] { json, "null" }, description } : new { type = (object)json, description };
    }

    private async Task<(FahCompanyPlan?, string?)> PrepareCreateAsync(JsonElement input, IReadOnlyList<UserOrgDto> companies)
    {
        if (Here(companies) is not { } here) return (null, "FAH can only create a policy for an admin of the signed-in company.");
        var name = Text(input, "name");
        if (name is null) return (null, "Say what the new policy is called.");
        if (name.Length > 80) return (null, "A policy name is 80 characters at most.");

        // The starting point: an existing policy, or the screen's defaults.
        SavePolicyDto policy;
        List<FahLeaveDays> days;
        var basedOn = Text(input, "basedOn");
        var from = here;
        if (basedOn is null)
        {
            policy = new SavePolicyDto();
            days = [];
            if (Text(input, "salaryType") is null)
                return (null, "Ask the admin whether staff on it are paid MONTHLY or HOURLY; don't choose it for them.");
        }
        else
        {
            if (Text(input, "basedOnCompany") is { } asked)
            {
                if (FahCompanyList.Find(companies, asked) is not { } named) return (null, Unknown(companies, asked));
                from = named;
                if (from.OrganizationId != here.OrganizationId && !IsOwner(from))
                    return (null, $"The admin isn't the owner of {from.Name}, so FAH can't read its policies from here. "
                        + "Start from one in the signed-in company, or ask them to switch.");
            }
            var (copy, error) = await InAsync(from, async sp =>
            {
                var policies = (await sp.GetRequiredService<IPolicyService>().GetVisibleAsync()).ToList();
                if (policies.FirstOrDefault(p => Same(p.Name, basedOn)) is not { } found)
                    return ((FahPolicyCopy?)null, (string?)$"{from.Name} has no policy named \"{basedOn}\". Its policies: "
                        + string.Join(", ", policies.Where(p => !p.IsArchived).Select(p => p.Name)) + ".");
                var leaveTypes = (await sp.GetRequiredService<ILeaveTypeService>().GetAllAsync()).ToDictionary(t => t.Id, t => t.Name);
                return (new FahPolicyCopy(ToSave(found), [.. found.LeaveEntitlements
                    .Where(e => leaveTypes.ContainsKey(e.LeaveTypeId))
                    .Select(e => new FahLeaveDays(leaveTypes[e.LeaveTypeId], e.DefaultDays))]), null);
            });
            if (copy is null) return (null, error);
            (policy, days) = (copy.Policy, [.. copy.LeaveDays]);
        }

        policy.Name = name;
        if (Text(input, "description") is { } description) policy.Description = description;
        if (Text(input, "salaryType") is { } salary)
        {
            if (!Enum.TryParse<SalaryType>(salary, true, out var type)) return (null, "salaryType is MONTHLY or HOURLY.");
            policy.SalaryType = type;
        }
        if (input.TryGetProperty("settings", out var settings) && ApplySettings(policy, settings) is { } bad)
            return (null, bad);
        if (input.TryGetProperty("leaveDays", out var leave) && ApplyLeaveDays(days, leave) is { } badLeave)
            return (null, badLeave);

        var problems = new List<ValidationResult>();
        if (!Validator.TryValidateObject(policy, new ValidationContext(policy), problems, validateAllProperties: true))
            return (null, string.Join(" ", problems.Select(p => p.ErrorMessage)));
        if (policy.AutoClockOutEnabled && policy.AutoClockOutAfterMinutes is null)
            return (null, "Auto clock-out needs autoClockOutAfterMinutes: ask after how long.");

        var (targets, targetError) = CreateTargets(input, companies, here);
        if (targetError is not null) return (null, targetError);

        var create = new FahPolicyCopy(policy, days);
        var steps = new List<FahCompanyStep>();
        foreach (var target in targets)
            steps.Add(target.OrganizationId == here.OrganizationId
                ? await PolicyStepAsync(_services, target, companies, create)
                : await InCompanyAsync(target, sp => PolicyStepAsync(sp, target, companies, create)));

        return (new FahCompanyPlan(IFahCompanyChanges.CreatePolicyTool, $"Create policy \"{name}\"",
            from.OrganizationId, from.Name, steps, Policy: create,
            Settings: SettingsText(policy, days, basedOn is null ? null : $"\"{basedOn}\" in {from.Name}")), null);
    }

    private static async Task<FahCompanyStep> PolicyStepAsync(
        IServiceProvider sp, UserOrgDto company, IReadOnlyList<UserOrgDto> companies, FahPolicyCopy policy)
    {
        var (skip, detail) = await PolicyCheckAsync(sp, policy);
        return Step(company, companies, skip, detail);
    }

    // The signed-in company, or the ones named in `in`: "all" = it and every
    // company they own. A named one they don't own stays on the card as
    // skipped, so the admin sees why.
    private static (IReadOnlyList<UserOrgDto> Targets, string? Error) CreateTargets(
        JsonElement input, IReadOnlyList<UserOrgDto> companies, UserOrgDto here)
    {
        var asked = input.TryGetProperty("in", out var into) && into.ValueKind == JsonValueKind.Array
            ? into.EnumerateArray().Select(v => v.GetString()?.Trim()).OfType<string>().Where(v => v.Length > 0).ToList()
            : [];
        if (asked.Count == 0) return ([here], null);

        var targets = new List<UserOrgDto>();
        foreach (var name in asked)
        {
            if (Same(name, All))
            {
                targets.Add(here);
                targets.AddRange(Owned(companies));
                continue;
            }
            var company = FahCompanyList.Find(companies, name);
            if (company is null) return ([], Unknown(companies, name));
            targets.Add(company);
        }
        return ([.. targets.DistinctBy(t => t.OrganizationId)], null);
    }

    private static string? ApplySettings(SavePolicyDto policy, JsonElement settings)
    {
        if (settings.ValueKind == JsonValueKind.Null) return null;
        if (settings.ValueKind != JsonValueKind.Object) return "settings is an object of the settings to change.";
        foreach (var setting in settings.EnumerateObject())
        {
            if (!Settings.TryGetValue(setting.Name, out var known))
                return $"\"{setting.Name}\" isn't a policy setting. The settings: {string.Join(", ", Settings.Keys)}.";
            try
            {
                known.Property.SetValue(policy, setting.Value.Deserialize(known.Property.PropertyType, SettingJson));
            }
            catch (JsonException)
            {
                return $"\"{setting.Name}\" doesn't take {setting.Value.GetRawText()}: {known.Description}";
            }
        }
        return null;
    }

    // Given days replace the starting point's for that leave type.
    private static string? ApplyLeaveDays(List<FahLeaveDays> days, JsonElement leave)
    {
        if (leave.ValueKind == JsonValueKind.Null) return null;
        if (leave.ValueKind != JsonValueKind.Array) return "leaveDays is a list of {leaveType, days}.";
        foreach (var item in leave.EnumerateArray())
        {
            var type = Text(item, "leaveType");
            if (type is null || !item.TryGetProperty("days", out var d) || !d.TryGetDouble(out var n))
                return "Each of leaveDays needs a leaveType name and a number of days.";
            if (n is < 0 or > 366) return $"{n} days of {type} a year isn't possible.";
            days.RemoveAll(x => Same(x.LeaveType, type));
            days.Add(new FahLeaveDays(type, n));
        }
        return null;
    }

    // Every setting of the new policy, for the card.
    private static string SettingsText(SavePolicyDto p, IReadOnlyList<FahLeaveDays> days, string? basedOn)
    {
        static string OnOff(bool on) => on ? "on" : "off";
        var uses = new[] { (p.CanAccessAttendance, "Attendance"), (p.CanAccessClaims, "Claims"), (p.CanAccessLeave, "Leave") }
            .Where(u => u.Item1).Select(u => u.Item2).ToList();
        var text = new StringBuilder();
        if (basedOn is not null) text.AppendLine($"Based on {basedOn}.");
        if (!string.IsNullOrWhiteSpace(p.Description)) text.AppendLine(p.Description);
        text.AppendLine($"Salary: {(p.SalaryType == SalaryType.MONTHLY ? "monthly" : "hourly")}{(p.Temporary ? " · temporary" : "")}.");
        text.AppendLine($"Staff use: {(uses.Count == 0 ? "none of Attendance, Claims, Leave" : string.Join(", ", uses))}.");
        text.AppendLine(p.GeolocationEnabled
            ? $"Location: geofence {OnOff(p.RequireGeofence)}; recorded at clock-in {OnOff(p.CaptureLocationOnClockIn)}, "
                + $"clock-out {OnOff(p.CaptureLocationOnClockOut)}, break start {OnOff(p.CaptureLocationOnBreakStart)}, "
                + $"break end {OnOff(p.CaptureLocationOnBreakEnd)}."
            : "Location: off.");
        text.AppendLine($"Selfie at clock-in {OnOff(p.RequireSelfie)}, at clock-out {OnOff(p.RequireClockOutSelfie)}; "
            + $"IP allow-list {OnOff(p.RequireIpWhitelist)}; auto clock-out "
            + (p.AutoClockOutEnabled ? $"after {p.AutoClockOutAfterMinutes} min." : "off."));
        text.AppendLine(p.OtEnabled
            ? $"OT: {(p.OtMethod == OtMethod.CASH ? "paid" : "time bank")}, after {p.OtDailyThresholdMinutes / 60.0:0.##} h a day; "
                + $"{p.OtRateNormalDay:0.##}× normal day, {p.OtRateRestDay:0.##}× rest day, {p.OtRatePublicHoliday:0.##}× public holiday; "
                + $"in-shift {p.OtRateRestDayInShift:0.##}× rest day, {p.OtRatePublicHolidayInShift:0.##}× public holiday"
                + (p.OtSalaryThreshold is { } cap ? $"; only for salaries up to RM {cap:N2}." : ".")
            : "OT: off.");
        text.Append(days.Count == 0
            ? "Leave days: each leave type's own default."
            : $"Leave days: {string.Join(", ", days.Select(d => $"{d.LeaveType} {d.Days:0.##}"))}; other types their own default.");
        return text.ToString();
    }

    // ─── Copy a policy ───────────────────────────────────────────────────

    private async Task<(FahCompanyPlan?, string?)> PreparePolicyAsync(
        JsonElement input, IReadOnlyList<UserOrgDto> companies, UserOrgDto source, IReadOnlyList<UserOrgDto> targets)
    {
        var name = Text(input, "policy");
        if (name is null) return (null, "Say which policy to copy.");

        var (copy, error) = await _scopes.RunAsync(source.OrganizationId, source.Role, async sp =>
        {
            var policies = (await sp.GetRequiredService<IPolicyService>().GetVisibleAsync()).ToList();
            var policy = policies.FirstOrDefault(p => Same(p.Name, name));
            if (policy is null)
                return ((FahPolicyCopy?)null, (string?)$"{source.Name} has no policy named \"{name}\". Its policies: "
                    + string.Join(", ", policies.Where(p => !p.IsArchived).Select(p => p.Name)) + ".");
            if (policy.IsArchived)
                return (null, $"\"{policy.Name}\" is archived in {source.Name}. Restore it there first, or copy another.");

            var leaveTypes = (await sp.GetRequiredService<ILeaveTypeService>().GetAllAsync()).ToDictionary(t => t.Id, t => t.Name);
            var days = policy.LeaveEntitlements
                .Where(e => leaveTypes.ContainsKey(e.LeaveTypeId))
                .Select(e => new FahLeaveDays(leaveTypes[e.LeaveTypeId], e.DefaultDays))
                .ToList();
            return (new FahPolicyCopy(ToSave(policy), days), null);
        });
        if (copy is null) return (null, error);

        var steps = new List<FahCompanyStep>();
        foreach (var target in targets)
            steps.Add(await InCompanyAsync(target, async sp =>
            {
                var (skip, detail) = await PolicyCheckAsync(sp, copy);
                return Step(target, companies, skip, detail);
            }));

        return (new FahCompanyPlan(IFahCompanyChanges.CopyPolicyTool, $"Copy policy \"{copy.Policy.Name}\"",
            source.OrganizationId, source.Name, steps, Policy: copy), null);
    }

    // What copying would do in this company, or why it's skipped.
    private static async Task<(bool Skip, string Detail)> PolicyCheckAsync(IServiceProvider sp, FahPolicyCopy copy)
    {
        var orgModules = await sp.GetRequiredService<IModuleAccessService>().GetOrgModulesAsync();
        if (!orgModules.Contains(OrgModules.Policies, StringComparer.OrdinalIgnoreCase))
            return (true, "Policies aren't on this company's plan.");
        if (!(await sp.GetRequiredService<IModuleAccessService>().GetAccessAsync()).ManagesAllPolicies)
            return (true, "Only the owner can create a policy here; you're an Admin.");

        var policies = (await sp.GetRequiredService<IPolicyService>().GetAllAsync()).ToList();
        if (policies.FirstOrDefault(p => Same(p.Name, copy.Policy.Name)) is { } existing)
            return (true, $"Already has a policy named \"{existing.Name}\"{(existing.IsArchived ? " (archived)" : "")} — left as it is.");

        var (_, missing) = await LeaveDaysAsync(sp, copy);
        var detail = policies.Any(p => p.IsDefault && !p.IsArchived)
            ? "New policy."
            : "New policy — it becomes the default, as the company has none.";
        if (missing.Count > 0)
            detail += $" No leave type named {string.Join(", ", missing.Select(m => $"\"{m}\""))} here, so those leave days aren't copied.";
        return (false, detail);
    }

    // The policy's leave days by this company's own leave types, and the
    // names it doesn't have.
    private static async Task<(List<PolicyLeaveEntitlementDto> Days, List<string> Missing)> LeaveDaysAsync(
        IServiceProvider sp, FahPolicyCopy copy)
    {
        var types = (await sp.GetRequiredService<ILeaveTypeService>().GetAllAsync())
            .OrderBy(t => t.IsArchived)   // a live type before an archived one of the same name
            .ToList();
        var days = new List<PolicyLeaveEntitlementDto>();
        var missing = new List<string>();
        foreach (var leave in copy.LeaveDays)
        {
            if (types.FirstOrDefault(t => Same(t.Name, leave.LeaveType)) is { } type)
                days.Add(new PolicyLeaveEntitlementDto { LeaveTypeId = type.Id, DefaultDays = leave.Days });
            else
                missing.Add(leave.LeaveType);
        }
        return (days, missing);
    }

    private static async Task<string> CopyPolicyAsync(IServiceProvider sp, FahPolicyCopy copy)
    {
        var (skip, detail) = await PolicyCheckAsync(sp, copy);
        if (skip) return $"skipped — {detail}";

        // Each company's own leave type ids (the plan is read afresh for
        // every Confirm, so setting them on it is this run's alone).
        (copy.Policy.LeaveEntitlements, _) = await LeaveDaysAsync(sp, copy);
        var result = await sp.GetRequiredService<IPolicyService>().CreateAsync(copy.Policy);
        return result.Ok ? $"done. {detail}" : $"not copied — {result.Error ?? "AltomateHR refused it."}";
    }

    // ─── Add an admin ────────────────────────────────────────────────────

    private async Task<(FahCompanyPlan?, string?)> PrepareAdminAsync(
        JsonElement input, IReadOnlyList<UserOrgDto> companies, UserOrgDto source, IReadOnlyList<UserOrgDto> targets)
    {
        var email = Text(input, "email");
        if (email is null) return (null, "Say whose access to copy, by their login email.");

        var (copy, error) = await _scopes.RunAsync(source.OrganizationId, source.Role, async sp =>
        {
            var admins = await sp.GetRequiredService<IOrganizationService>().ListAdminsAsync();
            var admin = admins.FirstOrDefault(a => Same(a.Email, email));
            if (admin is null)
                return ((FahAdminCopy?)null, (string?)$"{email} isn't an admin in {source.Name}, so there's no access to copy. "
                    + "Name a company where they're an admin, or ask the owner to add them there first (System Settings → Admins).");

            // An Owner's access is everything; an Admin's is what the Owner set.
            var owner = string.Equals(admin.Role, OrgRoles.Owner, StringComparison.OrdinalIgnoreCase);
            List<string>? policyNames = null;
            if (!owner && admin.PolicyIds is { } ids)
            {
                var names = (await sp.GetRequiredService<IPolicyService>().GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);
                policyNames = [.. ids.Where(names.ContainsKey).Select(id => names[id])];
            }
            return (new FahAdminCopy(
                admin.Email, admin.Name,
                owner ? null : admin.Levels,
                policyNames,
                owner || admin.CanChangeSettings), null);
        });
        if (copy is null) return (null, error);

        var steps = new List<FahCompanyStep>();
        foreach (var target in targets)
            steps.Add(await InCompanyAsync(target, async sp =>
            {
                var (skip, detail) = await AdminCheckAsync(sp, copy);
                return Step(target, companies, skip, detail);
            }));

        return (new FahCompanyPlan(IFahCompanyChanges.AddAdminTool, $"Add {copy.Name} as an admin",
            source.OrganizationId, source.Name, steps, Admin: copy), null);
    }

    // withAccess: say their access on each line (copied access), or not
    // (a new admin's, shown once above the companies).
    private static async Task<(bool Skip, string Detail)> AdminCheckAsync(IServiceProvider sp, FahAdminCopy copy, bool withAccess = true)
    {
        var member = (await sp.GetRequiredService<IEmployeeService>().GetAllAsync())
            .FirstOrDefault(e => Same(e.Email, copy.Email));
        if (member is not null)
            return (true, OrgRoles.IsAdministrative(member.Role)
                ? $"Already {(string.Equals(member.Role, OrgRoles.Owner, StringComparison.OrdinalIgnoreCase) ? "the owner" : "an admin")} here — left as they are."
                : $"Already in this company as {member.Role}. AltomateHR doesn't make an existing employee an admin.");

        var (policyIds, missing) = await PolicyIdsAsync(sp, copy);
        var detail = withAccess ? AccessText(copy) : "New admin.";
        if (missing.Count > 0)
            detail += policyIds is { Count: 0 }
                ? $" None of their policies ({string.Join(", ", missing)}) are here, so they'd see no employees until you choose policies for them."
                : $" No policy named {string.Join(", ", missing.Select(m => $"\"{m}\""))} here, so they won't see the people on it.";
        return (false, detail);
    }

    // Their employee limit by this company's own policies, and the names it
    // doesn't have. Null = every employee.
    private static async Task<(List<string>? Ids, List<string> Missing)> PolicyIdsAsync(IServiceProvider sp, FahAdminCopy copy)
    {
        if (copy.PolicyNames is null) return (null, []);
        var policies = (await sp.GetRequiredService<IPolicyService>().GetAllAsync()).ToList();
        var ids = new List<string>();
        var missing = new List<string>();
        foreach (var name in copy.PolicyNames)
        {
            if (policies.FirstOrDefault(p => Same(p.Name, name)) is { } policy) ids.Add(policy.Id);
            else missing.Add(name);
        }
        return (ids, missing);
    }

    private static async Task<string> AddAdminAsync(IServiceProvider sp, FahAdminCopy copy, bool withAccess)
    {
        var (skip, detail) = await AdminCheckAsync(sp, copy, withAccess);
        if (skip) return $"skipped — {detail}";

        // Added with no modules at all, then given their access: if the second
        // step fails they're locked out, never left with full access.
        var created = await sp.GetRequiredService<IEmployeeService>().CreateAsync(new CreateEmployeeDto
        {
            Email = copy.Email,
            Name = copy.Name,
            Role = OrgRoles.Admin,
            Modules = [],
            DateOfBirth = copy.DateOfBirth,
            SendWelcomeEmail = false,   // never from FAH: the Owner tells them, or resends it from the screen
        });
        if (!created.Ok || created.Employee is null) return $"not added — {created.Error ?? "AltomateHR refused it."}";

        var (policyIds, _) = await PolicyIdsAsync(sp, copy);
        var access = await sp.GetRequiredService<IOrganizationService>().SetAdminAccessAsync(created.Employee.Id, new SetAdminAccessDto
        {
            Levels = copy.Levels,
            PolicyIds = policyIds,
            CanChangeSettings = copy.CanChangeSettings,
        });
        return access is null
            ? "added, but their access couldn't be set, so they have no modules yet. Set it in System Settings → Admins there."
            : $"done. {detail}";
    }

    // ─── Create an admin ─────────────────────────────────────────────────

    private static FahTool CreateAdminTool(bool otherCompanies) => new(IFahCompanyChanges.CreateAdminTool,
        "Add someone as an admin, with the access the owner chooses. Before calling, ask the owner — unless they've "
        + "said — which modules (each View or Manage), which employees (everyone, or only those on certain "
        + "policies) and whether they may change settings; \"full access\" is an answer. Someone with an "
        + "AltomateHR account already keeps their password. A brand-new account needs their name and date of "
        + "birth: the first password is their email followed by the birthday as MMDD. Never ask for or set a "
        + "password. No welcome email is sent: the owner tells them, or resends it from System Settings → Admins. "
        + (otherCompanies
            ? "Added in the signed-in company unless `in` names others; only in companies the owner owns. "
            : "")
        + "The owner sees one card with the access and each company, with Confirm and Cancel; nothing changes "
        + "until they press Confirm. Someone already in a company is left as they are there.",
        Schema(new Dictionary<string, object>
        {
            ["email"] = new { type = "string", description = "Their login email." },
            ["name"] = new { type = "string", description = "Their full name. Needed for a new account." },
            ["dateOfBirth"] = new { type = "string", description = "YYYY-MM-DD. Needed only for a new account." },
            ["access"] = new
            {
                type = "object",
                description = "Either {\"full\": true}, or modules + employees + canChangeSettings.",
                properties = new Dictionary<string, object>
                {
                    ["full"] = new { type = "boolean", description = "Every module, every employee, may change settings." },
                    ["modules"] = new
                    {
                        type = "object",
                        description = "Module → \"View\" or \"Manage\"; a module left out is off. Modules: "
                            + string.Join(", ", OrgModules.GrantableModules) + ".",
                        additionalProperties = new { type = "string", @enum = new[] { "View", "Manage" } },
                    },
                    ["employees"] = new
                    {
                        description = "\"all\", or a list of policy names: only the people on those policies.",
                        anyOf = new object[] { new { type = "string", @enum = new[] { "all" } }, new { type = "array", items = new { type = "string" } } },
                    },
                    ["canChangeSettings"] = new { type = "boolean" },
                },
            },
            ["in"] = new
            {
                type = "array",
                items = new { type = "string" },
                description = otherCompanies
                    ? "The companies to add them in, by name (or id, where two share a name) — or [\"all\"] for the "
                        + "signed-in company and every company the owner owns. Default: the signed-in company."
                    : "Leave out: the signed-in company.",
            },
        }, "email", "access"));

    private async Task<(FahCompanyPlan?, string?)> PrepareCreateAdminAsync(JsonElement input, IReadOnlyList<UserOrgDto> companies)
    {
        if (Here(companies) is not { } here) return (null, "FAH can only add an admin for an admin of the signed-in company.");
        var email = Text(input, "email")?.ToLowerInvariant();
        if (email is null || !new EmailAddressAttribute().IsValid(email) || email.Length > 120)
            return (null, "Say their login email.");

        var (access, accessError) = Access(input);
        if (access is null) return (null, accessError);

        // A new account needs a name and the birthday its first password is
        // made from; an existing one keeps its own name and password.
        var account = await _users.GetByEmailAsync(email);
        var name = account?.Name ?? Text(input, "name");
        DateTime? born = null;
        if (account is null)
        {
            if (name is null) return (null, $"{email} has no AltomateHR account yet: ask for their full name.");
            if (Text(input, "dateOfBirth") is not { } dob)
                return (null, $"{email} has no AltomateHR account yet: ask for their date of birth. Their first "
                    + "password is their email followed by the birthday as MMDD. Don't ask for a password.");
            if (!DateTime.TryParseExact(dob, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date)
                || date >= DateTime.UtcNow.Date || date.Year < 1900)
                return (null, "dateOfBirth is a past date, YYYY-MM-DD.");
            born = date;
        }

        var (targets, targetError) = CreateTargets(input, companies, here);
        if (targetError is not null) return (null, targetError);

        var admin = access with { Email = email, Name = name!, DateOfBirth = born };
        var steps = new List<FahCompanyStep>();
        foreach (var target in targets)
            steps.Add(await InCompanyAsync(target, async sp =>
            {
                var (skip, detail) = await AdminCheckAsync(sp, admin, withAccess: false);
                return Step(target, companies, skip, detail);
            }));

        var who = account is null
            ? $"New AltomateHR account for {name}. First password: their email followed by their birthday as MMDD."
            : $"{name} already has an AltomateHR account and keeps their password.";
        return (new FahCompanyPlan(IFahCompanyChanges.CreateAdminTool, $"Add {name} as an admin",
            here.OrganizationId, here.Name, steps, Admin: admin,
            Settings: $"{email}\n{who}\nAccess: {AccessText(admin)}\nNo welcome email is sent: tell them, or resend it from System Settings → Admins."), null);
    }

    // The access the owner chose, or what's missing.
    private static (FahAdminCopy? Access, string? Error) Access(JsonElement input)
    {
        const string Ask = "Ask the owner what access they get: which modules (View or Manage), which employees "
            + "(everyone, or only those on certain policies) and whether they may change settings — or full access.";
        if (!input.TryGetProperty("access", out var access) || access.ValueKind != JsonValueKind.Object) return (null, Ask);
        if (access.TryGetProperty("full", out var full) && full.ValueKind == JsonValueKind.True)
            return (new FahAdminCopy("", "", null, null, true), null);

        if (!access.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Object
            || !access.TryGetProperty("employees", out var employees)
            || !access.TryGetProperty("canChangeSettings", out var settings) || settings.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return (null, Ask);

        var levels = new Dictionary<string, ModuleLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules.EnumerateObject())
        {
            var key = OrgModules.GrantableModules.FirstOrDefault(m => Same(m, module.Name));
            if (key is null)
                return (null, $"\"{module.Name}\" isn't a module. Modules: {string.Join(", ", OrgModules.GrantableModules)}.");
            if (module.Value.ValueKind != JsonValueKind.String || !Enum.TryParse<ModuleLevel>(module.Value.GetString(), true, out var level))
                return (null, $"{key} is \"View\" or \"Manage\".");
            levels[key] = level;
        }

        List<string>? policyNames;
        if (employees.ValueKind == JsonValueKind.String && Same(employees.GetString(), All)) policyNames = null;
        else if (employees.ValueKind == JsonValueKind.Array)
        {
            policyNames = [.. employees.EnumerateArray().Select(v => v.GetString()?.Trim()).OfType<string>().Where(v => v.Length > 0)];
            if (policyNames.Count == 0) return (null, "employees is \"all\" or the names of the policies whose people they see.");
        }
        else return (null, "employees is \"all\" or the names of the policies whose people they see.");

        return (new FahAdminCopy("", "", levels, policyNames, settings.ValueKind == JsonValueKind.True), null);
    }

    private static string AccessText(FahAdminCopy copy)
    {
        var text = new StringBuilder();
        if (copy.Levels is null)
            text.Append("Every module");
        else
        {
            var granted = copy.Levels.Where(l => l.Value != ModuleLevel.None).OrderBy(l => l.Key).ToList();
            text.Append(granted.Count == 0
                ? "No modules"
                : string.Join(", ", granted.Select(l => $"{Label(l.Key)}{(l.Value == ModuleLevel.View ? " (view)" : "")}")));
        }
        text.Append(copy.PolicyNames is null ? "; every employee" : $"; only people on {string.Join(", ", copy.PolicyNames)}");
        text.Append(copy.CanChangeSettings ? "; can change settings." : "; can't change settings.");
        return text.ToString();
    }

    private static string Label(string module) => module.Length == 0 ? module : char.ToUpperInvariant(module[0]) + module[1..];

    // ─── Confirm ─────────────────────────────────────────────────────────

    public async Task<(string Text, bool Made)> RunAsync(FahCompanyPlan plan)
    {
        // Who owns what now, not when the card was made.
        var companies = await CompaniesAsync();
        var creating = plan.Settings is not null;
        var text = new StringBuilder(creating ? $"{plan.Title}:" : $"{plan.Title}, from {plan.SourceCompany}:");
        var made = false;
        foreach (var step in plan.Steps)
        {
            string outcome;
            if (step.Skip)
                outcome = $"skipped — {step.Detail}";
            else if (plan.Tool == IFahCompanyChanges.CreatePolicyTool && step.OrganizationId == _currentUser.OrganizationId)
            {
                // The signed-in company: as the admin themselves.
                outcome = await CopyPolicyAsync(_services, plan.Policy!);
                made |= outcome.StartsWith("done", StringComparison.Ordinal);
            }
            else if (companies.FirstOrDefault(c => c.OrganizationId == step.OrganizationId) is not { } company || !IsOwner(company))
                outcome = "not done — the admin no longer owns this company.";
            else
            {
                outcome = await _scopes.RunAsync(company.OrganizationId, company.Role, sp => plan.Policy is { } policy
                    ? CopyPolicyAsync(sp, policy)
                    : AddAdminAsync(sp, plan.Admin!, withAccess: !creating));
                made |= outcome.StartsWith("done", StringComparison.Ordinal) || outcome.StartsWith("added", StringComparison.Ordinal);
            }
            text.Append($"\n- {step.Company}: {outcome}");
        }
        return (text.ToString(), made);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<UserOrgDto>> CompaniesAsync() =>
        [.. (await _auth.GetOrgsAsync(_currentUser.UserId!)).Where(o => !o.IsFormer && OrgRoles.IsAdministrative(o.Role))];

    private static bool IsOwner(UserOrgDto company) =>
        !company.IsFormer && string.Equals(company.Role, OrgRoles.Owner, StringComparison.OrdinalIgnoreCase);

    private static List<UserOrgDto> Owned(IReadOnlyList<UserOrgDto> companies) => [.. companies.Where(IsOwner)];

    private UserOrgDto? Here(IReadOnlyList<UserOrgDto> companies) =>
        companies.FirstOrDefault(c => c.OrganizationId == _currentUser.OrganizationId);

    // Work in a company: the signed-in one as the admin themselves, another
    // as its Owner (the caller has checked they own it).
    private Task<T> InAsync<T>(UserOrgDto company, Func<IServiceProvider, Task<T>> work) =>
        company.OrganizationId == _currentUser.OrganizationId
            ? work(_services)
            : _scopes.RunAsync(company.OrganizationId, company.Role, work);

    // The companies asked for, the source left out. "all" = every one they
    // own; a named one they don't own stays on the card as skipped, so the
    // admin sees why.
    private static (IReadOnlyList<UserOrgDto> Targets, string? Error) Targets(
        JsonElement input, IReadOnlyList<UserOrgDto> companies, UserOrgDto source)
    {
        var asked = input.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Array
            ? to.EnumerateArray().Select(v => v.GetString()?.Trim()).OfType<string>().Where(v => v.Length > 0).ToList()
            : [];
        if (asked.Count == 0) return ([], "Say which companies to make it in, or [\"all\"].");

        var targets = new List<UserOrgDto>();
        foreach (var name in asked)
        {
            if (Same(name, All))
            {
                targets.AddRange(Owned(companies));
                continue;
            }
            var company = FahCompanyList.Find(companies, name);
            if (company is null) return ([], Unknown(companies, name));
            targets.Add(company);
        }
        targets = [.. targets.Where(t => t.OrganizationId != source.OrganizationId).DistinctBy(t => t.OrganizationId)];
        return targets.Count == 0
            ? ([], $"There's no company to make it in besides {source.Name}.")
            : (targets, null);
    }

    // A company's line on the card: what will happen there, or — where the
    // admin doesn't own it — why not.
    private async Task<FahCompanyStep> InCompanyAsync(UserOrgDto company, Func<IServiceProvider, Task<FahCompanyStep>> check) =>
        IsOwner(company)
            ? await _scopes.RunAsync(company.OrganizationId, company.Role, check)
            : new FahCompanyStep(company.OrganizationId, company.Name, true, $"You're {company.Role} here, not the owner.");

    private static FahCompanyStep Step(UserOrgDto company, IReadOnlyList<UserOrgDto> companies, bool skip, string detail) =>
        new(company.OrganizationId, FahCompanyList.Label(companies, company), skip, detail);

    private static string Unknown(IReadOnlyList<UserOrgDto> companies, string asked) =>
        $"\"{asked}\" isn't one of the admin's companies, or more than one has that name. Theirs: "
        + $"{string.Join(", ", companies.Select(c => FahCompanyList.Label(companies, c)))}. Two with the same name are told apart by id.";

    private static string? Text(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && v.GetString()?.Trim() is { Length: > 0 } s ? s : null;

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static JsonElement Schema(Dictionary<string, object> properties, params string[] required) =>
        JsonSerializer.SerializeToElement(new { type = "object", properties, required });

    private static SavePolicyDto ToSave(PolicyDto p) => new()
    {
        Name = p.Name,
        Description = p.Description,
        CanAccessAttendance = p.CanAccessAttendance,
        CanAccessClaims = p.CanAccessClaims,
        CanAccessLeave = p.CanAccessLeave,
        RequireGeofence = p.RequireGeofence,
        RequireSelfie = p.RequireSelfie,
        RequireClockOutSelfie = p.RequireClockOutSelfie,
        CaptureLocationOnBreakStart = p.CaptureLocationOnBreakStart,
        CaptureLocationOnBreakEnd = p.CaptureLocationOnBreakEnd,
        RequireIpWhitelist = p.RequireIpWhitelist,
        GeolocationEnabled = p.GeolocationEnabled,
        CaptureLocationOnClockIn = p.CaptureLocationOnClockIn,
        CaptureLocationOnClockOut = p.CaptureLocationOnClockOut,
        AutoClockOutEnabled = p.AutoClockOutEnabled,
        AutoClockOutAfterMinutes = p.AutoClockOutAfterMinutes,
        SalaryType = p.SalaryType,
        OtEnabled = p.OtEnabled,
        OtDailyThresholdMinutes = p.OtDailyThresholdMinutes,
        OtMethod = p.OtMethod,
        OtRateNormalDay = p.OtRateNormalDay,
        OtRatePublicHoliday = p.OtRatePublicHoliday,
        OtRateRestDay = p.OtRateRestDay,
        OtRatePublicHolidayInShift = p.OtRatePublicHolidayInShift,
        OtRateRestDayInShift = p.OtRateRestDayInShift,
        OtSalaryThreshold = p.OtSalaryThreshold,
        Temporary = p.Temporary,
    };
}
