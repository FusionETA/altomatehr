using System.Globalization;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Mid-cycle salary changes.
//
// The calculator reads ONE salary off the profile and pays it for the whole
// month. When a raise takes effect on the 15th, that is wrong by the prorated
// delta — in which direction depends on whether the admin saved the new figure
// before or after generating the run.
//
// This does NOT auto-prorate, and that is deliberate. Every Malaysian payroll
// product this competes with — PayrollPanda, HReasily, Talenox, Kakitangan —
// leaves the correction to the admin, and the reference does too. Silently
// moving someone's pay because a date was typed on a profile is worse than
// asking: the admin knows whether the raise was meant to be backdated, and the
// engine does not. What this removes is the arithmetic and the chance of
// forgetting entirely.
//
// Pure: no EF, no clock.
public static class SalaryChangeHints
{
    // How the payslip that exists relates to the salary that should have
    // applied.
    public enum Scenario
    {
        // The run paid the NEW, higher rate for the whole month. Claw back
        // the pre-change days.
        OVERPAID,

        // The run paid the OLD rate for the whole month. Owe arrears for the
        // post-change days.
        UNDERPAID,

        // Effective on day one of the period, so there is nothing to prorate.
        MATCHED,

        // The payslip's snapshot matches neither side — a second change, a
        // hand-edit in between, or a salary-type switch. Surfaced so the
        // admin sees it, but no figure is suggested.
        UNKNOWN,
    }

    public sealed record Hint
    {
        public required string PayslipId { get; init; }
        public required string EmployeeProfileId { get; init; }
        public required string EmployeeName { get; init; }
        public required string SalaryChangeId { get; init; }

        public required DateTime EffectiveDate { get; init; }
        public decimal PreviousMonthlySalary { get; init; }
        public decimal NewMonthlySalary { get; init; }
        public required string ReasonLabel { get; init; }

        // What the payslip was actually generated against. This is what
        // decides the scenario, and it lets the UI say "this payslip shows
        // RM X but should be RM Y".
        public decimal PayslipSnapshotMonthlySalary { get; init; }

        public Scenario Outcome { get; init; }
        public WorkingDaysRule ProrationRule { get; init; }
        public int TotalDaysInPeriod { get; init; }
        public int DaysAtOldRate { get; init; }
        public int DaysAtNewRate { get; init; }

        // Always non-negative — the direction is in `Outcome`. Signing it as
        // well would invite a caller to apply the sign twice.
        public decimal Delta { get; init; }

        // The row to add to the run's adjustment. Null when there is nothing
        // to do, or when it has already been applied.
        public SuggestedLine? SuggestedLineItem { get; init; }

        // True when a line carrying this hint's marker is already on the run.
        public bool AlreadyApplied { get; init; }
    }

    public sealed record SuggestedLine(
        PayslipLineKind Kind, string Category, string Label, decimal Amount);

    public sealed record Input
    {
        public required string PayslipId { get; init; }
        public required string EmployeeProfileId { get; init; }
        public required string EmployeeName { get; init; }
        public required decimal PayslipSnapshotMonthlySalary { get; init; }
        public required SalaryChange Change { get; init; }
        public required int PeriodYear { get; init; }
        public required int PeriodMonth { get; init; }
        public required WorkingDaysRule ProrationRule { get; init; }

        // The labels already on this employee's adjustment for this run, so
        // the hint can tell it has been applied and stop asking.
        public IReadOnlyList<string> ExistingManualLineLabels { get; init; } = [];
    }

    // Embedded in the suggested line's label so an applied hint can be
    // recognised on the next page load. Deterministic, so the match is exact
    // rather than a guess at the wording.
    public static string Marker(string salaryChangeId) => $"[salary-hint:{salaryChangeId}]";

