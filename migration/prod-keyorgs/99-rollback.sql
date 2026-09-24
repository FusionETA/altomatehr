-- Removes EXACTLY what 02-settings.sql wrote for the key-holder orgs, and nothing else.
--
-- Safe because every one of these orgs was absent from v2 before the migration (00
-- only takes ids v2 does not have) and 01-preflight proved none of their child ids
-- belonged to another org — so every row under these OrganizationIds is ours.
--
-- CAVEAT: rows created in v2 afterwards under these orgs — by an admin, or by
-- New-Altomate through the copied keys — are removed too, and FKs from anything that
-- is NOT in this list (ApiKeys, OrganizationMemberships, EmployeeProfiles, ...) will
-- block the Organizations delete. Check before running once the orgs are in use.
DELETE FROM altomatehr.PolicyLeaveEntitlements WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.Teams                   WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.EmployeePolicies        WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.LeaveTypes              WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.Projects                WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.PayrollSettings         WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.PayrollCompanyInfos     WHERE OrganizationId IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
DELETE FROM altomatehr.Organizations           WHERE Id             IN (SELECT v2_id FROM altomatehr._mig_orgmap_keyorgs);
