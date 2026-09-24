-- Loads the mapped OWNER/ADMIN accounts into altomatehr. Requires 00-adminmap.sql.
-- Additive only: no deletes, no updates to anything outside the mapped set.

-- 1. Identity rows. Only for admins with no existing v2 account (reuse = 0).
--    PasswordHash is deliberately NOT refreshed on conflict: AuthService rewrites a v1
--    scrypt hash to BCrypt on first login (625639c), and re-running must not undo that.
INSERT INTO altomatehr.Users (Id, Email, Name, PasswordHash, AvatarUrl, CreatedAt)
SELECT a.v2_user_id, u.email, u.name, u.passwordHash, u.avatarUrl, u.createdAt
FROM altomatehr._mig_adminmap a
JOIN hr_prod.User u ON u.id = a.v1_user_id
WHERE a.reuse = 0
ON DUPLICATE KEY UPDATE
  Name = VALUES(Name),
  AvatarUrl = VALUES(AvatarUrl);

-- 2. Memberships. Deterministic id so a re-run upserts rather than duplicating; the
--    (OrganizationId, UserId) unique index catches it either way.
--    v1 admins carry no policy, shift or employee number, so those stay NULL -- the same
--    shape the existing v2 Owner/Admin rows already have (no EmployeeProfile either).
INSERT INTO altomatehr.OrganizationMemberships
  (Id, OrganizationId, UserId, Role, CreatedAt, UpdatedAt, OtTimeBalanceMin)
SELECT CONCAT('mig-', a.v2_org, '-', a.v2_user_id), a.v2_org, a.v2_user_id, a.v2_role,
       u.createdAt, UTC_TIMESTAMP(), 0
FROM altomatehr._mig_adminmap a
JOIN hr_prod.User u ON u.id = a.v1_user_id
ON DUPLICATE KEY UPDATE
  Role = VALUES(Role),
  UpdatedAt = UTC_TIMESTAMP();
