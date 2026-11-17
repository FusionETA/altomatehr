using System.Text.Json;
using AltomateHR.Api.Modules.PayrollCalculator.Dtos;

namespace AltomateHR.Api.Modules.PayrollCalculator;

// Reads the widget's nine fields out of the raw JSON body and validates them,
// so every problem comes back in the handover's shape — { field, code, message }
// with a message a visitor can act on — rather than ASP.NET's model-binding
// ProblemDetails, which the widget can't place under a field. Pure: no I/O.
//
// Fields are checked in the form's order and the first problem wins. Extra
// keys are ignored.
public static class PayrollCalculatorRequestParser
{
    public const string ErrRequired = "required";
    public const string ErrInvalidType = "invalid_type";
    public const string ErrInvalidValue = "invalid_value";
    public const string ErrOutOfRange = "out_of_range";

    public static (PayrollCalculatorRequest? Request, PayrollCalculatorError? Error) Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return Fail(null, ErrInvalidType, "Send the calculator's fields as a JSON object.");

        if (Money(body, "monthly_salary", "Enter a monthly salary.", out var salary) is { } e1) return Fail(e1);
        if (salary <= 0m) return Fail("monthly_salary", ErrOutOfRange, "Enter a monthly salary above RM0.");
        if (salary > 1_000_000m) return Fail("monthly_salary", ErrOutOfRange, "Enter a monthly salary of RM1,000,000 or less.");

        if (Money(body, "bonus", "Enter a bonus, or 0 if there is none.", out var bonus) is { } e2) return Fail(e2);
        if (bonus < 0m || bonus > 10_000_000m) return Fail("bonus", ErrOutOfRange, "Enter a bonus between RM0 and RM10,000,000.");

        if (Choice(body, "citizenship", "Choose Malaysian / PR or foreign worker.",
                [("malaysian_pr", CalculatorCitizenship.MalaysianPr), ("foreign", CalculatorCitizenship.Foreign)],
                out var citizenship) is { } e3) return Fail(e3);

        if (Choice(body, "age_group", "Choose below 60 or 60 and above.",
                [("below_60", CalculatorAgeGroup.Below60), ("60_and_above", CalculatorAgeGroup.SixtyAndAbove)],
                out var ageGroup) is { } e4) return Fail(e4);

        if (Choice(body, "marital_status", "Choose single, married with a non-working spouse, or married with a working spouse.",
                [("single", CalculatorMaritalStatus.Single),
                 ("married_spouse_not_working", CalculatorMaritalStatus.MarriedSpouseNotWorking),
                 ("married_spouse_working", CalculatorMaritalStatus.MarriedSpouseWorking)],
                out var marital) is { } e5) return Fail(e5);

        if (!body.TryGetProperty("children", out var childrenEl) || childrenEl.ValueKind == JsonValueKind.Null)
            return Fail("children", ErrRequired, "Enter the number of children, or 0.");
        if (childrenEl.ValueKind != JsonValueKind.Number || !childrenEl.TryGetInt32(out var children))
            return Fail("children", ErrInvalidType, "Enter the number of children as a whole number.");
        if (children < 0 || children > 10) return Fail("children", ErrOutOfRange, "Enter between 0 and 10 children.");

        if (!body.TryGetProperty("tax_resident", out var residentEl) || residentEl.ValueKind == JsonValueKind.Null)
            return Fail("tax_resident", ErrRequired, "Choose whether the employee is a tax resident.");
        if (residentEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Fail("tax_resident", ErrInvalidType, "Choose yes or no for tax resident.");

        if (Money(body, "other_reliefs_yearly", "Enter other reliefs, or 0.", out var reliefs) is { } e6) return Fail(e6);
        if (reliefs < 0m || reliefs > 1_000_000m) return Fail("other_reliefs_yearly", ErrOutOfRange, "Enter other reliefs between RM0 and RM1,000,000 a year.");

        if (Money(body, "zakat_monthly", "Enter monthly zakat, or 0.", out var zakat) is { } e7) return Fail(e7);
        if (zakat < 0m || zakat > 100_000m) return Fail("zakat_monthly", ErrOutOfRange, "Enter monthly zakat between RM0 and RM100,000.");

        return (new PayrollCalculatorRequest(
            salary, bonus, citizenship, ageGroup, marital,
            // Children only count toward relief for a married employee; the widget
            // sends 0 when single, but a single employee's children are ignored anyway.
            marital == CalculatorMaritalStatus.Single ? 0 : children,
            residentEl.ValueKind == JsonValueKind.True,
            reliefs, zakat), null);
    }

    // A ringgit amount: a JSON number with at most two decimals.
    private static PayrollCalculatorError? Money(JsonElement body, string key, string requiredMessage, out decimal value)
    {
        value = 0m;
        if (!body.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null)
            return new(key, ErrRequired, requiredMessage);
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDecimal(out value))
            return new(key, ErrInvalidType, "Enter an amount in ringgit, numbers only.");
        if (decimal.Round(value, 2) != value)
            return new(key, ErrInvalidValue, "Enter the amount to at most 2 decimal places.");
        return null;
    }

    private static PayrollCalculatorError? Choice<T>(
        JsonElement body, string key, string message, (string Wire, T Value)[] options, out T value)
    {
        value = default!;
        if (!body.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null)
            return new(key, ErrRequired, message);
        if (el.ValueKind != JsonValueKind.String) return new(key, ErrInvalidType, message);

        var wire = el.GetString();
        foreach (var option in options)
        {
            if (string.Equals(option.Wire, wire, StringComparison.Ordinal))
            {
                value = option.Value;
                return null;
            }
        }
        return new(key, ErrInvalidValue, message);
    }

    private static (PayrollCalculatorRequest?, PayrollCalculatorError?) Fail(PayrollCalculatorError error) => (null, error);

    private static (PayrollCalculatorRequest?, PayrollCalculatorError?) Fail(string? field, string code, string message) =>
        (null, new PayrollCalculatorError(field, code, message));
}
