namespace AltomateHR.Api.Modules.Policies.Entities;

// How an employee on this policy is paid. Drives payroll later.
public enum SalaryType
{
    HOURLY,
    MONTHLY,
}

// How approved overtime is paid out. (Rate multipliers land with the OT pass.)
public enum OtMethod
{
    CASH,
    TIME_BANK,
}
