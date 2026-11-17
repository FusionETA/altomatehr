using System.Text.Json.Serialization;

namespace AltomateHR.Api.Modules.PayrollCalculator.Dtos;

// The public payroll calculator's contract with the altomate.io widget
// (docs: payroll-calculator-api-handover.md, the widget's openapi.yaml). The
// widget reads these names exactly, so they are snake_case and must never be
// renamed or removed without telling web. Adding a field is fine.

public enum CalculatorCitizenship { MalaysianPr, Foreign }

public enum CalculatorAgeGroup { Below60, SixtyAndAbove }

public enum CalculatorMaritalStatus { Single, MarriedSpouseNotWorking, MarriedSpouseWorking }

// The nine request fields, after validation.
public sealed record PayrollCalculatorRequest(
    decimal MonthlySalary,
    decimal Bonus,
    CalculatorCitizenship Citizenship,
    CalculatorAgeGroup AgeGroup,
    CalculatorMaritalStatus MaritalStatus,
    int Children,
    bool TaxResident,
    decimal OtherReliefsYearly,
    decimal ZakatMonthly);

public sealed record PayrollCalculatorResponse
{
    [JsonPropertyName("tax_year")] public required int TaxYear { get; init; }
    [JsonPropertyName("gross")] public required decimal Gross { get; init; }
    [JsonPropertyName("take_home")] public required decimal TakeHome { get; init; }
    [JsonPropertyName("employer_cost")] public required decimal EmployerCost { get; init; }
    [JsonPropertyName("employee_total")] public required decimal EmployeeTotal { get; init; }
    [JsonPropertyName("employer_total")] public required decimal EmployerTotal { get; init; }
    [JsonPropertyName("items")] public required IReadOnlyList<PayrollCalculatorItem> Items { get; init; }
}

public sealed record PayrollCalculatorItem
{
    [JsonPropertyName("code")] public required string Code { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }

    [JsonPropertyName("sub_label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubLabel { get; init; }

    [JsonPropertyName("employee")] public required decimal Employee { get; init; }
    [JsonPropertyName("employer")] public required decimal Employer { get; init; }
    [JsonPropertyName("working")] public required IReadOnlyList<string> Working { get; init; }
}

public sealed record PayrollCalculatorErrorBody(
    [property: JsonPropertyName("error")] PayrollCalculatorError Error);

public sealed record PayrollCalculatorError(
    [property: JsonPropertyName("field")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Field,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

// What the service hands back: a result, or a visitor-facing error.
public sealed record PayrollCalculatorOutcome(
    PayrollCalculatorResponse? Result, PayrollCalculatorError? Error);
