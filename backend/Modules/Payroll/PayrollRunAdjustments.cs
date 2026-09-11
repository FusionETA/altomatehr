using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Turns an employee's profile-level fixed allowances plus this run's
// adjustment row into the single list the calculator actually prices.
//
// Pure and static like the rest of this module root: no EF, no clock, no HTTP.
//
// Why merge rather than give the calculator a second input list: the
// calculator already has one category-aware routing loop that decides which of
// the six wage bases each row lands in, applies annual exemption ceilings and
// splits additional remuneration out for PCB. A one-off deduction has to obey
// every one of those rules exactly as a recurring one does, so the cheapest way
// to be sure it does is to hand it to the same loop.
public static class PayrollRunAdjustments
{
    // The JSON in these columns is written by this app, but is still parsed
    // defensively — a malformed blob must not take a whole month's payroll down.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static IReadOnlyList<ManualLineItem> ParseManualLineItems(string? json) =>
        Parse<List<ManualLineItem>>(json) ?? [];

    public static IReadOnlyDictionary<string, FixedAllowanceOverride> ParseOverrides(string? json) =>
        Parse<Dictionary<string, FixedAllowanceOverride>>(json)
        ?? new Dictionary<string, FixedAllowanceOverride>();

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ─── The merge ──────────────────────────────────────────────────────

    // Profile rows with this run's overrides applied, followed by this run's
    // one-off rows. Order matters only for how the payslip reads: recurring
    // pay first, then what was special about this month.
    public static IReadOnlyList<FixedAllowance> Merge(
        IReadOnlyList<FixedAllowance> profileAllowances,
        PayrollRunAdjustment? adjustment)
    {
        if (adjustment is null) return profileAllowances;

        var merged = ApplyOverrides(
            profileAllowances,
            ParseOverrides(adjustment.FixedAllowanceOverridesJson));

        foreach (var item in ParseManualLineItems(adjustment.ManualLineItemsJson))
        {
            merged.Add(new FixedAllowance
            {
                Category = item.Category,
                Name = item.Label,
                Amount = item.Amount,
                TreatAsRecurring = item.TreatAsRecurring,
            });
        }

        return merged;
    }

    // Apply the sparse index → override map over the profile's rows.
    //
    //   skip            → the row is dropped for this run
    //   amount non-null → the amount is replaced
    //   neither         → the profile row stands
    //
    // The record `with` is what keeps the trap shut: everything except the
    // amount — the CATEGORY above all — survives the override, so the six
    // `SubjectTo*` flags, the exemption ceiling and the additional-remuneration
    // routing all still follow the original row.
    public static List<FixedAllowance> ApplyOverrides(
        IReadOnlyList<FixedAllowance> profileAllowances,
        IReadOnlyDictionary<string, FixedAllowanceOverride> overrides)
    {
        var result = new List<FixedAllowance>(profileAllowances.Count);

        for (var i = 0; i < profileAllowances.Count; i++)
        {
            var row = profileAllowances[i];

            // Keys are the array index as a string. An override pointing past
            // the end of the array (the profile lost a row after the override
            // was saved) simply never matches, which is the safe direction:
            // the profile's own rows are paid as they stand.
            if (!overrides.TryGetValue(
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    out var over))
            {
                result.Add(row);
                continue;
            }

            if (over.Skip) continue;

            result.Add(over.Amount is null ? row : row with { Amount = over.Amount.Value });
        }

        return result;
    }
}
