-- hr_prod -> altomatehr : leave (entitlements + applications)
--
-- LeaveTypes were already migrated by ../prod-settings (349 rows in v2).
-- Org comes from the LEAVE TYPE, which is org-scoped -- not from the employee,
-- who may hold memberships in several orgs.
--
-- EMPLOYEE ID HOP: v1's LeaveApplication.employeeId and LeaveEntitlement.employeeId
-- are EmployeeProfile ids, NOT User ids. v2's EmployeeId is always a User.Id, so
-- both go through EmployeeProfile.userId -> _mig_peoplemap. (v1's Claim and
-- AttendanceRecord use User ids directly -- do not copy this hop to those.)

SELECT IF((SELECT COUNT(*) FROM altomatehr.LeaveTypes) = 0,
          'ABORT: run ../prod-settings first', 'deps ok') AS precheck;

-- ------------------------------------------------------- 1. Entitlements
--
-- DEDUPE -- this is the one that bites. v1 keeps STALE EmployeeProfile rows that
-- no EmployeeOrganization links any more, and their leave entitlements are still
-- there. v1 tolerates it because its unique key is (employeeProfileId, type, year)
-- so two profiles for one person are two legitimate rows; v2's key is
-- (OrganizationId, EmployeeId, LeaveTypeId, Year) with EmployeeId = User.Id, so
-- those two rows become ONE key and the insert dies with a duplicate-entry error.
-- Measured: 56 colliding keys across 128 rows, every one of them the same v1 user
-- holding several profiles -- not the identity collapse in ../prod-employees.
--
-- The fix is not a tie-break, it is a correctness filter: EmployeeOrganization is
-- UNIQUE on (userId, organizationId) AND on employeeProfileId, so the profile it
-- links IS the live one for that person in that org. Joining through it picks the
-- right row by construction and drops 80 entitlements hanging off profiles that
-- v1 itself no longer links to any organization. 3735 rows in, 3735 distinct keys.
--
-- Dropped, no v2 column: usedDays (40 rows > 0), openingUsedDays (22 rows > 0),
-- balanceAsAt (1 row), carriedCashedOutAmount/At/RunId (0 rows).
-- v2 derives days used from APPROVED LeaveApplications rather than storing a
-- counter, so the 34 approved applications loaded below are what has to
-- reproduce those 40 usedDays values. 04-verify.sql diffs them -- a mismatch
-- means v1 counted something v2 cannot see (an application older than the ones
-- that survive in v1, or a manual adjustment), not a failed insert.
INSERT INTO altomatehr.LeaveEntitlements
  (Id, OrganizationId, EmployeeId, LeaveTypeId, Year, EntitledDays, AccruedDays,
   CarriedDays, CarriedExpiresAt, CarriedExpired, CarriedExpiredAt, CarriedExpiredDays,
   AccrualMethod, CreatedAt, UpdatedAt)
SELECT
  e.id, om.v2_id, pm.v2_user_id, e.leaveTypeId, e.year, e.entitledDays, e.accruedDays,
  e.carriedDays, e.carriedExpiresAt, e.carriedExpired, e.carriedExpiredAt, e.carriedExpiredDays,
  e.accrualMethod, e.createdAt, e.updatedAt
FROM hr_prod.LeaveEntitlement e
JOIN hr_prod.EmployeeProfile ep ON ep.id = e.employeeId
-- the live profile for this person in this org, and the dedupe (see above)
JOIN hr_prod.EmployeeOrganization eo ON eo.employeeProfileId = ep.id
JOIN hr_prod.LeaveType lt ON lt.id = e.leaveTypeId AND lt.organizationId = eo.organizationId
JOIN altomatehr._mig_peoplemap pm
  ON pm.v1_user_id COLLATE utf8mb4_unicode_ci = ep.userId
JOIN altomatehr._mig_orgmap om
  ON om.v1_id COLLATE utf8mb4_unicode_ci = lt.organizationId
WHERE NOT EXISTS (
  SELECT 1 FROM altomatehr.LeaveEntitlements v2
   WHERE v2.OrganizationId COLLATE utf8mb4_unicode_ci = om.v2_id
     AND v2.EmployeeId COLLATE utf8mb4_unicode_ci = pm.v2_user_id
     AND v2.LeaveTypeId COLLATE utf8mb4_unicode_ci = e.leaveTypeId
     AND v2.Year = e.year)
AND NOT EXISTS (
  SELECT 1 FROM altomatehr.LeaveEntitlements v3
   WHERE v3.Id COLLATE utf8mb4_unicode_ci = e.id);

-- ------------------------------------------------------- 2. Applications
-- Status, Duration and AccrualMethod enums are member-for-member identical
-- between v1 and v2 (PENDING/APPROVED/REJECTED/CANCELLED,
-- FULL_DAY/MORNING/AFTERNOON) -- no mapping needed, unlike claims.
-- `approvals` (per-step JSON) carries across intact into v2's Approvals column.
INSERT INTO altomatehr.LeaveApplications
  (Id, OrganizationId, EmployeeId, LeaveTypeId, StartDate, EndDate, TotalDays, Reason,
   Status, ReviewNotes, DecidedAt, CreatedAt, UpdatedAt, CurrentStep, XeroFileId,
   AppliedByAdminId, Approvals, AttachmentName, Duration, AttachmentUrl)
SELECT
  l.id, om.v2_id, pm.v2_user_id, l.leaveTypeId, l.startDate, l.endDate, l.totalDays, l.reason,
  l.status, NULL, l.decidedAt, l.createdAt, l.updatedAt, l.currentStep, l.xeroFileId,
  apm.v2_user_id, CAST(l.approvals AS CHAR), LEFT(l.attachmentName, 260), l.duration,
  LEFT(l.attachmentUrl, 400)
FROM hr_prod.LeaveApplication l
JOIN hr_prod.EmployeeProfile ep ON ep.id = l.employeeId
JOIN altomatehr._mig_peoplemap pm
  ON pm.v1_user_id COLLATE utf8mb4_unicode_ci = ep.userId
JOIN hr_prod.LeaveType lt ON lt.id = l.leaveTypeId
JOIN altomatehr._mig_orgmap om
  ON om.v1_id COLLATE utf8mb4_unicode_ci = lt.organizationId
LEFT JOIN altomatehr._mig_peoplemap apm
  ON apm.v1_user_id COLLATE utf8mb4_unicode_ci = l.appliedByAdminId
WHERE NOT EXISTS (
  SELECT 1 FROM altomatehr.LeaveApplications v2
   WHERE v2.Id COLLATE utf8mb4_unicode_ci = l.id);
