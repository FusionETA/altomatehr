-- Loads EmployeeProfiles. v2 merges two v1 tables: EmployeeProfile (the org link, job
-- title, employee number, policy) and PayrollProfile (personal details, statutory numbers,
-- bank, salary). Requires 01-people.sql.
--
-- Id = the v1 EmployeeProfile id, prefixed 'prod-' when its org was, matching how the
-- settings migration treated every other child row.
--
-- Columns v2 declares NOT NULL are defaulted here rather than left to fail:
--   PaymentMethod -> BANK_TRANSFER, SalaryType -> MONTHLY, flags -> 0, EpfEmployeeRate -> 0
-- EpfEmployeeRate is carried VERBATIM as a percentage (v1 holds 11.00 / 2.00), which is
-- what PayslipCalculator expects -- EpfCalculator clamps upward from 11m, so a 0 here
-- means "statutory rate", not "no contribution".
--
-- Dropped, no v2 column: PayrollProfile.addressLine3 (14 rows carry one).
INSERT INTO altomatehr.EmployeeProfiles (
  Id, OrganizationId, UserId,
  Phone, AlternateEmail, Gender, DateOfBirth, Nationality, Race, HasPr,
  IdType, IdNumber, MaritalStatus, IsResident, IsOku,
  AddressLine1, AddressLine2, City, Postcode, State,
  EmergencyContactName, EmergencyContactPhone, EmergencyContactRelation,
  JoinDate, LeaveDate, Department, Location, WorkSchedule,
  SpouseWorking, SpouseDisabled, SpousePcbNumber, SpouseIdNumber, ChildReliefJson,
  PrevEmploymentYear, PrevRemuneration, PrevEpf, PrevAllowableDeductions, PrevPcb,
  PrevZakat, PrevIncludesPriorThisOrgPeriod,
  ContributeToEpf, EpfNumber, EpfEmployeeRate, EpfEmployeeVoluntary, EpfEmployerVoluntary,
  EpfMemberBefore1998, SocsoNumber, SocsoScheme, ContributeToEis, ContributeToSkbbk,
  IncomeTaxNumber, PcbBorneByEmployer, SsfwNumber, ReportedToLhdn,
  PaymentMethod, BankName, BankAccountHolderName, BankAccountNumber,
  SalaryType, MonthlySalary, HourlyRate, FixedAllowancesJson,
  PayrollPolicy, PayrollCycle, LeaveEntitlementJson, PayrollDocumentsJson,
  IsArchived, ArchivedAt, ArchiveReason, TemporaryReviewDate,
  CreatedAt, UpdatedAt)
SELECT
  IF(m.remapped, CONCAT('prod-', ep.id), ep.id), m.v2_id, p.v2_user_id,
  pp.phone, pp.alternateEmail, pp.gender, pp.dateOfBirth, pp.nationality, pp.race,
  COALESCE(pp.hasPr, 0),
  pp.idType, pp.idNumber, pp.maritalStatus, COALESCE(pp.isResident, 0), COALESCE(pp.isOku, 0),
  pp.addressLine1, pp.addressLine2, pp.city, pp.postcode, pp.state,
  pp.emergencyContactName, pp.emergencyContactPhone, pp.emergencyContactRelation,
  pp.joinDate, pp.leaveDate, pp.department, pp.location, pp.workSchedule,
  pp.spouseWorking, pp.spouseDisabled, pp.spousePcbNumber, pp.spouseIdNumber, pp.childRelief,
  pp.prevEmploymentYear, pp.prevRemuneration, pp.prevEpf, pp.prevAllowableDeductions,
  pp.prevPcb, pp.prevZakat, COALESCE(pp.prevIncludesPriorThisOrgPeriod, 0),
  COALESCE(pp.contributeToEpf, 0), pp.epfNumber, COALESCE(pp.epfEmployeeRate, 0),
  COALESCE(pp.epfEmployeeVoluntary, 0), COALESCE(pp.epfEmployerVoluntary, 0),
  COALESCE(pp.epfMemberBefore1998, 0), pp.socsoNumber, pp.socsoScheme,
  COALESCE(pp.contributeToEis, 0), COALESCE(pp.contributeToSkbbk, 0),
  pp.incomeTaxNumber, COALESCE(pp.pcbBorneByEmployer, 0), pp.ssfwNumber,
  COALESCE(pp.reportedToLhdn, 0),
  COALESCE(pp.paymentMethod, 'BANK_TRANSFER'), pp.bankName, pp.bankAccountHolderName,
  pp.bankAccountNumber,
  COALESCE(pp.salaryType, 'MONTHLY'), pp.monthlySalary, pp.hourlyRate, pp.fixedAllowances,
  pp.payrollPolicy, pp.payrollCycle, pp.leaveEntitlement, pp.payrollDocuments,
  COALESCE(pp.isArchived, 0), pp.archivedAt, pp.archiveReason, pp.temporaryReviewDate,
  ep.createdAt, UTC_TIMESTAMP()
