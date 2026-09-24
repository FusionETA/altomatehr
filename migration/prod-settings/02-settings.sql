-- hr_prod -> altomatehr : COMPANY + SETTINGS ONLY (no people, no transactional history).
-- Source is read-only; every write is a PK upsert, so the script is re-runnable.
-- Columns that exist only in v2 (ClaimSettlementRoute, XeroBillStage, LunchBreakMinutes)
-- are set on INSERT but never overwritten on re-run: they are v2-side admin choices.

-- ---------------------------------------------------------------- Organizations
INSERT INTO altomatehr.Organizations
  (Id, Name, DefaultCurrency, DefaultMileageRate, CreatedAt, GeofenceRadiusMeters,
   MileageUnit, Addons, Plan, Tier, WorkingHoursEnd, WorkingHoursStart, WorkingDays,
   ClaimRunCutoffDay, ClaimSettlementRoute, XeroBillStage, SupervisorSlaMinutes, LunchBreakMinutes)
SELECT m.v2_id,
       o.name,
       COALESCE(o.defaultCurrency, 'MYR'),
       COALESCE(o.defaultMileageRate, 0),
       o.createdAt,
       COALESCE(o.geofenceRadiusMeters, 0),
       COALESCE(o.mileageUnit, 'KM'),
       -- v1 stores a JSON array, v2 a comma-joined string (OrgModules.Join)
       CASE WHEN o.addons IS NULL OR CAST(o.addons AS CHAR) = 'null' THEN ''
            ELSE REPLACE(REPLACE(REPLACE(REPLACE(CAST(o.addons AS CHAR), '"', ''), '[', ''), ']', ''), ', ', ',')
       END,
       COALESCE(o.plan, 'DIY'),
       o.tier,
       COALESCE(o.workingHoursEnd, '18:00'),
       COALESCE(o.workingHoursStart, '09:00'),
       o.workingDays,
       COALESCE(o.claimCutoffDay, 25),
       'XERO_BILL', 'AwaitingPayment',
       COALESCE(o.supervisorSlaMinutes, 60),
       0
FROM hr_prod.Organization o
JOIN altomatehr._mig_orgmap m ON m.v1_id = o.id
ON DUPLICATE KEY UPDATE
  Name = VALUES(Name), DefaultCurrency = VALUES(DefaultCurrency),
  DefaultMileageRate = VALUES(DefaultMileageRate), GeofenceRadiusMeters = VALUES(GeofenceRadiusMeters),
  MileageUnit = VALUES(MileageUnit), Addons = VALUES(Addons), Plan = VALUES(Plan), Tier = VALUES(Tier),
  WorkingHoursEnd = VALUES(WorkingHoursEnd), WorkingHoursStart = VALUES(WorkingHoursStart),
  WorkingDays = VALUES(WorkingDays), ClaimRunCutoffDay = VALUES(ClaimRunCutoffDay),
  SupervisorSlaMinutes = VALUES(SupervisorSlaMinutes);

-- ---------------------------------------------------------------- LeaveTypes
INSERT INTO altomatehr.LeaveTypes
  (Id, OrganizationId, Code, Name, Paid, DefaultDays, IsArchived, CreatedAt, UpdatedAt,
   AccrualMethod, CarryExpiryMonth, CarryForward, MaxCarryForwardDays, ProrateFirstYear)
SELECT IF(m.remapped, CONCAT('prod-', t.id), t.id), m.v2_id,
       t.code, t.name, t.paid, t.defaultDays, t.archivedAt IS NOT NULL,
       t.createdAt, t.updatedAt, t.accrualMethod, t.carryExpiryMonth,
       t.carryForward, t.maxCarryForwardDays, t.prorateFirstYear
