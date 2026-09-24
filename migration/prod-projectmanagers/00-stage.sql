-- hr_prod -> altomatehr : project managers, STAGING ONLY
--
-- Already run 2026-09-18. Builds altomatehr._mig_projectmanagers: the fully
-- resolved v1->v2 mapping for every project-manager assignment. Idempotent.
--
-- This stages the mapping ONLY. It does not write to Projects, because v2 has
-- no project-manager column or table yet. See 01-load.sql.PENDING.
--
-- Depends on _mig_orgmap (../prod-settings/00-orgmap.sql) and
-- _mig_peoplemap (../prod-employees/00-peoplemap.sql).
--
-- Collation note: altomatehr is utf8mb4_0900_ai_ci, hr_prod is
-- utf8mb4_unicode_ci. Every cross-database join needs an explicit COLLATE or
-- MySQL throws "Illegal mix of collations".

CREATE TABLE IF NOT EXISTS altomatehr._mig_projectmanagers (
  v1_project_id varchar(191) NOT NULL,
  v1_user_id    varchar(191) NOT NULL,
  v2_project_id varchar(191) NOT NULL,
  v2_user_id    varchar(191) NOT NULL,
  manager_name  varchar(191) NOT NULL,
  manager_email varchar(191) NOT NULL,
  project_name  varchar(191) NOT NULL,
  PRIMARY KEY (v1_project_id, v1_user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO altomatehr._mig_projectmanagers
  (v1_project_id, v1_user_id, v2_project_id, v2_user_id, manager_name, manager_email, project_name)
SELECT pm.projectId, pm.userId,
       IF(om.remapped, CONCAT('prod-', pm.projectId), pm.projectId),
       pmap.v2_user_id, u.name, u.email, xp.name
FROM hr_prod.ProjectManager pm
JOIN hr_prod.XeroProject xp ON xp.id = pm.projectId
JOIN hr_prod.User u ON u.id = pm.userId
JOIN altomatehr._mig_orgmap om    ON om.v1_id      COLLATE utf8mb4_unicode_ci = xp.organizationId COLLATE utf8mb4_unicode_ci
JOIN altomatehr._mig_peoplemap pmap ON pmap.v1_user_id COLLATE utf8mb4_unicode_ci = pm.userId      COLLATE utf8mb4_unicode_ci
ON DUPLICATE KEY UPDATE
  v2_project_id = VALUES(v2_project_id), v2_user_id = VALUES(v2_user_id),
  manager_name = VALUES(manager_name), manager_email = VALUES(manager_email),
  project_name = VALUES(project_name);

-- Verify: all three must be 0 except the first, which must be 84.
SELECT 'staged rows (expect 84)' AS check_, COUNT(*) AS n FROM altomatehr._mig_projectmanagers
UNION ALL SELECT 'project missing in v2 (expect 0)', COUNT(*)
  FROM altomatehr._mig_projectmanagers m LEFT JOIN altomatehr.Projects p ON p.Id = m.v2_project_id
  WHERE p.Id IS NULL
UNION ALL SELECT 'manager missing in v2 (expect 0)', COUNT(*)
  FROM altomatehr._mig_projectmanagers m LEFT JOIN altomatehr.Users u ON u.Id = m.v2_user_id
  WHERE u.Id IS NULL;
