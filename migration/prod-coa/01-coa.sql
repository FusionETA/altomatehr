-- hr_prod.ChartOfAccount -> altomatehr.ChartOfAccounts
--
-- Chart of accounts for the 42 orgs already mapped by ../prod-settings/00-orgmap.sql.
-- Requires altomatehr._mig_orgmap to exist (that migration builds it).
--
-- NO Xero tokens are touched. This is display/reference data only: the accounts
-- claims are coded to. Each org still has to reconnect Xero to resume syncing.
--
-- Every write is a PK upsert, so re-running is safe.

-- Precheck. An empty org map makes the INSERT a silent no-op rather than an error,
-- so read this line before trusting the run; 02-verify.sql asserts the counts anyway.
SELECT IF(COUNT(*) = 0,
          'ABORT: _mig_orgmap is empty - run ../prod-settings/00-orgmap.sql first',
          CONCAT('orgmap ok: ', COUNT(*), ' orgs')) AS precheck
FROM altomatehr._mig_orgmap;

INSERT INTO altomatehr.ChartOfAccounts
  (Id, OrganizationId, Code, Name, Type, XeroAccountId, XeroStatus, XeroSyncedAt,
   IsSelectable, LimitAmount, AllowMileageClaim, MileageRate, IsArchived, CreatedAt)
SELECT
  c.id,
  m.v2_id,
  c.code,
  c.name,
  -- v2 only models BANK and EXPENSE (ChartOfAccount.Type, and the UI's two tabs in
  -- AccountsSettings.tsx filter on an exact string match). Collapse v1's raw Xero
  -- types the same way v2's own importer does -- XeroService.ToLocalAccountType():
  -- bank is bank, every other importable type is an expense family type.
  -- NULL type = an account created by hand in the app, never synced: those are expenses.
  CASE WHEN UPPER(c.type) = 'BANK' THEN 'BANK' ELSE 'EXPENSE' END,
  c.xeroAccountId,
  c.status,
  -- v1 has no sync timestamp. For Xero-sourced rows updatedAt IS the last sync write;
  -- hand-made accounts have never synced, so they stay NULL.
  CASE WHEN c.xeroAccountId IS NOT NULL THEN c.updatedAt END,
  c.isSelectable,
  c.limitAmount,
  c.allowMileageClaim,
  c.mileageRate,
  c.isDisabled,          -- v1 `isDisabled` is v2 `IsArchived` (same rename as prod-settings)
  c.createdAt
FROM hr_prod.ChartOfAccount c
JOIN altomatehr._mig_orgmap m ON m.v1_id = c.organizationId
-- Skip what v2 has no home for. XeroService.ShouldImportAccount() imports only
-- claimable types (EXPENSE/DIRECTCOSTS/OVERHEADS) plus BANK; a v2 org that
-- reconnects Xero would never receive CURRLIAB/TERMLIAB/LIABILITY. Carrying them
-- over would put 497 rows in the database that no tab can show and that would
-- diverge from the org's next sync. All 497 are isSelectable=0 in v1 and have
-- zero claims filed against them, so nothing is lost.
WHERE c.type IS NULL
   OR UPPER(c.type) IN ('BANK', 'EXPENSE', 'DIRECTCOSTS', 'OVERHEADS')
   OR UPPER(c.type) LIKE 'EXP%'      -- 'Expenses', 'Exp': hand-typed custom accounts
ON DUPLICATE KEY UPDATE
  OrganizationId    = VALUES(OrganizationId),
  Code              = VALUES(Code),
  Name              = VALUES(Name),
  Type              = VALUES(Type),
  XeroAccountId     = VALUES(XeroAccountId),
  XeroStatus        = VALUES(XeroStatus),
  XeroSyncedAt      = VALUES(XeroSyncedAt),
  IsSelectable      = VALUES(IsSelectable),
  LimitAmount       = VALUES(LimitAmount),
  AllowMileageClaim = VALUES(AllowMileageClaim),
  MileageRate       = VALUES(MileageRate),
  IsArchived        = VALUES(IsArchived),
  CreatedAt         = VALUES(CreatedAt);

SELECT ROW_COUNT() AS rows_affected;