FROM hr_prod.LeaveType t
JOIN altomatehr._mig_orgmap m ON m.v1_id = t.organizationId
ON DUPLICATE KEY UPDATE
  Code = VALUES(Code), Name = VALUES(Name), Paid = VALUES(Paid), DefaultDays = VALUES(DefaultDays),
  IsArchived = VALUES(IsArchived), UpdatedAt = VALUES(UpdatedAt), AccrualMethod = VALUES(AccrualMethod),
  CarryExpiryMonth = VALUES(CarryExpiryMonth), CarryForward = VALUES(CarryForward),
  MaxCarryForwardDays = VALUES(MaxCarryForwardDays), ProrateFirstYear = VALUES(ProrateFirstYear);

-- ---------------------------------------------------------------- Projects (v1 XeroProject)
-- Dropped by design: isManual, projectManagerId, allowedIpsList (v2 has no column).
INSERT INTO altomatehr.Projects
  (Id, OrganizationId, Name, IsArchived, CreatedAt, Latitude, Longitude, XeroProjectId,
   XeroSyncedAt, XeroStatus, AllowedIps, Location, LunchBreakMinutes, WorkingDays,
   WorkingHoursEnd, WorkingHoursStart, ArchivedByXeroConnect, XeroTrackingCategoryId, XeroTrackingOptionId)
SELECT IF(m.remapped, CONCAT('prod-', x.id), x.id), m.v2_id,
       x.name, COALESCE(x.isDisabled, 0), x.createdAt, x.latitude, x.longitude, x.xeroProjectId,
       NULL, x.status, x.allowedIps, x.location, COALESCE(x.lunchBreakMinutes, 0), x.workingDays,
       x.workingHoursEnd, x.workingHoursStart, COALESCE(x.archivedByXeroConnect, 0),
       x.xeroTrackingCategoryId, x.xeroTrackingOptionId
FROM hr_prod.XeroProject x
JOIN altomatehr._mig_orgmap m ON m.v1_id = x.organizationId
ON DUPLICATE KEY UPDATE
  Name = VALUES(Name), IsArchived = VALUES(IsArchived), Latitude = VALUES(Latitude),
  Longitude = VALUES(Longitude), XeroProjectId = VALUES(XeroProjectId), XeroStatus = VALUES(XeroStatus),
  AllowedIps = VALUES(AllowedIps), Location = VALUES(Location), LunchBreakMinutes = VALUES(LunchBreakMinutes),
  WorkingDays = VALUES(WorkingDays), WorkingHoursEnd = VALUES(WorkingHoursEnd),
  WorkingHoursStart = VALUES(WorkingHoursStart), ArchivedByXeroConnect = VALUES(ArchivedByXeroConnect),
  XeroTrackingCategoryId = VALUES(XeroTrackingCategoryId), XeroTrackingOptionId = VALUES(XeroTrackingOptionId);

-- ---------------------------------------------------------------- Teams
-- v1 Team is scoped by projectId only; v2 carries OrganizationId too, derived here.
-- Dropped by design: requireClockIn/ClockOut/BreakStart/BreakEndApproval (no v2 column).
INSERT INTO altomatehr.Teams
  (Id, OrganizationId, ProjectId, Name, LayerCount, LayerLabels, CreatedAt, UpdatedAt, ModuleApprovalConfig)
SELECT IF(m.remapped, CONCAT('prod-', t.id), t.id), m.v2_id,
       IF(m.remapped, CONCAT('prod-', t.projectId), t.projectId),
       t.name, t.layerCount, COALESCE(CAST(t.layerLabels AS CHAR), '[]'),
       t.createdAt, t.updatedAt, COALESCE(CAST(t.moduleConfig AS CHAR), '{}')
FROM hr_prod.Team t
JOIN hr_prod.XeroProject x ON x.id = t.projectId
JOIN altomatehr._mig_orgmap m ON m.v1_id = x.organizationId
ON DUPLICATE KEY UPDATE
  Name = VALUES(Name), LayerCount = VALUES(LayerCount), LayerLabels = VALUES(LayerLabels),
  UpdatedAt = VALUES(UpdatedAt), ModuleApprovalConfig = VALUES(ModuleApprovalConfig);

