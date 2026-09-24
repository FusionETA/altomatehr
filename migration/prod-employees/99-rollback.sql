-- Undoes 01-people.sql and 02-profiles.sql exactly. Removes only rows this migration
-- created: profiles and memberships for the mapped (org, user) pairs, then the identity
-- rows. Accounts under reuse = 1 existed before this ran and are left alone, as are the
-- admin accounts from ../prod-admins/.
DELETE x FROM altomatehr.EmployeeProfiles x
JOIN altomatehr._mig_peoplemap p ON p.v2_user_id COLLATE utf8mb4_0900_ai_ci = x.UserId
JOIN altomatehr._mig_orgmap m    ON m.v2_id      COLLATE utf8mb4_0900_ai_ci = x.OrganizationId;

DELETE om FROM altomatehr.OrganizationMemberships om
JOIN altomatehr._mig_peoplemap p ON p.v2_user_id COLLATE utf8mb4_0900_ai_ci = om.UserId
JOIN altomatehr._mig_orgmap m    ON m.v2_id      COLLATE utf8mb4_0900_ai_ci = om.OrganizationId;

DELETE u FROM altomatehr.Users u
JOIN altomatehr._mig_peoplemap p ON p.v2_user_id COLLATE utf8mb4_0900_ai_ci = u.Id
WHERE p.reuse = 0;
