-- hr_prod -> altomatehr : GLOBE SUCCESS LEARNING SDN. BHD (cmpcs5z6300006qkzpew0mymt)
--
-- Top-up for ONE org, run 2026-09-24. The 2026-09-18 estate migrations (../prod-settings,
-- ../prod-employees, ../prod-records) already loaded GSL's org row, members, profiles,
-- leave types, projects, teams, team memberships, leave entitlements and payroll settings.
-- This loads what they did not:
--   * ChartOfAccounts      - GSL connected Xero in v1 on 2026-09-24 07:32 UTC, AFTER ../prod-coa ran
--   * Projects (1 row)     - GIFT-MFL lat/lng set in v1 the same minute
--   * ProjectGeofencePoints- the matching v1 ProjectGeoLocation row
--   * SalaryChanges        - never migrated for any org
--   * Payroll runs         - never migrated for any org: runs, payslips, line items, adjustments
-- NO Xero tokens (XeroConnection) - by request, and see ../prod-coa/README.md.
--
-- Org is not remapped (_mig_orgmap.remapped = 0) and every child id is carried verbatim.
-- Every write is a PK upsert -> re-runnable. hr_prod is only read.

SET @org := 'cmpcs5z6300006qkzpew0mymt';

START TRANSACTION;

-- ---------------------------------------------------------------- Chart of accounts
-- Same mapping and liability skip as ../prod-coa/01-coa.sql, scoped to this org.
INSERT INTO altomatehr.ChartOfAccounts
  (Id, OrganizationId, Code, Name, Type, XeroAccountId, XeroStatus, XeroSyncedAt,
   IsSelectable, LimitAmount, AllowMileageClaim, MileageRate, IsArchived, CreatedAt)
SELECT c.id, c.organizationId, c.code, c.name,
       CASE WHEN UPPER(c.type) = 'BANK' THEN 'BANK' ELSE 'EXPENSE' END,
       c.xeroAccountId, c.status,
       CASE WHEN c.xeroAccountId IS NOT NULL THEN c.updatedAt END,
       c.isSelectable, c.limitAmount, c.allowMileageClaim, c.mileageRate, c.isDisabled, c.createdAt
FROM hr_prod.ChartOfAccount c
WHERE c.organizationId = @org
  AND (c.type IS NULL OR UPPER(c.type) IN ('BANK','EXPENSE','DIRECTCOSTS','OVERHEADS') OR UPPER(c.type) LIKE 'EXP%')
ON DUPLICATE KEY UPDATE
  Code = VALUES(Code), Name = VALUES(Name), Type = VALUES(Type), XeroAccountId = VALUES(XeroAccountId),
  XeroStatus = VALUES(XeroStatus), XeroSyncedAt = VALUES(XeroSyncedAt), IsSelectable = VALUES(IsSelectable),
  LimitAmount = VALUES(LimitAmount), AllowMileageClaim = VALUES(AllowMileageClaim),
  MileageRate = VALUES(MileageRate), IsArchived = VALUES(IsArchived);
SELECT 'ChartOfAccounts' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Projects: re-sync geo fields
UPDATE altomatehr.Projects p
JOIN hr_prod.XeroProject x ON x.id = p.Id COLLATE utf8mb4_unicode_ci
SET p.Latitude = x.latitude, p.Longitude = x.longitude, p.Location = x.location
WHERE x.organizationId = @org
  AND NOT (p.Latitude <=> x.latitude AND p.Longitude <=> x.longitude AND p.Location <=> x.location COLLATE utf8mb4_0900_ai_ci);
SELECT 'Projects(geo)' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Geofence points
-- SortOrder = createdAt order within the project (the legacy order, see ProjectGeofencePoint.cs).
INSERT INTO altomatehr.ProjectGeofencePoints
  (Id, OrganizationId, ProjectId, Label, Latitude, Longitude, SortOrder, CreatedAt, UpdatedAt)
SELECT g.id, @org, g.projectId, COALESCE(g.label, ''), g.latitude, g.longitude,
       ROW_NUMBER() OVER (PARTITION BY g.projectId ORDER BY g.createdAt, g.id) - 1,
       g.createdAt, g.updatedAt
FROM hr_prod.ProjectGeoLocation g
JOIN hr_prod.XeroProject x ON x.id = g.projectId
WHERE x.organizationId = @org
ON DUPLICATE KEY UPDATE Label = VALUES(Label), Latitude = VALUES(Latitude),
  Longitude = VALUES(Longitude), SortOrder = VALUES(SortOrder), UpdatedAt = VALUES(UpdatedAt);
