using AltomateHR.Api.Modules.Employees.Entities;   // SocsoScheme
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;    // SalaryType

namespace AltomateHR.Api.Modules.Payroll;

// The orchestrator: one employee, one period, one payslip.
//
// This is where the phase 0/1 engine finally gets used for real. It owns no
// statutory arithmetic of its own — EpfCalculator, PerkesoCalculator,
// PcbCalculator, PayPeriod and OvertimePay each own their statute — and instead
// answers the question none of them can: which wage does each agency actually
// see, once this employee's allowances and deductions are taken into account.
//
// Pure, like everything else at this module root: no EF, no HTTP, no clock.
// Every input arrives as a parameter, the payroll period included, so rerunning
// an old month reproduces that month's law rather than today's.
//
// Order of work:
//
//   1. Working-days basis and hourly rate      (s.60I)
//   2. Prorate basic pay for join/leave        (s.18A)
//   3. Overtime
//   4. Fixed adjustments → line items and the six wage bases
//   5. Reimbursements and manual deductions
//   6. EPF / SOCSO / EIS / SKBBK
//   7. PCB, then the zakat offset
//   8. HRDF
//   9. Gross, net, cost to employer
public static class PayslipCalculator
{
    // ─── Inputs ─────────────────────────────────────────────────────────

    public sealed record Input
    {
        // ---- Period ----
        public required int PeriodYear { get; init; }
        public required int PeriodMonth { get; init; }

        // ---- Salary ----
        public required SalaryType SalaryType { get; init; }
        public decimal? MonthlySalary { get; init; }
        public decimal? HourlyRate { get; init; }
        public IReadOnlyList<FixedAllowance> FixedAllowances { get; init; } = [];
        public DateTime? JoinDate { get; init; }
        public DateTime? LeaveDate { get; init; }

        // ---- Statutory identity ----
        public string? Nationality { get; init; }
        public bool HasPr { get; init; }
        public bool IsResident { get; init; } = true;
        public bool IsOku { get; init; }
        public DateTime? DateOfBirth { get; init; }

        public bool ContributeToEpf { get; init; } = true;
        public bool EpfMemberBefore1998 { get; init; }
        public decimal EpfEmployeeRate { get; init; }
        public decimal EpfEmployeeVoluntary { get; init; }
        public decimal EpfEmployerVoluntary { get; init; }

        public SocsoScheme? SocsoScheme { get; init; }
        public bool ContributeToEis { get; init; } = true;
        public bool ContributeToSkbbk { get; init; }

        public bool? SpouseWorking { get; init; }
        public bool? SpouseDisabled { get; init; }
        public IReadOnlyList<ChildRelief> Children { get; init; } = [];

        // Presence only — the numbers themselves are not used, but a missing one
        // blocks a submission file, so it is reported as a warning.
        public string? IncomeTaxNumber { get; init; }
        public string? EpfNumber { get; init; }
        public string? SocsoNumber { get; init; }

        // ---- Org settings ----
        public required WorkingDaysRule WorkingDaysRule { get; init; }
        public decimal DefaultEpfEmployeeRate { get; init; } = 11m;
        public bool HrdfEnabled { get; init; }
        public decimal? HrdfRate { get; init; }

        // Auto-apply the RM 350 PERKESO relief inside PCB. Off means the
        // employee claims it themselves at year end instead.
        public bool AutoApplySocsoEisRelief { get; init; } = true;

        // ---- Overtime ----
        // Multipliers come from the employee's policy; the hours come from the
        // run's adjustment row (phase 4) and are 0 until then.
        public decimal OtRateNormal { get; init; } = 1.5m;
        public decimal OtRateRest { get; init; } = 2.0m;
        public decimal OtRatePublicHoliday { get; init; } = 3.0m;

        public decimal OtNormalHours { get; init; }
        public decimal OtRestHours { get; init; }
        public decimal OtPublicHours { get; init; }

        // Effective daily hours for the monthly→hourly conversion. Null falls
        // back to OvertimePay's standard day.
        public decimal? DailyHours { get; init; }

        // ---- Attendance (display only, except for HOURLY staff) ----
        // Monthly pay is day-based, not attendance-based; these are snapshotted
        // for the run table. For HOURLY staff WorkedHours IS the paid quantity.
        public decimal? WorkedHours { get; init; }
        public decimal? ExpectedHours { get; init; }
        public decimal? UnpaidLeaveDays { get; init; }

        // ---- Per-run extras (phase 4) ----
        public IReadOnlyList<Reimbursement> Reimbursements { get; init; } = [];
        public IReadOnlyList<ManualDeduction> ManualDeductions { get; init; } = [];

