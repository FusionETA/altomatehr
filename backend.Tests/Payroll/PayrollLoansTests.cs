using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// Loan repayment arithmetic.
//
// One rule dominates: **the installments add up to the principal**. Repaying
// a sen more than was borrowed is a complaint; a sen less is an unapproved
// write-off. Every schedule built here is checked against its own principal.
public class PayrollLoansTests
{
    private static EmployeeLoan Loan(
        decimal principal = 1200m,
        decimal installmentAmount = 100m,
        int installmentCount = 12,
        int startYear = 2026,
        int startMonth = 1,
        LoanStatus status = LoanStatus.ACTIVE,
        string? scheduleJson = null) => new()
        {
            OrganizationId = "org-1",
            EmployeeProfileId = "emp-1",
            PrincipalAmount = principal,
            InstallmentAmount = installmentAmount,
            InstallmentCount = installmentCount,
            StartYear = startYear,
            StartMonth = startMonth,
            Status = status,
            ScheduleJson = scheduleJson,
        };

    // ─── The invariant ──────────────────────────────────────────────────

    // The headline monthly figure rarely divides exactly. The last
    // installment absorbs the difference so the total is the principal, not
    // twelve times a rounded number.
    [Theory]
    [InlineData(1200, 12)]
    [InlineData(1000, 3)]      // 333.33 × 3 = 999.99, so the last is 333.34
    [InlineData(5000, 7)]
    [InlineData(99.99, 4)]
    [InlineData(10000, 13)]
    public void AnEqualSplitAlwaysTotalsThePrincipal(decimal principal, int count)
    {
        var terms = PayrollLoans.ComputeTerms(LoanRepaymentMode.FIXED, principal, count, null);
        var schedule = PayrollLoans.BuildEqualSchedule(
            principal, terms.InstallmentAmount, terms.InstallmentCount);

        Assert.Equal(count, schedule.Count);
        Assert.Equal(principal, schedule.Sum());
    }

    [Fact]
    public void TheLastInstallmentCarriesTheRounding()
    {
        var schedule = PayrollLoans.BuildEqualSchedule(1000m, 333.33m, 3);

        Assert.Equal([333.33m, 333.33m, 333.34m], schedule);
    }

    // ─── Terms ──────────────────────────────────────────────────────────

    [Fact]
    public void FixedMode_DividesThePrincipalOverTheMonths()
    {
        var terms = PayrollLoans.ComputeTerms(LoanRepaymentMode.FIXED, 1200m, 12, null);

        Assert.Equal(100m, terms.InstallmentAmount);
        Assert.Equal(12, terms.InstallmentCount);
    }

    [Fact]
    public void CustomMode_WorksOutHowManyMonths()
    {
        var terms = PayrollLoans.ComputeTerms(LoanRepaymentMode.CUSTOM, 1000m, null, 300m);

        Assert.Equal(300m, terms.InstallmentAmount);
        Assert.Equal(4, terms.InstallmentCount);   // 3 × 300 + 100
    }

    // A repayment at or above the loan settles it in one go rather than
    // deducting more than was borrowed.
    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    public void ARepaymentCoveringTheWholeLoan_SettlesInOneInstallment(decimal monthly)
    {
        var terms = PayrollLoans.ComputeTerms(LoanRepaymentMode.CUSTOM, 1000m, null, monthly);

        Assert.Equal(1000m, terms.InstallmentAmount);
        Assert.Equal(1, terms.InstallmentCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void APrincipalOfNothing_IsRefused(decimal principal)
    {
        Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.ComputeTerms(LoanRepaymentMode.FIXED, principal, 12, null));
    }

    [Fact]
    public void FixedModeWithNoMonths_IsRefused()
    {
        Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.ComputeTerms(LoanRepaymentMode.FIXED, 1200m, 0, null));
    }

