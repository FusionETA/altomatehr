using System.Globalization;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.PayrollCalculator.Dtos;

namespace AltomateHR.Api.Modules.PayrollCalculator;

// The "how this was worked out" lines under each item: what rate, on what
// amount, giving what — written for a business owner, one idea per sentence,
// the visitor's own figures as RM1,234.56.
//
// Every figure is READ from the engine's result (its rates snapshot, PERKESO's
// table rows, PCB's breakdown). Nothing is recomputed here, so a sentence can
// never describe a different number from the one charged.
public static class PayrollCalculatorWorking
{
    private const int MaxSentences = 4;

    public static IReadOnlyList<string> Epf(PayrollCalculatorRequest request, PayslipCalculator.Result r)
    {
        var rates = r.EpfRates;
        var lines = new List<string>();

        switch (rates.Branch)
        {
            case EpfBranch.MALAYSIAN_CITIZEN_60_PLUS:
                lines.Add("From age 60 there is no employee EPF.");
                lines.Add($"Employer {Pct(rates.Employer)}% = {Rm(r.EpfEmployer)}.");
                break;

            case EpfBranch.POST_1998_NON_MALAYSIAN:
                lines.Add($"Foreign workers pay {Pct(rates.Employee)}% and their employer {Pct(rates.Employer)}%, required since October 2025.");
                lines.Add($"Employee {Rm(r.EpfEmployee)}, employer {Rm(r.EpfEmployer)}.");
                break;

            default:
                lines.Add(request.MonthlySalary <= 5000m
                    ? $"Employee {Pct(rates.Employee)}%. Employer {Pct(rates.Employer)}% because the salary is RM5,000 or less."
                    : $"Employee {Pct(rates.Employee)}%. Employer {Pct(rates.Employer)}% because the salary is above RM5,000.");
                lines.Add(request.Bonus > 0m
                    ? $"EPF is charged on salary plus bonus, {Rm(request.MonthlySalary + request.Bonus)}: employee {Rm(r.EpfEmployee)}, employer {Rm(r.EpfEmployer)}."
                    : $"KWSP's contribution table for {Rm(request.MonthlySalary)} gives employee {Rm(r.EpfEmployee)}, employer {Rm(r.EpfEmployer)}.");
                break;
        }

        return lines;
    }

    public static IReadOnlyList<string> Socso(PayrollCalculatorRequest request, PayslipCalculator.Result r)
    {
        var lines = new List<string>();
        var foreign = request.Citizenship == CalculatorCitizenship.Foreign;

        if (foreign)
        {
            lines.Add($"Foreign workers have employment injury cover only, paid by the employer: {Rm(r.SocsoEmployer)}.");
            if (r.SkbbkEmployee > 0m) lines.Add($"The employee pays {Rm(r.SkbbkEmployee)} for Lindung 24, which foreign workers must contribute to.");
        }
        else if (r.SocsoEmployee == 0m)
        {
            // Category 2: the 60+ (the engine flips them whatever the profile says).
            lines.Add($"From age 60 only employment injury cover applies, paid by the employer: {Rm(r.SocsoEmployer)}.");
            if (r.SkbbkEmployee > 0m) lines.Add($"The employee still pays {Rm(r.SkbbkEmployee)} for Lindung 24.");
        }
        else
        {
            lines.Add($"{Band(request.MonthlySalary)} Employer {Rm(r.SocsoEmployer)}.");
            lines.Add(r.SkbbkEmployee > 0m
                ? $"Employee {Rm(r.SocsoEmployee)}, plus {Rm(r.SkbbkEmployee)} for Lindung 24 = {Rm(r.SocsoEmployee + r.SkbbkEmployee)}."
                : $"Employee {Rm(r.SocsoEmployee)}.");
        }

        if (request.Bonus > 0m) lines.Add("SOCSO is not charged on the bonus.");
        return lines;
    }

    public static IReadOnlyList<string> Eis(PayrollCalculatorRequest request, PayslipCalculator.Result r)
    {
        if (request.Citizenship == CalculatorCitizenship.Foreign)
            return ["EIS does not apply to foreign workers."];
        if (r.EisEmployee == 0m && r.EisEmployer == 0m)
            return ["EIS stops at age 60, so nothing is deducted."];

        var lines = new List<string>
        {
            $"0.2% each, from PERKESO's EIS table on the same wage band as SOCSO: {Rm(r.EisEmployee)} each.",
        };
        if (request.Bonus > 0m) lines.Add("EIS is not charged on the bonus.");
        return lines;
    }