        // ---- Year to date, from this year's SUBMITTED payslips ----
        // The caller folds in the employee's prior-employer carryover (TP3) when
        // they joined mid-year, so a mid-year joiner is not over-withheld.
        public decimal YtdTaxable { get; init; }
        public decimal YtdEpf { get; init; }
        public decimal YtdPcb { get; init; }
        public decimal YtdZakat { get; init; }
        public decimal YtdSocsoEis { get; init; }
        public decimal YtdAllowableDeductions { get; init; }

        // Per-category totals for the year, so the annual exemption ceilings
        // (travel RM 6,000, childcare RM 2,400, award RM 2,000, the TP1 items)
        // pick up where the last run left off.
        public IReadOnlyDictionary<string, decimal> YtdAllowanceByCategory { get; init; } =
            new Dictionary<string, decimal>();
    }

    public sealed record Reimbursement(string ClaimId, string Label, decimal Amount);

    public sealed record ManualDeduction(string Label, decimal Amount);

    // ─── Results ────────────────────────────────────────────────────────

    public sealed record LineItem
    {
        public required PayslipLineKind Kind { get; init; }
        public required string Label { get; init; }
        public required decimal Amount { get; init; }

        // Set only when an exemption ceiling actually clamped this row. Null
        // means "no clamp — use Amount", which is what the YTD read path needs
        // so an exempt portion cannot leak into next month's taxable base.
        public decimal? PcbTaxableAmount { get; init; }

        public string? Category { get; init; }
        public string? ClaimId { get; init; }

        public required bool SubjectToEpf { get; init; }
        public required bool SubjectToSocso { get; init; }
        public required bool SubjectToEis { get; init; }
        public required bool SubjectToPcb { get; init; }
    }

    // The rates and ringgit that actually applied, for the payslip's audit
    // trail. The branch overrides the profile's declared rate everywhere except
    // Part A, so the profile's number would misdescribe the deduction.
    public sealed record EpfRatesSnapshot
    {
        public required EpfBranch Branch { get; init; }
        public required decimal Employee { get; init; }
        public required decimal Employer { get; init; }
        public required decimal VoluntaryEmployee { get; init; }
        public required decimal VoluntaryEmployer { get; init; }
        public required decimal MandatoryAmountEmployee { get; init; }
        public required decimal MandatoryAmountEmployer { get; init; }
        public required decimal VoluntaryAmountEmployee { get; init; }
        public required decimal VoluntaryAmountEmployer { get; init; }
    }

    // Statutory identifiers a filing needs but the profile is missing. The calc
    // still runs — LHDN's MTD spec never gates the computation on having a TIN.
    public static class Warnings
    {
        public const string MissingIncomeTaxNumber = "MISSING_INCOME_TAX_NUMBER";
        public const string MissingEpfNumber = "MISSING_EPF_NUMBER";
        public const string MissingSocsoNumber = "MISSING_SOCSO_NUMBER";
    }

    public sealed record Result
    {
        // Hours and proration
        public required int TotalWorkingDays { get; init; }
        public required int ProratedDays { get; init; }
        public required int ProrationDaysInPeriod { get; init; }
        public required decimal ProratedFactor { get; init; }
        public decimal? WorkedHours { get; init; }
        public decimal? ExpectedHours { get; init; }
        public decimal? UnpaidLeaveDays { get; init; }

        // Earnings
        public required decimal BasicPay { get; init; }
        public required decimal ProratedPay { get; init; }
        public required decimal OtNormalHours { get; init; }
        public required decimal OtRestHours { get; init; }
        public required decimal OtPublicHours { get; init; }
        public required decimal OtPay { get; init; }
        public required decimal TotalAllowances { get; init; }
        public required decimal TotalReimbursements { get; init; }
        public required decimal TotalDeductions { get; init; }
        public required decimal TotalBenefitsInKind { get; init; }

        // Statutory
        public required decimal EpfEmployee { get; init; }
        public required decimal EpfEmployer { get; init; }
        public required decimal SocsoEmployee { get; init; }
        public required decimal SocsoEmployer { get; init; }
        public required decimal EisEmployee { get; init; }
        public required decimal EisEmployer { get; init; }
        public required decimal SkbbkEmployee { get; init; }
        public required decimal SkbbkWage { get; init; }
        public required decimal Pcb { get; init; }
        public required decimal PcbNormal { get; init; }
        public required decimal PcbAdditional { get; init; }

