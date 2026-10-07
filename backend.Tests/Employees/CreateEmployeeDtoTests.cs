using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Tests.Employees;

// POST /employees must refuse a new employee without an employee number: the
// profile treats a missing one as blocking payroll (LHDN's CP39), so the add
// form letting it through only moved the problem to the first payroll run.
public class CreateEmployeeDtoTests
{
    private static List<ValidationResult> Validate(CreateEmployeeDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    private static CreateEmployeeDto Dto(string? employeeNumber) => new()
    {
        Email = "aisyah@example.com",
        Name = "Aisyah Binti Rahman",
        Role = "Employee",
        EmployeeNumber = employeeNumber,
    };

    // Optional now: a blank one is assigned (EmployeeNumbers.Next), so the
    // add form no longer has to refuse it.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_employee_number_is_allowed(string? number)
    {
        Assert.Empty(Validate(Dto(number)));
    }

    [Fact]
    public void Employee_number_present_passes()
    {
        Assert.Empty(Validate(Dto("EMP-001")));
    }
}
