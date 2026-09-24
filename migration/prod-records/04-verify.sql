-- Every check must return 0 unless its name says otherwise.
SELECT 'attendance records loaded (expect 891 = 893 - 2 hr_dev twins)' AS check_, COUNT(*) AS n
  FROM altomatehr.AttendanceRecords v JOIN hr_prod.AttendanceRecord s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
UNION ALL SELECT 'sessions loaded (expect 905 = 912 - 7 on skipped records)', COUNT(*)
  FROM altomatehr.AttendanceSessions v JOIN hr_prod.AttendanceSession s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
UNION ALL SELECT 'breaks loaded (expect 9 = 18 - 5 sessionless - 4 on skipped sessions)', COUNT(*)
  FROM altomatehr.AttendanceBreaks v JOIN hr_prod.BreakSession s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
UNION ALL SELECT 'approval requests loaded (expect 1816 = 1860 - 3 OT - 13 no record - 28 skipped)', COUNT(*)
  FROM altomatehr.AttendanceApprovalRequests v JOIN hr_prod.ApprovalRequest s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
UNION ALL SELECT 'claims loaded (expect 64 of 74)', COUNT(*)
  FROM altomatehr.Claims v JOIN hr_prod.Claim s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
  WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap)
UNION ALL SELECT 'leave applications loaded (expect 48 = 47 new + 1 pre-existing twin)', COUNT(*)
  FROM altomatehr.LeaveApplications v JOIN hr_prod.LeaveApplication s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
UNION ALL SELECT 'leave entitlements loaded (expect 3735 of 3815)', COUNT(*)
  FROM altomatehr.LeaveEntitlements v JOIN hr_prod.LeaveEntitlement s ON s.id = v.Id COLLATE utf8mb4_unicode_ci

-- Referential integrity: every FK must resolve inside v2.
UNION ALL SELECT 'attendance -> Users orphan', COUNT(*) FROM altomatehr.AttendanceRecords v
  LEFT JOIN altomatehr.Users u ON u.Id = v.EmployeeId WHERE u.Id IS NULL
UNION ALL SELECT 'attendance -> Organizations orphan', COUNT(*) FROM altomatehr.AttendanceRecords v
  LEFT JOIN altomatehr.Organizations o ON o.Id = v.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'attendance -> Projects orphan', COUNT(*) FROM altomatehr.AttendanceRecords v
  LEFT JOIN altomatehr.Projects p ON p.Id = v.ProjectId WHERE v.ProjectId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT 'sessions -> record orphan', COUNT(*) FROM altomatehr.AttendanceSessions v
  LEFT JOIN altomatehr.AttendanceRecords r ON r.Id = v.AttendanceRecordId WHERE r.Id IS NULL
UNION ALL SELECT 'breaks -> session orphan', COUNT(*) FROM altomatehr.AttendanceBreaks v
  LEFT JOIN altomatehr.AttendanceSessions s ON s.Id = v.AttendanceSessionId WHERE s.Id IS NULL
UNION ALL SELECT 'approvals -> record orphan', COUNT(*) FROM altomatehr.AttendanceApprovalRequests v
  LEFT JOIN altomatehr.AttendanceRecords r ON r.Id = v.AttendanceRecordId WHERE r.Id IS NULL
UNION ALL SELECT 'claims -> Users orphan', COUNT(*) FROM altomatehr.Claims v
  LEFT JOIN altomatehr.Users u ON u.Id = v.EmployeeId WHERE u.Id IS NULL
UNION ALL SELECT 'claims -> ChartOfAccounts orphan', COUNT(*) FROM altomatehr.Claims v
  LEFT JOIN altomatehr.ChartOfAccounts a ON a.Id = v.ChartOfAccountId
  WHERE v.ChartOfAccountId IS NOT NULL AND a.Id IS NULL
UNION ALL SELECT 'claims -> Projects orphan', COUNT(*) FROM altomatehr.Claims v
  LEFT JOIN altomatehr.Projects p ON p.Id = v.ProjectId WHERE v.ProjectId IS NOT NULL AND p.Id IS NULL
UNION ALL SELECT 'leave apps -> LeaveTypes orphan', COUNT(*) FROM altomatehr.LeaveApplications v
  LEFT JOIN altomatehr.LeaveTypes t ON t.Id = v.LeaveTypeId WHERE t.Id IS NULL
UNION ALL SELECT 'leave entitlements -> LeaveTypes orphan', COUNT(*) FROM altomatehr.LeaveEntitlements v
  LEFT JOIN altomatehr.LeaveTypes t ON t.Id = v.LeaveTypeId WHERE t.Id IS NULL

-- Enum spellings. v2 stores enums as strings and throws on READ, not on write,
-- so a bad value here is an empty screen later, not a failed migration.
UNION ALL SELECT 'bad AttendanceRecords.Status', COUNT(*) FROM altomatehr.AttendanceRecords
  WHERE Status NOT IN ('ON_TIME','LATE','MISSING','CLOCKED_IN','CLOCKED_OUT','ON_LEAVE')