    // Null when the change needs no hint at all: a salary-type switch (the
    // arithmetic is a different shape and is left to the admin), or a change
    // that did not move the figure.
    public static Hint? Compute(Input input)
    {
        var change = input.Change;

        if (change.PreviousSalaryType != SalaryType.MONTHLY
            || change.NewSalaryType != SalaryType.MONTHLY
            || change.PreviousMonthlySalary is null
            || change.NewMonthlySalary is null)
        {
            return null;
        }

        var oldSalary = change.PreviousMonthlySalary.Value;
        var newSalary = change.NewMonthlySalary.Value;
        if (oldSalary == newSalary) return null;

        var (totalDays, daysAtOld, daysAtNew) = DaySplit(
            input.PeriodYear, input.PeriodMonth, change.EffectiveDate, input.ProrationRule);

        var baseHint = new Hint
        {
            PayslipId = input.PayslipId,
            EmployeeProfileId = input.EmployeeProfileId,
            EmployeeName = input.EmployeeName,
            SalaryChangeId = change.Id,
            EffectiveDate = change.EffectiveDate,
            PreviousMonthlySalary = oldSalary,
            NewMonthlySalary = newSalary,
            ReasonLabel = ReasonLabel(change.Reason),
            PayslipSnapshotMonthlySalary = input.PayslipSnapshotMonthlySalary,
            ProrationRule = input.ProrationRule,
            TotalDaysInPeriod = totalDays,
            DaysAtOldRate = daysAtOld,
            DaysAtNewRate = daysAtNew,
        };

        // Effective on the first of the month: the whole period is at the new
        // rate, so paying one salary for the month is correct and there is
        // nothing to nag about.
        if (daysAtOld == 0)
        {
            return baseHint with { Outcome = Scenario.MATCHED, Delta = 0m };
        }

        var marker = Marker(change.Id);
        var alreadyApplied = input.ExistingManualLineLabels
            .Any(l => l.Contains(marker, StringComparison.Ordinal));

        // A sen of tolerance: the snapshot is a stored decimal and the
        // comparison should not turn on the last place.
        var snapshot = input.PayslipSnapshotMonthlySalary;
        var matchesNew = Math.Abs(snapshot - newSalary) < 0.01m;
        var matchesOld = Math.Abs(snapshot - oldSalary) < 0.01m;

        var salaryDelta = newSalary - oldSalary;

        if (matchesNew)
        {
            // The run used the new rate all month. The pre-change days were
            // overpaid by the delta.
            var delta = Money.Round2(salaryDelta * daysAtOld / totalDays);

            return baseHint with
            {
                Outcome = Scenario.OVERPAID,
                Delta = Math.Abs(delta),
                AlreadyApplied = alreadyApplied,
                SuggestedLineItem = alreadyApplied ? null : new SuggestedLine(
                    PayslipLineKind.DEDUCTION,
                    PayrollAdjustmentCategories.DeductSalaryAdjustment,
                    Label("deduct", change, oldSalary, newSalary),
                    Math.Abs(delta)),
            };
        }

        if (matchesOld)
        {
            // The run used the old rate all month. The post-change days are
            // owed as arrears.
            var delta = Money.Round2(salaryDelta * daysAtNew / totalDays);

            return baseHint with
            {
                Outcome = Scenario.UNDERPAID,
                Delta = Math.Abs(delta),
                AlreadyApplied = alreadyApplied,
                SuggestedLineItem = alreadyApplied ? null : new SuggestedLine(
                    PayslipLineKind.ALLOWANCE,
                    PayrollAdjustmentCategories.WagesArrears,
                    Label("arrears", change, oldSalary, newSalary),
                    Math.Abs(delta)),
            };
        }

        // Neither side matches. Suggesting a figure here would be a guess at
        // which of several edits the admin meant.
        return baseHint with
        {
            Outcome = Scenario.UNKNOWN,
            Delta = 0m,
            AlreadyApplied = alreadyApplied,
        };
    }

    // The before/after split is always CALENDAR days — the 15th is the 15th
    // whatever basis the org pays on. The org's rule decides the DIVISOR only.
    private static (int TotalDays, int DaysAtOld, int DaysAtNew) DaySplit(
        int periodYear, int periodMonth, DateTime effectiveDate, WorkingDaysRule rule)
    {
        var calendarDays = PayPeriod.CalendarDaysInMonth(periodYear, periodMonth);
        var totalDays = rule == WorkingDaysRule.TWENTY_SIX ? 26 : calendarDays;

        var periodStart = new DateTime(periodYear, periodMonth, 1);
        var periodEnd = new DateTime(periodYear, periodMonth, calendarDays);
        var effective = effectiveDate.Date;

        // Outside the period this should not have been called, but a hint is
        // not worth throwing over.
        if (effective < periodStart || effective > periodEnd) return (totalDays, 0, 0);

        // The effective date itself is paid at the NEW rate, so the old rate
        // covers the days before it.
        var daysAtOld = effective.Day - 1;

        return (totalDays, daysAtOld, calendarDays - daysAtOld);
    }

    // The human half is what lands on a payslip; the marker at the end is
    // what the next page load matches on.
    private static string Label(
        string direction, SalaryChange change, decimal oldSalary, decimal newSalary) =>
        $"Mid-cycle salary change {direction} (effective "
        + $"{change.EffectiveDate:yyyy-MM-dd}, "
        + $"{oldSalary.ToString("0", CultureInfo.InvariantCulture)} → "
        + $"{newSalary.ToString("0", CultureInfo.InvariantCulture)}) {Marker(change.Id)}";

    public static string ReasonLabel(SalaryChangeReason reason) => reason switch
    {
        SalaryChangeReason.RAISE => "Raise",
        SalaryChangeReason.PROMOTION => "Promotion",
        SalaryChangeReason.DEMOTION => "Demotion",
        SalaryChangeReason.RESTRUCTURE => "Restructure",
        _ => "Other",
    };

    // The percentage move, for the history view. Null when either side is
    // missing or the salary type changed — a percentage across a
    // monthly-to-hourly switch means nothing.
    public static decimal? RaisePercent(SalaryChange change)
    {
        if (change.PreviousSalaryType != SalaryType.MONTHLY
            || change.NewSalaryType != SalaryType.MONTHLY)
        {
            return null;
        }

        var previous = change.PreviousMonthlySalary ?? 0m;
        var next = change.NewMonthlySalary ?? 0m;
        if (previous <= 0m || next <= 0m) return null;

        return Money.Round2((next - previous) / previous * 100m);
    }
}
