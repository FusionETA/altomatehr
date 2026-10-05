USE altomatehr;
-- Post-run checks for the top-up. Section A = coverage (v1 rows vs rows present in v2
-- by id, per table). Section B = every "bad" must be 0. Section C = skip reasons.

-- ============================================================ A. coverage
SELECT t, v1, in_v2, v1 - in_v2 AS not_in_v2 FROM (
  SELECT 'AttendanceRecord' t, (SELECT COUNT(*) FROM hr_prod.AttendanceRecord) v1,
    (SELECT COUNT(*) FROM hr_prod.AttendanceRecord a JOIN altomatehr.AttendanceRecords v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci) in_v2
  UNION ALL SELECT 'AttendanceSession', (SELECT COUNT(*) FROM hr_prod.AttendanceSession),
    (SELECT COUNT(*) FROM hr_prod.AttendanceSession a JOIN altomatehr.AttendanceSessions v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci)
  UNION ALL SELECT 'BreakSession', (SELECT COUNT(*) FROM hr_prod.BreakSession),
    (SELECT COUNT(*) FROM hr_prod.BreakSession a JOIN altomatehr.AttendanceBreaks v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci)
  UNION ALL SELECT 'ApprovalRequest (non-OT)', (SELECT COUNT(*) FROM hr_prod.ApprovalRequest WHERE kind <> 'OT'),
    (SELECT COUNT(*) FROM hr_prod.ApprovalRequest a JOIN altomatehr.AttendanceApprovalRequests v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci)
  UNION ALL SELECT 'Claim', (SELECT COUNT(*) FROM hr_prod.Claim),
    (SELECT COUNT(*) FROM hr_prod.Claim a JOIN altomatehr.Claims v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci
      WHERE v.OrganizationId <> 'cmogup4k90000kukzos5fj1qn')
  UNION ALL SELECT 'LeaveApplication', (SELECT COUNT(*) FROM hr_prod.LeaveApplication),
    (SELECT COUNT(*) FROM hr_prod.LeaveApplication a JOIN altomatehr.LeaveApplications v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci)
  UNION ALL SELECT 'LeaveEntitlement', (SELECT COUNT(*) FROM hr_prod.LeaveEntitlement),
    (SELECT COUNT(*) FROM hr_prod.LeaveEntitlement a JOIN altomatehr.LeaveEntitlements v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci)
  UNION ALL SELECT 'PayrollRun', (SELECT COUNT(*) FROM hr_prod.PayrollRun),
    (SELECT COUNT(*) FROM hr_prod.PayrollRun a JOIN altomatehr.PayrollRuns v
      ON v.Id IN (a.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', a.id) COLLATE utf8mb4_0900_ai_ci))
  UNION ALL SELECT 'Payslip', (SELECT COUNT(*) FROM hr_prod.Payslip),
    (SELECT COUNT(*) FROM hr_prod.Payslip a JOIN altomatehr.Payslips v
      ON v.Id IN (a.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', a.id) COLLATE utf8mb4_0900_ai_ci))
  UNION ALL SELECT 'PayslipLineItem', (SELECT COUNT(*) FROM hr_prod.PayslipLineItem),
    (SELECT COUNT(*) FROM hr_prod.PayslipLineItem a JOIN altomatehr.PayslipLineItems v
      ON v.Id IN (a.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', a.id) COLLATE utf8mb4_0900_ai_ci))
  UNION ALL SELECT 'PayrollRunAdjustment', (SELECT COUNT(*) FROM hr_prod.PayrollRunAdjustment),
    (SELECT COUNT(*) FROM hr_prod.PayrollRunAdjustment a JOIN altomatehr.PayrollRunAdjustments v
      ON v.Id IN (a.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', a.id) COLLATE utf8mb4_0900_ai_ci))
  UNION ALL SELECT 'SalaryChange', (SELECT COUNT(*) FROM hr_prod.SalaryChange),
    (SELECT COUNT(*) FROM hr_prod.SalaryChange a JOIN altomatehr.SalaryChanges v
      ON v.Id IN (a.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', a.id) COLLATE utf8mb4_0900_ai_ci))
  UNION ALL SELECT 'EmployeeLoan', (SELECT COUNT(*) FROM hr_prod.EmployeeLoan),
    (SELECT COUNT(*) FROM hr_prod.EmployeeLoan a JOIN altomatehr.EmployeeLoans v
      ON v.Id IN (a.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', a.id) COLLATE utf8mb4_0900_ai_ci))
) x;

-- ============================================================ B. integrity (all 0)
SELECT chk, bad FROM (
  SELECT 'drift: v1 newer + v2 untouched (records)' chk, COUNT(*) bad FROM altomatehr.AttendanceRecords v
    JOIN hr_prod.AttendanceRecord a ON a.id = v.Id COLLATE utf8mb4_unicode_ci
    WHERE v.OrganizationId <> 'cmogup4k90000kukzos5fj1qn' AND a.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0
  UNION ALL SELECT 'drift: claims', COUNT(*) FROM altomatehr.Claims v JOIN hr_prod.Claim c ON c.id = v.Id COLLATE utf8mb4_unicode_ci
    WHERE v.OrganizationId <> 'cmogup4k90000kukzos5fj1qn' AND c.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0
  UNION ALL SELECT 'drift: leave apps', COUNT(*) FROM altomatehr.LeaveApplications v JOIN hr_prod.LeaveApplication l ON l.id = v.Id COLLATE utf8mb4_unicode_ci
    WHERE l.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0
  UNION ALL SELECT 'enum: PayrollRuns.Status', COUNT(*) FROM altomatehr.PayrollRuns WHERE Status NOT IN ('DRAFT','PENDING_APPROVAL','SUBMITTED')
  UNION ALL SELECT 'enum: PayrollRuns.Source', COUNT(*) FROM altomatehr.PayrollRuns WHERE Source NOT IN ('COMPUTED','IMPORTED')
  UNION ALL SELECT 'enum: PayrollRuns.XeroSyncStatus', COUNT(*) FROM altomatehr.PayrollRuns WHERE XeroSyncStatus NOT IN ('NOT_SYNCED','SYNCED','ERROR')
  UNION ALL SELECT 'enum: Payslips.SnapshotSalaryType', COUNT(*) FROM altomatehr.Payslips WHERE SnapshotSalaryType NOT IN ('HOURLY','MONTHLY')
  UNION ALL SELECT 'enum: PayslipLineItems.Kind', COUNT(*) FROM altomatehr.PayslipLineItems WHERE Kind NOT IN ('ALLOWANCE','DEDUCTION','REIMBURSEMENT')
  UNION ALL SELECT 'enum: SalaryChanges types', COUNT(*) FROM altomatehr.SalaryChanges
    WHERE NewSalaryType NOT IN ('HOURLY','MONTHLY') OR (PreviousSalaryType IS NOT NULL AND PreviousSalaryType NOT IN ('HOURLY','MONTHLY'))
       OR Reason NOT IN ('RAISE','PROMOTION','DEMOTION','RESTRUCTURE','OTHER')
  UNION ALL SELECT 'enum: EmployeeLoans', COUNT(*) FROM altomatehr.EmployeeLoans WHERE Mode NOT IN ('FIXED','CUSTOM') OR Status NOT IN ('ACTIVE','COMPLETED','CANCELLED')
  UNION ALL SELECT 'enum: Claims.Status', COUNT(*) FROM altomatehr.Claims WHERE Status NOT IN ('SUBMITTED','PENDING','APPROVED','REJECTED')
  UNION ALL SELECT 'enum: Claims.XeroSyncStatus', COUNT(*) FROM altomatehr.Claims WHERE XeroSyncStatus NOT IN ('NOT_SYNCED','SYNCED','ERROR')
  UNION ALL SELECT 'enum: AttendanceRecords.Status', COUNT(*) FROM altomatehr.AttendanceRecords
    WHERE Status NOT IN ('ON_TIME','LATE','MISSING','CLOCKED_IN','CLOCKED_OUT','ON_LEAVE')
  UNION ALL SELECT 'enum: LeaveApplications.Status', COUNT(*) FROM altomatehr.LeaveApplications WHERE Status NOT IN ('PENDING','APPROVED','REJECTED','CANCELLED')
  UNION ALL SELECT 'orphan: payslip -> run', COUNT(*) FROM altomatehr.Payslips p LEFT JOIN altomatehr.PayrollRuns r ON r.Id = p.PayrollRunId WHERE r.Id IS NULL
  UNION ALL SELECT 'orphan: payslip -> profile', COUNT(*) FROM altomatehr.Payslips p LEFT JOIN altomatehr.EmployeeProfiles e ON e.Id = p.EmployeeProfileId WHERE e.Id IS NULL
  UNION ALL SELECT 'orphan: payslip -> user', COUNT(*) FROM altomatehr.Payslips p LEFT JOIN altomatehr.Users u ON u.Id = p.UserId WHERE u.Id IS NULL
  UNION ALL SELECT 'org mismatch: payslip vs run', COUNT(*) FROM altomatehr.Payslips p JOIN altomatehr.PayrollRuns r ON r.Id = p.PayrollRunId WHERE r.OrganizationId <> p.OrganizationId
  UNION ALL SELECT 'org mismatch: payslip vs profile', COUNT(*) FROM altomatehr.Payslips p JOIN altomatehr.EmployeeProfiles e ON e.Id = p.EmployeeProfileId WHERE e.OrganizationId <> p.OrganizationId
  UNION ALL SELECT 'orphan: line item -> payslip', COUNT(*) FROM altomatehr.PayslipLineItems li LEFT JOIN altomatehr.Payslips p ON p.Id = li.PayslipId WHERE p.Id IS NULL
  UNION ALL SELECT 'orphan: adjustment -> run/profile', COUNT(*) FROM altomatehr.PayrollRunAdjustments a
    LEFT JOIN altomatehr.PayrollRuns r ON r.Id = a.PayrollRunId LEFT JOIN altomatehr.EmployeeProfiles e ON e.Id = a.EmployeeProfileId
    WHERE r.Id IS NULL OR e.Id IS NULL
  UNION ALL SELECT 'orphan: member -> run/profile', COUNT(*) FROM altomatehr.PayrollRunMembers m
    LEFT JOIN altomatehr.PayrollRuns r ON r.Id = m.PayrollRunId LEFT JOIN altomatehr.EmployeeProfiles e ON e.Id = m.EmployeeProfileId
    WHERE r.Id IS NULL OR e.Id IS NULL
  UNION ALL SELECT 'orphan: salary change / loan -> profile', COUNT(*) FROM (
      SELECT EmployeeProfileId FROM altomatehr.SalaryChanges UNION ALL SELECT EmployeeProfileId FROM altomatehr.EmployeeLoans) z
    LEFT JOIN altomatehr.EmployeeProfiles e ON e.Id = z.EmployeeProfileId WHERE e.Id IS NULL
  UNION ALL SELECT 'payslip sum != run total (migrated runs)', COUNT(*) FROM altomatehr.PayrollRuns r
    JOIN hr_prod.PayrollRun v1 ON r.Id IN (v1.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', v1.id) COLLATE utf8mb4_0900_ai_ci)
    WHERE ABS(r.TotalNet - (SELECT COALESCE(SUM(NetPay),0) FROM altomatehr.Payslips p WHERE p.PayrollRunId = r.Id))
        > ABS(v1.totalNet - (SELECT COALESCE(SUM(netPay),0) FROM hr_prod.Payslip s WHERE s.payrollRunId = v1.id)) + 0.01
  UNION ALL SELECT 'orphan: attendance -> user', COUNT(*) FROM altomatehr.AttendanceRecords a LEFT JOIN altomatehr.Users u ON u.Id = a.EmployeeId WHERE u.Id IS NULL
  UNION ALL SELECT 'orphan: session -> record', COUNT(*) FROM altomatehr.AttendanceSessions s LEFT JOIN altomatehr.AttendanceRecords r ON r.Id = s.AttendanceRecordId WHERE r.Id IS NULL
  UNION ALL SELECT 'orphan: claim -> user', COUNT(*) FROM altomatehr.Claims c LEFT JOIN altomatehr.Users u ON u.Id = c.EmployeeId WHERE u.Id IS NULL AND c.OrganizationId <> 'org-altomate'
  UNION ALL SELECT 'orphan: claim -> COA', COUNT(*) FROM altomatehr.Claims c LEFT JOIN altomatehr.ChartOfAccounts a ON a.Id = c.ChartOfAccountId WHERE c.ChartOfAccountId IS NOT NULL AND a.Id IS NULL
  UNION ALL SELECT 'orphan: claim -> project', COUNT(*) FROM altomatehr.Claims c LEFT JOIN altomatehr.Projects p ON p.Id = c.ProjectId WHERE c.ProjectId IS NOT NULL AND p.Id IS NULL
  UNION ALL SELECT 'orphan: leave -> leave type', COUNT(*) FROM altomatehr.LeaveApplications l LEFT JOIN altomatehr.LeaveTypes t ON t.Id = l.LeaveTypeId WHERE t.Id IS NULL
  UNION ALL SELECT 'path: v1-shaped receipt url left', COUNT(*) FROM altomatehr.Claims WHERE ReceiptUrl LIKE '/api/%' OR ReceiptUrl LIKE '/uploads/%'
  UNION ALL SELECT 'path: v1-shaped supporting doc left', COUNT(*) FROM altomatehr.Claims WHERE SupportingDocumentUrls LIKE '%/api/xero/%' OR SupportingDocumentUrls LIKE '%/uploads/%'
  UNION ALL SELECT 'path: xero receipt without ReceiptXeroFileId', COUNT(*) FROM altomatehr.Claims WHERE ReceiptUrl LIKE '/claims/receipts/xero/%' AND ReceiptXeroFileId IS NULL
  UNION ALL SELECT 'path: v1 selfie not carried (record)', COUNT(*) FROM altomatehr.AttendanceRecords v
    JOIN hr_prod.AttendanceRecord a ON a.id = v.Id COLLATE utf8mb4_unicode_ci
    WHERE v.OrganizationId <> 'cmogup4k90000kukzos5fj1qn'
      AND ((a.xeroSelfieFileId IS NOT NULL AND v.ClockInPhotoUrl IS NULL) OR (a.clockOutXeroSelfieFileId IS NOT NULL AND v.ClockOutPhotoUrl IS NULL))
  UNION ALL SELECT 'path: camelCase employee documents', COUNT(*) FROM altomatehr.EmployeeProfiles
    WHERE JSON_VALID(PayrollDocumentsJson) AND JSON_CONTAINS_PATH(PayrollDocumentsJson, 'one', '$[0].url')
) y;

-- ============================================================ C. why rows are not in v2
-- Payroll runs skipped because v2 already holds that org+period (v2 is source of truth).
SELECT o.name org, r.periodYear, r.periodMonth, r.status v1_status, v.Id v2_run, v.Status v2_status, v.CreatedAt v2_created
FROM hr_prod.PayrollRun r
JOIN altomatehr._mig_orgmap om ON om.v1_id = r.organizationId
JOIN altomatehr.PayrollRuns v ON v.OrganizationId = om.v2_id COLLATE utf8mb4_0900_ai_ci
 AND v.PeriodYear = r.periodYear AND v.PeriodMonth = r.periodMonth
JOIN hr_prod.Organization o ON o.id = r.organizationId
WHERE v.Id NOT IN (r.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', r.id) COLLATE utf8mb4_0900_ai_ci)
ORDER BY o.name, r.periodYear, r.periodMonth;

-- Payroll runs whose v1 org is not in scope (org has 0 members, never migrated).
SELECT o.name org, COUNT(*) runs FROM hr_prod.PayrollRun r JOIN hr_prod.Organization o ON o.id = r.organizationId
LEFT JOIN altomatehr._mig_orgmap om ON om.v1_id = r.organizationId WHERE om.v1_id IS NULL GROUP BY o.name;

-- Payslips of migrated runs whose employee profile has no v2 row.
SELECT o.name org, s.snapshotName, COUNT(*) payslips
FROM hr_prod.Payslip s JOIN hr_prod.PayrollRun r ON r.id = s.payrollRunId
JOIN hr_prod.Organization o ON o.id = r.organizationId
JOIN altomatehr.PayrollRuns v ON v.Id IN (r.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', r.id) COLLATE utf8mb4_0900_ai_ci)
LEFT JOIN altomatehr.Payslips p ON p.Id IN (s.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', s.id) COLLATE utf8mb4_0900_ai_ci)
WHERE p.Id IS NULL GROUP BY o.name, s.snapshotName;
