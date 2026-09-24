-- Removes the DbSeeder demo staff and their fake activity from the live v2 db.
--
-- KEPT deliberately:
--   * usr-admin (admin@altomate.com) — sole Owner of Fusioneta Sdn Bhd, Altomate and
--     Oscar Test Org. Deleting it would leave all three without an administrator.
--   * shift-demo-office — a real account (testimport@example.com) is assigned to it.
--   * client-appraisify — the AppraisifyAlt integration uses this ApiClient.
--   * The GUID LeaveTypes / EmployeePolicies / Projects in org-altomate. The seeder
--     creates those by name rather than fixed id, so they are indistinguishable from
--     rows the three real accounts in that org may have created or be using.
--
-- The v2 database declares NO foreign keys, so delete order does not matter here;
-- it is ordered child-first anyway for readability.
SET @seed := 'usr-super,usr-emp,usr-demo-aisyah,usr-demo-arjun,usr-demo-farid,usr-demo-mei,usr-demo-priya,usr-demo-syafiq';

DELETE FROM altomatehr.AttendanceBreaks           WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.AttendanceApprovalRequests WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.AttendanceSessions         WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.AttendanceRecords          WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.OvertimeRequests           WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.LeaveApplications          WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.LeaveEntitlements          WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.Claims                     WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.TeamMemberships            WHERE FIND_IN_SET(EmployeeId, @seed);
DELETE FROM altomatehr.Notifications              WHERE FIND_IN_SET(UserId, @seed);
DELETE FROM altomatehr.RefreshTokens              WHERE FIND_IN_SET(UserId, @seed);
DELETE FROM altomatehr.PasswordResetOtps          WHERE FIND_IN_SET(UserId, @seed);
DELETE FROM altomatehr.EmployeeProfiles           WHERE FIND_IN_SET(UserId, @seed);
DELETE FROM altomatehr.OrganizationMemberships    WHERE FIND_IN_SET(UserId, @seed);
DELETE FROM altomatehr.Users                      WHERE FIND_IN_SET(Id, @seed);

-- Only the shift nothing surviving points at.
DELETE FROM altomatehr.Shifts
 WHERE Id LIKE 'shift-demo-%'
   AND NOT EXISTS (SELECT 1 FROM altomatehr.OrganizationMemberships m WHERE m.ShiftId = Shifts.Id);
