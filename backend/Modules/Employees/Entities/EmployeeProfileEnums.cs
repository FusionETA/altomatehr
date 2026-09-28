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

// The three approvals LHDN taxes at a flat 15% instead of the resident bands
// (MTD Spec 2026, D.b.3–5). REP and knowledge workers keep the RM 400/800
// rebate when chargeable income is RM 35,000 or less; C-suite has no rebate.
public enum SpecialTaxScheme
{
    // Returning Expert Programme — five consecutive years of assessment.
    RETURNING_EXPERT,
    // Knowledge worker employed by a designated company in a specified region.
    KNOWLEDGE_WORKER,
    // A resident non-citizen in a C-suite position with an approved company.
    C_SUITE,
}

public enum PaymentMethod
{
    BANK_TRANSFER,
    CASH,
    CHEQUE,
}