    [Fact]
    public void CustomModeWithNoAmount_IsRefused()
    {
        Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.ComputeTerms(LoanRepaymentMode.CUSTOM, 1200m, null, 0m));
    }

    // ─── Periods ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 2026, 11)]
    [InlineData(1, 2026, 12)]
    [InlineData(2, 2027, 1)]     // rolls into the next year
    [InlineData(13, 2027, 12)]
    [InlineData(14, 2028, 1)]
    public void TheScheduleRollsIntoLaterYears(int index, int year, int month)
    {
        var period = PayrollLoans.PeriodAtIndex(2026, 11, index);

        Assert.Equal(new PayrollLoans.Period(year, month), period);
    }

    [Fact]
    public void TheEndPeriodIsTheLastDeduction()
    {
        var end = PayrollLoans.EndPeriod(Loan(startYear: 2026, startMonth: 11, installmentCount: 4));

        Assert.Equal(new PayrollLoans.Period(2027, 2), end);
    }

    // ─── What a run deducts ─────────────────────────────────────────────

    [Fact]
    public void EachPeriodDeductsItsOwnInstallment()
    {
        var loan = Loan(principal: 1000m, installmentAmount: 333.33m, installmentCount: 3);

        Assert.Equal(333.33m, PayrollLoans.InstallmentForPeriod(loan, 2026, 1));
        Assert.Equal(333.33m, PayrollLoans.InstallmentForPeriod(loan, 2026, 2));
        Assert.Equal(333.34m, PayrollLoans.InstallmentForPeriod(loan, 2026, 3));
    }

    [Theory]
    [InlineData(2025, 12)]   // before it starts
    [InlineData(2027, 1)]    // after it ends
    public void APeriodOutsideTheWindow_DeductsNothing(int year, int month)
    {
        Assert.Equal(0m, PayrollLoans.InstallmentForPeriod(Loan(), year, month));
    }

    // A cancelled loan has to stop deducting NOW, not at the end of its
    // schedule — that is the entire point of cancelling one.
    [Theory]
    [InlineData(LoanStatus.CANCELLED)]
    [InlineData(LoanStatus.COMPLETED)]
    public void AnInactiveLoan_DeductsNothing(LoanStatus status)
    {
        Assert.Equal(0m, PayrollLoans.InstallmentForPeriod(Loan(status: status), 2026, 1));
    }

    // ─── Stored schedules ───────────────────────────────────────────────

    // A hand-varied schedule — a smaller month, a lump sum at bonus time —
    // must survive, or the admin's edit is silently undone every run.
    [Fact]
    public void AStoredSchedule_IsUsedInsteadOfAnEqualSplit()
    {
        var loan = Loan(principal: 1200m, installmentCount: 3,
            scheduleJson: "[100.00,100.00,1000.00]");

        Assert.Equal(100m, PayrollLoans.InstallmentForPeriod(loan, 2026, 1));
        Assert.Equal(1000m, PayrollLoans.InstallmentForPeriod(loan, 2026, 3));
    }

    // A stored schedule of the wrong length means the terms were edited
    // without it. Reading past its end would silently deduct zero, so the
    // equal split takes over instead.
    [Fact]
    public void AScheduleOfTheWrongLength_IsIgnored()
    {
        var loan = Loan(principal: 1200m, installmentAmount: 100m, installmentCount: 12,
            scheduleJson: "[100.00,100.00]");

        Assert.Equal(12, PayrollLoans.ResolveSchedule(loan).Count);
        Assert.Equal(1200m, PayrollLoans.ResolveSchedule(loan).Sum());
    }

    // Failing a whole payroll run over a malformed column would be far worse
    // than falling back to the split the principal and count already imply.
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"nope\":1}")]
    [InlineData("")]
    public void AMalformedSchedule_FallsBackToTheEqualSplit(string json)
    {
        var loan = Loan(scheduleJson: json);

        Assert.Equal(12, PayrollLoans.ResolveSchedule(loan).Count);
        Assert.Equal(1200m, PayrollLoans.ResolveSchedule(loan).Sum());
    }

    [Fact]
    public void AScheduleRoundTripsThroughJson()
    {
        var original = PayrollLoans.BuildEqualSchedule(1000m, 333.33m, 3);
        var loan = Loan(principal: 1000m, installmentCount: 3,
            scheduleJson: PayrollLoans.SerialiseSchedule(original));

        Assert.Equal(original, PayrollLoans.ResolveSchedule(loan));
    }

    // ─── Validation ─────────────────────────────────────────────────────

    [Fact]
    public void AScheduleThatDoesNotAddUp_IsRefused()
    {
        var ex = Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.ValidateSchedule([100m, 100m, 100m], principal: 1200m));

        Assert.Contains("1200.00", ex.Message);
        Assert.Contains("300.00", ex.Message);
    }

    // A sen of slack, because each installment is already rounded.
    [Fact]
    public void ASenOfRoundingIsTolerated()
    {
        PayrollLoans.ValidateSchedule([333.33m, 333.33m, 333.33m], principal: 1000m);
    }

    [Fact]
    public void AnEmptySchedule_IsRefused()
    {
        Assert.Throws<PayrollLoanException>(() => PayrollLoans.ValidateSchedule([], 1000m));
    }

    [Fact]
    public void ANegativeInstallment_IsRefused()
    {
        Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.ValidateSchedule([650m, -50m, 600m], 1200m));
    }

    // RM 0 in the middle is a skipped or paused month — the loan just ends
    // later. On the END it would report a last month nothing is taken in.
    [Fact]
    public void AZeroMonthInTheMiddle_IsASkip()
    {
        PayrollLoans.ValidateSchedule([600m, 0m, 600m], 1200m);
    }

    [Fact]
    public void AZeroLastMonth_IsRefused()
    {
        Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.ValidateSchedule([600m, 600m, 0m], 1200m));
    }

    [Fact]
    public void AGeneratedScheduleAlwaysValidates()
    {
        var (terms, schedule) = PayrollLoans.BuildFromTerms(
            LoanRepaymentMode.CUSTOM, 4999.99m, null, 700m);

        PayrollLoans.ValidateSchedule(schedule, 4999.99m);
        Assert.Equal(terms.InstallmentCount, schedule.Count);
    }

    // ─── Progress ───────────────────────────────────────────────────────

    // An installment is paid when its period has a SUBMITTED run. The loan
    // records no payments of its own, which is what makes a reverted month
    // un-pay its installment for free.
    [Fact]
    public void ProgressIsReadFromTheSubmittedPeriods()
    {
        var loan = Loan(principal: 1200m, installmentCount: 12);
        var submitted = new[]
        {
            new PayrollLoans.Period(2026, 1),
            new PayrollLoans.Period(2026, 2),
        };

        var summary = PayrollLoans.Summarise(loan, submitted);

        Assert.Equal(2, summary.PaidInstallments);
        Assert.Equal(200m, summary.PaidAmount);
        Assert.Equal(1000m, summary.RemainingAmount);
        Assert.True(summary.HasStarted);
        Assert.False(summary.FullyRepaid);
        Assert.Equal(2026, summary.EndYear);
        Assert.Equal(12, summary.EndMonth);
    }

    [Fact]
    public void RevertingAMonth_UnpaysItsInstallment()
    {
        var loan = Loan(principal: 1200m, installmentCount: 12);

        var afterThree = PayrollLoans.Summarise(loan,
        [
            new(2026, 1), new(2026, 2), new(2026, 3),
        ]);
        var afterRevertingMarch = PayrollLoans.Summarise(loan,
        [
            new(2026, 1), new(2026, 2),
        ]);

        Assert.Equal(300m, afterThree.PaidAmount);
        Assert.Equal(200m, afterRevertingMarch.PaidAmount);
    }

    [Fact]
    public void ALoanWithNoSubmittedPeriods_HasNotStarted()
    {
        var summary = PayrollLoans.Summarise(Loan(), []);

        Assert.False(summary.HasStarted);
        Assert.Equal(0m, summary.PaidAmount);
        Assert.Equal(1200m, summary.RemainingAmount);
    }

    [Fact]
    public void EveryPeriodSubmitted_IsFullyRepaid()
    {
        var loan = Loan(principal: 300m, installmentAmount: 100m, installmentCount: 3);

        var summary = PayrollLoans.Summarise(loan,
        [
            new(2026, 1), new(2026, 2), new(2026, 3),
        ]);

        Assert.True(summary.FullyRepaid);
        Assert.Equal(300m, summary.PaidAmount);
        Assert.Equal(0m, summary.RemainingAmount);
    }

    // A period outside the loan's own window must not count towards it.
    [Fact]
    public void ASubmittedMonthOutsideTheWindow_DoesNotCount()
    {
        var loan = Loan(principal: 300m, installmentAmount: 100m, installmentCount: 3);

        var summary = PayrollLoans.Summarise(loan, [new(2027, 6)]);

        Assert.Equal(0, summary.PaidInstallments);
    }

    [Fact]
    public void TheBreakdownNamesEveryPeriodInOrder()
    {
        var loan = Loan(principal: 300m, installmentAmount: 100m,
            installmentCount: 3, startYear: 2026, startMonth: 11);

        var breakdown = PayrollLoans.Breakdown(loan, []);

        Assert.Equal([(2026, 11), (2026, 12), (2027, 1)],
            breakdown.Select(i => (i.Year, i.Month)));
    }

    [Fact]
    public void APeriodLabelIsShortAndUnambiguous()
    {
        Assert.Equal("Jan 2026", PayrollLoans.PeriodLabel(2026, 1));
        Assert.Equal("Dec 2027", PayrollLoans.PeriodLabel(2027, 12));
    }

    // ─── Changing a loan that has started ───────────────────────────────

    private static PayrollLoans.Period P(int year, int month) => new(year, month);

    // Locked = submitted or awaiting approval. The first month after the last
    // locked one is where changes may start.
    [Fact]
    public void TheFirstEditableMonth_FollowsTheLastLockedOne()
    {
        var loan = Loan();   // Jan–Dec 2026

        Assert.Equal(0, PayrollLoans.FirstEditableIndex(loan, []));
        Assert.Equal(3, PayrollLoans.FirstEditableIndex(loan, [P(2026, 1), P(2026, 2), P(2026, 3)]));
        // A run from before the loan began locks nothing of it.
        Assert.Equal(0, PayrollLoans.FirstEditableIndex(loan, [P(2025, 12)]));
    }

    // RM 1,200 over 12 months, three repaid: RM 900 left, re-spread over three
    // more months instead of nine. The three repaid months do not move.
    [Fact]
    public void Replan_KeepsTheLockedMonthsAndSpreadsTheBalance()
    {
        var before = PayrollLoans.BuildEqualSchedule(1200m, 100m, 12);

        var after = PayrollLoans.Replan(before, 1200m, 3, LoanRepaymentMode.FIXED, 3, null, null);

        Assert.Equal([100m, 100m, 100m, 300m, 300m, 300m], after);
    }

    // Longer, the other way: the same RM 900 at RM 50 a month.
    [Fact]
    public void Replan_AtAMonthlyAmount_WorksOutHowManyMonths()
    {
        var before = PayrollLoans.BuildEqualSchedule(1200m, 100m, 12);

        var after = PayrollLoans.Replan(before, 1200m, 3, LoanRepaymentMode.CUSTOM, null, 50m, null);

        Assert.Equal(3 + 18, after.Count);
        Assert.Equal(1200m, after.Sum());
    }

    [Fact]
    public void Replan_TypedAmountsMustAddUpToTheBalance()
    {
        var before = PayrollLoans.BuildEqualSchedule(1200m, 100m, 12);

        var ok = PayrollLoans.Replan(before, 1200m, 3, LoanRepaymentMode.CUSTOM, null, null, [500m, 400m]);
        Assert.Equal([100m, 100m, 100m, 500m, 400m], ok);

        var ex = Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.Replan(before, 1200m, 3, LoanRepaymentMode.CUSTOM, null, null, [500m, 300m]));
        Assert.Contains("900.00", ex.Message);
    }

    [Fact]
    public void Replan_WithEverythingFiled_IsRefused()
    {
        var before = PayrollLoans.BuildEqualSchedule(1200m, 100m, 12);

        Assert.Throws<PayrollLoanException>(() =>
            PayrollLoans.Replan(before, 1200m, 12, LoanRepaymentMode.FIXED, 3, null, null));
    }

    [Fact]
    public void Skipping_PushesTheRestBackWithoutForgivingAnything()
    {
        var after = PayrollLoans.InsertSkipped([100m, 100m, 100m, 100m], 2, 2);

        Assert.Equal([100m, 100m, 0m, 0m, 100m, 100m], after);
    }

    // Nov–Dec already skipped; now Oct–Dec too. Only October's installment
    // was still due in that window, so the loan ends ONE month later — and
    // the Nov–Dec skip stays in November and December rather than moving on.
    [Fact]
    public void SkippingOverAnEarlierSkip_DisplacesOnlyTheMoneyStillDue()
    {
        //                 Oct   Nov  Dec  Jan   Feb
        decimal[] before = [100m, 0m, 0m, 100m, 100m];

        var after = PayrollLoans.InsertSkipped(before, 0, 3);

        Assert.Equal([0m, 0m, 0m, 100m, 100m, 100m], after);
    }

    // A later skip does not drag an earlier one along either.
    [Fact]
    public void ALaterSkip_LeavesAnEarlierOneInItsMonth()
    {
        //                 Oct   Nov  Dec   Jan
        decimal[] before = [100m, 0m, 100m, 100m];

        var after = PayrollLoans.InsertSkipped(before, 0, 1);

        Assert.Equal([0m, 0m, 100m, 100m, 100m], after);
    }

    // Paused from April: January–March still deduct; April onwards takes
    // nothing, and a submitted April run is not a repayment.
    [Fact]
    public void APausedLoan_DeductsBeforeThePauseOnly()
    {
        var loan = Loan(status: LoanStatus.PAUSED);
        loan.PausedFromYear = 2026;
        loan.PausedFromMonth = 4;

        Assert.Equal(100m, PayrollLoans.InstallmentForPeriod(loan, 2026, 3));
        Assert.Equal(0m, PayrollLoans.InstallmentForPeriod(loan, 2026, 4));
        Assert.Equal(0m, PayrollLoans.InstallmentForPeriod(loan, 2026, 9));

        var breakdown = PayrollLoans.Breakdown(loan, [P(2026, 3), P(2026, 4)]);
        Assert.True(breakdown[2].Paid);
        Assert.False(breakdown[3].Paid);
        Assert.True(breakdown[3].Paused);
    }

    // ─── Warnings ───────────────────────────────────────────────────────

    [Fact]
    public void AWarning_WhenRepaymentsRunPastTheLastDay()
    {
        var warnings = PayrollLoans.Warnings(Loan(), [], [], null, new DateTime(2026, 9, 30));

        var warning = Assert.Single(warnings);
        Assert.Contains("Dec 2026", warning);
        Assert.Contains("RM 300.00", warning);   // Oct, Nov, Dec
    }

    // RM 1,200 a month from a RM 2,000 salary is more than half of it.
    [Fact]
    public void AWarning_WhenLoanDeductionsPassHalfTheSalary()
    {
        var big = Loan(principal: 2400m, installmentAmount: 1200m, installmentCount: 2);

        var warning = Assert.Single(PayrollLoans.Warnings(big, [], [], 2000m, null));
        Assert.Contains("Jan 2026", warning);
        Assert.Contains("half", warning);
    }

    // Two loans that are fine alone can be too much together.
    [Fact]
    public void TheHalfSalaryWarning_CountsTheEmployeesOtherLoans()
    {
        var first = Loan(principal: 1800m, installmentAmount: 600m, installmentCount: 3);
        var second = Loan(principal: 1800m, installmentAmount: 600m, installmentCount: 3);
        second.Id = "loan-2";

        Assert.Empty(PayrollLoans.Warnings(first, [], [], 2000m, null));
        Assert.Single(PayrollLoans.Warnings(first, [second], [], 2000m, null));
    }

    [Fact]
    public void ACancelledLoan_WarnsAboutNothing()
    {
        var loan = Loan(status: LoanStatus.CANCELLED);

        Assert.Empty(PayrollLoans.Warnings(loan, [], [], 100m, new DateTime(2026, 1, 1)));
    }
}
