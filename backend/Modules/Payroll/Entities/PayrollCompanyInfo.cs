using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// The employer's filing identity — one row per organization.
//
// Every statutory body issues its own registration number, so these are
// genuinely separate fields rather than one "company number": EmployerTin is
// LHDN's E number, PerkesoEmployerCode is SOCSO/EIS, EpfEmployerNo is KWSP,
// HrdfEmployerNo is HRD Corp, ZakatNumber is the state zakat authority.
//
// Updated rarely — annually at most — which is why it is not on PayrollSettings.
public class PayrollCompanyInfo : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — one row per org

    // ---- Employer identity ----
    [MaxLength(200)] public string? EmployerName { get; set; }
    [MaxLength(40)] public string? EmployerTin { get; set; }         // LHDN E number
    [MaxLength(60)] public string? RegistrationNo { get; set; }      // SSM
    [MaxLength(40)] public string? ReferenceType { get; set; }
    [MaxLength(60)] public string? ReferenceNo { get; set; }
    [MaxLength(40)] public string? EmployerCategory { get; set; }
    [MaxLength(40)] public string? EmployerStatus { get; set; }
    [MaxLength(40)] public string? Cp8dFurnishType { get; set; }

    // ---- Statutory registrations ----
    [MaxLength(40)] public string? PerkesoEmployerCode { get; set; }
    [MaxLength(40)] public string? EpfEmployerNo { get; set; }

    // Only populated when the org is registered under Part I / II of the PSMB
    // Act — smaller or newly-registered orgs leave it blank.
    [MaxLength(40)] public string? HrdfEmployerNo { get; set; }

    // From LZS / PPZ / the relevant state zakat authority.
    [MaxLength(40)] public string? ZakatNumber { get; set; }

    // ---- Registered address ----
    [MaxLength(160)] public string? AddressLine1 { get; set; }
    [MaxLength(160)] public string? AddressLine2 { get; set; }
    [MaxLength(20)] public string? Postcode { get; set; }
    [MaxLength(120)] public string? City { get; set; }
    [MaxLength(60)] public string? State { get; set; }
    [MaxLength(60)] public string? Country { get; set; } = "Malaysia";

    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(40)] public string? Handphone { get; set; }
    [MaxLength(160)] public string? Email { get; set; }

    // ---- Tax agent ----
    [MaxLength(200)] public string? TaxAgentName { get; set; }
    [MaxLength(40)] public string? TaxAgentTin { get; set; }
    [MaxLength(60)] public string? TaxAgentLicenceNo { get; set; }
    [MaxLength(40)] public string? TaxAgentPhone { get; set; }
    [MaxLength(160)] public string? TaxAgentEmail { get; set; }

    [MaxLength(200)] public string? TaxAgentFirmName { get; set; }
    [MaxLength(160)] public string? TaxAgentFirmAddressLine1 { get; set; }
    [MaxLength(160)] public string? TaxAgentFirmAddressLine2 { get; set; }
    [MaxLength(20)] public string? TaxAgentFirmPostcode { get; set; }
    [MaxLength(120)] public string? TaxAgentFirmCity { get; set; }
    [MaxLength(60)] public string? TaxAgentFirmState { get; set; }

    // ---- Declarant (who signs Form E) ----
    [MaxLength(200)] public string? DeclarantName { get; set; }
    public IdType? DeclarantIdType { get; set; }
    [MaxLength(40)] public string? DeclarantIdNumber { get; set; }
    [MaxLength(120)] public string? DeclarantPosition { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
