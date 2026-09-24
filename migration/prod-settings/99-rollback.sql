-- Removes EXACTLY what 02-settings.sql wrote, and nothing else.
--
-- Why this is safe: every migrated row hangs off an OrganizationId listed in
-- _mig_orgmap.v2_id. The 41 non-remapped orgs did not exist in v2 before the
-- migration, and the 42nd was written under a 'prod-' prefixed id, so no
-- pre-existing v2 row shares any of these OrganizationIds.
--
-- Do NOT rewrite this to delete by "Id IN (SELECT id FROM hr_prod.<table>)":
-- dev and prod share cuids, so that form would also delete the 8 LeaveTypes and
-- 3 EmployeePolicies that belong to the v2 ZR TEST org and were never migrated.
--
-- CAVEAT: rows created in v2's UI under a migrated org after the migration are
-- also removed. Check before running if the orgs have been in use.
DELETE FROM altomatehr.PolicyLeaveEntitlements WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.Teams                   WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.EmployeePolicies        WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.LeaveTypes              WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.Projects                WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.PayrollSettings         WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.PayrollCompanyInfos     WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap);
DELETE FROM altomatehr.Organizations           WHERE Id             IN (SELECT v2_id FROM altomatehr._mig_orgmap);
