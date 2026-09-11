using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollCompanyInfoService : IPayrollCompanyInfoService
{
    private readonly IPayrollCompanyInfoRepository _repo;
    private readonly IAuditService _audit;

    public PayrollCompanyInfoService(IPayrollCompanyInfoRepository repo, IAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

    // An unconfigured org gets an empty profile rather than a 404, so the form
    // renders the same way whether or not anything has been filled in yet.
    public async Task<PayrollCompanyInfoDto> GetAsync()
    {
        var info = await _repo.GetAsync();

        return info is null
            ? ToDto(new PayrollCompanyInfo(), isConfigured: false)
            : ToDto(info);
    }

    // Null when nothing has been saved. The annual filings need to distinguish
    // "no employer profile" from "an empty one" — CP8D cannot be produced
    // without a TIN, and silently emitting blanks would be worse than failing.
    public Task<PayrollCompanyInfo?> GetEntityAsync() => _repo.GetAsync();

    public async Task<PayrollCompanyInfoDto> SaveAsync(SavePayrollCompanyInfoDto dto)
    {
        var info = await _repo.GetAsync();
        var isFirstSave = info is null;
        var now = DateTime.UtcNow;

        info ??= new PayrollCompanyInfo { CreatedAt = now };

        Apply(info, dto);
        info.UpdatedAt = now;

        if (isFirstSave)
        {
            await _repo.AddAsync(info);
        }
        else
        {
            await _repo.UpdateAsync(info);
        }

        // The values are filing identifiers, not secrets, but they decide what
        // lands on a statutory submission — so the audit records WHICH
        // registrations changed hands, not the whole profile.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollCompanyInfoUpdate,
            isFirstSave ? "Configured payroll company profile" : "Updated payroll company profile",
            TargetType: "PayrollCompanyInfo",
            TargetId: info.Id,
            Metadata: new
            {
                info.EmployerName,
                info.EmployerTin,
                info.PerkesoEmployerCode,
                info.EpfEmployerNo,
                info.HrdfEmployerNo,
            }));

        return ToDto(info);
    }

    private static void Apply(PayrollCompanyInfo info, SavePayrollCompanyInfoDto dto)
    {
        info.EmployerName = dto.EmployerName;
        info.EmployerTin = dto.EmployerTin;
        info.RegistrationNo = dto.RegistrationNo;
        info.ReferenceType = dto.ReferenceType;
        info.ReferenceNo = dto.ReferenceNo;
        info.EmployerCategory = dto.EmployerCategory;
        info.EmployerStatus = dto.EmployerStatus;
        info.Cp8dFurnishType = dto.Cp8dFurnishType;

        info.PerkesoEmployerCode = dto.PerkesoEmployerCode;
        info.EpfEmployerNo = dto.EpfEmployerNo;
        info.HrdfEmployerNo = dto.HrdfEmployerNo;
        info.ZakatNumber = dto.ZakatNumber;

        info.AddressLine1 = dto.AddressLine1;
        info.AddressLine2 = dto.AddressLine2;
        info.Postcode = dto.Postcode;
        info.City = dto.City;
        info.State = dto.State;
        // Malaysia-only product, so a blank country means Malaysia rather than
        // "unknown" — the filings all assume it.
        info.Country = string.IsNullOrWhiteSpace(dto.Country) ? "Malaysia" : dto.Country;
        info.Phone = dto.Phone;
        info.Handphone = dto.Handphone;
        info.Email = dto.Email;

        info.TaxAgentName = dto.TaxAgentName;
        info.TaxAgentTin = dto.TaxAgentTin;
        info.TaxAgentLicenceNo = dto.TaxAgentLicenceNo;
        info.TaxAgentPhone = dto.TaxAgentPhone;
        info.TaxAgentEmail = dto.TaxAgentEmail;
        info.TaxAgentFirmName = dto.TaxAgentFirmName;
        info.TaxAgentFirmAddressLine1 = dto.TaxAgentFirmAddressLine1;
        info.TaxAgentFirmAddressLine2 = dto.TaxAgentFirmAddressLine2;
        info.TaxAgentFirmPostcode = dto.TaxAgentFirmPostcode;
        info.TaxAgentFirmCity = dto.TaxAgentFirmCity;
        info.TaxAgentFirmState = dto.TaxAgentFirmState;

        info.DeclarantName = dto.DeclarantName;
        info.DeclarantIdType = dto.DeclarantIdType;
        info.DeclarantIdNumber = dto.DeclarantIdNumber;
        info.DeclarantPosition = dto.DeclarantPosition;
    }

    private static PayrollCompanyInfoDto ToDto(PayrollCompanyInfo i, bool isConfigured = true) => new()
    {
        EmployerName = i.EmployerName,
        EmployerTin = i.EmployerTin,
        RegistrationNo = i.RegistrationNo,
        ReferenceType = i.ReferenceType,
        ReferenceNo = i.ReferenceNo,
        EmployerCategory = i.EmployerCategory,
        EmployerStatus = i.EmployerStatus,
        Cp8dFurnishType = i.Cp8dFurnishType,
        PerkesoEmployerCode = i.PerkesoEmployerCode,
        EpfEmployerNo = i.EpfEmployerNo,
        HrdfEmployerNo = i.HrdfEmployerNo,
        ZakatNumber = i.ZakatNumber,
        AddressLine1 = i.AddressLine1,
        AddressLine2 = i.AddressLine2,
        Postcode = i.Postcode,
        City = i.City,
        State = i.State,
        Country = i.Country,
        Phone = i.Phone,
        Handphone = i.Handphone,
        Email = i.Email,
        TaxAgentName = i.TaxAgentName,
        TaxAgentTin = i.TaxAgentTin,
        TaxAgentLicenceNo = i.TaxAgentLicenceNo,
        TaxAgentPhone = i.TaxAgentPhone,
        TaxAgentEmail = i.TaxAgentEmail,
        TaxAgentFirmName = i.TaxAgentFirmName,
        TaxAgentFirmAddressLine1 = i.TaxAgentFirmAddressLine1,
        TaxAgentFirmAddressLine2 = i.TaxAgentFirmAddressLine2,
        TaxAgentFirmPostcode = i.TaxAgentFirmPostcode,
        TaxAgentFirmCity = i.TaxAgentFirmCity,
        TaxAgentFirmState = i.TaxAgentFirmState,
        DeclarantName = i.DeclarantName,
        DeclarantIdType = i.DeclarantIdType,
        DeclarantIdNumber = i.DeclarantIdNumber,
        DeclarantPosition = i.DeclarantPosition,
        IsConfigured = isConfigured,
        UpdatedAt = isConfigured ? i.UpdatedAt : null,
    };
}
