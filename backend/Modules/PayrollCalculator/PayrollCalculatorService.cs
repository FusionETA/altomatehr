using System.Text.Json;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.PayrollCalculator.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.PayrollCalculator;

// Prices one hypothetical employee with the SAME engine payroll runs use
// (PayslipCalculator), so the website's figures match what clients see on real
// payslips. No second set of rates lives here: this only translates the
// widget's nine answers into an employee profile, then reads the result back
// out with sentences explaining it.
//
// Two engine runs, one engine:
//   • this month — EPF, SOCSO, Lindung 24 and EIS at today's rates. Lindung 24
//     only exists from Jun 2026, so a January period would leave it out.
//   • January of the same year — PCB. A first month with no year-to-date is
//     LHDN's formula reduced to "a year's tax ÷ 12", which is what the website
//     promises ("paid this salary since January") and what payroll.my shows.
//     A later month spreads the rounding differently and can sit 5 sen apart.
//
// Stores nothing and touches no database.
public class PayrollCalculatorService : IPayrollCalculatorService
{
    // Malaysia has no daylight saving; UTC+8 all year.
    private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);

    private readonly TimeProvider _clock;

    public PayrollCalculatorService(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public PayrollCalculatorOutcome Calculate(JsonElement body)
    {
        var (request, error) = PayrollCalculatorRequestParser.Parse(body);
        if (error is not null) return new PayrollCalculatorOutcome(null, error);
        return new PayrollCalculatorOutcome(Price(request!), null);
    }

    public PayrollCalculatorResponse Price(PayrollCalculatorRequest request)
    {
        var today = _clock.GetUtcNow().ToOffset(MalaysiaOffset);
        var current = PayslipCalculator.Calculate(BuildInput(request, today.Year, today.Month));
        var january = today.Month == 1
            ? current
            : PayslipCalculator.Calculate(BuildInput(request, today.Year, 1));

        var gross = Money.Round2(request.MonthlySalary + request.Bonus);
        var socsoEmployee = Money.Round2(current.SocsoEmployee + current.SkbbkEmployee);

        var items = new List<PayrollCalculatorItem>
        {
            new()
            {
                Code = "epf", Label = "EPF", SubLabel = "KWSP",
                Employee = current.EpfEmployee, Employer = current.EpfEmployer,
                Working = PayrollCalculatorWorking.Epf(request, current),
            },
            new()
            {
                Code = "socso", Label = "SOCSO", SubLabel = "PERKESO",
                Employee = socsoEmployee, Employer = current.SocsoEmployer,
                Working = PayrollCalculatorWorking.Socso(request, current),
            },
            new()
            {
                Code = "eis", Label = "EIS", SubLabel = "SIP",
                Employee = current.EisEmployee, Employer = current.EisEmployer,
                Working = PayrollCalculatorWorking.Eis(request, current),
            },
            new()
            {
                Code = "pcb", Label = "PCB", SubLabel = "income tax",
                // After zakat: the engine already took it off, ringgit for ringgit.
                Employee = january.Pcb, Employer = 0m,
                Working = PayrollCalculatorWorking.Pcb(request, january),
            },
        };

        if (request.ZakatMonthly > 0m)
        {
            items.Add(new PayrollCalculatorItem
            {
                Code = "zakat", Label = "Zakat",
                Employee = Money.Round2(request.ZakatMonthly), Employer = 0m,
                Working = PayrollCalculatorWorking.Zakat(request, january),
            });
        }

        var employeeTotal = Money.Round2(items.Sum(i => i.Employee));
        var employerTotal = Money.Round2(items.Sum(i => i.Employer));

        return new PayrollCalculatorResponse
        {
            TaxYear = today.Year,
            Gross = gross,
            TakeHome = Money.Round2(gross - employeeTotal),
            EmployerCost = Money.Round2(gross + employerTotal),
            EmployeeTotal = employeeTotal,
            EmployerTotal = employerTotal,
            Items = items,
        };
    }

    // The visitor's answers as an employee profile. Everything not asked is the
    // ordinary case: a full month, no overtime, no year-to-date, no claims.
    public static PayslipCalculator.Input BuildInput(PayrollCalculatorRequest request, int year, int month)
    {
        var foreign = request.Citizenship == CalculatorCitizenship.Foreign;
        var over60 = request.AgeGroup == CalculatorAgeGroup.SixtyAndAbove;

        // An age safely inside the chosen group at the end of the period: the
        // engine reads age, not a flag, for EPF's 60+ branch and SOCSO's flip.
        var periodEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var dateOfBirth = periodEnd.AddYears(over60 ? -62 : -30);

        var lines = new List<FixedAllowance>();
        // An annual bonus: EPF yes, SOCSO and EIS no, PCB as additional
        // remuneration. Commission would be charged SOCSO/EIS — the widget's
        // field is a bonus.
        if (request.Bonus > 0m)
            lines.Add(new FixedAllowance { Category = PayrollAdjustmentCategories.WagesBonusAnnual, Amount = request.Bonus });
        if (request.ZakatMonthly > 0m)
            lines.Add(new FixedAllowance { Category = PayrollAdjustmentCategories.DeductZakat, Amount = request.ZakatMonthly });

        return new PayslipCalculator.Input
        {
            PeriodYear = year,
            PeriodMonth = month,
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = request.MonthlySalary,
            FixedAllowances = lines,

            // PRs are treated as Malaysians, as the widget says.
            Nationality = foreign ? "Foreign" : "Malaysian",
            DateOfBirth = dateOfBirth,
            IsResident = request.TaxResident,

            EpfEmployeeRate = 11m,
            DefaultEpfEmployeeRate = 11m,

            // Foreign workers and the 60+ are employment-injury only (the engine
            // also flips the 60+ itself). EIS doesn't cover foreign workers.
            // Lindung 24 applies to everyone, foreign workers included: PERKESO's
            // Lindung 24 Jam page says "Foreign employees are required to
            // contribute under SKBBK" (perkeso.gov.my, our-services/protection/
            // lindung-24-jam), matching StatutoryTables' SKBBK note.
            SocsoScheme = foreign || over60 ? SocsoScheme.EMPLOYMENT_INJURY_ONLY : SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            ContributeToEis = !foreign,
            ContributeToSkbbk = true,

            SpouseWorking = request.MaritalStatus switch
            {
                CalculatorMaritalStatus.MarriedSpouseNotWorking => false,
                CalculatorMaritalStatus.MarriedSpouseWorking => true,
                _ => null,
            },
            Children = Enumerable.Range(0, request.Children)
                .Select(_ => new ChildRelief { PcbDeduction = ChildPcbDeductionLevel.FULL })
                .ToList(),

            // The RM 350 SOCSO/EIS relief is left out, as payroll.my does: LHDN
            // only grants it through a TP1 claim.
            AutoApplySocsoEisRelief = false,
            ExtraAllowableDeductions = request.OtherReliefsYearly,

            WorkingDaysRule = WorkingDaysRule.CALENDAR,
        };
    }
}
