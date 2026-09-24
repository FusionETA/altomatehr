-- Verification for 20-admin-org-grants.sql. Every 'bad' must be 0.
SELECT 'AdminOrganization grants still missing' chk, COUNT(*) bad FROM hr_prod.AdminOrganization a
  JOIN altomatehr._mig_orgmap m ON m.v1_id = a.organizationId
  JOIN altomatehr._mig_adminmap am ON am.v1_user_id = a.adminId
  LEFT JOIN altomatehr.OrganizationMemberships om
    ON om.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
   AND om.UserId = am.v2_user_id COLLATE utf8mb4_0900_ai_ci
  WHERE om.Id IS NULL
UNION ALL SELECT 'orgs with >1 Owner', COUNT(*) FROM (
    SELECT OrganizationId FROM altomatehr.OrganizationMemberships
    WHERE Role = 'Owner' GROUP BY 1 HAVING COUNT(*) > 1) z
UNION ALL SELECT 'orphan membership -> Users', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.Users u ON u.Id = om.UserId WHERE u.Id IS NULL
UNION ALL SELECT 'orphan membership -> Organizations', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'bad Role value', COUNT(*) FROM altomatehr.OrganizationMemberships
  WHERE Role NOT IN ('Owner','Admin','Supervisor','Employee')
UNION ALL SELECT 'duplicate (org,user) memberships', COUNT(*) FROM (
    SELECT OrganizationId, UserId FROM altomatehr.OrganizationMemberships
    GROUP BY 1,2 HAVING COUNT(*) > 1) z;

SELECT COUNT(*) AS total_memberships FROM altomatehr.OrganizationMemberships;
SELECT Role, COUNT(*) n FROM altomatehr.OrganizationMemberships GROUP BY Role ORDER BY n DESC;

SELECT COUNT(*) AS orgs_of_42_still_without_admin FROM (
  SELECT m.v2_id, MAX(om.Role IN ('Owner','Admin')) has_admin
  FROM altomatehr._mig_orgmap m
  LEFT JOIN altomatehr.OrganizationMemberships om
    ON om.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
  GROUP BY m.v2_id) z
WHERE COALESCE(has_admin, 0) = 0;

-- The account that surfaced the bug.
SELECT u.Email, o.Name AS org, om.Role, om.Modules
FROM altomatehr.Users u
JOIN altomatehr.OrganizationMemberships om ON om.UserId = u.Id
JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId
WHERE u.Email = 'adminglobe@gmail.com';
