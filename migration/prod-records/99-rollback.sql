-- Removes exactly what 01/02/03 wrote, and nothing else.
--
-- The org filter is the safety catch: every row this migration created sits in
-- one of the 42 mapped orgs (_mig_orgmap.v2_id). The rows it deliberately
-- SKIPPED -- the hr_dev ZR TEST twins that share an id -- live under the
-- unmapped org id `cmogup4k90000kukzos5fj1qn`, so they are never touched here.
-- Delete children before parents.
--
-- COLLATION TRAP: _mig_orgmap.v2_id is utf8mb4_unicode_ci (it was created by
-- hand in ../prod-settings) while every EF-created table is utf8mb4_0900_ai_ci,
-- so even a same-database `OrganizationId IN (SELECT v2_id ...)` throws
-- "Illegal mix of collations" without the explicit COLLATE below.

DELETE v FROM altomatehr.AttendanceApprovalRequests v
 JOIN hr_prod.ApprovalRequest s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);

DELETE v FROM altomatehr.AttendanceBreaks v
 JOIN hr_prod.BreakSession s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);

DELETE v FROM altomatehr.AttendanceSessions v
 JOIN hr_prod.AttendanceSession s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);

DELETE v FROM altomatehr.AttendanceRecords v
 JOIN hr_prod.AttendanceRecord s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);

DELETE v FROM altomatehr.Claims v
 JOIN hr_prod.Claim s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);

DELETE v FROM altomatehr.LeaveApplications v
 JOIN hr_prod.LeaveApplication s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);

DELETE v FROM altomatehr.LeaveEntitlements v
 JOIN hr_prod.LeaveEntitlement s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
 WHERE v.OrganizationId IN (SELECT v2_id COLLATE utf8mb4_0900_ai_ci FROM altomatehr._mig_orgmap);