-- ---------------------------------------------------------------- EmployeePolicies
INSERT INTO altomatehr.EmployeePolicies
  (Id, OrganizationId, Name, Description, IsDefault, IsArchived, CanAccessAttendance, CanAccessClaims,
   CanAccessLeave, RequireGeofence, RequireSelfie, RequireClockOutSelfie, SalaryType, OtEnabled,
   OtDailyThresholdMinutes, OtMethod, Temporary, CreatedAt, UpdatedAt, CaptureLocationOnBreakEnd,
   CaptureLocationOnBreakStart, CaptureLocationOnClockIn, CaptureLocationOnClockOut, GeolocationEnabled,
   RequireIpWhitelist, OtRateNormalDay, OtRatePublicHoliday, OtRatePublicHolidayInShift, OtRateRestDay,
   OtRateRestDayInShift, OtSalaryThreshold, AutoClockOutAfterMinutes, AutoClockOutEnabled)
SELECT IF(m.remapped, CONCAT('prod-', p.id), p.id), m.v2_id,
       p.name, p.description, p.isDefault, p.archivedAt IS NOT NULL,
       p.canAccessAttendance, p.canAccessClaims, p.canAccessLeave, p.requireGeofence, p.requireSelfie,
       p.requireClockOutSelfie,
       -- v1 enum is ('HOURLY','MONTHLY_BASED'); v2's C# SalaryType enum is (HOURLY, MONTHLY).
       -- EF throws "Cannot convert string value 'MONTHLY_BASED'" on read if this is carried verbatim.
       IF(p.salaryType = 'MONTHLY_BASED', 'MONTHLY', p.salaryType), p.otEnabled, p.otDailyThresholdMinutes, p.otMethod,
       p.temporary, p.createdAt, p.updatedAt, p.captureLocationOnBreakEnd, p.captureLocationOnBreakStart,
       p.captureLocationOnClockIn, p.captureLocationOnClockOut, p.geolocationEnabled, p.requireIpWhitelist,
       p.otRateNormalDay, p.otRatePublicHoliday, p.otRatePublicHolidayInShift, p.otRateRestDay,
       p.otRateRestDayInShift, p.otSalaryThreshold, p.autoClockOutAfterMin, p.autoClockOutEnabled
FROM hr_prod.EmployeePolicy p
JOIN altomatehr._mig_orgmap m ON m.v1_id = p.organizationId
ON DUPLICATE KEY UPDATE
  Name = VALUES(Name), Description = VALUES(Description), IsDefault = VALUES(IsDefault),
  IsArchived = VALUES(IsArchived), CanAccessAttendance = VALUES(CanAccessAttendance),
  CanAccessClaims = VALUES(CanAccessClaims), CanAccessLeave = VALUES(CanAccessLeave),
  RequireGeofence = VALUES(RequireGeofence), RequireSelfie = VALUES(RequireSelfie),
  RequireClockOutSelfie = VALUES(RequireClockOutSelfie), SalaryType = VALUES(SalaryType),
  OtEnabled = VALUES(OtEnabled), OtDailyThresholdMinutes = VALUES(OtDailyThresholdMinutes),
  OtMethod = VALUES(OtMethod), Temporary = VALUES(Temporary), UpdatedAt = VALUES(UpdatedAt),
  CaptureLocationOnBreakEnd = VALUES(CaptureLocationOnBreakEnd),
  CaptureLocationOnBreakStart = VALUES(CaptureLocationOnBreakStart),
  CaptureLocationOnClockIn = VALUES(CaptureLocationOnClockIn),
  CaptureLocationOnClockOut = VALUES(CaptureLocationOnClockOut),
  GeolocationEnabled = VALUES(GeolocationEnabled), RequireIpWhitelist = VALUES(RequireIpWhitelist),
  OtRateNormalDay = VALUES(OtRateNormalDay), OtRatePublicHoliday = VALUES(OtRatePublicHoliday),
  OtRatePublicHolidayInShift = VALUES(OtRatePublicHolidayInShift), OtRateRestDay = VALUES(OtRateRestDay),
  OtRateRestDayInShift = VALUES(OtRateRestDayInShift), OtSalaryThreshold = VALUES(OtSalaryThreshold),
  AutoClockOutAfterMinutes = VALUES(AutoClockOutAfterMinutes), AutoClockOutEnabled = VALUES(AutoClockOutEnabled);

