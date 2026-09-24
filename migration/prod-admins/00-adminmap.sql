-- Builds altomatehr._mig_adminmap: the v1->v2 identity map for OWNER/ADMIN accounts.
--
-- Scope: hr_prod.User rows with role OWNER or ADMIN whose organizationId is one of the 42
-- orgs the settings migration loaded (altomatehr._mig_orgmap). v1 admins have NO
-- EmployeeOrganization row and no EmployeeProfile -- they are attached to their org by
-- User.organizationId alone -- so that column, not EmployeeOrganization, defines the scope.
--
-- Id rules, in priority order. v2 Users.Email is UNIQUE globally, so email wins over id:
--   1. email already in altomatehr.Users -> REUSE that account's id, insert no user row.
--   2. id already in altomatehr.Users under a different email -> collision, prefix 'prod-'.
--   3. otherwise -> carry the v1 cuid verbatim.
-- Re-running after a successful load re-resolves everyone through rule 1 to the same ids,
-- which is what keeps the whole thing idempotent.

DROP TABLE IF EXISTS altomatehr._mig_adminmap;
CREATE TABLE altomatehr._mig_adminmap (
  v1_user_id varchar(191) NOT NULL PRIMARY KEY,
  v2_user_id varchar(191) NOT NULL,
  email      varchar(191) NOT NULL,
  v1_org     varchar(191) NOT NULL,
  v2_org     varchar(191) NOT NULL,
  org_name   varchar(191) NOT NULL,
  v2_role    varchar(20)  NOT NULL,
  reuse      tinyint      NOT NULL DEFAULT 0,  -- 1 = existing v2 account, no Users insert
  remapped   tinyint      NOT NULL DEFAULT 0   -- 1 = id prefixed because of a cuid collision
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO altomatehr._mig_adminmap
  (v1_user_id, v2_user_id, email, v1_org, v2_org, org_name, v2_role, reuse, remapped)
SELECT
  u.id,
  COALESCE(be.Id COLLATE utf8mb4_unicode_ci,
           CASE WHEN bi.Id IS NOT NULL THEN CONCAT('prod-', u.id) ELSE u.id END),
  u.email,
  u.organizationId,
  m.v2_id,
  m.name,
  CASE u.role WHEN 'OWNER' THEN 'Owner' ELSE 'Admin' END,
  be.Id IS NOT NULL,
  (be.Id IS NULL AND bi.Id IS NOT NULL)
FROM hr_prod.User u
JOIN altomatehr._mig_orgmap m ON m.v1_id = u.organizationId
LEFT JOIN altomatehr.Users be ON LOWER(be.Email) = LOWER(u.email) COLLATE utf8mb4_0900_ai_ci
LEFT JOIN altomatehr.Users bi ON bi.Id = u.id COLLATE utf8mb4_0900_ai_ci
WHERE u.role IN ('OWNER','ADMIN');

SELECT COUNT(*) AS mapped, SUM(reuse) AS reused_existing, SUM(remapped) AS id_prefixed,
       COUNT(DISTINCT v2_org) AS orgs_covered
FROM altomatehr._mig_adminmap;
