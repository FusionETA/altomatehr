-- Builds the org scope + id map for the hr_prod -> altomatehr key-holder migration.
--
-- Scope: every hr_prod Organization that holds an ACTIVE ApiIntegration key and has
-- no row in v2 yet (26 as of 2026-09-24). These are the companies New-Altomate
-- provisioned through v1 but that the 2026-09-18 settings migration skipped because
-- they had no members — once New-Altomate points at v2, their keys must resolve to a
-- real v2 org.
--
-- The map is FROZEN on first run: CREATE IF NOT EXISTS + INSERT IGNORE. After 02 has
-- run, the orgs exist in v2 and would drop out of a recomputed scope, which would make
-- 99-rollback delete nothing. Re-running this only adds companies that appeared since.
--
-- Separate table from ../prod-settings' _mig_orgmap on purpose: that one is what its
-- own 99-rollback deletes by, and must not grow.
CREATE TABLE IF NOT EXISTS altomatehr._mig_orgmap_keyorgs (
  v1_id    VARCHAR(40)  NOT NULL PRIMARY KEY,
  v2_id    VARCHAR(40)  NOT NULL,
  name     VARCHAR(191) NOT NULL,
  remapped TINYINT(1)   NOT NULL DEFAULT 0
) COLLATE utf8mb4_unicode_ci;

-- An id that is absent from v2 cannot be a cuid collision, so nothing here is remapped;
-- the column is kept so 02-settings.sql is the same script as ../prod-settings.
INSERT IGNORE INTO altomatehr._mig_orgmap_keyorgs (v1_id, v2_id, name, remapped)
SELECT o.id, o.id, o.name, 0
FROM hr_prod.Organization o
WHERE EXISTS (SELECT 1 FROM hr_prod.ApiIntegration a WHERE a.organizationId = o.id AND a.active = 1)
  AND o.id COLLATE utf8mb4_0900_ai_ci NOT IN (SELECT Id FROM altomatehr.Organizations);
