USE altomatehr;
-- hr_prod -> altomatehr : payroll history for EVERY mapped org
-- (runs, members, payslips, line items, adjustments, salary changes, loans).
--
-- Generalises ../prod-gsl/01-gsl.sql (one org) to all 42 orgs in _mig_orgmap; the
-- column mapping is identical -- see that file for the per-column reasoning
-- (PcbCalculationJson NULL, PcbNormal/PcbAdditional split, ProrationDaysInPeriod).
--
-- CONFLICT POLICY: insert-if-free, never update. Payroll is being actively run in
-- v2 by some orgs (Peak Bridges re-entered Apr-Aug 2026 itself on 09-23; GSL,
-- Fusioneta, Oscar run natively), and a run's (Org, Year, Month) is UNIQUE, so a
-- v1 run whose period v2 already holds is SKIPPED along with all its children.
-- 05-verify.sql lists every skip.
--
-- IDS: verbatim, except the one remapped org (Fusion ETA, remapped=1) whose child
-- ids get the same deterministic 'prod-' prefix ../prod-employees gave its
-- EmployeeProfiles -- its hr_dev twin (ZR TEST) shares the unprefixed ids.
-- Profiles are resolved against v2 by id OR 'prod-'+id, scoped to the run's org.
--
-- Dropped, no v2 column (counts as of 2026-09-25): Payslip.netShortfall (6 non-zero),
-- Payslip.voluntaryPcb (3 non-zero), PayrollRunClaim (0 rows).

SELECT IF((SELECT COUNT(*) FROM altomatehr._mig_orgmap) = 0,
          'ABORT: run ../prod-settings first', 'deps ok') AS precheck;

-- v1 profile id -> v2 profile id, per org. Materialised once; every step joins it.
DROP TEMPORARY TABLE IF EXISTS _profmap;
CREATE TEMPORARY TABLE _profmap (
  v1_id VARCHAR(64) COLLATE utf8mb4_unicode_ci PRIMARY KEY,
  v2_id VARCHAR(64) COLLATE utf8mb4_unicode_ci NOT NULL,
  v2_org VARCHAR(64) COLLATE utf8mb4_unicode_ci NOT NULL,
  v2_user VARCHAR(64) COLLATE utf8mb4_unicode_ci NOT NULL
);
INSERT INTO _profmap
SELECT p.id, ep.Id, ep.OrganizationId, ep.UserId
FROM hr_prod.EmployeeProfile p
JOIN altomatehr._mig_orgmap om ON om.v1_id = p.organizationId
JOIN altomatehr.EmployeeProfiles ep
  ON ep.Id = CONCAT(IF(om.remapped, 'prod-', ''), p.id) COLLATE utf8mb4_0900_ai_ci
 AND ep.OrganizationId = om.v2_id COLLATE utf8mb4_0900_ai_ci;
SELECT '_profmap' tbl, COUNT(*) mapped_profiles FROM _profmap;

START TRANSACTION;

-- ---------------------------------------------------------------- Runs
INSERT INTO altomatehr.PayrollRuns
  (Id, OrganizationId, PeriodYear, PeriodMonth, Status, Source, EmployeeCount, TotalGross, TotalNet,
   TotalEmployeeEpf, TotalEmployerEpf, TotalEmployeeSocso, TotalEmployerSocso, TotalEmployeeEis,
   TotalEmployerEis, TotalEmployeeSkbbk, TotalPcb, TotalCp38, TotalZakat, TotalHrdf,
   EmployeesSubjectToHrdf, TotalWagesSubjectToHrdf, TotalCostToEmployer, GeneratedAt, LastMutatedAt,
   CreatedAt, UpdatedAt, ApprovalRejectionReason, SubmittedAt, SubmittedById, SubmittedForApprovalAt,
   SubmittedForApprovalById, XeroJournalNumber, XeroManualJournalId, XeroSyncError, XeroSyncStatus, XeroSyncedAt)
