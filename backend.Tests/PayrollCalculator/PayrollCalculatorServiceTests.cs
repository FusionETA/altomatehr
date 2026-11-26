using System.Text.Json;
using AltomateHR.Api.Modules.PayrollCalculator;
using AltomateHR.Api.Modules.PayrollCalculator.Dtos;

namespace AltomateHR.Api.Tests.PayrollCalculator;

// The public payroll calculator against the handover's test cases (§8) — run
// through the real payroll engine, so these also pin that the website and real
// payslips agree. TC1 is the widget's built-in example: it must not drift.
public class PayrollCalculatorServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // November 2026: Lindung 24 (from Jun 2026) is in force.
    private static readonly PayrollCalculatorService Service =
        new(new FixedClock(new DateTimeOffset(2026, 11, 17, 2, 0, 0, TimeSpan.Zero)));

    private static JsonElement Body(
        decimal salary, decimal bonus = 0, string citizenship = "malaysian_pr", string age = "below_60",
        string marital = "single", int children = 0, bool resident = true, decimal reliefs = 0, decimal zakat = 0) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["monthly_salary"] = salary,
            ["bonus"] = bonus,
            ["citizenship"] = citizenship,
            ["age_group"] = age,
            ["marital_status"] = marital,
            ["children"] = children,
            ["tax_resident"] = resident,
            ["other_reliefs_yearly"] = reliefs,
            ["zakat_monthly"] = zakat,
        });

    private static PayrollCalculatorResponse Ok(JsonElement body)
    {
        var outcome = Service.Calculate(body);
        Assert.Null(outcome.Error);
        return outcome.Result!;
    }

    private static PayrollCalculatorItem Item(PayrollCalculatorResponse r, string code) =>
        r.Items.Single(i => i.Code == code);

    public static TheoryData<string, JsonElement, decimal[]> Cases() => new()
    {
        // id, body, [epf ee, epf er, socso ee, socso er, eis ee, eis er, pcb, take-home, cost]
        { "TC1", Body(5000), [550, 650, 61.90m, 86.65m, 9.90m, 9.90m, 110, 4268.20m, 5746.55m] },
        { "TC2", Body(8000), [880, 960, 74.40m, 104.15m, 11.90m, 11.90m, 514.20m, 6519.50m, 9076.05m] },
        { "TC3", Body(5000, marital: "married_spouse_not_working", children: 2), [550, 650, 61.90m, 86.65m, 9.90m, 9.90m, 70, 4308.20m, 5746.55m] },
        { "TC4", Body(3000, citizenship: "foreign"), [60, 60, 22.15m, 36.90m, 0, 0, 0, 2917.85m, 3096.90m] },
        { "TC5", Body(4000, age: "60_and_above"), [0, 160, 29.65m, 49.40m, 0, 0, 70, 3900.35m, 4209.40m] },
        { "TC6", Body(5000, resident: false), [550, 650, 61.90m, 86.65m, 9.90m, 9.90m, 1500, 2878.20m, 5746.55m] },
        { "TC7", Body(5000, bonus: 5000), [1100, 1300, 61.90m, 86.65m, 9.90m, 9.90m, 510, 8318.20m, 11396.55m] },
        { "TC8", Body(5000, zakat: 50), [550, 650, 61.90m, 86.65m, 9.90m, 9.90m, 60, 4268.20m, 5746.55m] },
        { "TC9", Body(2000), [220, 260, 24.40m, 34.15m, 3.90m, 3.90m, 0, 1751.70m, 2298.05m] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void HandoverCases_MatchTheTable(string id, JsonElement body, decimal[] e)
    {
        var r = Ok(body);

        Assert.True(Item(r, "epf").Employee == e[0], $"{id} EPF employee {Item(r, "epf").Employee}");
        Assert.True(Item(r, "epf").Employer == e[1], $"{id} EPF employer {Item(r, "epf").Employer}");
        Assert.True(Item(r, "socso").Employee == e[2], $"{id} SOCSO employee {Item(r, "socso").Employee}");
        Assert.True(Item(r, "socso").Employer == e[3], $"{id} SOCSO employer {Item(r, "socso").Employer}");
        Assert.True(Item(r, "eis").Employee == e[4], $"{id} EIS employee {Item(r, "eis").Employee}");
        Assert.True(Item(r, "eis").Employer == e[5], $"{id} EIS employer {Item(r, "eis").Employer}");
        Assert.True(Item(r, "pcb").Employee == e[6], $"{id} PCB {Item(r, "pcb").Employee}");
        Assert.True(r.TakeHome == e[7], $"{id} take-home {r.TakeHome}");
        Assert.True(r.EmployerCost == e[8], $"{id} cost {r.EmployerCost}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryResponse_KeepsTheContractsArithmeticAndShape(string id, JsonElement body, decimal[] _)
    {
        var r = Ok(body);

        Assert.Equal(2026, r.TaxYear);
        Assert.Equal(r.Items.Sum(i => i.Employee), r.EmployeeTotal);
        Assert.Equal(r.Items.Sum(i => i.Employer), r.EmployerTotal);
        Assert.Equal(r.Gross - r.EmployeeTotal, r.TakeHome);
        Assert.Equal(r.Gross + r.EmployerTotal, r.EmployerCost);

        // epf, socso, eis, pcb in that order; zakat last and only when paid.
        var codes = r.Items.Select(i => i.Code).ToList();
        Assert.Equal(["epf", "socso", "eis", "pcb"], codes.Take(4));
        Assert.Equal(id == "TC8", codes.Contains("zakat"));
        if (id == "TC8") Assert.Equal("zakat", codes[^1]);

        foreach (var item in r.Items)
            Assert.InRange(item.Working.Count, 1, 4);
    }

    [Fact]
    public void Tc1_ExplainsEachFigureInPlainWords()
    {
        var r = Ok(Body(5000));

        Assert.Equal("Employee 11%. Employer 13% because the salary is RM5,000 or less.", Item(r, "epf").Working[0]);
        Assert.Equal("PERKESO's table for wages of RM4,900.01–RM5,000.00: Employer RM86.65.", Item(r, "socso").Working[0]);
        Assert.Equal("Employee RM24.75, plus RM37.15 for Lindung 24 = RM61.90.", Item(r, "socso").Working[1]);
        Assert.Equal(
            [
                "Yearly salary RM60,000 − reliefs RM13,000 (personal RM9,000, EPF RM4,000) = chargeable income RM47,000.",
                "Tax on the first RM35,000 is RM600, then 6% on the next RM12,000 = RM1,320.00 for the year.",
                "RM1,320.00 ÷ 12 months = RM110.00 a month.",
            ],
            Item(r, "pcb").Working);
    }

    [Fact]
    public void ZeroItems_SayWhy()
    {
        var foreign = Ok(Body(3000, citizenship: "foreign"));
        Assert.Equal(["EIS does not apply to foreign workers."], Item(foreign, "eis").Working);

        var senior = Ok(Body(4000, age: "60_and_above"));
        Assert.Equal(["EIS stops at age 60, so nothing is deducted."], Item(senior, "eis").Working);
        Assert.Equal("From age 60 there is no employee EPF.", Item(senior, "epf").Working[0]);

        var low = Ok(Body(2000));
        Assert.Contains("No income tax is due on that.", Item(low, "pcb").Working);
    }

    [Fact]
    public void Bonus_IsExplainedAsAddedTaxThisMonth()
    {
        var r = Ok(Body(5000, bonus: 5000));
        Assert.Contains("The RM5,000.00 bonus raises the year's tax by RM400.00, all deducted this month.", Item(r, "pcb").Working);
        Assert.Contains("SOCSO is not charged on the bonus.", Item(r, "socso").Working);
    }

    // Under RM10 a month no normal PCB is withheld, so the bonus month also
    // carries the year's normal tax — the sentence must not call it all bonus tax.
    [Fact]
    public void Bonus_WithNoMonthlyPcb_SaysTheWholeYearsTaxIsDeducted()
    {
        var r = Ok(Body(3700, bonus: 5000));
        var working = Item(r, "pcb").Working;
        Assert.Contains("That is under RM10 a month, so nothing is deducted.", working);
        Assert.Contains(working, l => l.StartsWith("With the RM5,000.00 bonus the year's tax is RM")
                                      && l.EndsWith("is deducted this month."));
        Assert.DoesNotContain(working, l => l.Contains("raises the year's tax by"));
    }

    [Fact]
    public void NonResident_IsAFlatThirtyPercent()
    {
        var r = Ok(Body(5000, resident: false));
        Assert.Equal(["Non-residents pay a flat 30% with no reliefs: RM5,000.00 × 30% = RM1,500.00."], Item(r, "pcb").Working);
    }

    [Fact]
    public void OtherReliefs_ComeOffChargeableIncome()
    {
        // RM47,000 chargeable less RM6,000 more relief = RM41,000:
        // RM600 + 6% × RM6,000 = RM960 a year, RM80 a month.
        var r = Ok(Body(5000, reliefs: 6000));
        Assert.Equal(80m, Item(r, "pcb").Employee);
        Assert.Contains("other RM6,000", Item(r, "pcb").Working[0]);
        Assert.Contains("chargeable income RM41,000.", Item(r, "pcb").Working[0]);
    }

    [Fact]
    public void Zakat_OffsetsPcbAndIsItsOwnItem()
    {
        var r = Ok(Body(5000, zakat: 50));
        Assert.Equal(60m, Item(r, "pcb").Employee);
        Assert.Equal(50m, Item(r, "zakat").Employee);
        Assert.Contains("Zakat of RM50.00 comes off the PCB, leaving RM60.00.", Item(r, "pcb").Working);
    }

    [Fact]
    public void SingleEmployee_ChildrenAreIgnored()
    {
        var single = Ok(Body(5000, children: 3));
        Assert.Equal(110m, Item(single, "pcb").Employee);
    }

    // The working must land on the PCB actually charged, which LHDN rounds UP
    // to the next 5 sen: RM626.64 ÷ 12 = RM52.22, charged RM52.25.
    [Fact]
    public void Pcb_ShowsTheFiveSenRoundUp_ToTheChargedFigure()
    {
        var r = Ok(Body(4037));
        var pcb = Item(r, "pcb");
        Assert.Equal(52.25m, pcb.Employee);
        Assert.Contains("RM626.64 ÷ 12 months = RM52.22 a month.", pcb.Working);
        Assert.Contains("Rounded up to the next 5 sen: RM52.25.", pcb.Working);
    }

    // P lands at RM35,000.07 at RM4,000/month: the engine still gives the
    // rebate, so the working must not show the "RM600 + 6% …" sum.
    [Fact]
    public void Pcb_AtTheRebateBoundary_AddsUp()
    {
        var pcb = Item(Ok(Body(4000)), "pcb");
        Assert.Equal(16.70m, pcb.Employee);
        Assert.Contains("Tax on that is RM200.00 for the year, after LHDN's tax rebate.", pcb.Working);
        Assert.DoesNotContain(pcb.Working, w => w.Contains("Tax on the first"));
        Assert.Contains("Rounded up to the next 5 sen: RM16.70.", pcb.Working);
    }

    // Zakat is offset against the ROUNDED PCB (RM16.70), so RM16.69 of zakat
    // all counts and leaves RM0.01 — no "PCB can't go below RM0" note.
    [Fact]
    public void Zakat_ComparesAgainstTheChargedPcb()
    {
        var r = Ok(Body(4000, zakat: 16.69m));
        Assert.Equal(0.01m, Item(r, "pcb").Employee);
        Assert.DoesNotContain(Item(r, "zakat").Working, w => w.Contains("can't go below"));

        var over = Ok(Body(4000, zakat: 20m));
        Assert.Contains("PCB can't go below RM0, so only RM16.70 of it reduces the tax.", Item(over, "zakat").Working);
    }

    [Fact]
    public void NonResident_SmallBonus_SaysItIsUnderTheMinimum()
    {
        var r = Ok(Body(5000, bonus: 30, resident: false));
        Assert.Equal(1500m, Item(r, "pcb").Employee);
        Assert.Equal(
            [
                "Non-residents pay a flat 30% with no reliefs: RM5,000.00 × 30% = RM1,500.00.",
                "Bonus RM30.00 × 30% is under RM10, so nothing is deducted on it.",
            ],
            Item(r, "pcb").Working);
    }

    [Fact]
    public void AboveTheSocsoCeiling_SaysSo()
    {
        var r = Ok(Body(8000));
        Assert.StartsWith("The salary is above the RM6,000.00 ceiling", Item(r, "socso").Working[0]);
    }

    // ─── Validation (§5, §8 errors) ─────────────────────────────────────

    private static PayrollCalculatorError Error(JsonElement body)
    {
        var outcome = Service.Calculate(body);
        Assert.Null(outcome.Result);
        return outcome.Error!;
    }

    private static JsonElement With(string key, object? value)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(Body(5000).GetRawText())!;
        map[key] = value;
        return JsonSerializer.SerializeToElement(map);
    }

    private static JsonElement Without(string key)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(Body(5000).GetRawText())!;
        map.Remove(key);
        return JsonSerializer.SerializeToElement(map);
    }

    [Fact]
    public void ZeroSalary_IsOutOfRangeOnThatField()
    {
        var e = Error(With("monthly_salary", 0));
        Assert.Equal(("monthly_salary", "out_of_range"), (e.Field, e.Code));
        Assert.Equal("Enter a monthly salary above RM0.", e.Message);
    }

    [Fact]
    public void UnknownCitizenship_IsAnInvalidValue()
    {
        var e = Error(With("citizenship", "pr"));
        Assert.Equal(("citizenship", "invalid_value"), (e.Field, e.Code));
    }

    [Fact]
    public void ElevenChildren_IsOutOfRange()
    {
        var e = Error(With("children", 11));
        Assert.Equal(("children", "out_of_range"), (e.Field, e.Code));
    }

    [Fact]
    public void MissingField_IsRequired()
    {
        var e = Error(Without("bonus"));
        Assert.Equal(("bonus", "required"), (e.Field, e.Code));
    }

    [Fact]
    public void SalaryAsText_IsAnInvalidType()
    {
        var e = Error(With("monthly_salary", "5000"));
        Assert.Equal(("monthly_salary", "invalid_type"), (e.Field, e.Code));
    }

    [Fact]
    public void ThreeDecimals_IsAnInvalidValue()
    {
        var e = Error(With("zakat_monthly", 10.555m));
        Assert.Equal(("zakat_monthly", "invalid_value"), (e.Field, e.Code));
    }

    [Fact]
    public void TaxResidentAsText_IsAnInvalidType()
    {
        var e = Error(With("tax_resident", "yes"));
        Assert.Equal(("tax_resident", "invalid_type"), (e.Field, e.Code));
    }

    [Fact]
    public void NotAnObject_HasNoField()
    {
        var e = Error(JsonSerializer.SerializeToElement(new[] { 1, 2 }));
        Assert.Null(e.Field);
        Assert.Equal("invalid_type", e.Code);
    }

    [Fact]
    public void ExtraKeys_AreIgnored()
    {
        var r = Ok(With("favourite_colour", "purple"));
        Assert.Equal(4268.20m, r.TakeHome);
    }
}
