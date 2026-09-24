-- Same checks as ../prod-settings/03-verify.sql, scoped to _mig_orgmap_keyorgs. All must return 0.
--
SELECT 'orphan Teams.ProjectId' chk, COUNT(*) bad FROM altomatehr.Teams t
  LEFT JOIN altomatehr.Projects p ON p.Id = t.ProjectId WHERE p.Id IS NULL
UNION ALL SELECT 'orphan Teams.OrganizationId', COUNT(*) FROM altomatehr.Teams t
  LEFT JOIN altomatehr.Organizations o ON o.Id = t.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'orphan LeaveTypes.OrganizationId', COUNT(*) FROM altomatehr.LeaveTypes x
  LEFT JOIN altomatehr.Organizations o ON o.Id = x.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'orphan Projects.OrganizationId', COUNT(*) FROM altomatehr.Projects x
  LEFT JOIN altomatehr.Organizations o ON o.Id = x.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'orphan EmployeePolicies.OrganizationId', COUNT(*) FROM altomatehr.EmployeePolicies x
  LEFT JOIN altomatehr.Organizations o ON o.Id = x.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'orphan PLE.PolicyId', COUNT(*) FROM altomatehr.PolicyLeaveEntitlements e
  LEFT JOIN altomatehr.EmployeePolicies p ON p.Id = e.PolicyId WHERE p.Id IS NULL
UNION ALL SELECT 'orphan PLE.LeaveTypeId', COUNT(*) FROM altomatehr.PolicyLeaveEntitlements e
  LEFT JOIN altomatehr.LeaveTypes l ON l.Id = e.LeaveTypeId WHERE l.Id IS NULL
UNION ALL SELECT 'orphan PayrollSettings.OrganizationId', COUNT(*) FROM altomatehr.PayrollSettings x
  LEFT JOIN altomatehr.Organizations o ON o.Id = x.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'orphan PayrollCompanyInfos.OrganizationId', COUNT(*) FROM altomatehr.PayrollCompanyInfos x
  LEFT JOIN altomatehr.Organizations o ON o.Id = x.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'org name mismatch vs hr_prod', COUNT(*) FROM altomatehr._mig_orgmap_keyorgs m
  JOIN altomatehr.Organizations o ON o.Id = m.v2_id COLLATE utf8mb4_0900_ai_ci
  WHERE o.Name <> m.name COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'bad Addons format', COUNT(*) FROM altomatehr.Organizations o
  JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = o.Id COLLATE utf8mb4_unicode_ci
  WHERE o.Addons NOT IN ('', 'expense_claim,clock', 'clock,expense_claim')
UNION ALL SELECT 'leavetype count drift', COUNT(*) FROM (
    SELECT m.v2_id id, COUNT(*) n FROM hr_prod.LeaveType t JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = t.organizationId GROUP BY 1) src
  JOIN (SELECT OrganizationId id, COUNT(*) n FROM altomatehr.LeaveTypes GROUP BY 1) dst
    ON dst.id = src.id COLLATE utf8mb4_0900_ai_ci AND dst.n <> src.n
UNION ALL SELECT 'project count drift', COUNT(*) FROM (
    SELECT m.v2_id id, COUNT(*) n FROM hr_prod.XeroProject x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId GROUP BY 1) src
  JOIN (SELECT OrganizationId id, COUNT(*) n FROM altomatehr.Projects GROUP BY 1) dst
    ON dst.id = src.id COLLATE utf8mb4_0900_ai_ci AND dst.n <> src.n;

