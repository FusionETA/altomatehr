-- hr_prod -> altomatehr : claims
--
-- Depends on ../prod-settings (orgmap, Projects), ../prod-employees (peoplemap),
-- ../prod-coa (ChartOfAccounts). Same conflict policy as 01: insert only where
-- the unique key is free, never update or delete an existing v2 row.
--
-- ID CONVENTIONS -- these differ per table and getting them wrong silently
-- orphans a reference:
--   * Projects  ids ARE prefixed 'prod-' for the one remapped org (Fusion ETA),
--     because ../prod-settings wrote them that way.
--   * ChartOfAccounts ids are NOT prefixed -- ../prod-coa/01-coa.sql inserts
--     `c.id` raw.
--   * Claim ids are kept raw too, matching the 10 claims already in v2.
--
-- STATUS: v1's REVIEWED has no v2 member (v2's ClaimStatus is
-- SUBMITTED|PENDING|APPROVED|REJECTED) and maps to APPROVED -- the same mapping
-- the 10 already-loaded rows used. v2 stores enums as strings and throws
-- `Cannot convert string value` on READ, so an unmapped spelling would load
-- fine and then break the claims screen.

SELECT IF((SELECT COUNT(*) FROM altomatehr.ChartOfAccounts) = 0,
          'ABORT: run ../prod-coa first', 'deps ok') AS precheck;

INSERT INTO altomatehr.Claims
  (Id, ClaimNumber, Title, Description, Category, Amount, Currency, SpentAt, SubmittedAt,
   Status, ClaimType, PaymentType, EmployeeId, ReceiptUrl, ReviewNotes, CreatedAt, UpdatedAt,
   OrganizationId, ChartOfAccountId, ExceedsLimit, ProjectId, CurrentStep, Distance,
   MileageDestinationAddress, MileageOriginAddress, MileageRateUsed, MileageUnitUsed,
   PayViaAccountId, SpendingAt, SpendingWith, SupportingDocumentUrls,
   XeroBillId, XeroBillRef, XeroSyncError, XeroSyncStatus, XeroSyncedAt, Settlement)
SELECT
  c.id, c.claimNumber, LEFT(c.title, 200), c.description, c.category, c.amount, c.currency,
  c.spentAt, c.submittedAt,
  CASE WHEN c.status = 'REVIEWED' THEN 'APPROVED' ELSE c.status END,
  c.claimType, c.paymentType, pm.v2_user_id, LEFT(c.receiptUrl, 1000), c.reviewNotes,
  c.createdAt, c.updatedAt, om.v2_id,
  c.chartOfAccountId,                      -- raw: prod-coa did not prefix
  c.exceedsLimit,
  CASE WHEN c.projectId IS NULL THEN NULL
       ELSE IF(pom.remapped, CONCAT('prod-', c.projectId), c.projectId) END,
  0,                                       -- CurrentStep: v1 has no equivalent
  c.distance, c.mileageDestinationAddress, c.mileageOriginAddress,
  c.mileageRateUsed, c.mileageUnitUsed,
  c.payViaAccountId,                       -- raw, same as ChartOfAccountId
  LEFT(c.spendingAt, 200), LEFT(c.spendingWith, 200),
  -- v1 keeps extra files in their own table; v2 keeps a JSON array of urls on
  -- the claim. The primary receipt stays on ReceiptUrl in both.
  (SELECT JSON_ARRAYAGG(sa.fileUrl) FROM hr_prod.ClaimSupportingAttachment sa
    WHERE sa.claimId = c.id),
  c.xeroBillId, c.xeroBillRef, c.xeroSyncError, c.xeroSyncStatus, c.xeroSyncedAt,
  'XERO_BILL'                              -- v1 has no settlement concept
FROM hr_prod.Claim c
JOIN altomatehr._mig_peoplemap pm
  ON pm.v1_user_id COLLATE utf8mb4_unicode_ci = c.employeeId
JOIN altomatehr._mig_orgmap om
  ON om.v1_id COLLATE utf8mb4_unicode_ci = c.organizationId
LEFT JOIN hr_prod.XeroProject xp ON xp.id = c.projectId
LEFT JOIN altomatehr._mig_orgmap pom
  ON pom.v1_id COLLATE utf8mb4_unicode_ci = xp.organizationId
-- ClaimNumber and XeroBillId are both UNIQUE in v2. 10 claims share a
-- ClaimNumber with a row already loaded from hr_dev (ZR TEST) -- the two
-- databases are forks of one ancestor, so the same claim exists in both with
-- the same id. Those 10 are skipped; 04-verify.sql lists them.
WHERE NOT EXISTS (
  SELECT 1 FROM altomatehr.Claims v2
   WHERE v2.ClaimNumber COLLATE utf8mb4_unicode_ci = c.claimNumber)
AND NOT EXISTS (
  SELECT 1 FROM altomatehr.Claims v3
   WHERE v3.Id COLLATE utf8mb4_unicode_ci = c.id)
AND (c.xeroBillId IS NULL OR NOT EXISTS (
  SELECT 1 FROM altomatehr.Claims v4
   WHERE v4.XeroBillId COLLATE utf8mb4_unicode_ci = c.xeroBillId));