SELECT CONCAT(IF(om.remapped, 'prod-', ''), r.id), om.v2_id, r.periodYear, r.periodMonth, r.status, r.source,
       COALESCE(r.employeeCount, 0), COALESCE(r.totalGross, 0), COALESCE(r.totalNet, 0),
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
JOIN altomatehr._mig_orgmap om ON om.v1_id = r.organizationId
LEFT JOIN altomatehr._mig_peoplemap pm1 ON pm1.v1_user_id = r.submittedById
LEFT JOIN altomatehr._mig_peoplemap pm2 ON pm2.v1_user_id = r.submittedForApprovalById
WHERE NOT EXISTS (SELECT 1 FROM altomatehr.PayrollRuns x
                   WHERE x.Id = CONCAT(IF(om.remapped, 'prod-', ''), r.id) COLLATE utf8mb4_0900_ai_ci)
  AND NOT EXISTS (SELECT 1 FROM altomatehr.PayrollRuns x
                   WHERE x.OrganizationId = om.v2_id COLLATE utf8mb4_0900_ai_ci
                     AND x.PeriodYear = r.periodYear AND x.PeriodMonth = r.periodMonth)
  AND (r.xeroManualJournalId IS NULL OR NOT EXISTS (SELECT 1 FROM altomatehr.PayrollRuns x
                   WHERE x.XeroManualJournalId = r.xeroManualJournalId COLLATE utf8mb4_0900_ai_ci));
SELECT 'PayrollRuns' tbl, ROW_COUNT() inserted;

-- The v1 runs that now have a v2 counterpart WE own (id match). Children only
-- ever hang off these -- never off a v2-native run that happens to share a period.
DROP TEMPORARY TABLE IF EXISTS _runs;
CREATE TEMPORARY TABLE _runs (
  v1_id VARCHAR(64) COLLATE utf8mb4_unicode_ci PRIMARY KEY,
  v2_id VARCHAR(64) COLLATE utf8mb4_unicode_ci NOT NULL,
  v2_org VARCHAR(64) COLLATE utf8mb4_unicode_ci NOT NULL,
  pfx VARCHAR(8) COLLATE utf8mb4_unicode_ci NOT NULL,
  periodYear INT, periodMonth INT, excluded JSON
);
INSERT INTO _runs
SELECT r.id, v.Id, v.OrganizationId, IF(om.remapped, 'prod-', ''), r.periodYear, r.periodMonth,
       r.excludedEmployeeProfileIds
FROM hr_prod.PayrollRun r
JOIN altomatehr._mig_orgmap om ON om.v1_id = r.organizationId
JOIN altomatehr.PayrollRuns v
  ON v.Id = CONCAT(IF(om.remapped, 'prod-', ''), r.id) COLLATE utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------- Payslips
INSERT INTO altomatehr.Payslips
  (Id, OrganizationId, PayrollRunId, EmployeeProfileId, UserId, SnapshotName, SnapshotEmployeeNumber,
   SnapshotPosition, SnapshotNationality, SnapshotIsResident, SnapshotSalaryType, SnapshotMonthlySalary,
   SnapshotHourlyRate, SnapshotEpfRatesJson, TotalWorkingDays, ProratedDays, ProrationDaysInPeriod,
   ProratedFactor, WorkedHours, ExpectedHours, UnpaidLeaveDays, BasicPay, ProratedPay, OtNormalHours,
   OtRestHours, OtPublicHours, OtPay, TotalAllowances, TotalReimbursements, TotalDeductions,
   TotalBenefitsInKind, EpfEmployee, EpfEmployer, SocsoEmployee, SocsoEmployer, EisEmployee, EisEmployer,
   SkbbkEmployee, SkbbkWage, Pcb, Cp38, Zakat, Hrdf, HrdfWage, PcbNormal, PcbAdditional,
   PcbCalculationJson, GrossPay, NetPay, TotalCostToEmployer, StatutoryWarnings, CreatedAt, UpdatedAt)
SELECT CONCAT(ru.pfx, s.id), ru.v2_org, ru.v2_id, pf.v2_id, pf.v2_user, s.snapshotName, s.snapshotEmployeeId,
       s.snapshotPosition, s.snapshotNationality, s.snapshotIsResident,
       IF(s.snapshotSalaryType = 'MONTHLY_BASED', 'MONTHLY', s.snapshotSalaryType),
       s.snapshotMonthlySalary, s.snapshotHourlyRate, CAST(s.snapshotEpfRates AS CHAR),
       COALESCE(s.totalWorkingDays, 0), COALESCE(s.proratedDays, 0),
       DAY(LAST_DAY(MAKEDATE(ru.periodYear, 1) + INTERVAL (ru.periodMonth - 1) MONTH)),
       s.proratedFactor, s.workedHours, s.expectedHours, s.unpaidLeaveDays, s.basicPay, s.proratedPay,
       s.otNormalHours, s.otRestHours, s.otPublicHours, s.otPay, s.totalAllowances, s.totalReimbursements,
       s.totalDeductions, s.totalBenefitsInKind, s.epfEmployee, s.epfEmployer, s.socsoEmployee,
       s.socsoEmployer, s.eisEmployee, s.eisEmployer, s.skbbkEmployee, s.skbbkWage, s.pcb, s.cp38,
       s.zakat, s.hrdf, s.hrdfWage,
       s.pcb - COALESCE(JSON_EXTRACT(s.pcbCalculation, '$.pcbAdditional'), 0),
       COALESCE(JSON_EXTRACT(s.pcbCalculation, '$.pcbAdditional'), 0),
       NULL, s.grossPay, s.netPay, s.totalCostToEmployer, NULL, s.createdAt, s.updatedAt
