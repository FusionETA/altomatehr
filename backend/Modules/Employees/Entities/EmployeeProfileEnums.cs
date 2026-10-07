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

// The terms someone is employed on — LHDN's C.P.8D "Status Pekerja" (field 5).
// MANAGEMENT is company directors, principal officers, partners and the like.
public enum EmploymentStatus
{
    MANAGEMENT,
    PERMANENT,
    CONTRACT,
    PART_TIME,
    INDUSTRIAL_TRAINEE,
    OTHER,
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
    // A Malaysian bank account — paid through the company's bank payroll file.
    BANK_TRANSFER,
    CASH,
    CHEQUE,
    // A bank or e-wallet the payroll file can't reach — Merchantrade, an
    // overseas bank (Union Bank, a Bangladeshi or Nepali bank …). Paid by hand
    // from the run's Manual payments sheet, never put in the bank file.
    OTHER_TRANSFER,
}
