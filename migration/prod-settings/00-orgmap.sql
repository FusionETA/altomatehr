-- Builds the org scope + id map for the hr_prod -> altomatehr settings migration.
-- Scope: every hr_prod Organization that has at least one member (42 as of 2026-09-18).
-- Remap rule: if a prod org id already exists in v2 under a DIFFERENT name, it is a
-- cuid collision between the dev- and prod-sourced lineages (dev and prod share 15 org
-- ids). Those rows get a deterministic 'prod-' prefix so nothing existing is overwritten.
-- Matching names mean the row is one WE wrote on an earlier run, so it keeps its id and
-- the migration stays idempotent.
DROP TABLE IF EXISTS altomatehr._mig_orgmap;
CREATE TABLE altomatehr._mig_orgmap (
  v1_id    VARCHAR(40)  NOT NULL PRIMARY KEY,
  v2_id    VARCHAR(40)  NOT NULL,
  name     VARCHAR(191) NOT NULL,
  remapped TINYINT(1)   NOT NULL DEFAULT 0
) COLLATE utf8mb4_unicode_ci;

INSERT INTO altomatehr._mig_orgmap (v1_id, v2_id, name, remapped)
SELECT o.id,
       IF(c.Id IS NULL, o.id, CONCAT('prod-', o.id)),
       o.name,
       IF(c.Id IS NULL, 0, 1)
FROM hr_prod.Organization o
LEFT JOIN altomatehr.Organizations c
       ON c.Id = o.id COLLATE utf8mb4_0900_ai_ci
      AND c.Name <> o.name COLLATE utf8mb4_0900_ai_ci
WHERE (SELECT COUNT(*) FROM hr_prod.EmployeeOrganization e WHERE e.organizationId = o.id) > 0;