        // The LHDN form decomposition behind those two figures. Snapshotted
        // onto the payslip so the Detailed Calculations PDF renders the
        // formula that produced THIS month's deduction, not today's.
        public required PcbBreakdown PcbCalculation { get; init; }
        public required decimal Cp38 { get; init; }
        public required decimal Zakat { get; init; }
        public required decimal Hrdf { get; init; }
        public required decimal HrdfWage { get; init; }

        // Aggregates
        public required decimal GrossPay { get; init; }
        public required decimal NetPay { get; init; }
        public required decimal TotalCostToEmployer { get; init; }

        public required IReadOnlyList<LineItem> LineItems { get; init; }
        public required EpfRatesSnapshot EpfRates { get; init; }
        public required IReadOnlyList<string> StatutoryWarnings { get; init; }
    }

    // ─── The calculation ────────────────────────────────────────────────

    public static Result Calculate(Input input)
    {
        // 1. Two divisors, two statutes. The s.60I basis honours the org's rule
        //    and drives the hourly rate; proration below uses calendar days,
        //    because s.18A opens "Notwithstanding section 60I" to say so.
        var totalWorkingDays = PayPeriod.WorkingDaysForPeriod(
            input.PeriodYear, input.PeriodMonth, input.WorkingDaysRule);

        var hourlyRate = OvertimePay.DeriveHourlyRate(
            input.SalaryType,
            input.MonthlySalary,
            input.HourlyRate,
            totalWorkingDays,
            input.DailyHours);

        // 2. Proration. The EXACT ratio does the money; the rounded factor is
        //    only a snapshot. Rounding before multiplying loses sen —
        //    4999.99 × round(19/28) is not 4999.99 × 19/28.
        var calendarDays = PayPeriod.CalendarDaysInMonth(input.PeriodYear, input.PeriodMonth);

        var proratedDays = PayPeriod.EffectiveWorkedDays(
            input.PeriodYear, input.PeriodMonth, input.JoinDate, input.LeaveDate, calendarDays) ?? 0;

        var prorationRatio = calendarDays > 0 ? (decimal)proratedDays / calendarDays : 0m;
        var proratedFactor = Math.Round(prorationRatio, 6, MidpointRounding.AwayFromZero);

        var basicPay = input.SalaryType == SalaryType.HOURLY
            ? Money.Round2((input.WorkedHours ?? 0m) * (input.HourlyRate ?? 0m))
            : input.MonthlySalary ?? 0m;

        // Hourly pay is already the exact quantity worked — prorating it a
        // second time would dock the same days twice.
        var proratedPay = input.SalaryType == SalaryType.HOURLY
            ? basicPay
            : Money.Round2(basicPay * prorationRatio);

        // 3. Overtime.
        var otPay = OvertimePay.Calculate(
            hourlyRate,
            input.OtNormalHours, input.OtRestHours, input.OtPublicHours,
            input.OtRateNormal, input.OtRateRest, input.OtRatePublicHoliday);

        // 4. Fixed adjustments. Each row can land in six wage bases at once, and
        //    the category's flags are the only thing that decides which.
        var buckets = RouteFixedAllowances(input, prorationRatio);
        var lineItems = buckets.LineItems;

        // Overtime is not a fixed monthly amount, so LHDN taxes it through the
        // additional-remuneration formula rather than projecting it across the
        // rest of the year. SOCSO and EIS still see it as ordinary wages — only
        // the PCB routing differs.
        var pcbAdditionalRemuneration = Money.Round2(buckets.PcbAdditionalRemuneration + otPay);

        // 5. Reimbursements and manual deductions. Neither is wage-like, so
        //    nothing statutory is computed on a reimbursement.
        var totalReimbursements = buckets.RecurringReimbursements;
        foreach (var r in input.Reimbursements)
        {
            if (r.Amount <= 0m) continue;

            totalReimbursements += r.Amount;
            lineItems.Add(new LineItem
            {
                Kind = PayslipLineKind.REIMBURSEMENT,
                Label = r.Label,
                Amount = Money.Round2(r.Amount),
                ClaimId = r.ClaimId,
                SubjectToEpf = false, SubjectToSocso = false,
                SubjectToEis = false, SubjectToPcb = false,
            });
        }
        totalReimbursements = Money.Round2(totalReimbursements);

        var totalDeductions = buckets.RecurringDeductions;
        foreach (var d in input.ManualDeductions)
        {
            if (d.Amount <= 0m) continue;

            totalDeductions += d.Amount;
            lineItems.Add(new LineItem
            {
                Kind = PayslipLineKind.DEDUCTION,
                Label = string.IsNullOrWhiteSpace(d.Label) ? "Deduction" : d.Label,
                Amount = Money.Round2(d.Amount),
                SubjectToEpf = true, SubjectToSocso = true,
                SubjectToEis = true, SubjectToPcb = true,
            });
        }
        totalDeductions = Money.Round2(totalDeductions);

        // 6. The wage each agency actually sees.
        //
        //    EPF excludes overtime outright (EPF Act 1991 s.2). SOCSO and EIS
        //    include it. PCB's REGULAR wage excludes it too, because the OT
        //    portion has already gone into the additional-remuneration bucket —
        //    counting it in both would tax it twice.
        var epfWage = Money.Round2(Math.Max(0m, proratedPay + buckets.EpfBase));
        var socsoWage = Money.Round2(Math.Max(0m, proratedPay + otPay + buckets.SocsoBase));
        var eisWage = Money.Round2(Math.Max(0m, proratedPay + otPay + buckets.EisBase));
        var pcbWage = Money.Round2(Math.Max(0m, proratedPay + buckets.PcbBase));

        var isMalaysianCitizen = IsMalaysianNationality(input.Nationality);
        var ageAtPeriodEnd = AgeAtEndOfPeriod(
            input.DateOfBirth, input.PeriodYear, input.PeriodMonth);

        var employeeRate = input.EpfEmployeeRate > 0m
            ? input.EpfEmployeeRate
            : input.DefaultEpfEmployeeRate;

        // The KWSP 13%→12% cliff is decided by the REGULAR monthly wage, not by
        // the wage a one-off bonus happens to inflate this month. So the whole
        // wage (bonus included) is contributed on, but the rate is picked from
        // the wage without it.
        var regularEpfWage = Money.Round2(Math.Max(0m, epfWage - buckets.ArEpfAmount));

        var epfInput = new EpfCalculator.Input
        {
            Wage = epfWage,
            RateDeterminingWage = regularEpfWage,
            EmployeeRate = employeeRate,
            EmployeeVoluntary = input.EpfEmployeeVoluntary,
            EmployerVoluntary = input.EpfEmployerVoluntary,
            ContributeToEpf = input.ContributeToEpf,
            IsMalaysianCitizen = isMalaysianCitizen,
            HasPr = input.HasPr,
            EpfMemberBefore1998 = input.EpfMemberBefore1998,
            AgeAtPeriodEnd = ageAtPeriodEnd,
        };
        var epf = EpfCalculator.Calculate(epfInput);

        // Kt on the LHDN form — the EPF the bonus adds ON TOP of the regular
        // wage — is a band DIFFERENCE, not a percentage of the bonus. Taking the
        // percentage attributes a band-tier jump to K1 instead of Kt, so K1 + Kt
        // stops reconciling to the EPF actually deducted.
        var epfFromAr = 0m;
        if (buckets.ArEpfAmount > 0m)
        {
            var regularOnly = EpfCalculator.Calculate(epfInput with
            {
                Wage = regularEpfWage,
                RateDeterminingWage = regularEpfWage,
            });

            epfFromAr = Money.Round2(Math.Max(0m, epf.Employee - regularOnly.Employee));
        }

        var socso = PerkesoCalculator.CalculateSocso(new PerkesoCalculator.SocsoInput
        {
            Wage = socsoWage,
            Scheme = input.SocsoScheme,
            PeriodYear = input.PeriodYear,
            PeriodMonth = input.PeriodMonth,
            AgeAtPeriodEnd = ageAtPeriodEnd,
            ContributeToSkbbk = input.ContributeToSkbbk,
        });

        // EIS covers everyone in the private sector — citizenship is not a gate —
        // but only between 18 and 60. With no date of birth on file we cannot
        // tell, so the admin's flag decides rather than a guess.
        var eisAgeEligible = input.DateOfBirth is null
            || (ageAtPeriodEnd >= 18 && ageAtPeriodEnd < 60);

        var eis = PerkesoCalculator.CalculateEis(eisWage, input.ContributeToEis && eisAgeEligible);

        // 7. PCB. Computed for every employee on every run — the MTD spec does
        //    not make it conditional on having a tax number; the number is only
        //    needed to file. The EPF handed over is the share arising from
        //    NORMAL pay, with the AR share passed separately, so the annual
        //    projection is not inflated by a one-off month.
        var epfFromNormal = Money.Round2(Math.Max(0m, epf.Employee - epfFromAr));

        // SOCSO + EIS + SKBBK share the one RM 350/year relief bucket. The
        // employer knows the exact figure, so it is applied without waiting for
        // a TP1 — unless the org has asked to leave it to the employee.
        var thisMonthSocsoEis = input.AutoApplySocsoEisRelief
            ? Money.Round2(socso.Employee + eis.Employee + socso.EmployeeSkbbk)
            : 0m;
        var ytdSocsoEis = input.AutoApplySocsoEisRelief ? input.YtdSocsoEis : 0m;

        // Explain, not Calculate: the same one computation, with every
        // intermediate the LHDN form needs kept rather than discarded. The
        // deducted money reads off this, so the payslip's stored breakdown can
        // never describe a different figure from the one withheld.
        var pcbResult = PcbCalculator.Explain(new PcbCalculator.Input
        {
            IsResident = input.IsResident,
            PeriodMonth = input.PeriodMonth,
            ThisMonthTaxable = pcbWage,
            ThisMonthEpf = epfFromNormal,
            ThisMonthAdditionalRemuneration = pcbAdditionalRemuneration,
            ThisMonthEpfFromAr = epfFromAr,
            YtdTaxable = input.YtdTaxable,
            YtdEpf = input.YtdEpf,
            YtdPcb = input.YtdPcb,
            YtdZakat = input.YtdZakat,
            ThisMonthSocsoEis = thisMonthSocsoEis,
            YtdSocsoEis = ytdSocsoEis,
            ThisMonthAllowableDeductions = buckets.Tp1Relief,
            YtdAllowableDeductions = input.YtdAllowableDeductions,
            IsOku = input.IsOku,
            SpouseWorking = input.SpouseWorking,
            SpouseDisabled = input.SpouseDisabled,
            Children = input.Children,
        });

        // Zakat comes off the PCB ringgit for ringgit — the employee pays zakat
        // OUT OF the tax they owe, not on top of it — but it cannot push the
        // withholding below zero.
        var zakatOffset = Math.Min(pcbResult.PcbTotal, buckets.Zakat);
        var pcb = Money.Round2(Math.Max(0m, pcbResult.PcbTotal - zakatOffset));

        // 8. HRDF. PSMB Act 2001 s.2 defines the levy's wage base narrowly and
        //    its "employee" as a Malaysian citizen, so PR holders and foreign
        //    workers are outside it entirely. The category flags have already
        //    kept travel, bonus, commission and gratuity out of the base.
        var hrdfRate = input.HrdfRate ?? 0m;
        var hrdfActive = input.HrdfEnabled && hrdfRate > 0m && isMalaysianCitizen;
        var hrdfWage = hrdfActive
            ? Money.Round2(Math.Max(0m, proratedPay + buckets.HrdfBase))
            : 0m;
        var hrdf = hrdfActive ? Money.Round2(hrdfWage * hrdfRate / 100m) : 0m;

        // 9. Gross, net, cost to employer.
        //
        //    Lost earnings (unpaid leave) come off GROSS and are deliberately
        //    absent from totalDeductions — subtracting them in both places docks
        //    the employee twice for one absence. Benefits in kind are absent
        //    from gross for the opposite reason: no cash ever changes hands.
        var grossPay = Money.Round2(
            proratedPay + otPay + buckets.Allowances + totalReimbursements
            - buckets.GrossReducingDeductions);

        // Zakat is already inside totalDeductions as a line item; the offset
        // above has already lowered `pcb` to what the employee actually pays, so
        // neither is subtracted twice. SKBBK is employee-only, which is why it
        // appears here but not in the employer's cost below.
        var netPay = Money.Round2(
            grossPay
            - epf.Employee - socso.Employee - eis.Employee - socso.EmployeeSkbbk
            - totalDeductions - pcb);

        var totalCostToEmployer = Money.Round2(
            grossPay + epf.Employer + socso.Employer + eis.Employer + hrdf);

        return new Result
        {
            TotalWorkingDays = totalWorkingDays,
            ProratedDays = proratedDays,
            ProrationDaysInPeriod = calendarDays,
            ProratedFactor = proratedFactor,
            WorkedHours = input.WorkedHours,
            ExpectedHours = input.ExpectedHours,
            UnpaidLeaveDays = input.UnpaidLeaveDays,

            BasicPay = Money.Round2(basicPay),
            ProratedPay = proratedPay,
            OtNormalHours = input.OtNormalHours,
            OtRestHours = input.OtRestHours,
            OtPublicHours = input.OtPublicHours,
            OtPay = otPay,
            TotalAllowances = buckets.Allowances,
            TotalReimbursements = totalReimbursements,
            TotalDeductions = totalDeductions,
            TotalBenefitsInKind = buckets.BenefitsInKind,

            EpfEmployee = epf.Employee,
            EpfEmployer = epf.Employer,
            SocsoEmployee = socso.Employee,
            SocsoEmployer = socso.Employer,
            EisEmployee = eis.Employee,
            EisEmployer = eis.Employer,
            SkbbkEmployee = socso.EmployeeSkbbk,
            // Equal to the SOCSO wage by definition — same gazette, same Act 4
            // wage — but stored in its own column so a historical payslip stays
            // readable if PERKESO ever separates them.
            SkbbkWage = socsoWage,
            Pcb = pcb,
            PcbNormal = pcbResult.PcbNormal,
            PcbAdditional = pcbResult.PcbAdditional,
            PcbCalculation = pcbResult,
            Cp38 = buckets.Cp38,
            Zakat = buckets.Zakat,
            Hrdf = hrdf,
            HrdfWage = hrdfWage,

            GrossPay = grossPay,
            NetPay = netPay,
            TotalCostToEmployer = totalCostToEmployer,

            LineItems = lineItems,
            EpfRates = SnapshotEpfRates(
                epf, employeeRate, epfWage, regularEpfWage,
                input.EpfEmployeeVoluntary, input.EpfEmployerVoluntary),
            StatutoryWarnings = CollectWarnings(input),
        };
    }