    public static IReadOnlyList<string> Pcb(PayrollCalculatorRequest request, PayslipCalculator.Result r)
    {
        var b = r.PcbCalculation;
        var lines = new List<string>();

        if (b.Formula == PcbFormula.NonResident)
        {
            // The engine prices salary and bonus separately, each rounded up
            // to 5 sen and each under the RM10 minimum on its own, so the
            // sentence does too.
            var rate = Pct(b.Rate * 100m);
            lines.Add($"Non-residents pay a flat {rate}% with no reliefs: {Rm(request.MonthlySalary)} × {rate}% = {Rm(r.PcbNormal)}{RoundedNote(request.MonthlySalary * b.Rate, r.PcbNormal)}.");
            if (request.Bonus > 0m)
            {
                lines.Add(r.PcbAdditional > 0m
                    ? $"Bonus {Rm(request.Bonus)} × {rate}% = {Rm(r.PcbAdditional)}{RoundedNote(request.Bonus * b.Rate, r.PcbAdditional)}."
                    : $"Bonus {Rm(request.Bonus)} × {rate}% is under RM10, so nothing is deducted on it.");
            }
        }
        else
        {
            // January: no earlier months, so the year is this month plus the
            // projection of the rest (Y2 is already the projected total; K2 is
            // per month, as LHDN's form writes it).
            var yearly = b.Y + b.Y1 + b.Y2;
            var personal = b.D + b.Du;
            var spouse = b.S + b.Su;
            var other = b.SumLp + b.Lp1;
            // EPF relief as the engine actually allowed it: what's left between
            // the year's income and its chargeable income once the other reliefs
            // are accounted for. Summing the form's K pieces instead drifts by
            // sen (K1 is shown in whole ringgit, K2 per month), so the sentence
            // would stop adding up.
            var epfRelief = Money.Round2(yearly - (b.PBeforeFloor ?? b.P) - personal - spouse - b.QC - other);

            // The yearly line is in whole ringgit, as the handover writes it
            // ("…EPF relief RM4,000 = chargeable income RM47,000"): LHDN's
            // monthly EPF pieces leave the engine's figures a few sen off the
            // round number (RM3,999.93, RM47,000.07), which reads as an error to
            // a business owner. The tax and the monthly deduction stay exact.
            var parts = new List<string> { $"personal {RmWhole(personal)}" };
            if (spouse > 0m) parts.Add($"spouse {RmWhole(spouse)}");
            if (b.QC > 0m) parts.Add($"children {RmWhole(b.QC)}");
            if (epfRelief > 0m) parts.Add($"EPF {RmWhole(epfRelief)}");
            if (other > 0m) parts.Add($"other {RmWhole(other)}");
            var reliefs = personal + spouse + b.QC + epfRelief + other;

            lines.Add($"Yearly salary {RmWhole(yearly)} − reliefs {RmWhole(reliefs)} ({string.Join(", ", parts)}) = chargeable income {RmWhole(b.P)}.");

            if (b.YearlyTax <= 0m)
            {
                lines.Add("No income tax is due on that.");
            }
            else
            {
                // Floored, like the engine's own rebate test: P of RM35,000.07
                // still gets the rebate (and B already has it taken off).
                lines.Add(Math.Floor(b.P) > 35000m
                    ? $"Tax on the first {RmWhole(b.M)} is {RmWhole(b.B)}, then {Pct(Rate(b.R))}% on the next {RmWhole(b.P - b.M)} = {Rm(b.YearlyTax)} for the year."
                    : $"Tax on that is {Rm(b.YearlyTax)} for the year, after LHDN's tax rebate.");

                lines.Add(r.PcbNormal > 0m
                    ? $"{Rm(b.YearlyTax)} ÷ 12 months = {Rm(r.PcbNormal)} a month."
                    : "That is under RM10 a month, so nothing is deducted.");
            }

            if (request.Bonus > 0m)
            {
                // Under RM10 a month, no normal PCB is withheld, so the bonus
                // month's PCB(C) also carries the year's normal tax (LHDN's
                // PCB(B) counts the normal PCB as deducted: nil).
                lines.Add(r.PcbAdditional <= 0m
                    ? $"The {Rm(request.Bonus)} bonus adds no tax this month."
                    : r.PcbNormal <= 0m && b.YearlyTax > 0m && b.Ar is { } ar
                        ? $"With the {Rm(request.Bonus)} bonus the year's tax is {Rm(ar.Cs)}. None of it is covered by monthly PCB, so {Rm(r.PcbAdditional)} is deducted this month."
                        : $"The {Rm(request.Bonus)} bonus raises the year's tax by {Rm(r.PcbAdditional)}, all deducted this month.");
            }

            // LHDN rounds the month's deduction UP to the next 5 sen, so the
            // charged PCB (b.PcbTotal) can be a few sen above the parts above.
            var beforeRounding = r.PcbNormal + r.PcbAdditional;
            if (b.PcbTotal != beforeRounding && b.PcbTotal > 0m)
            {
                lines.Add(request.Bonus > 0m && r.PcbAdditional > 0m && r.PcbNormal > 0m
                    ? $"{Rm(r.PcbNormal)} + {Rm(r.PcbAdditional)}, rounded up to the next 5 sen = {Rm(b.PcbTotal)}."
                    : $"Rounded up to the next 5 sen: {Rm(b.PcbTotal)}.");
            }
        }

        if (request.ZakatMonthly > 0m)
            lines.Add($"Zakat of {Rm(request.ZakatMonthly)} comes off the PCB, leaving {Rm(r.Pcb)}.");

        return Fit(lines);
    }