FROM hr_prod.EmployeeOrganization e
JOIN altomatehr._mig_orgmap m    ON m.v1_id = e.organizationId
JOIN altomatehr._mig_peoplemap p ON p.v1_user_id = e.userId
JOIN hr_prod.EmployeeProfile ep  ON ep.id = e.employeeProfileId
LEFT JOIN hr_prod.PayrollProfile pp ON pp.employeeProfileId = ep.id
ON DUPLICATE KEY UPDATE
  Phone = VALUES(Phone), AlternateEmail = VALUES(AlternateEmail), Gender = VALUES(Gender),
  DateOfBirth = VALUES(DateOfBirth), Nationality = VALUES(Nationality), Race = VALUES(Race),
  HasPr = VALUES(HasPr), IdType = VALUES(IdType), IdNumber = VALUES(IdNumber),
  MaritalStatus = VALUES(MaritalStatus), IsResident = VALUES(IsResident), IsOku = VALUES(IsOku),
  AddressLine1 = VALUES(AddressLine1), AddressLine2 = VALUES(AddressLine2), City = VALUES(City),
  Postcode = VALUES(Postcode), State = VALUES(State),
  EmergencyContactName = VALUES(EmergencyContactName),
  EmergencyContactPhone = VALUES(EmergencyContactPhone),
  EmergencyContactRelation = VALUES(EmergencyContactRelation),
  JoinDate = VALUES(JoinDate), LeaveDate = VALUES(LeaveDate), Department = VALUES(Department),
  Location = VALUES(Location), WorkSchedule = VALUES(WorkSchedule),
  SpouseWorking = VALUES(SpouseWorking), SpouseDisabled = VALUES(SpouseDisabled),
  SpousePcbNumber = VALUES(SpousePcbNumber), SpouseIdNumber = VALUES(SpouseIdNumber),
  ChildReliefJson = VALUES(ChildReliefJson), PrevEmploymentYear = VALUES(PrevEmploymentYear),
  PrevRemuneration = VALUES(PrevRemuneration), PrevEpf = VALUES(PrevEpf),
  PrevAllowableDeductions = VALUES(PrevAllowableDeductions), PrevPcb = VALUES(PrevPcb),
  PrevZakat = VALUES(PrevZakat),
  PrevIncludesPriorThisOrgPeriod = VALUES(PrevIncludesPriorThisOrgPeriod),
  ContributeToEpf = VALUES(ContributeToEpf), EpfNumber = VALUES(EpfNumber),
  EpfEmployeeRate = VALUES(EpfEmployeeRate), EpfEmployeeVoluntary = VALUES(EpfEmployeeVoluntary),
  EpfEmployerVoluntary = VALUES(EpfEmployerVoluntary),
  EpfMemberBefore1998 = VALUES(EpfMemberBefore1998), SocsoNumber = VALUES(SocsoNumber),
  SocsoScheme = VALUES(SocsoScheme), ContributeToEis = VALUES(ContributeToEis),
  ContributeToSkbbk = VALUES(ContributeToSkbbk), IncomeTaxNumber = VALUES(IncomeTaxNumber),
  PcbBorneByEmployer = VALUES(PcbBorneByEmployer), SsfwNumber = VALUES(SsfwNumber),
  ReportedToLhdn = VALUES(ReportedToLhdn), PaymentMethod = VALUES(PaymentMethod),
  BankName = VALUES(BankName), BankAccountHolderName = VALUES(BankAccountHolderName),
  BankAccountNumber = VALUES(BankAccountNumber), SalaryType = VALUES(SalaryType),
  MonthlySalary = VALUES(MonthlySalary), HourlyRate = VALUES(HourlyRate),
  FixedAllowancesJson = VALUES(FixedAllowancesJson), PayrollPolicy = VALUES(PayrollPolicy),
  PayrollCycle = VALUES(PayrollCycle), LeaveEntitlementJson = VALUES(LeaveEntitlementJson),
  PayrollDocumentsJson = VALUES(PayrollDocumentsJson), IsArchived = VALUES(IsArchived),
  ArchivedAt = VALUES(ArchivedAt), ArchiveReason = VALUES(ArchiveReason),
  TemporaryReviewDate = VALUES(TemporaryReviewDate), UpdatedAt = UTC_TIMESTAMP();
