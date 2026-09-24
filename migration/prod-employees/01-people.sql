-- Loads the mapped SUPERVISOR/EMPLOYEE accounts and their memberships. Requires
-- 00-peoplemap.sql. Additive only.

-- 1. Identity rows, one per distinct v2 account. Rows collapsed onto another v1 row
--    (canonical = 0) and rows reusing a pre-existing v2 account contribute no insert.
--    PasswordHash is not refreshed on conflict -- AuthService rewrites v1 scrypt to
--    BCrypt on first login (625639c) and a re-run must not undo that.
INSERT INTO altomatehr.Users (Id, Email, Name, PasswordHash, AvatarUrl, CreatedAt)
SELECT p.v2_user_id, u.email, u.name, u.passwordHash, u.avatarUrl, u.createdAt
FROM altomatehr._mig_peoplemap p
JOIN hr_prod.User u ON u.id = p.v1_user_id
WHERE p.reuse = 0 AND p.canonical = 1
ON DUPLICATE KEY UPDATE
  Name = VALUES(Name),
  AvatarUrl = VALUES(AvatarUrl);

-- 2. Memberships, one per EmployeeOrganization row.
--    PolicyId carries the same 'prod-' prefix its org did, and is dropped to NULL if the
--    policy did not come across, so this can never plant an orphan.
INSERT INTO altomatehr.OrganizationMemberships
  (Id, OrganizationId, UserId, Role, PolicyId, EmployeeNumber, JobTitle,
   OtTimeBalanceMin, JoinDate, CreatedAt, UpdatedAt)
SELECT
  CONCAT('mig-', m.v2_id, '-', p.v2_user_id),
  m.v2_id,
  p.v2_user_id,
  CASE u.role WHEN 'SUPERVISOR' THEN 'Supervisor' ELSE 'Employee' END,
  pol.Id,
  ep.employeeId,
  ep.jobTitle,
  COALESCE(ep.otTimeBalanceMin, 0),
  pp.joinDate,
  ep.createdAt,
  UTC_TIMESTAMP()
FROM hr_prod.EmployeeOrganization e
JOIN altomatehr._mig_orgmap m   ON m.v1_id = e.organizationId
JOIN altomatehr._mig_peoplemap p ON p.v1_user_id = e.userId
JOIN hr_prod.User u             ON u.id = e.userId
JOIN hr_prod.EmployeeProfile ep ON ep.id = e.employeeProfileId
LEFT JOIN hr_prod.PayrollProfile pp ON pp.employeeProfileId = ep.id
LEFT JOIN altomatehr.EmployeePolicies pol
       ON pol.Id = IF(m.remapped, CONCAT('prod-', ep.policyId), ep.policyId) COLLATE utf8mb4_0900_ai_ci
ON DUPLICATE KEY UPDATE
  Role = VALUES(Role),
  PolicyId = VALUES(PolicyId),
  EmployeeNumber = VALUES(EmployeeNumber),
  JobTitle = VALUES(JobTitle),
  OtTimeBalanceMin = VALUES(OtTimeBalanceMin),
  JoinDate = VALUES(JoinDate),
  UpdatedAt = UTC_TIMESTAMP();
