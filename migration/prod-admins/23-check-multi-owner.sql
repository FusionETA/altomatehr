-- Who the two Owners are in each multi-owner org, and the v1 evidence for each.
SELECT o.Name AS org, u.Email, om.Role, om.Modules,
       (SELECT COUNT(*) FROM hr_prod.AdminOrganization a
         JOIN altomatehr._mig_adminmap am ON am.v1_user_id = a.adminId
         JOIN altomatehr._mig_orgmap m2 ON m2.v1_id = a.organizationId
        WHERE am.v2_user_id COLLATE utf8mb4_0900_ai_ci = om.UserId
          AND m2.v2_id COLLATE utf8mb4_0900_ai_ci = om.OrganizationId) AS v1_grant_rows,
       (SELECT COUNT(*) FROM hr_prod.User u2
         JOIN altomatehr._mig_adminmap am2 ON am2.v1_user_id = u2.id
         JOIN altomatehr._mig_orgmap m3 ON m3.v1_id = u2.organizationId
        WHERE am2.v2_user_id COLLATE utf8mb4_0900_ai_ci = om.UserId
          AND m3.v2_id COLLATE utf8mb4_0900_ai_ci = om.OrganizationId) AS v1_home_org
FROM altomatehr.OrganizationMemberships om
JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId
JOIN altomatehr.Users u ON u.Id = om.UserId
WHERE om.Role = 'Owner'
  AND om.OrganizationId IN (
      SELECT OrganizationId FROM altomatehr.OrganizationMemberships
      WHERE Role='Owner' GROUP BY 1 HAVING COUNT(*)>1)
ORDER BY o.Name, u.Email;

-- Are the two owners of each org distinct people, or one person with two v1 accounts?
SELECT o.Name AS org, COUNT(DISTINCT om.UserId) AS distinct_v2_accounts,
       COUNT(DISTINCT LOWER(u.Email)) AS distinct_emails
FROM altomatehr.OrganizationMemberships om
JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId
JOIN altomatehr.Users u ON u.Id = om.UserId
WHERE om.Role='Owner'
  AND om.OrganizationId IN (
      SELECT OrganizationId FROM altomatehr.OrganizationMemberships
      WHERE Role='Owner' GROUP BY 1 HAVING COUNT(*)>1)
GROUP BY o.Name;

-- Do these orgs have any other members at all?
SELECT o.Name AS org, om.Role, COUNT(*) n
FROM altomatehr.OrganizationMemberships om
JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId
WHERE om.OrganizationId IN (
      SELECT OrganizationId FROM altomatehr.OrganizationMemberships
      WHERE Role='Owner' GROUP BY 1 HAVING COUNT(*)>1)
GROUP BY o.Name, om.Role ORDER BY o.Name, om.Role;