SELECT 'ProjectGeofencePoints' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Salary changes
INSERT INTO altomatehr.SalaryChanges
  (Id, OrganizationId, EmployeeProfileId, EffectiveDate, PreviousSalaryType, PreviousMonthlySalary,
   PreviousHourlyRate, NewSalaryType, NewMonthlySalary, NewHourlyRate, Reason, Notes, ChangedByUserId, CreatedAt)
SELECT s.id, @org, s.employeeProfileId, s.effectiveDate,
       IF(s.previousSalaryType = 'MONTHLY_BASED', 'MONTHLY', s.previousSalaryType), s.previousMonthlySalary,
       s.previousHourlyRate,
       IF(s.newSalaryType = 'MONTHLY_BASED', 'MONTHLY', s.newSalaryType), s.newMonthlySalary, s.newHourlyRate,
       s.reason, s.notes, COALESCE(pm.v2_user_id, s.changedByUserId), s.createdAt
FROM hr_prod.SalaryChange s
JOIN hr_prod.EmployeeProfile e ON e.id = s.employeeProfileId
LEFT JOIN altomatehr._mig_peoplemap pm ON pm.v1_user_id = s.changedByUserId
WHERE e.organizationId = @org
ON DUPLICATE KEY UPDATE EffectiveDate = VALUES(EffectiveDate), NewSalaryType = VALUES(NewSalaryType),
  NewMonthlySalary = VALUES(NewMonthlySalary), NewHourlyRate = VALUES(NewHourlyRate),
  Reason = VALUES(Reason), Notes = VALUES(Notes);
SELECT 'SalaryChanges' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Payroll runs
-- v1 has no SKBBK/CP38 run totals -> summed from its payslips. GeneratedAt has no v1 column;
-- lastMutatedAt is the closest (last generation write). No PayrollRunMembers rows: v1 has
-- no exclusions for these runs, and a run with no member rows means "everyone" in v2.
-- Xero journal id/status are carried as history; they are not credentials.
INSERT INTO altomatehr.PayrollRuns
  (Id, OrganizationId, PeriodYear, PeriodMonth, Status, Source, EmployeeCount, TotalGross, TotalNet,
   TotalEmployeeEpf, TotalEmployerEpf, TotalEmployeeSocso, TotalEmployerSocso, TotalEmployeeEis,
   TotalEmployerEis, TotalEmployeeSkbbk, TotalPcb, TotalCp38, TotalZakat, TotalHrdf,
   EmployeesSubjectToHrdf, TotalWagesSubjectToHrdf, TotalCostToEmployer, GeneratedAt, LastMutatedAt,
   CreatedAt, UpdatedAt, ApprovalRejectionReason, SubmittedAt, SubmittedById, SubmittedForApprovalAt,
   SubmittedForApprovalById, XeroJournalNumber, XeroManualJournalId, XeroSyncError, XeroSyncStatus, XeroSyncedAt)
SELECT r.id, @org, r.periodYear, r.periodMonth, r.status, r.source, COALESCE(r.employeeCount, 0),
       COALESCE(r.totalGross, 0), COALESCE(r.totalNet, 0),
       COALESCE(r.totalEmployeeEpf, 0), COALESCE(r.totalEmployerEpf, 0),
       COALESCE(r.totalEmployeeSocso, 0), COALESCE(r.totalEmployerSocso, 0),
       COALESCE(r.totalEmployeeEis, 0), COALESCE(r.totalEmployerEis, 0),
       (SELECT COALESCE(SUM(s.skbbkEmployee), 0) FROM hr_prod.Payslip s WHERE s.payrollRunId = r.id),
       COALESCE(r.totalPcb, 0),
       (SELECT COALESCE(SUM(s.cp38), 0) FROM hr_prod.Payslip s WHERE s.payrollRunId = r.id),
       COALESCE(r.totalZakat, 0), COALESCE(r.totalHrdf, 0),
       COALESCE(r.employeesSubjectToHrdf, 0), COALESCE(r.totalWagesSubjectToHrdf, 0),
       COALESCE(r.totalCostToEmployer, 0), r.lastMutatedAt, r.lastMutatedAt,
       r.createdAt, r.updatedAt, r.approvalRejectionReason, r.submittedAt,
       COALESCE(pm1.v2_user_id, r.submittedById), r.submittedForApprovalAt,
       COALESCE(pm2.v2_user_id, r.submittedForApprovalById),
       r.xeroJournalNumber, r.xeroManualJournalId, r.xeroSyncError, r.xeroSyncStatus, r.xeroSyncedAt
