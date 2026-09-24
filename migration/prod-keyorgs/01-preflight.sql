-- Run BEFORE 02. Every row must return 0.
--
-- dev and prod share cuids, and 02 upserts by primary key. A child id that already
-- exists in v2 under some other org would have its fields overwritten by this
-- company's settings. The org ids are known clean (00 only takes ids absent from v2);
-- this checks the children. 0 across the board on 2026-09-24.
SELECT 'LeaveTypes id clash' chk, COUNT(*) bad FROM hr_prod.LeaveType x
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId
  JOIN altomatehr.LeaveTypes d ON d.Id = x.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'Projects id clash', COUNT(*) FROM hr_prod.XeroProject x
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId
  JOIN altomatehr.Projects d ON d.Id = x.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'Teams id clash', COUNT(*) FROM hr_prod.Team t
  JOIN hr_prod.XeroProject x ON x.id = t.projectId
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId
  JOIN altomatehr.Teams d ON d.Id = t.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'EmployeePolicies id clash', COUNT(*) FROM hr_prod.EmployeePolicy x
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId
  JOIN altomatehr.EmployeePolicies d ON d.Id = x.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'PolicyLeaveEntitlements id clash', COUNT(*) FROM hr_prod.PolicyLeaveEntitlement e
  JOIN hr_prod.EmployeePolicy p ON p.id = e.policyId
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = p.organizationId
  JOIN altomatehr.PolicyLeaveEntitlements d ON d.Id = e.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'PayrollSettings id clash', COUNT(*) FROM hr_prod.PayrollSettings x
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId
  JOIN altomatehr.PayrollSettings d ON d.Id = x.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'PayrollCompanyInfos id clash', COUNT(*) FROM hr_prod.PayrollCompanyInfo x
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId
  JOIN altomatehr.PayrollCompanyInfos d ON d.Id = x.id COLLATE utf8mb4_0900_ai_ci
  WHERE d.OrganizationId <> m.v2_id COLLATE utf8mb4_0900_ai_ci
-- PayrollSettings / PayrollCompanyInfos are one row per org (UNIQUE OrganizationId):
-- a v2 row for the org under a different id would make the insert fail.
UNION ALL SELECT 'PayrollSettings already exists for org', COUNT(*) FROM altomatehr.PayrollSettings d
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = d.OrganizationId COLLATE utf8mb4_unicode_ci
  JOIN hr_prod.PayrollSettings x ON x.organizationId = m.v1_id
  WHERE d.Id <> x.id COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'PayrollCompanyInfos already exists for org', COUNT(*) FROM altomatehr.PayrollCompanyInfos d
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = d.OrganizationId COLLATE utf8mb4_unicode_ci
  JOIN hr_prod.PayrollCompanyInfo x ON x.organizationId = m.v1_id
  WHERE d.Id <> x.id COLLATE utf8mb4_0900_ai_ci;
