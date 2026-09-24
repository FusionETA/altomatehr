-- Every check must return 0. Anything non-zero means 01-coa.sql did not land cleanly.

SELECT 'rows not loaded' AS check_name, COUNT(*) AS failures
FROM hr_prod.ChartOfAccount c
JOIN altomatehr._mig_orgmap m ON m.v1_id = c.organizationId
LEFT JOIN altomatehr.ChartOfAccounts v2 ON v2.Id = c.id COLLATE utf8mb4_0900_ai_ci
WHERE (c.type IS NULL OR UPPER(c.type) IN ('BANK','EXPENSE','DIRECTCOSTS','OVERHEADS')
       OR UPPER(c.type) LIKE 'EXP%')
  AND v2.Id IS NULL

UNION ALL
-- The excluded liability accounts must NOT be present.
SELECT 'liability rows leaked in', COUNT(*)
FROM hr_prod.ChartOfAccount c
JOIN altomatehr.ChartOfAccounts v2 ON v2.Id = c.id COLLATE utf8mb4_0900_ai_ci
WHERE UPPER(c.type) IN ('CURRLIAB','TERMLIAB','LIABILITY')

UNION ALL
-- Type must be exactly one of the two strings the UI tabs filter on. Anything else
-- loads fine and is then invisible in the app. Scoped to rows this migration wrote --
-- v2 already held one pre-existing row with Type='Expenses' (see README), which is a
-- separate pre-existing defect and not something this migration should mask or fix.
SELECT 'migrated Type outside {EXPENSE,BANK}', COUNT(*)
FROM altomatehr.ChartOfAccounts v2
JOIN altomatehr._mig_orgmap m ON m.v2_id COLLATE utf8mb4_0900_ai_ci = v2.OrganizationId
JOIN hr_prod.ChartOfAccount c ON c.id = v2.Id COLLATE utf8mb4_unicode_ci
WHERE v2.Type NOT IN ('EXPENSE','BANK')

UNION ALL
-- Field-level diff of every migrated row against the source.
SELECT 'field mismatch vs hr_prod', COUNT(*)
FROM hr_prod.ChartOfAccount c
JOIN altomatehr._mig_orgmap m ON m.v1_id = c.organizationId
JOIN altomatehr.ChartOfAccounts v2 ON v2.Id = c.id COLLATE utf8mb4_0900_ai_ci
WHERE v2.OrganizationId    <> m.v2_id COLLATE utf8mb4_0900_ai_ci
   OR v2.Code              <> c.code  COLLATE utf8mb4_0900_ai_ci
   OR v2.Name              <> c.name  COLLATE utf8mb4_0900_ai_ci
   OR v2.Type              <> CASE WHEN UPPER(c.type)='BANK' THEN 'BANK' ELSE 'EXPENSE' END
   OR NOT (v2.XeroAccountId <=> c.xeroAccountId COLLATE utf8mb4_0900_ai_ci)
   OR NOT (v2.XeroStatus    <=> c.status        COLLATE utf8mb4_0900_ai_ci)
   OR v2.IsSelectable      <> c.isSelectable
   OR NOT (v2.LimitAmount   <=> c.limitAmount)
   OR v2.AllowMileageClaim <> c.allowMileageClaim
   OR NOT (v2.MileageRate   <=> c.mileageRate)
   OR v2.IsArchived        <> c.isDisabled

UNION ALL
-- Orphans: an account pointing at an org that does not exist in v2.
SELECT 'orphaned OrganizationId', COUNT(*)
FROM altomatehr.ChartOfAccounts c
LEFT JOIN altomatehr.Organizations o ON o.Id = c.OrganizationId
WHERE o.Id IS NULL

UNION ALL
-- Duplicate code within one org would make the account picker ambiguous. Scoped to the
-- migrated orgs: v2 already held two such pairs in orgs this migration never touches
-- (see README). hr_prod itself has none.
SELECT 'duplicate Code within a migrated org', COUNT(*) FROM (
  SELECT v2.OrganizationId, v2.Code FROM altomatehr.ChartOfAccounts v2
  JOIN altomatehr._mig_orgmap m ON m.v2_id COLLATE utf8mb4_0900_ai_ci = v2.OrganizationId
  GROUP BY v2.OrganizationId, v2.Code HAVING COUNT(*) > 1
) d;

-- Informational: what each org ends up with, and how it splits across the two tabs.
SELECT m.name AS org,
       SUM(v2.Type='EXPENSE') AS expense_tab,
       SUM(v2.Type='BANK')    AS bank_tab,
       SUM(v2.IsSelectable)   AS claimable,
       SUM(v2.IsArchived)     AS archived,
       COUNT(*)               AS total
FROM altomatehr.ChartOfAccounts v2
JOIN altomatehr._mig_orgmap m ON m.v2_id COLLATE utf8mb4_0900_ai_ci = v2.OrganizationId
GROUP BY m.name ORDER BY total DESC;