FROM hr_prod.PayrollRun r
LEFT JOIN altomatehr._mig_peoplemap pm1 ON pm1.v1_user_id = r.submittedById
LEFT JOIN altomatehr._mig_peoplemap pm2 ON pm2.v1_user_id = r.submittedForApprovalById
WHERE r.organizationId = @org
ON DUPLICATE KEY UPDATE
  Status = VALUES(Status), Source = VALUES(Source), EmployeeCount = VALUES(EmployeeCount),
  TotalGross = VALUES(TotalGross), TotalNet = VALUES(TotalNet),
  TotalEmployeeEpf = VALUES(TotalEmployeeEpf), TotalEmployerEpf = VALUES(TotalEmployerEpf),
  TotalEmployeeSocso = VALUES(TotalEmployeeSocso), TotalEmployerSocso = VALUES(TotalEmployerSocso),
  TotalEmployeeEis = VALUES(TotalEmployeeEis), TotalEmployerEis = VALUES(TotalEmployerEis),
  TotalEmployeeSkbbk = VALUES(TotalEmployeeSkbbk), TotalPcb = VALUES(TotalPcb), TotalCp38 = VALUES(TotalCp38),
  TotalZakat = VALUES(TotalZakat), TotalHrdf = VALUES(TotalHrdf),
  EmployeesSubjectToHrdf = VALUES(EmployeesSubjectToHrdf), TotalWagesSubjectToHrdf = VALUES(TotalWagesSubjectToHrdf),
  TotalCostToEmployer = VALUES(TotalCostToEmployer), LastMutatedAt = VALUES(LastMutatedAt),
  UpdatedAt = VALUES(UpdatedAt), SubmittedAt = VALUES(SubmittedAt), SubmittedById = VALUES(SubmittedById),
  XeroJournalNumber = VALUES(XeroJournalNumber), XeroManualJournalId = VALUES(XeroManualJournalId),
  XeroSyncStatus = VALUES(XeroSyncStatus), XeroSyncedAt = VALUES(XeroSyncedAt);
SELECT 'PayrollRuns' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Payslips
-- UserId comes from the v2 profile (a reused account can differ from the v1 user id).
-- PcbCalculationJson -> NULL on purpose: v1's breakdown keys (sumLP, B, pcbFinal, lowercase
--   formula) do not match v2's PcbBreakdown, and a half-bound parse prints a WRONG worksheet;
--   NULL renders as "working not available". Same as v2's own YtdImportService.
-- PcbAdditional comes out of that JSON; PcbNormal = pcb - PcbAdditional.
-- ProrationDaysInPeriod has no v1 column: calendar days in the period.
-- Dropped, all zero/NULL for this org: netShortfall, voluntaryPcb, contributeToSkbbk.
INSERT INTO altomatehr.Payslips
  (Id, OrganizationId, PayrollRunId, EmployeeProfileId, UserId, SnapshotName, SnapshotEmployeeNumber,
   SnapshotPosition, SnapshotNationality, SnapshotIsResident, SnapshotSalaryType, SnapshotMonthlySalary,
   SnapshotHourlyRate, SnapshotEpfRatesJson, TotalWorkingDays, ProratedDays, ProrationDaysInPeriod,
   ProratedFactor, WorkedHours, ExpectedHours, UnpaidLeaveDays, BasicPay, ProratedPay, OtNormalHours,
   OtRestHours, OtPublicHours, OtPay, TotalAllowances, TotalReimbursements, TotalDeductions,
   TotalBenefitsInKind, EpfEmployee, EpfEmployer, SocsoEmployee, SocsoEmployer, EisEmployee, EisEmployer,
   SkbbkEmployee, SkbbkWage, Pcb, Cp38, Zakat, Hrdf, HrdfWage, PcbNormal, PcbAdditional,
   PcbCalculationJson, GrossPay, NetPay, TotalCostToEmployer, StatutoryWarnings, CreatedAt, UpdatedAt)
SELECT s.id, @org, s.payrollRunId, s.employeeProfileId, ep.UserId, s.snapshotName, s.snapshotEmployeeId,
       s.snapshotPosition, s.snapshotNationality, s.snapshotIsResident, s.snapshotSalaryType,
       s.snapshotMonthlySalary, s.snapshotHourlyRate, CAST(s.snapshotEpfRates AS CHAR),
       COALESCE(s.totalWorkingDays, 0), COALESCE(s.proratedDays, 0),
       DAY(LAST_DAY(MAKEDATE(r.periodYear, 1) + INTERVAL (r.periodMonth - 1) MONTH)),
       s.proratedFactor, s.workedHours, s.expectedHours, s.unpaidLeaveDays, s.basicPay, s.proratedPay,
       s.otNormalHours, s.otRestHours, s.otPublicHours, s.otPay, s.totalAllowances, s.totalReimbursements,
       s.totalDeductions, s.totalBenefitsInKind, s.epfEmployee, s.epfEmployer, s.socsoEmployee,
       s.socsoEmployer, s.eisEmployee, s.eisEmployer, s.skbbkEmployee, s.skbbkWage, s.pcb, s.cp38,
       s.zakat, s.hrdf, s.hrdfWage,
       s.pcb - COALESCE(JSON_EXTRACT(s.pcbCalculation, '$.pcbAdditional'), 0),
       COALESCE(JSON_EXTRACT(s.pcbCalculation, '$.pcbAdditional'), 0),
       NULL, s.grossPay, s.netPay, s.totalCostToEmployer, NULL, s.createdAt, s.updatedAt
