-- Undoes 01-admins.sql exactly. Removes only rows this migration created:
-- memberships keyed by the mapped (org, user) pairs, and identity rows for admins that
-- did NOT reuse a pre-existing v2 account. Accounts under reuse=1 are left alone -- they
-- existed before this ran.
DELETE om FROM altomatehr.OrganizationMemberships om
JOIN altomatehr._mig_adminmap a
  ON a.v2_user_id COLLATE utf8mb4_0900_ai_ci = om.UserId
 AND a.v2_org     COLLATE utf8mb4_0900_ai_ci = om.OrganizationId;

DELETE u FROM altomatehr.Users u
JOIN altomatehr._mig_adminmap a ON a.v2_user_id COLLATE utf8mb4_0900_ai_ci = u.Id
WHERE a.reuse = 0;
