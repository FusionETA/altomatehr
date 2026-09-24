-- Status sweep of every item still open across the three migrations.

SELECT '1. owner-only orgs not in v2' item, COUNT(*) v1_rows,
       SUM(o.id IN (SELECT Id COLLATE utf8mb4_unicode_ci FROM altomatehr.Organizations)) in_v2
FROM hr_prod.Organization o
WHERE o.id NOT IN (SELECT v1_id FROM altomatehr._mig_orgmap)
  AND EXISTS (SELECT 1 FROM hr_prod.User u WHERE u.organizationId=o.id AND u.role IN ('OWNER','ADMIN'));

SELECT '2. ChartOfAccount' item,
       (SELECT COUNT(*) FROM hr_prod.ChartOfAccount x JOIN altomatehr._mig_orgmap m ON m.v1_id=x.organizationId) v1_in_scope,
       (SELECT COUNT(*) FROM altomatehr.ChartOfAccounts c JOIN altomatehr._mig_orgmap m ON m.v2_id COLLATE utf8mb4_0900_ai_ci=c.OrganizationId) in_migrated_orgs;

SELECT '3. LeaveEntitlement (balances)' item,
       (SELECT COUNT(*) FROM hr_prod.LeaveEntitlement le JOIN hr_prod.LeaveType lt ON lt.id=le.leaveTypeId
         JOIN altomatehr._mig_orgmap m ON m.v1_id=lt.organizationId) v1_in_scope,
       (SELECT COUNT(*) FROM altomatehr.LeaveEntitlements e JOIN altomatehr._mig_orgmap m ON m.v2_id COLLATE utf8mb4_0900_ai_ci=e.OrganizationId) in_migrated_orgs;

-- 4. the 2 orgs with no admin: confirm v1 has none by ANY route (home org or grant)
SELECT '4. orgs with no admin' item, m.name AS org,
       (SELECT COUNT(*) FROM hr_prod.User u WHERE u.organizationId=m.v1_id AND u.role IN ('OWNER','ADMIN')) owners_by_home_org,
       (SELECT COUNT(*) FROM hr_prod.AdminOrganization a WHERE a.organizationId=m.v1_id) grants_in_v1,
       (SELECT COUNT(*) FROM hr_prod.EmployeeOrganization e WHERE e.organizationId=m.v1_id) members
FROM altomatehr._mig_orgmap m
LEFT JOIN altomatehr.OrganizationMemberships om
  ON om.OrganizationId=m.v2_id COLLATE utf8mb4_0900_ai_ci AND om.Role IN ('Owner','Admin')
GROUP BY m.v1_id, m.name HAVING COUNT(om.Id)=0;

-- 5. the admins stranded with no org at all, and whether a grant would save them
SELECT '5. admins with NULL organizationId' item, COUNT(*) admins,
       SUM(EXISTS (SELECT 1 FROM hr_prod.AdminOrganization a WHERE a.adminId=u.id)) AS rescued_by_a_grant
FROM hr_prod.User u WHERE u.role IN ('OWNER','ADMIN') AND u.organizationId IS NULL;

-- 6. Xero: every migrated org must reconnect
SELECT '6. XeroConnection' item,
       (SELECT COUNT(*) FROM hr_prod.XeroConnection x JOIN altomatehr._mig_orgmap m ON m.v1_id=x.organizationId) v1_in_scope,
       (SELECT COUNT(*) FROM altomatehr.XeroConnections) in_v2_total;
