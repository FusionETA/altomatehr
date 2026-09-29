using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// A previous employer's TP3 figures item by item (EmployeeProfile.PrevByCategoryJson).
//
// The yearly limits — a RM 6,000 travel-allowance exemption, a RM 2,500
// lifestyle relief — are the EMPLOYEE's for the year, not each employer's.
// TP3's totals cannot say how much of each is already used, so they are
// declared per category here and folded into the per-category year to date
// that PayslipCalculator reads for every exemption ceiling and TP1 limit.
//
//   • A tax-exempt allowance only uses up its ceiling. It is not pay (the
//     previous employer never taxed it, so it is not in PrevRemuneration)
//     and not a relief, so it adds nothing to Y or ΣLP.
//   • A TP1 item uses up its limit AND was relief already given, so it adds
//     to ΣLP — at most its own yearly limit.
//
// Pure: no EF, no clock. Malformed or unknown rows are dropped rather than
// failing a payroll run, which under-relieves rather than over-relieves.
public static class PreviousEmployerItems
{
    public sealed record Item(string Category, decimal Amount);

    public sealed record Result(IReadOnlyDictionary<string, decimal> ByCategory, decimal Tp1Relief);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static IReadOnlyList<Item> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var rows = JsonSerializer.Deserialize<List<Row>>(json, Json) ?? [];
            return rows
                .Where(r => r.Category is not null && r.Amount is > 0m
                            && PayrollAdjustmentCategories.Find(r.Category) is { } meta
                            && PayrollAdjustmentCategories.CarriesFromPreviousEmployer(meta))
                .Select(r => new Item(r.Category!, r.Amount!.Value))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // `includesOwnOrgYtd` false = a rehire whose declared figures already cover
    // their earlier months here, so this org's own YTD is taken back out first
    // (floored at zero), as PayrollRunService.Carry does for the totals.
    public static Result Carry(
        IReadOnlyList<Item> items,
        IReadOnlyDictionary<string, decimal> ownOrgByCategory,
        bool isPriorEmployerOnly = true)
    {
        var merged = new Dictionary<string, decimal>(ownOrgByCategory, StringComparer.Ordinal);
        var tp1Relief = 0m;

        foreach (var group in items.GroupBy(i => i.Category, StringComparer.Ordinal))
        {
            var declared = group.Sum(i => i.Amount);
            var own = merged.GetValueOrDefault(group.Key);
            var carried = isPriorEmployerOnly ? declared : Math.Max(0m, declared - own);
            if (carried <= 0m) continue;

            merged[group.Key] = own + carried;

            var meta = PayrollAdjustmentCategories.Find(group.Key)!;
            if (meta.FeedsLp1Relief)
            {
                tp1Relief += meta.TaxExemptLimit is > 0m and var cap ? Math.Min(carried, cap) : carried;
            }
        }

        return new Result(merged, Money.Round2(tp1Relief));
    }

    private sealed class Row
    {
        public string? Category { get; set; }
        public decimal? Amount { get; set; }
    }
}
