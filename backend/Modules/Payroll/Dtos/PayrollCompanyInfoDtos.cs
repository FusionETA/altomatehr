using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

public class PayrollCompanyInfoDto
{
    public string? EmployerName { get; set; }
    public string? EmployerTin { get; set; }
    public string? RegistrationNo { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceNo { get; set; }
    public string? EmployerCategory { get; set; }
    public string? EmployerStatus { get; set; }
    public string? Cp8dFurnishType { get; set; }

    public string? PerkesoEmployerCode { get; set; }
    public string? EpfEmployerNo { get; set; }
    public string? HrdfEmployerNo { get; set; }
    public string? ZakatNumber { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Postcode { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Handphone { get; set; }
    public string? Email { get; set; }

    public string? TaxAgentName { get; set; }
    public string? TaxAgentTin { get; set; }
    public string? TaxAgentLicenceNo { get; set; }
    public string? TaxAgentPhone { get; set; }
    public string? TaxAgentEmail { get; set; }
    public string? TaxAgentFirmName { get; set; }
    public string? TaxAgentFirmAddressLine1 { get; set; }
    public string? TaxAgentFirmAddressLine2 { get; set; }
    public string? TaxAgentFirmPostcode { get; set; }
    public string? TaxAgentFirmCity { get; set; }
    public string? TaxAgentFirmState { get; set; }

    public string? DeclarantName { get; set; }
    public IdType? DeclarantIdType { get; set; }
    public string? DeclarantIdNumber { get; set; }
    public string? DeclarantPosition { get; set; }

    // False until the admin saves for the first time.
    public bool IsConfigured { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

// Everything is optional: the employer fills this in over time as the numbers
// arrive from each statutory body. What a given FILING requires is enforced by
// that filing, not here — blocking a save because HRD Corp hasn't issued a
// number yet would just stop the admin recording what they do have.
public class SavePayrollCompanyInfoDto
{
    [MaxLength(200)] public string? EmployerName { get; set; }
    [MaxLength(40)] public string? EmployerTin { get; set; }
    [MaxLength(60)] public string? RegistrationNo { get; set; }
    [MaxLength(40)] public string? ReferenceType { get; set; }
    [MaxLength(60)] public string? ReferenceNo { get; set; }
    [MaxLength(40)] public string? EmployerCategory { get; set; }
    [MaxLength(40)] public string? EmployerStatus { get; set; }
    [MaxLength(40)] public string? Cp8dFurnishType { get; set; }

    [MaxLength(40)] public string? PerkesoEmployerCode { get; set; }
    [MaxLength(40)] public string? EpfEmployerNo { get; set; }
    [MaxLength(40)] public string? HrdfEmployerNo { get; set; }
    [MaxLength(40)] public string? ZakatNumber { get; set; }

    [MaxLength(160)] public string? AddressLine1 { get; set; }
    [MaxLength(160)] public string? AddressLine2 { get; set; }
    [MaxLength(20)] public string? Postcode { get; set; }
    [MaxLength(120)] public string? City { get; set; }
    [MaxLength(60)] public string? State { get; set; }
    [MaxLength(60)] public string? Country { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(40)] public string? Handphone { get; set; }
    [MaxLength(160), EmailAddress] public string? Email { get; set; }

    [MaxLength(200)] public string? TaxAgentName { get; set; }
    [MaxLength(40)] public string? TaxAgentTin { get; set; }
    [MaxLength(60)] public string? TaxAgentLicenceNo { get; set; }
    [MaxLength(40)] public string? TaxAgentPhone { get; set; }
    [MaxLength(160), EmailAddress] public string? TaxAgentEmail { get; set; }
    [MaxLength(200)] public string? TaxAgentFirmName { get; set; }
    [MaxLength(160)] public string? TaxAgentFirmAddressLine1 { get; set; }
    [MaxLength(160)] public string? TaxAgentFirmAddressLine2 { get; set; }
    [MaxLength(20)] public string? TaxAgentFirmPostcode { get; set; }
    [MaxLength(120)] public string? TaxAgentFirmCity { get; set; }
    [MaxLength(60)] public string? TaxAgentFirmState { get; set; }

    [MaxLength(200)] public string? DeclarantName { get; set; }
    public IdType? DeclarantIdType { get; set; }
    [MaxLength(40)] public string? DeclarantIdNumber { get; set; }
    [MaxLength(120)] public string? DeclarantPosition { get; set; }
}