-- ---------------------------------------------------------------- PolicyLeaveEntitlements
INSERT INTO altomatehr.PolicyLeaveEntitlements
  (Id, OrganizationId, PolicyId, LeaveTypeId, DefaultDays, CreatedAt, UpdatedAt, AccrualMethod)
SELECT IF(m.remapped, CONCAT('prod-', e.id), e.id), m.v2_id,
       IF(m.remapped, CONCAT('prod-', e.policyId), e.policyId),
       IF(m.remapped, CONCAT('prod-', e.leaveTypeId), e.leaveTypeId),
       e.defaultDays, e.createdAt, e.updatedAt, e.accrualMethod
FROM hr_prod.PolicyLeaveEntitlement e
JOIN hr_prod.EmployeePolicy p ON p.id = e.policyId
JOIN altomatehr._mig_orgmap m ON m.v1_id = p.organizationId
ON DUPLICATE KEY UPDATE
  DefaultDays = VALUES(DefaultDays), UpdatedAt = VALUES(UpdatedAt), AccrualMethod = VALUES(AccrualMethod);

-- ---------------------------------------------------------------- PayrollSettings
INSERT INTO altomatehr.PayrollSettings
  (Id, OrganizationId, WorkingDaysRule, DefaultEpfEmployeeRate, DefaultEpfEmployerRate, HrdfEnabled,
   HrdfRate, AutoApplySocsoEisRelief, SyncClaimsToXeroOnSubmit, SyncPayrollToXeroOnSubmit,
   XeroMappingJson, PayrollBankName, PayorAccountHolderName, PayorOrganisationCode,
   EcpPayorAccountNo, EcpPayorBic, CreatedAt, UpdatedAt)
SELECT IF(m.remapped, CONCAT('prod-', s.id), s.id), m.v2_id,
       s.workingDaysRule, s.defaultEpfEmployeeRate, s.defaultEpfEmployerRate, s.hrdfEnabled,
       s.hrdfRate, s.autoApplySocsoEisRelief, s.syncClaimsToXeroOnSubmit, s.syncPayrollToXeroOnSubmit,
       CAST(s.xeroMapping AS CHAR), s.payrollBankName, s.payorAccountHolderName, s.payorOrganisationCode,
       s.ecpPayorAccountNo, s.ecpPayorBic, s.createdAt, s.updatedAt
FROM hr_prod.PayrollSettings s
JOIN altomatehr._mig_orgmap m ON m.v1_id = s.organizationId
ON DUPLICATE KEY UPDATE
  WorkingDaysRule = VALUES(WorkingDaysRule), DefaultEpfEmployeeRate = VALUES(DefaultEpfEmployeeRate),
  DefaultEpfEmployerRate = VALUES(DefaultEpfEmployerRate), HrdfEnabled = VALUES(HrdfEnabled),
  HrdfRate = VALUES(HrdfRate), AutoApplySocsoEisRelief = VALUES(AutoApplySocsoEisRelief),
  SyncClaimsToXeroOnSubmit = VALUES(SyncClaimsToXeroOnSubmit),
  SyncPayrollToXeroOnSubmit = VALUES(SyncPayrollToXeroOnSubmit), XeroMappingJson = VALUES(XeroMappingJson),
  PayrollBankName = VALUES(PayrollBankName), PayorAccountHolderName = VALUES(PayorAccountHolderName),
  PayorOrganisationCode = VALUES(PayorOrganisationCode), EcpPayorAccountNo = VALUES(EcpPayorAccountNo),
  EcpPayorBic = VALUES(EcpPayorBic), UpdatedAt = VALUES(UpdatedAt);

