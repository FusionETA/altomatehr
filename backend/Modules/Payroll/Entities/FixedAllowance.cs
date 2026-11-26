namespace AltomateHR.Api.Modules.Payroll.Entities;

// One recurring adjustment that applies to an employee on every run. Persisted
// as a JSON array in `EmployeeProfile.FixedAllowancesJson` — the Employees
// module owns the column, payroll owns the shape.
//
// The `Category` string is a `PayrollAdjustmentCategories` code. It is a string
// rather than an enum because it is stored in free-form JSON written by an
// older system: an unrecognised code has to be skippable, not a deserialisation
// failure that takes the whole run down.
public sealed record FixedAllowance
{
    public string Category { get; init; } = PayrollAdjustmentCategories.AllowanceStandard;

    // The admin's own label for this row. Falls back to the category's label.
    public string? Name { get; init; }

    public decimal Amount { get; init; }

    // Only meaningful on an `IsAdditionalRemuneration` category. Ticking it
    // routes the amount through the smoothed monthly PCB path instead of LHDN's
    // one-shot additional-remuneration formula — an annual bonus paid in twelve
    // equal parts really is recurring, and taxing it as a spike over-withholds.
    //
    // It has never controlled EPF: EPF Act 1991 s.2 reads "wages" broadly, so a
    // bonus always joins the regular wage that picks the KWSP Third Schedule
    // tier. Letting one flag drive both forced admins to choose between right
    // EPF and right PCB on the same line.
    public bool TreatAsRecurring { get; init; }
}

// Reads `EmployeeProfile.FixedAllowancesJson` one row at a time.
//
// A strict read of the whole array failed on a single bad row — a blank
// amount (`"amount": null`, which the profile editor saves for a row left
// empty) or a `null` element — and returned an EMPTY list, so every recurring
// allowance that person had silently left gross, EPF and PCB. Now each row is
// read on its own: an unreadable row becomes an RM 0 row (the calculator skips
// non-positive amounts) and keeps its POSITION, because run overrides are keyed
// by the array index. Text that isn't a JSON array still reads as no rows.
public static class FixedAllowanceJson
{
    public static IReadOnlyList<FixedAllowance> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];

            var rows = new List<FixedAllowance>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    rows.Add(new FixedAllowance { Amount = 0m });
                    continue;
                }
                var category = Text(item, "category");
                rows.Add(new FixedAllowance
                {
                    Category = string.IsNullOrWhiteSpace(category) ? PayrollAdjustmentCategories.AllowanceStandard : category,
                    Name = Text(item, "name"),
                    Amount = Number(item, "amount"),
                    TreatAsRecurring = Flag(item, "treatAsRecurring"),
                });
            }
            return rows;
        }
    }

    private static System.Text.Json.JsonElement? Prop(System.Text.Json.JsonElement item, string name)
    {
        foreach (var p in item.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? Text(System.Text.Json.JsonElement item, string name) =>
        Prop(item, name) is { ValueKind: System.Text.Json.JsonValueKind.String } v ? v.GetString() : null;

    // A number, or a number written as text ("300"); anything else is RM 0.
    private static decimal Number(System.Text.Json.JsonElement item, string name) =>
        Prop(item, name) switch
        {
            { ValueKind: System.Text.Json.JsonValueKind.Number } v when v.TryGetDecimal(out var d) => d,
            { ValueKind: System.Text.Json.JsonValueKind.String } v when decimal.TryParse(
                v.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => 0m,
        };

    private static bool Flag(System.Text.Json.JsonElement item, string name) =>
        Prop(item, name) is { ValueKind: System.Text.Json.JsonValueKind.True };
}