FROM hr_prod.Payslip s
JOIN _runs ru ON ru.v1_id = s.payrollRunId
JOIN _profmap pf ON pf.v1_id = s.employeeProfileId AND pf.v2_org = ru.v2_org
WHERE NOT EXISTS (SELECT 1 FROM altomatehr.Payslips x WHERE x.Id = CONCAT(ru.pfx, s.id) COLLATE utf8mb4_0900_ai_ci)
  AND NOT EXISTS (SELECT 1 FROM altomatehr.Payslips x
                   WHERE x.PayrollRunId = ru.v2_id COLLATE utf8mb4_0900_ai_ci
                     AND x.EmployeeProfileId = pf.v2_id COLLATE utf8mb4_0900_ai_ci);
SELECT 'Payslips' tbl, ROW_COUNT() inserted;

-- ---------------------------------------------------------------- Line items
-- ClaimId stays raw: ../prod-records/02-claims.sql did not prefix claim ids.
INSERT INTO altomatehr.PayslipLineItems
  (Id, OrganizationId, PayslipId, Kind, Label, Amount, Category, PcbTaxableAmount, ClaimId,
   SubjectToEpf, SubjectToSocso, SubjectToEis, SubjectToPcb, CreatedAt)
SELECT CONCAT(ru.pfx, li.id), ru.v2_org, ps.Id, li.kind, li.label, li.amount, li.category, li.pcbTaxableAmount,
       li.claimId, li.subjectToEpf, li.subjectToSocso, li.subjectToEis, li.subjectToPcb, li.createdAt
FROM hr_prod.PayslipLineItem li
JOIN hr_prod.Payslip s ON s.id = li.payslipId
JOIN _runs ru ON ru.v1_id = s.payrollRunId
JOIN altomatehr.Payslips ps ON ps.Id = CONCAT(ru.pfx, s.id) COLLATE utf8mb4_0900_ai_ci
WHERE NOT EXISTS (SELECT 1 FROM altomatehr.PayslipLineItems x WHERE x.Id = CONCAT(ru.pfx, li.id) COLLATE utf8mb4_0900_ai_ci);
SELECT 'PayslipLineItems' tbl, ROW_COUNT() inserted;

-- ---------------------------------------------------------------- Adjustments
INSERT INTO altomatehr.PayrollRunAdjustments
  (Id, OrganizationId, PayrollRunId, EmployeeProfileId, OtNormalHours, OtRestHours, OtPublicHours,
   ManualLineItemsJson, FixedAllowanceOverridesJson, WorkedHours, ExpectedHours, Notes, CreatedAt, UpdatedAt)
SELECT CONCAT(ru.pfx, a.id), ru.v2_org, ru.v2_id, pf.v2_id, a.otNormalHours, a.otRestHours, a.otPublicHours,
       CAST(a.manualLineItems AS CHAR), CAST(a.fixedAllowanceOverrides AS CHAR),
       a.workedHours, a.expectedHours, a.notes, a.createdAt, a.updatedAt
FROM hr_prod.PayrollRunAdjustment a
JOIN _runs ru ON ru.v1_id = a.payrollRunId
JOIN _profmap pf ON pf.v1_id = a.employeeProfileId AND pf.v2_org = ru.v2_org
WHERE NOT EXISTS (SELECT 1 FROM altomatehr.PayrollRunAdjustments x WHERE x.Id = CONCAT(ru.pfx, a.id) COLLATE utf8mb4_0900_ai_ci)
  AND NOT EXISTS (SELECT 1 FROM altomatehr.PayrollRunAdjustments x
                   WHERE x.PayrollRunId = ru.v2_id COLLATE utf8mb4_0900_ai_ci
                     AND x.EmployeeProfileId = pf.v2_id COLLATE utf8mb4_0900_ai_ci);