-- ---------------------------------------------------------------- PayrollCompanyInfos
INSERT INTO altomatehr.PayrollCompanyInfos
  (Id, OrganizationId, EmployerName, EmployerTin, RegistrationNo, ReferenceType, ReferenceNo,
   EmployerCategory, EmployerStatus, Cp8dFurnishType, PerkesoEmployerCode, EpfEmployerNo, HrdfEmployerNo,
   ZakatNumber, AddressLine1, AddressLine2, Postcode, City, State, Country, Phone, Handphone, Email,
   TaxAgentName, TaxAgentTin, TaxAgentLicenceNo, TaxAgentPhone, TaxAgentEmail, TaxAgentFirmName,
   TaxAgentFirmAddressLine1, TaxAgentFirmAddressLine2, TaxAgentFirmPostcode, TaxAgentFirmCity,
   TaxAgentFirmState, DeclarantName, DeclarantIdType, DeclarantIdNumber, DeclarantPosition,
   CreatedAt, UpdatedAt)
SELECT IF(m.remapped, CONCAT('prod-', c.id), c.id), m.v2_id,
       c.employerName, c.employerTin, c.registrationNo, c.referenceType, c.referenceNo,
       c.employerCategory, c.employerStatus, c.cp8dFurnishType, c.perkesoEmployerCode, c.epfEmployerNo,
       c.hrdfEmployerNo, c.zakatNumber, c.addressLine1, c.addressLine2, c.postcode, c.city, c.state,
       c.country, c.phone, c.handphone, c.email, c.taxAgentName, c.taxAgentTin, c.taxAgentLicenceNo,
       c.taxAgentPhone, c.taxAgentEmail, c.taxAgentFirmName, c.taxAgentFirmAddressLine1,
       c.taxAgentFirmAddressLine2, c.taxAgentFirmPostcode, c.taxAgentFirmCity, c.taxAgentFirmState,
       c.declarantName, c.declarantIdType, c.declarantIdNumber, c.declarantPosition,
       c.createdAt, c.updatedAt
FROM hr_prod.PayrollCompanyInfo c
JOIN altomatehr._mig_orgmap m ON m.v1_id = c.organizationId
ON DUPLICATE KEY UPDATE
  EmployerName = VALUES(EmployerName), EmployerTin = VALUES(EmployerTin),
  RegistrationNo = VALUES(RegistrationNo), ReferenceType = VALUES(ReferenceType),
  ReferenceNo = VALUES(ReferenceNo), EmployerCategory = VALUES(EmployerCategory),
  EmployerStatus = VALUES(EmployerStatus), Cp8dFurnishType = VALUES(Cp8dFurnishType),
  PerkesoEmployerCode = VALUES(PerkesoEmployerCode), EpfEmployerNo = VALUES(EpfEmployerNo),
  HrdfEmployerNo = VALUES(HrdfEmployerNo), ZakatNumber = VALUES(ZakatNumber),
  AddressLine1 = VALUES(AddressLine1), AddressLine2 = VALUES(AddressLine2), Postcode = VALUES(Postcode),
  City = VALUES(City), State = VALUES(State), Country = VALUES(Country), Phone = VALUES(Phone),
  Handphone = VALUES(Handphone), Email = VALUES(Email), TaxAgentName = VALUES(TaxAgentName),
  TaxAgentTin = VALUES(TaxAgentTin), TaxAgentLicenceNo = VALUES(TaxAgentLicenceNo),
  TaxAgentPhone = VALUES(TaxAgentPhone), TaxAgentEmail = VALUES(TaxAgentEmail),
  TaxAgentFirmName = VALUES(TaxAgentFirmName), TaxAgentFirmAddressLine1 = VALUES(TaxAgentFirmAddressLine1),
  TaxAgentFirmAddressLine2 = VALUES(TaxAgentFirmAddressLine2),
  TaxAgentFirmPostcode = VALUES(TaxAgentFirmPostcode), TaxAgentFirmCity = VALUES(TaxAgentFirmCity),
  TaxAgentFirmState = VALUES(TaxAgentFirmState), DeclarantName = VALUES(DeclarantName),
  DeclarantIdType = VALUES(DeclarantIdType), DeclarantIdNumber = VALUES(DeclarantIdNumber),
  DeclarantPosition = VALUES(DeclarantPosition), UpdatedAt = VALUES(UpdatedAt);