-- Enum guards. v2 stores enums as strings and EF throws on read if a value is not a
-- member of the C# enum, so every migrated enum column is checked here. This is how
-- 'MONTHLY_BASED' (v1's spelling of SalaryType.MONTHLY) was caught on 2026-09-18.
SELECT 'bad SalaryType' chk, COUNT(*) bad FROM altomatehr.EmployeePolicies WHERE SalaryType NOT IN ('HOURLY','MONTHLY')
UNION ALL SELECT 'bad OtMethod', COUNT(*) FROM altomatehr.EmployeePolicies WHERE OtMethod NOT IN ('CASH','TIME_BANK')
UNION ALL SELECT 'bad LeaveTypes.AccrualMethod', COUNT(*) FROM altomatehr.LeaveTypes WHERE AccrualMethod NOT IN ('LUMP_SUM','PRO_RATED')
UNION ALL SELECT 'bad PLE.AccrualMethod', COUNT(*) FROM altomatehr.PolicyLeaveEntitlements WHERE AccrualMethod IS NOT NULL AND AccrualMethod NOT IN ('LUMP_SUM','PRO_RATED')
UNION ALL SELECT 'bad WorkingDaysRule', COUNT(*) FROM altomatehr.PayrollSettings WHERE WorkingDaysRule NOT IN ('CALENDAR','TWENTY_SIX')
UNION ALL SELECT 'bad Plan', COUNT(*) FROM altomatehr.Organizations WHERE Plan NOT IN ('DIY','EXPERT')
UNION ALL SELECT 'bad Tier', COUNT(*) FROM altomatehr.Organizations WHERE Tier IS NOT NULL AND Tier NOT IN ('FREE','PAID')
UNION ALL SELECT 'bad MileageUnit', COUNT(*) FROM altomatehr.Organizations WHERE MileageUnit NOT IN ('KM','MILE')
UNION ALL SELECT 'bad ClaimSettlementRoute', COUNT(*) FROM altomatehr.Organizations WHERE ClaimSettlementRoute NOT IN ('XERO_BILL','PAYROLL')
UNION ALL SELECT 'bad XeroBillStage', COUNT(*) FROM altomatehr.Organizations WHERE XeroBillStage NOT IN ('Draft','AwaitingApproval','AwaitingPayment');

-- Row counts for THIS migration's orgs: source (hr_prod) minus loaded (v2). All must be 0.
-- The global orphan checks above also count pre-existing debris: one PayrollSettings
-- and one PayrollCompanyInfos row for 'org-phase2-verify' (see ../prod-settings/README.md).
SELECT 'Organizations shortfall' chk,
       (SELECT COUNT(*) FROM altomatehr._mig_orgmap_keyorgs)
     - (SELECT COUNT(*) FROM altomatehr.Organizations o JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = o.Id COLLATE utf8mb4_unicode_ci) bad
UNION ALL SELECT 'LeaveTypes shortfall',
       (SELECT COUNT(*) FROM hr_prod.LeaveType x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId)
     - (SELECT COUNT(*) FROM altomatehr.LeaveTypes x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = x.OrganizationId COLLATE utf8mb4_unicode_ci)
UNION ALL SELECT 'Projects shortfall',
       (SELECT COUNT(*) FROM hr_prod.XeroProject x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId)
     - (SELECT COUNT(*) FROM altomatehr.Projects x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = x.OrganizationId COLLATE utf8mb4_unicode_ci)
UNION ALL SELECT 'Teams shortfall',
       (SELECT COUNT(*) FROM hr_prod.Team t JOIN hr_prod.XeroProject x ON x.id = t.projectId JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId)
     - (SELECT COUNT(*) FROM altomatehr.Teams x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = x.OrganizationId COLLATE utf8mb4_unicode_ci)
UNION ALL SELECT 'EmployeePolicies shortfall',
       (SELECT COUNT(*) FROM hr_prod.EmployeePolicy x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId)
     - (SELECT COUNT(*) FROM altomatehr.EmployeePolicies x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = x.OrganizationId COLLATE utf8mb4_unicode_ci)
UNION ALL SELECT 'PayrollSettings shortfall',
       (SELECT COUNT(*) FROM hr_prod.PayrollSettings x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId)
     - (SELECT COUNT(*) FROM altomatehr.PayrollSettings x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = x.OrganizationId COLLATE utf8mb4_unicode_ci)
UNION ALL SELECT 'PayrollCompanyInfos shortfall',
       (SELECT COUNT(*) FROM hr_prod.PayrollCompanyInfo x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v1_id = x.organizationId)
     - (SELECT COUNT(*) FROM altomatehr.PayrollCompanyInfos x JOIN altomatehr._mig_orgmap_keyorgs m ON m.v2_id = x.OrganizationId COLLATE utf8mb4_unicode_ci);
