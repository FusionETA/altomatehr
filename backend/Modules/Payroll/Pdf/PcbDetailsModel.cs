namespace AltomateHR.Api.Modules.Payroll.Pdf;

// What the PCB calculation-details PDF renders. One entry per payslip on the
// run, in the same order every other document uses.
public sealed record PcbDetailsModel
{
    public required string OrganizationName { get; init; }

    public required string PeriodLabel { get; init; }

    public IReadOnlyList<PcbDetailsEmployee> Employees { get; init; } = [];
}

public sealed record PcbDetailsEmployee
{
    public required string Name { get; init; }

    public string? Position { get; init; }

    public string? EmployeeCode { get; init; }

    // Null when the payslip has no stored breakdown — either it predates the
    // column being filled, or its JSON failed to parse. The renderer says so
    // on the page instead of printing a page of zeroes, which would read as
    // "no tax was due" rather than "we do not have the working".
    //
    // ⚠ This is DESERIALISED from `Payslip.PcbCalculationJson`. It is never
    // recomputed: the snapshot is the month's law, and running today's engine
    // over a historical payslip is how a filed figure quietly changes.
    public PcbBreakdown? Breakdown { get; init; }
}
