using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Tests.Employees;

// A blank employee ID is assigned the next number in the company's own pattern.
public class EmployeeNumbersTests
{
    [Fact]
    public void A_company_with_none_starts_at_EMP_001() =>
        Assert.Equal("EMP-001", EmployeeNumbers.Next([null, "", "  "]));

    [Fact]
    public void Continues_the_pattern_keeping_the_padding() =>
        Assert.Equal("GE-0043", EmployeeNumbers.Next(["GE-0041", "GE-0042", "GE-0007"]));

    [Fact]
    public void Plain_numbers_continue_too() =>
        Assert.Equal("1008", EmployeeNumbers.Next(["1007", "1003"]));

    [Fact]
    public void Follows_the_pattern_most_numbers_use_not_a_stray_one() =>
        Assert.Equal("E-004", EmployeeNumbers.Next(["E-001", "E-002", "E-003", "TEMP-900"]));

    [Fact]
    public void Steps_past_a_number_already_taken() =>
        // E-003 is the next by count, but someone typed it by hand on another row.
        Assert.Equal("E-004", EmployeeNumbers.Next(["E-001", "E-002", "e-003"]));
}
