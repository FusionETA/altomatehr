namespace AltomateHR.Api.Modules.Employees.Entities;

// Malaysian HR / statutory enums, ported from the monolith's PayrollProfile.
// SalaryType is reused from the Policies module (HOURLY | MONTHLY).

public enum Gender
{
    MALE,
    FEMALE,
}

public enum IdType
{
    NRIC,
    PASSPORT,
    ARMY_NO,
    POLICE_NO,
}

public enum MaritalStatus
{
    SINGLE,
    MARRIED,
    DIVORCED,
    WIDOWED,
}

public enum SocsoScheme
{
    EMPLOYMENT_INJURY_INVALIDITY,
    EMPLOYMENT_INJURY_ONLY,
}

public enum PaymentMethod
{
    BANK_TRANSFER,
    CASH,
    CHEQUE,
}
