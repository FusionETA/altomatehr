-- Verification for the admin migration. Every 'bad' must be 0.
SELECT 'users missing for mapped admins' chk, COUNT(*) bad FROM altomatehr._mig_adminmap a
  LEFT JOIN altomatehr.Users u ON u.Id = a.v2_user_id COLLATE utf8mb4_0900_ai_ci WHERE u.Id IS NULL
UNION ALL SELECT 'memberships missing', COUNT(*) FROM altomatehr._mig_adminmap a
  LEFT JOIN altomatehr.OrganizationMemberships om
    ON om.UserId = a.v2_user_id COLLATE utf8mb4_0900_ai_ci
   AND om.OrganizationId = a.v2_org COLLATE utf8mb4_0900_ai_ci
  WHERE om.Id IS NULL
UNION ALL SELECT 'wrong role', COUNT(*) FROM altomatehr._mig_adminmap a
  JOIN altomatehr.OrganizationMemberships om
    ON om.UserId = a.v2_user_id COLLATE utf8mb4_0900_ai_ci
   AND om.OrganizationId = a.v2_org COLLATE utf8mb4_0900_ai_ci
  WHERE om.Role <> a.v2_role COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'email drift vs hr_prod', COUNT(*) FROM altomatehr._mig_adminmap a
  JOIN altomatehr.Users u ON u.Id = a.v2_user_id COLLATE utf8mb4_0900_ai_ci
  JOIN hr_prod.User s ON s.id = a.v1_user_id
  WHERE a.reuse = 0 AND LOWER(u.Email) <> LOWER(s.email) COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'empty password hash', COUNT(*) FROM altomatehr._mig_adminmap a
  JOIN altomatehr.Users u ON u.Id = a.v2_user_id COLLATE utf8mb4_0900_ai_ci
  WHERE u.PasswordHash IS NULL OR u.PasswordHash = ''
UNION ALL SELECT 'duplicate emails in Users', COUNT(*) FROM (
    SELECT LOWER(Email) e FROM altomatehr.Users GROUP BY 1 HAVING COUNT(*) > 1) z
UNION ALL SELECT 'orphan membership -> Users', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.Users u ON u.Id = om.UserId WHERE u.Id IS NULL
UNION ALL SELECT 'orphan membership -> Organizations', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'bad Role value', COUNT(*) FROM altomatehr.OrganizationMemberships
  WHERE Role NOT IN ('Owner','Admin','Supervisor','Employee')
UNION ALL SELECT 'org with >1 Owner', COUNT(*) FROM (
    SELECT OrganizationId FROM altomatehr.OrganizationMemberships
    WHERE Role = 'Owner' GROUP BY 1 HAVING COUNT(*) > 1) z;

-- Coverage: how many of the 42 settings-migrated orgs now have an admin, and how many
-- still have none because hr_prod has no OWNER/ADMIN for them.
SELECT COUNT(*) AS orgs_total,
       SUM(has_admin) AS orgs_with_admin,
       COUNT(*) - SUM(has_admin) AS orgs_without_admin
FROM (SELECT m.v2_id, MAX(om.Id IS NOT NULL AND om.Role IN ('Owner','Admin')) has_admin
      FROM altomatehr._mig_orgmap m
      LEFT JOIN altomatehr.OrganizationMemberships om
        ON om.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
      GROUP BY m.v2_id) z;