    // ─── Fixed adjustments → wage bases ─────────────────────────────────

    // Everything a run of fixed allowances contributes, split by where it lands.
    private sealed class Buckets
    {
        public List<LineItem> LineItems { get; } = [];

        public decimal Allowances;
        public decimal BenefitsInKind;
        public decimal RecurringDeductions;
        public decimal GrossReducingDeductions;
        public decimal RecurringReimbursements;

        public decimal EpfBase;
        public decimal SocsoBase;
        public decimal EisBase;
        public decimal PcbBase;
        public decimal HrdfBase;

        public decimal PcbAdditionalRemuneration;

        // The EPF-able portion flagged as additional remuneration. Used only to
        // derive the LHDN form's Kt — the EPF actually deducted always comes off
        // the combined wage.
        public decimal ArEpfAmount;

        public decimal Zakat;
        public decimal Tp1Relief;
        public decimal Cp38;
    }

    private static Buckets RouteFixedAllowances(Input input, decimal prorationRatio)
    {
        var b = new Buckets();

        // How much of each category's annual ceiling this run has already used.
        // Kept apart from the YTD figures so a single run with two rows in the
        // same category cannot claim the headroom twice.
        var exemptUsed = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var tp1Used = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var a in input.FixedAllowances)
        {
            if (a.Amount <= 0m) continue;

            // An unrecognised code is skipped rather than fatal — these rows come
            // out of JSON written by an older system, and one bad code must not
            // take a whole month's payroll down.
            var meta = PayrollAdjustmentCategories.Find(a.Category);
            if (meta is null) continue;

            // Most rows shrink with the month a joiner or leaver actually worked.
            // A `SkipProration` row (unpaid leave) is already stated at the full
            // daily rate, so prorating it again would under-deduct.
            var amount = meta.SkipProration
                ? Money.Round2(a.Amount)
                : Money.Round2(a.Amount * prorationRatio);

            var pcbTaxable = PcbTaxablePortion(a, meta, amount, input.YtdAllowanceByCategory, exemptUsed);

            if (meta.Kind == PayslipLineKind.DEDUCTION)
            {
                RouteDeduction(b, a, meta, amount, pcbTaxable, input.YtdAllowanceByCategory, tp1Used);
            }
            else if (meta.Kind == PayslipLineKind.REIMBURSEMENT)
            {
                b.RecurringReimbursements += amount;
            }
            else
            {
                RouteAllowance(b, a, meta, amount, pcbTaxable);
            }

            b.LineItems.Add(new LineItem
            {
                Kind = meta.Kind,
                Label = string.IsNullOrWhiteSpace(a.Name) ? meta.Label : a.Name!,
                Amount = amount,
                // Only record the clamp when one happened; null means "use
                // Amount", which is also what pre-existing rows imply.
                PcbTaxableAmount = pcbTaxable < amount ? pcbTaxable : null,
                Category = a.Category,
                SubjectToEpf = meta.SubjectToEpf,
                SubjectToSocso = meta.SubjectToSocso,
                SubjectToEis = meta.SubjectToEis,
                SubjectToPcb = meta.SubjectToPcb,
            });
        }

