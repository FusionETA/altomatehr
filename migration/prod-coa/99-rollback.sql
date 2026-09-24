-- Removes exactly what 01-coa.sql wrote: rows whose id came from hr_prod.ChartOfAccount
-- AND whose org is in the migration scope. Pre-existing v2 accounts (Fusioneta Sdn Bhd,
-- ZR TEST, Oscar Test Org) are matched by neither and are left alone.
DELETE v2 FROM altomatehr.ChartOfAccounts v2
JOIN altomatehr._mig_orgmap m ON m.v2_id COLLATE utf8mb4_0900_ai_ci = v2.OrganizationId
JOIN hr_prod.ChartOfAccount c ON c.id = v2.Id COLLATE utf8mb4_unicode_ci
                             AND c.organizationId = m.v1_id;
SELECT ROW_COUNT() AS rows_deleted;
