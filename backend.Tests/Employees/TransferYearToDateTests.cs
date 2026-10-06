using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Employees;

// What an employee transfer writes as the target's "previous employment" for
// the year: everything earned this year anywhere EXCEPT at the target. Each
// case is RM 5,000 a month.
//
// The bug these guard: on a return (A → B → A) the transfer used to carry
// A's own months back into A and set PrevIncludesPriorThisOrgPeriod, which
// payroll nets against A's GROWING year-to-date — so every month back at A
// ate into B's months until PCB fell to zero.
public class TransferYearToDateTests
{
    private const int Year = 2026;

    private static PayrollYtdTotals Ytd(decimal taxable, decimal pcb = 0m) =>
        new() { Taxable = taxable, Epf = taxable * 0.11m, Pcb = pcb };

    [Fact]
    public void First_transfer_carries_the_source_pay()
    {
        // Jan–Mar at A, moved to B on 1 Apr.
        var source = new EmployeeProfile();
        var target = new EmployeeProfile();

        EmployeeTransferService.CarryYearToDate(source, Ytd(15_000m, 300m), null, Year, target);

        Assert.Equal(Year, target.PrevEmploymentYear);
        Assert.Equal(15_000m, target.PrevRemuneration);
        Assert.Equal(1_650m, target.PrevEpf);
        Assert.Equal(300m, target.PrevPcb);
        Assert.False(target.PrevIncludesPriorThisOrgPeriod);
    }

    [Fact]
    public void Returning_carries_only_the_other_company_s_months()
    {
        // Jan–Mar at A, Apr–Jun at B (whose prev holds A's 15k), back to A on 1 Jul.
        var source = new EmployeeProfile { PrevEmploymentYear = Year, PrevRemuneration = 15_000m };
        var target = new EmployeeProfile();

        EmployeeTransferService.CarryYearToDate(
            source, sourceYtd: Ytd(15_000m), targetOwnYtd: Ytd(15_000m), Year, target);

        // B's three months — not A's own, which payroll adds from A's own YTD.
        Assert.Equal(15_000m, target.PrevRemuneration);
        Assert.False(target.PrevIncludesPriorThisOrgPeriod);
    }

    [Fact]
    public void Moving_on_again_counts_each_month_once()
    {
        // …then A → C on 1 Oct. A's prev holds B's 15k; A itself paid Jan–Mar + Jul–Sep.
        var source = new EmployeeProfile { PrevEmploymentYear = Year, PrevRemuneration = 15_000m };
        var target = new EmployeeProfile();

        EmployeeTransferService.CarryYearToDate(source, Ytd(30_000m), null, Year, target);

        Assert.Equal(45_000m, target.PrevRemuneration);   // nine months, not twelve
    }

    [Fact]
    public void A_flagged_source_has_its_own_months_taken_out_first()
    {
        // A source whose prev was declared as already including its own months
        // (a rehire): the same netting payroll applies, so it isn't counted twice.
        var source = new EmployeeProfile
        {
            PrevEmploymentYear = Year,
            PrevRemuneration = 20_000m,
            PrevIncludesPriorThisOrgPeriod = true,
        };
        var target = new EmployeeProfile();

        EmployeeTransferService.CarryYearToDate(source, Ytd(5_000m), null, Year, target);

        Assert.Equal(20_000m, target.PrevRemuneration);   // 15k elsewhere + 5k here
    }

    [Fact]
    public void Last_year_s_previous_employment_is_not_carried()
    {
        var source = new EmployeeProfile { PrevEmploymentYear = Year - 1, PrevRemuneration = 50_000m };
        var target = new EmployeeProfile();

        EmployeeTransferService.CarryYearToDate(source, Ytd(10_000m), null, Year, target);

        Assert.Equal(10_000m, target.PrevRemuneration);
    }

    // The old company's later submits re-carry only while the target still holds
    // what the transfer wrote. The database hands decimals back at 2 dp, so the
    // fingerprint must not change with the scale.
    [Fact]
    public void Fingerprint_ignores_decimal_scale_but_sees_a_hand_edit()
    {
        var written = new EmployeeProfile { PrevEmploymentYear = Year, PrevRemuneration = 15000m, PrevPcb = 300m };
        var reloaded = new EmployeeProfile { PrevEmploymentYear = Year, PrevRemuneration = 15000.00m, PrevPcb = 300.00m };
        var edited = new EmployeeProfile { PrevEmploymentYear = Year, PrevRemuneration = 15000m, PrevPcb = 350m };

        Assert.Equal(EmployeeTransferService.PrevFingerprint(written), EmployeeTransferService.PrevFingerprint(reloaded));
        Assert.NotEqual(EmployeeTransferService.PrevFingerprint(written), EmployeeTransferService.PrevFingerprint(edited));
    }
}
