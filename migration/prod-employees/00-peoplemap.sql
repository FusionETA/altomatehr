-- Identity map for the remaining hr_prod people: SUPERVISOR and EMPLOYEE accounts that
-- belong to the 42 migrated orgs. Admins were handled by ../prod-admins/.
--
-- Unlike admins, employees ARE attached through EmployeeOrganization (userId,
-- employeeProfileId, organizationId), and every one of them has a real EmployeeProfile.
--
-- Id rules, in priority order. v2 Users.Email is UNIQUE globally and v1's is NOT, so
-- email always wins over id:
--   1. email already in altomatehr.Users            -> reuse that account, no insert
--   2. several v1 User rows share one email         -> collapse onto the EARLIEST row
--                                                      (v1 gives a person one User row per
--                                                      org; v2 gives one account + N
--                                                      memberships)
--   3. id already in altomatehr.Users, other email  -> cuid collision, prefix 'prod-'
--   4. otherwise                                    -> carry the v1 cuid verbatim

DROP TABLE IF EXISTS altomatehr._mig_peoplemap;
CREATE TABLE altomatehr._mig_peoplemap (
  v1_user_id varchar(191) NOT NULL PRIMARY KEY,
  v2_user_id varchar(191) NOT NULL,
  email      varchar(191) NOT NULL,
  canonical  tinyint      NOT NULL DEFAULT 1,  -- 0 = collapsed onto another v1 row (rule 2)
  reuse      tinyint      NOT NULL DEFAULT 0,  -- 1 = pre-existing v2 account, no Users insert
  remapped   tinyint      NOT NULL DEFAULT 0,  -- 1 = id prefixed because of a cuid collision
  KEY (v2_user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Everyone in scope: a member of one of the 42 orgs, not already migrated as an admin.
CREATE TEMPORARY TABLE altomatehr._scope (
  user_id varchar(191) NOT NULL PRIMARY KEY,
  email   varchar(191) NOT NULL,
  created datetime(3)  NOT NULL
) COLLATE utf8mb4_unicode_ci;
INSERT INTO altomatehr._scope
SELECT DISTINCT u.id, u.email, u.createdAt
FROM hr_prod.EmployeeOrganization e
JOIN altomatehr._mig_orgmap m ON m.v1_id = e.organizationId
JOIN hr_prod.User u ON u.id = e.userId
WHERE u.id NOT IN (SELECT v1_user_id FROM altomatehr._mig_adminmap);

-- Rule 2: the canonical v1 row per email is the earliest created (id breaks ties).
CREATE TEMPORARY TABLE altomatehr._canon (
  email   varchar(191) NOT NULL PRIMARY KEY,
  user_id varchar(191) NOT NULL
) COLLATE utf8mb4_unicode_ci;
INSERT INTO altomatehr._canon
SELECT LOWER(s.email),
       SUBSTRING_INDEX(GROUP_CONCAT(s.user_id ORDER BY s.created, s.user_id), ',', 1)
FROM altomatehr._scope s GROUP BY LOWER(s.email);

INSERT INTO altomatehr._mig_peoplemap
  (v1_user_id, v2_user_id, email, canonical, reuse, remapped)
SELECT
  s.user_id,
  COALESCE(
    be.Id COLLATE utf8mb4_unicode_ci,                                    -- rule 1
    IF(bi.Id IS NULL, c.user_id, CONCAT('prod-', c.user_id))),           -- rules 3 / 4
  s.email,
  (c.user_id = s.user_id),
  be.Id IS NOT NULL,
  (be.Id IS NULL AND bi.Id IS NOT NULL)
FROM altomatehr._scope s
JOIN altomatehr._canon c ON c.email = LOWER(s.email)
LEFT JOIN altomatehr.Users be ON LOWER(be.Email) = LOWER(s.email) COLLATE utf8mb4_0900_ai_ci
LEFT JOIN altomatehr.Users bi ON bi.Id = c.user_id COLLATE utf8mb4_0900_ai_ci;

SELECT COUNT(*) AS v1_rows, COUNT(DISTINCT v2_user_id) AS v2_accounts,
       SUM(reuse) AS reused, SUM(remapped) AS id_prefixed,
       SUM(canonical = 0) AS collapsed_duplicates
FROM altomatehr._mig_peoplemap;