        b.Allowances = Money.Round2(b.Allowances);
        b.BenefitsInKind = Money.Round2(b.BenefitsInKind);
        b.RecurringDeductions = Money.Round2(b.RecurringDeductions);
        b.RecurringReimbursements = Money.Round2(b.RecurringReimbursements);
        b.HrdfBase = Money.Round2(b.HrdfBase);
        b.Zakat = Money.Round2(b.Zakat);
        b.Tp1Relief = Money.Round2(b.Tp1Relief);
        b.Cp38 = Money.Round2(b.Cp38);

        return b;
    }

    // How much of this row reaches the PCB base. Where a category carries an
    // annual exemption ceiling, the first (ceiling − used) ringgit stay exempt
    // and only the overflow is taxable. The row is still fully wage-like for
    // every other agency — the ceiling is a tax rule, not a wage definition.
    private static decimal PcbTaxablePortion(
        FixedAllowance a,
        PayrollAdjustmentCategoryMeta meta,
        decimal amount,
        IReadOnlyDictionary<string, decimal> ytdByCategory,
        Dictionary<string, decimal> exemptUsed)
    {
        if (meta.Kind != PayslipLineKind.ALLOWANCE
            || !meta.SubjectToPcb
            || meta.TaxExemptLimit is null)
        {
            return amount;
        }

        var used = Lookup(ytdByCategory, a.Category) + Lookup(exemptUsed, a.Category);
        var headroom = Math.Max(0m, meta.TaxExemptLimit.Value - used);
        var exempt = Math.Min(amount, headroom);

        exemptUsed[a.Category] = Lookup(exemptUsed, a.Category) + exempt;

        return Money.Round2(Math.Max(0m, amount - exempt));
    }

    private static void RouteAllowance(
        Buckets b,
        FixedAllowance a,
        PayrollAdjustmentCategoryMeta meta,
        decimal amount,
        decimal pcbTaxable)
    {
        // A category flagged as additional remuneration is taxed through LHDN's
        // one-shot formula — unless the admin ticked "treat as recurring",
        // because a bonus genuinely paid every month is not a spike.
        var treatAsAdditional = meta.IsAdditionalRemuneration && !a.TreatAsRecurring;

        if (meta.NonCash)
        {
            // A benefit in kind is taxable income but not money. It never
            // reaches gross, net, or any contribution base — only PCB.
            b.BenefitsInKind += amount;

            if (meta.SubjectToPcb)
            {
                if (treatAsAdditional) b.PcbAdditionalRemuneration += pcbTaxable;
                else b.PcbBase += pcbTaxable;
            }

            return;
        }

        b.Allowances += amount;

        if (meta.SubjectToEpf)
        {
            // Every wage paid in the month — bonus included — joins the regular
            // wage that picks the KWSP tier. EPF Act 1991 s.2 reads "wages"
            // broadly, and the tier follows the money, not the tax treatment.
            b.EpfBase += amount;

            // Tracked separately only so Kt can be derived as a band difference
            // after the fact. It does not change what is deducted.
            if (treatAsAdditional) b.ArEpfAmount += amount;
        }

        if (meta.SubjectToSocso) b.SocsoBase += amount;
        if (meta.SubjectToEis) b.EisBase += amount;
        if (meta.SubjectToHrdf) b.HrdfBase += amount;

        if (meta.SubjectToPcb)
        {
            if (treatAsAdditional) b.PcbAdditionalRemuneration += pcbTaxable;
            else b.PcbBase += pcbTaxable;
        }
    }

    private static void RouteDeduction(
        Buckets b,
        FixedAllowance a,
        PayrollAdjustmentCategoryMeta meta,
        decimal amount,
        decimal pcbTaxable,
        IReadOnlyDictionary<string, decimal> ytdByCategory,
        Dictionary<string, decimal> tp1Used)
    {
        if (meta.ReducesGross)
        {
            // Lost earnings. Kept out of the deductions total so net is not
            // reduced by the same absence twice.
            b.GrossReducingDeductions += amount;
        }
        else if (!meta.CashNeutral)
        {
            // Cash-neutral rows lower PCB but take nothing from the payslip —
            // the employee already paid the third party directly.
            b.RecurringDeductions += amount;
        }

        if (meta.ReducesBase)
        {
            if (meta.SubjectToEpf) b.EpfBase -= amount;
            if (meta.SubjectToSocso) b.SocsoBase -= amount;
            if (meta.SubjectToEis) b.EisBase -= amount;
            if (meta.SubjectToPcb) b.PcbBase -= pcbTaxable;
            if (meta.SubjectToHrdf) b.HrdfBase -= amount;
        }

        if (meta.OffsetsPcb) b.Zakat += amount;
        if (meta.AddsToCp38Field) b.Cp38 += amount;

        if (meta.FeedsLp1Relief)
        {
            // Clamp an over-claim to LHDN's per-item ceiling rather than letting
            // it under-withhold. The admin-trusted catch-all has no ceiling.
            if (meta.TaxExemptLimit is > 0m)
            {
                var used = Lookup(ytdByCategory, a.Category) + Lookup(tp1Used, a.Category);
                var headroom = Math.Max(0m, meta.TaxExemptLimit.Value - used);
                var eligible = Math.Min(amount, headroom);

                b.Tp1Relief += eligible;
                tp1Used[a.Category] = Lookup(tp1Used, a.Category) + eligible;
            }
            else
            {
                b.Tp1Relief += amount;
            }
        }
    }

    // ─── Snapshots and small helpers ────────────────────────────────────

    private static EpfRatesSnapshot SnapshotEpfRates(
        EpfCalculator.Result epf,
        decimal profileEmployeeRate,
        decimal wage,
        decimal rateDeterminingWage,
        decimal voluntaryEmployee,
        decimal voluntaryEmployer)
    {
        // Mirrors the rates EpfCalculator actually applied, including its floor
        // on the Part A employee share. Storing the profile's declared rate here
        // would describe a deduction that never happened.
        var (employee, employer) = epf.Branch switch
        {
            EpfBranch.MALAYSIAN_UNDER_60 =>
                (Math.Max(11m, profileEmployeeRate), rateDeterminingWage <= 5000m ? 13m : 12m),
            EpfBranch.MALAYSIAN_CITIZEN_60_PLUS => (0m, 4m),
            EpfBranch.PR_OR_PRE1998_60_PLUS =>
                (5.5m, rateDeterminingWage <= 5000m ? 6.5m : 6m),
            EpfBranch.POST_1998_NON_MALAYSIAN => (2m, 2m),
            _ => (0m, 0m),
        };

        // Voluntary is ceiled to the next ringgit on the whole wage, matching
        // how EpfCalculator applies it; mandatory is then the remainder. The two
        // halves let the payslip PDF show them as separate lines without redoing
        // the arithmetic from a single total.
        var voluntaryAmountEmployee = Money.CeilRinggit(wage, voluntaryEmployee);
        var voluntaryAmountEmployer = Money.CeilRinggit(wage, voluntaryEmployer);

        return new EpfRatesSnapshot
        {
            Branch = epf.Branch,
            Employee = employee,
            Employer = employer,
            VoluntaryEmployee = voluntaryEmployee,
            VoluntaryEmployer = voluntaryEmployer,
            MandatoryAmountEmployee = Money.Round2(epf.Employee - voluntaryAmountEmployee),
            MandatoryAmountEmployer = Money.Round2(epf.Employer - voluntaryAmountEmployer),
            VoluntaryAmountEmployee = voluntaryAmountEmployee,
            VoluntaryAmountEmployer = voluntaryAmountEmployer,
        };
    }

    private static List<string> CollectWarnings(Input input)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(input.IncomeTaxNumber))
            warnings.Add(Warnings.MissingIncomeTaxNumber);

        if (input.ContributeToEpf && string.IsNullOrWhiteSpace(input.EpfNumber))
            warnings.Add(Warnings.MissingEpfNumber);

        if (input.SocsoScheme is not null && string.IsNullOrWhiteSpace(input.SocsoNumber))
            warnings.Add(Warnings.MissingSocsoNumber);

        return warnings;
    }

    // Age on the LAST day of the period, so a birthday inside the month counts —
    // 60 is the threshold for both the EPF Part E flip and the SOCSO Category 2
    // flip. No date of birth reads as 0, i.e. under 60, which keeps the
    // under-60 rates: over-deducting is recoverable, under-deducting is not.
    private static int AgeAtEndOfPeriod(DateTime? dateOfBirth, int periodYear, int periodMonth)
    {
        if (dateOfBirth is null) return 0;

        var periodEnd = new DateTime(
            periodYear, periodMonth, PayPeriod.CalendarDaysInMonth(periodYear, periodMonth));

        return SocsoSchemeAdvisor.CalculateAge(dateOfBirth.Value, periodEnd);
    }

    // `Nationality` is free text, and the spreadsheets it arrives on spell it
    // every way there is. Getting this wrong routes a citizen into the wrong EPF
    // branch and silently drops them out of the HRDF levy, so the variants are
    // accepted deliberately rather than demanding one exact string.
    public static bool IsMalaysianNationality(string? nationality)
    {
        var value = (nationality ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return false;

        return value is "malaysian" or "malaysia" or "my" or "mys"
            || value.Contains("warganegara malaysia", StringComparison.Ordinal)
            || value.Contains("rakyat malaysia", StringComparison.Ordinal);
    }

    private static decimal Lookup(IReadOnlyDictionary<string, decimal> map, string key) =>
        map.TryGetValue(key, out var value) ? value : 0m;
}