UNION ALL SELECT 'bad AttendanceSessions.Status', COUNT(*) FROM altomatehr.AttendanceSessions
  WHERE Status NOT IN ('ON_TIME','LATE','MISSING','CLOCKED_IN','CLOCKED_OUT','ON_LEAVE')
UNION ALL SELECT 'bad AttendanceApprovalRequests.Kind', COUNT(*) FROM altomatehr.AttendanceApprovalRequests
  WHERE Kind NOT IN ('CLOCK_IN','CLOCK_OUT','BREAK_START','BREAK_END')
UNION ALL SELECT 'bad AttendanceApprovalRequests.ApprovalStatus', COUNT(*) FROM altomatehr.AttendanceApprovalRequests
  WHERE ApprovalStatus NOT IN ('PENDING','APPROVED','REJECTED')
UNION ALL SELECT 'bad Claims.Status (REVIEWED must be gone)', COUNT(*) FROM altomatehr.Claims
  WHERE Status NOT IN ('SUBMITTED','PENDING','APPROVED','REJECTED')
UNION ALL SELECT 'bad Claims.Category', COUNT(*) FROM altomatehr.Claims
  WHERE Category NOT IN ('TRAVEL','TRANSPORT','MEAL','MEDICAL','WELLNESS','HARDWARE','OFFICE','OTHER')
UNION ALL SELECT 'bad Claims.XeroSyncStatus', COUNT(*) FROM altomatehr.Claims
  WHERE XeroSyncStatus NOT IN ('NOT_SYNCED','SYNCED','ERROR')
UNION ALL SELECT 'bad LeaveApplications.Status', COUNT(*) FROM altomatehr.LeaveApplications
  WHERE Status NOT IN ('PENDING','APPROVED','REJECTED','CANCELLED')
UNION ALL SELECT 'bad LeaveApplications.Duration', COUNT(*) FROM altomatehr.LeaveApplications
  WHERE Duration NOT IN ('FULL_DAY','MORNING','AFTERNOON')
UNION ALL SELECT 'bad LeaveEntitlements.AccrualMethod', COUNT(*) FROM altomatehr.LeaveEntitlements
  WHERE AccrualMethod IS NOT NULL AND AccrualMethod NOT IN ('LUMP_SUM','PRO_RATED')

-- Tenant isolation: a child must sit in the same org as its parent.
UNION ALL SELECT 'session org != record org', COUNT(*) FROM altomatehr.AttendanceSessions v
  JOIN altomatehr.AttendanceRecords r ON r.Id = v.AttendanceRecordId WHERE r.OrganizationId <> v.OrganizationId
UNION ALL SELECT 'approval org != record org', COUNT(*) FROM altomatehr.AttendanceApprovalRequests v
  JOIN altomatehr.AttendanceRecords r ON r.Id = v.AttendanceRecordId WHERE r.OrganizationId <> v.OrganizationId

-- Not-yet-loaded, on purpose. Expected: 10 claims, 1 leave app, 2 attendance,
-- 5 breaks, 3 OT, 13 approval requests with no record.
UNION ALL SELECT 'SKIPPED claims (ClaimNumber taken by an hr_dev twin)', COUNT(*)
  FROM hr_prod.Claim c JOIN altomatehr.Claims v2
    ON v2.ClaimNumber COLLATE utf8mb4_unicode_ci = c.claimNumber
  WHERE v2.OrganizationId NOT IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap)
UNION ALL SELECT 'SKIPPED breaks (record has no session)', COUNT(*)
  FROM hr_prod.BreakSession WHERE attendanceSessionId IS NULL
UNION ALL SELECT 'SKIPPED OT approvals (no v2 home)', COUNT(*)
  FROM hr_prod.ApprovalRequest WHERE kind = 'OT'
-- Leave balances v2 cannot reproduce. v2 has no UsedDays column -- it derives
-- days taken from APPROVED LeaveApplications -- so any v1 usedDays that no
-- application backs is lost. Expect 22 (all of them opening balances entered at
-- onboarding: usedDays == openingUsedDays, no application rows behind them).
UNION ALL SELECT 'LOST leave usedDays with no application behind them (expect 22)', COUNT(*)
  FROM hr_prod.LeaveEntitlement e
  JOIN hr_prod.EmployeeProfile ep ON ep.id = e.employeeId
  JOIN hr_prod.EmployeeOrganization eo ON eo.employeeProfileId = ep.id
  JOIN hr_prod.LeaveType lt ON lt.id = e.leaveTypeId AND lt.organizationId = eo.organizationId
  WHERE e.usedDays > 0
    AND e.usedDays <> COALESCE((SELECT SUM(l.totalDays) FROM hr_prod.LeaveApplication l
          WHERE l.employeeId = e.employeeId AND l.leaveTypeId = e.leaveTypeId
            AND l.status = 'APPROVED' AND YEAR(l.startDate) = e.year), 0);
