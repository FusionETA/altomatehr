SET NAMES utf8mb4 COLLATE utf8mb4_0900_ai_ci;

-- Verification for the employee migration. Every 'bad' must be 0.
SELECT 'users missing for mapped people' chk, COUNT(*) bad FROM altomatehr._mig_peoplemap p
  LEFT JOIN altomatehr.Users u ON u.Id = p.v2_user_id COLLATE utf8mb4_0900_ai_ci WHERE u.Id IS NULL
UNION ALL SELECT 'memberships missing', COUNT(*) FROM hr_prod.EmployeeOrganization e
  JOIN altomatehr._mig_orgmap m ON m.v1_id = e.organizationId
  JOIN altomatehr._mig_peoplemap p ON p.v1_user_id = e.userId
  LEFT JOIN altomatehr.OrganizationMemberships om
    ON om.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
   AND om.UserId = p.v2_user_id COLLATE utf8mb4_0900_ai_ci
  WHERE om.Id IS NULL
UNION ALL SELECT 'profiles missing', COUNT(*) FROM hr_prod.EmployeeOrganization e
  JOIN altomatehr._mig_orgmap m ON m.v1_id = e.organizationId
  JOIN altomatehr._mig_peoplemap p ON p.v1_user_id = e.userId
  LEFT JOIN altomatehr.EmployeeProfiles x
    ON x.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
   AND x.UserId = p.v2_user_id COLLATE utf8mb4_0900_ai_ci
  WHERE x.Id IS NULL
UNION ALL SELECT 'wrong role', COUNT(*) FROM hr_prod.EmployeeOrganization e
  JOIN altomatehr._mig_orgmap m ON m.v1_id = e.organizationId
  JOIN altomatehr._mig_peoplemap p ON p.v1_user_id = e.userId
  JOIN hr_prod.User u ON u.id = e.userId
  JOIN altomatehr.OrganizationMemberships om
    ON om.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
   AND om.UserId = p.v2_user_id COLLATE utf8mb4_0900_ai_ci
  WHERE om.Role <> CASE u.role WHEN 'SUPERVISOR' THEN 'Supervisor' WHEN 'EMPLOYEE' THEN 'Employee'
                               WHEN 'OWNER' THEN 'Owner' ELSE 'Admin' END COLLATE utf8mb4_0900_ai_ci
UNION ALL SELECT 'duplicate emails in Users', COUNT(*) FROM (
    SELECT LOWER(Email) e FROM altomatehr.Users GROUP BY 1 HAVING COUNT(*) > 1) z
UNION ALL SELECT 'orphan membership -> Users', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.Users u ON u.Id = om.UserId WHERE u.Id IS NULL
UNION ALL SELECT 'orphan membership -> Organizations', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.Organizations o ON o.Id = om.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'orphan membership -> EmployeePolicies', COUNT(*) FROM altomatehr.OrganizationMemberships om
  LEFT JOIN altomatehr.EmployeePolicies pol ON pol.Id = om.PolicyId
  WHERE om.PolicyId IS NOT NULL AND pol.Id IS NULL
UNION ALL SELECT 'orphan profile -> Users', COUNT(*) FROM altomatehr.EmployeeProfiles x
  LEFT JOIN altomatehr.Users u ON u.Id = x.UserId WHERE u.Id IS NULL
UNION ALL SELECT 'orphan profile -> Organizations', COUNT(*) FROM altomatehr.EmployeeProfiles x
  LEFT JOIN altomatehr.Organizations o ON o.Id = x.OrganizationId WHERE o.Id IS NULL
UNION ALL SELECT 'profile without a membership', COUNT(*) FROM altomatehr.EmployeeProfiles x
  LEFT JOIN altomatehr.OrganizationMemberships om
    ON om.OrganizationId = x.OrganizationId AND om.UserId = x.UserId
  WHERE om.Id IS NULL;

-- Enum guards. v2 stores enums as strings and EF throws 'Cannot convert string value' on
-- READ, so a bad value sits in the db until something reads it.
SELECT 'bad Gender' chk, COUNT(*) bad FROM altomatehr.EmployeeProfiles WHERE Gender IS NOT NULL AND Gender NOT IN ('MALE','FEMALE')
UNION ALL SELECT 'bad IdType', COUNT(*) FROM altomatehr.EmployeeProfiles WHERE IdType IS NOT NULL AND IdType NOT IN ('NRIC','PASSPORT','ARMY_NO','POLICE_NO')
UNION ALL SELECT 'bad MaritalStatus', COUNT(*) FROM altomatehr.EmployeeProfiles WHERE MaritalStatus IS NOT NULL AND MaritalStatus NOT IN ('SINGLE','MARRIED','DIVORCED','WIDOWED')
UNION ALL SELECT 'bad SocsoScheme', COUNT(*) FROM altomatehr.EmployeeProfiles WHERE SocsoScheme IS NOT NULL AND SocsoScheme NOT IN ('EMPLOYMENT_INJURY_INVALIDITY','EMPLOYMENT_INJURY_ONLY')
UNION ALL SELECT 'bad PaymentMethod', COUNT(*) FROM altomatehr.EmployeeProfiles WHERE PaymentMethod NOT IN ('BANK_TRANSFER','CASH','CHEQUE')
UNION ALL SELECT 'bad SalaryType', COUNT(*) FROM altomatehr.EmployeeProfiles WHERE SalaryType NOT IN ('HOURLY','MONTHLY')
UNION ALL SELECT 'bad membership Role', COUNT(*) FROM altomatehr.OrganizationMemberships WHERE Role NOT IN ('Owner','Admin','Supervisor','Employee');

-- Field-level diff against hr_prod on the numbers that matter most.
SELECT 'salary/statutory field mismatches' chk, COUNT(*) bad
FROM hr_prod.EmployeeOrganization e
JOIN altomatehr._mig_orgmap m ON m.v1_id = e.organizationId
JOIN altomatehr._mig_peoplemap p ON p.v1_user_id = e.userId
JOIN hr_prod.EmployeeProfile ep ON ep.id = e.employeeProfileId
JOIN hr_prod.PayrollProfile pp ON pp.employeeProfileId = ep.id
JOIN altomatehr.EmployeeProfiles x
  ON x.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
 AND x.UserId = p.v2_user_id COLLATE utf8mb4_0900_ai_ci
WHERE NOT (x.MonthlySalary <=> pp.monthlySalary)
   OR NOT (x.HourlyRate <=> pp.hourlyRate)
   OR NOT (x.EpfEmployeeRate <=> pp.epfEmployeeRate)
   OR NOT (x.IdNumber <=> pp.idNumber COLLATE utf8mb4_0900_ai_ci)
   OR NOT (x.BankAccountNumber <=> pp.bankAccountNumber COLLATE utf8mb4_0900_ai_ci)
   OR NOT (x.JoinDate <=> pp.joinDate)
   OR NOT (x.DateOfBirth <=> pp.dateOfBirth);

-- Headcount per org, v2 against hr_prod.
SELECT COUNT(*) AS orgs_with_headcount_drift FROM (
  SELECT m.v2_id,
         (SELECT COUNT(*) FROM hr_prod.EmployeeOrganization e WHERE e.organizationId = m.v1_id) src,
         (SELECT COUNT(*) FROM altomatehr.OrganizationMemberships om
           WHERE om.OrganizationId = m.v2_id COLLATE utf8mb4_0900_ai_ci
             AND om.Role IN ('Supervisor','Employee')) dst
  FROM altomatehr._mig_orgmap m) z
WHERE src <> dst;