    public static IReadOnlyList<string> Zakat(PayrollCalculatorRequest request, PayslipCalculator.Result r)
    {
        // What zakat is offset against: the PCB as charged, after LHDN's
        // round-up to 5 sen — not the unrounded parts.
        var before = r.PcbCalculation.PcbTotal;
        var lines = new List<string>
        {
            $"Paid through payroll: {Rm(request.ZakatMonthly)} this month.",
            "It comes off the PCB ringgit for ringgit, so the employee doesn't pay both.",
        };
        if (request.ZakatMonthly > before)
            lines.Add($"PCB can't go below RM0, so only {Rm(before)} of it reduces the tax.");
        return lines;
    }

    // "PERKESO's table for wages of RM4,900.01–RM5,000.00:" — read off the
    // gazetted rows, the same ones the engine priced from.
    private static string Band(decimal salary)
    {
        var rows = StatutoryTables.SocsoTable;
        for (var i = 0; i < rows.Count; i++)
        {
            if (salary > rows[i].UpTo) continue;
            if (salary > StatutoryTables.SocsoEisWageCeiling || rows[i].UpTo == decimal.MaxValue)
                return $"The salary is above the {Rm(StatutoryTables.SocsoEisWageCeiling)} ceiling, so SOCSO uses the top band of PERKESO's table.";
            var lower = i == 0 ? 0m : rows[i - 1].UpTo;
            return $"PERKESO's table for wages of {Rm(lower + 0.01m)}–{Rm(rows[i].UpTo)}:";
        }
        return $"The salary is above the {Rm(StatutoryTables.SocsoEisWageCeiling)} ceiling, so SOCSO uses the top band of PERKESO's table.";
    }

    // The widget asks for 1–4 sentences an item; fold any overflow into the last.
    private static IReadOnlyList<string> Fit(List<string> lines)
    {
        while (lines.Count > MaxSentences)
        {
            lines[^2] = $"{lines[^2]} {lines[^1]}";
            lines.RemoveAt(lines.Count - 1);
        }
        return lines;
    }

    // Tax bands carry their rate as a fraction (0.06) in some places and a
    // percentage (6) in others; show it as a percentage either way.
    private static decimal Rate(decimal r) => r < 1m ? r * 100m : r;

    private static string RoundedNote(decimal exact, decimal charged) =>
        charged > 0m && Money.Round2(exact) != charged ? ", rounded up to the next 5 sen" : "";

    private static string Pct(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Rm(decimal value) => $"RM{value.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string RmWhole(decimal value) =>
        $"RM{Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture)}";
}