FROM hr_prod.Payslip s
JOIN hr_prod.PayrollRun r ON r.id = s.payrollRunId
JOIN altomatehr.EmployeeProfiles ep ON ep.Id = s.employeeProfileId COLLATE utf8mb4_0900_ai_ci
WHERE r.organizationId = @org
ON DUPLICATE KEY UPDATE
  UserId = VALUES(UserId), SnapshotName = VALUES(SnapshotName), BasicPay = VALUES(BasicPay),
  ProratedPay = VALUES(ProratedPay), TotalAllowances = VALUES(TotalAllowances),
  TotalDeductions = VALUES(TotalDeductions), EpfEmployee = VALUES(EpfEmployee), EpfEmployer = VALUES(EpfEmployer),
  SocsoEmployee = VALUES(SocsoEmployee), SocsoEmployer = VALUES(SocsoEmployer),
  EisEmployee = VALUES(EisEmployee), EisEmployer = VALUES(EisEmployer), Pcb = VALUES(Pcb),
  PcbNormal = VALUES(PcbNormal), PcbAdditional = VALUES(PcbAdditional),
  GrossPay = VALUES(GrossPay), NetPay = VALUES(NetPay), TotalCostToEmployer = VALUES(TotalCostToEmployer),
  UpdatedAt = VALUES(UpdatedAt);
SELECT 'Payslips' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Payslip line items
INSERT INTO altomatehr.PayslipLineItems
  (Id, OrganizationId, PayslipId, Kind, Label, Amount, Category, PcbTaxableAmount, ClaimId,
   SubjectToEpf, SubjectToSocso, SubjectToEis, SubjectToPcb, CreatedAt)
SELECT li.id, @org, li.payslipId, li.kind, li.label, li.amount, li.category, li.pcbTaxableAmount,
       li.claimId, li.subjectToEpf, li.subjectToSocso, li.subjectToEis, li.subjectToPcb, li.createdAt
FROM hr_prod.PayslipLineItem li
JOIN hr_prod.Payslip s ON s.id = li.payslipId
JOIN hr_prod.PayrollRun r ON r.id = s.payrollRunId
WHERE r.organizationId = @org
ON DUPLICATE KEY UPDATE Kind = VALUES(Kind), Label = VALUES(Label), Amount = VALUES(Amount),
  Category = VALUES(Category), PcbTaxableAmount = VALUES(PcbTaxableAmount);
SELECT 'PayslipLineItems' tbl, ROW_COUNT() affected;

-- ---------------------------------------------------------------- Run adjustments
-- v1 manualLineItems is already camelCase {kind,label,amount,category} - the exact shape
-- PayrollRunAdjustments.ParseManualLineItems reads (treatAsRecurring defaults false).
INSERT INTO altomatehr.PayrollRunAdjustments
  (Id, OrganizationId, PayrollRunId, EmployeeProfileId, OtNormalHours, OtRestHours, OtPublicHours,
   ManualLineItemsJson, FixedAllowanceOverridesJson, WorkedHours, ExpectedHours, Notes, CreatedAt, UpdatedAt)
SELECT a.id, @org, a.payrollRunId, a.employeeProfileId, a.otNormalHours, a.otRestHours, a.otPublicHours,
       CAST(a.manualLineItems AS CHAR), CAST(a.fixedAllowanceOverrides AS CHAR),
       a.workedHours, a.expectedHours, a.notes, a.createdAt, a.updatedAt
FROM hr_prod.PayrollRunAdjustment a
JOIN hr_prod.PayrollRun r ON r.id = a.payrollRunId
WHERE r.organizationId = @org
ON DUPLICATE KEY UPDATE OtNormalHours = VALUES(OtNormalHours), OtRestHours = VALUES(OtRestHours),
  OtPublicHours = VALUES(OtPublicHours), ManualLineItemsJson = VALUES(ManualLineItemsJson),
  FixedAllowanceOverridesJson = VALUES(FixedAllowanceOverridesJson), WorkedHours = VALUES(WorkedHours),
  ExpectedHours = VALUES(ExpectedHours), Notes = VALUES(Notes), UpdatedAt = VALUES(UpdatedAt);
SELECT 'PayrollRunAdjustments' tbl, ROW_COUNT() affected;

COMMIT;
