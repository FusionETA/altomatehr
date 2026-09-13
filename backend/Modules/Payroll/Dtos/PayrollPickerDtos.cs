namespace AltomateHR.Api.Modules.Payroll.Dtos;

// What the "Start a payroll run" picker needs: every policy the admin can scope
// a run to, each with the employees under it. Only employees whose profile is
// complete (payable) are listed — the whole point of the picker is choosing
// among the people a run can actually pay; incomplete profiles surface in the
// run's "Needs attention" instead.
public class PayrollRunPickerDto
{
    public List<PayrollPickerPolicyDto> Policies { get; set; } = [];
}

public class PayrollPickerPolicyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public List<PayrollPickerMemberDto> Members { get; set; } = [];
}

public class PayrollPickerMemberDto
{
    public string EmployeeProfileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;   // the org's employee number
    public string JobTitle { get; set; } = string.Empty;
}