SELECT 'PayrollRunAdjustments' tbl, ROW_COUNT() inserted;

-- ---------------------------------------------------------------- Run members
-- v1 records EXCLUSIONS (excludedEmployeeProfileIds); v2 records INCLUSIONS
-- (PayrollRunMembers), and a run with no member rows means "everyone". So only runs
-- that excluded someone get member rows, and the members are exactly the people
-- who got a payslip -- otherwise a v2 regeneration would pay the excluded staff.
INSERT INTO altomatehr.PayrollRunMembers (Id, OrganizationId, PayrollRunId, EmployeeProfileId, CreatedAt)
SELECT CONCAT('mig-', ps.Id), ps.OrganizationId, ps.PayrollRunId, ps.EmployeeProfileId, ps.CreatedAt
FROM _runs ru
JOIN altomatehr.Payslips ps ON ps.PayrollRunId = ru.v2_id COLLATE utf8mb4_0900_ai_ci
WHERE ru.excluded IS NOT NULL AND JSON_LENGTH(ru.excluded) > 0
  AND NOT EXISTS (SELECT 1 FROM altomatehr.PayrollRunMembers x
                   WHERE x.PayrollRunId = ps.PayrollRunId AND x.EmployeeProfileId = ps.EmployeeProfileId);
SELECT 'PayrollRunMembers' tbl, ROW_COUNT() inserted;

-- ---------------------------------------------------------------- Salary changes
INSERT INTO altomatehr.SalaryChanges
  (Id, OrganizationId, EmployeeProfileId, EffectiveDate, PreviousSalaryType, PreviousMonthlySalary,
   PreviousHourlyRate, NewSalaryType, NewMonthlySalary, NewHourlyRate, Reason, Notes, ChangedByUserId, CreatedAt)
SELECT CONCAT(IF(pf.v2_id LIKE 'prod-%', 'prod-', ''), s.id), pf.v2_org, pf.v2_id, s.effectiveDate,
       IF(s.previousSalaryType = 'MONTHLY_BASED', 'MONTHLY', s.previousSalaryType), s.previousMonthlySalary,
       s.previousHourlyRate,
       IF(s.newSalaryType = 'MONTHLY_BASED', 'MONTHLY', s.newSalaryType), s.newMonthlySalary, s.newHourlyRate,
       s.reason, s.notes, COALESCE(pm.v2_user_id, s.changedByUserId), s.createdAt
FROM hr_prod.SalaryChange s
JOIN _profmap pf ON pf.v1_id = s.employeeProfileId
LEFT JOIN altomatehr._mig_peoplemap pm ON pm.v1_user_id = s.changedByUserId
WHERE NOT EXISTS (SELECT 1 FROM altomatehr.SalaryChanges x
                   WHERE x.Id = CONCAT(IF(pf.v2_id LIKE 'prod-%', 'prod-', ''), s.id) COLLATE utf8mb4_0900_ai_ci);
SELECT 'SalaryChanges' tbl, ROW_COUNT() inserted;

-- ---------------------------------------------------------------- Loans
-- schedule is a JSON array of numbers in both apps (PayrollLoans.ParseSchedule).
INSERT INTO altomatehr.EmployeeLoans
  (Id, OrganizationId, EmployeeProfileId, PrincipalAmount, Mode, InstallmentAmount, StartYear, StartMonth,
   InstallmentCount, Status, Notes, ScheduleJson, CreatedAt, UpdatedAt)
SELECT CONCAT(IF(pf.v2_id LIKE 'prod-%', 'prod-', ''), l.id), pf.v2_org, pf.v2_id, l.principalAmount, l.mode,
       l.installmentAmount, l.startYear, l.startMonth, l.installmentCount, l.status, l.notes,
       CAST(l.schedule AS CHAR), l.createdAt, l.updatedAt
FROM hr_prod.EmployeeLoan l
JOIN _profmap pf ON pf.v1_id = l.employeeProfileId
WHERE NOT EXISTS (SELECT 1 FROM altomatehr.EmployeeLoans x
                   WHERE x.Id = CONCAT(IF(pf.v2_id LIKE 'prod-%', 'prod-', ''), l.id) COLLATE utf8mb4_0900_ai_ci);
SELECT 'EmployeeLoans' tbl, ROW_COUNT() inserted;

COMMIT;
