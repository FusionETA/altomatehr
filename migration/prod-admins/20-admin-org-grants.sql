-- CORRECTION to 01-admins.sql.
--
-- 01-admins.sql gave each admin ONE membership, in the org named by User.organizationId.
-- That is wrong: v1 grants an admin access to additional companies through the
-- AdminOrganization table (adminId, organizationId, modules, policyIds), and a multi-org
-- admin's other companies were silently dropped.
--
-- Symptom that found it: adminglobe@gmail.com holds 3 AdminOrganization grants (GLOBE
-- SUCCESS LEARNING, GLOBE ENGINEERING, GLOBE EXPRESS) but landed in v2 with only GLOBE
-- SUCCESS LEARNING, its User.organizationId. It is also why GLOBE ENGINEERING and GLOBE
-- EXPRESS looked like they had no admin at all -- their admin is granted, not homed.
--
-- Run after 00-adminmap.sql and 01-admins.sql. Idempotent.

-- Orgs that already have an Owner, so a granted OWNER never becomes a second one.
-- Precomputed because MySQL will not let the INSERT ... ON DUPLICATE KEY UPDATE read its
-- own target table in a subquery.
DROP TEMPORARY TABLE IF EXISTS altomatehr._owner_orgs;
CREATE TEMPORARY TABLE altomatehr._owner_orgs (
  org_id varchar(40) NOT NULL,
  user_id varchar(40) NOT NULL,
  PRIMARY KEY (org_id, user_id)
) COLLATE utf8mb4_unicode_ci;
INSERT INTO altomatehr._owner_orgs
SELECT OrganizationId, UserId FROM altomatehr.OrganizationMemberships WHERE Role = 'Owner';

INSERT INTO altomatehr.OrganizationMemberships
  (Id, OrganizationId, UserId, Role, Modules, CreatedAt, UpdatedAt, OtTimeBalanceMin)
SELECT
  CONCAT('mig-', m.v2_id, '-', am.v2_user_id),
  m.v2_id,
  am.v2_user_id,
  -- The v1 role is global to the user; a grant only says which companies it applies to.
  -- An OWNER granted a company that already has a different Owner lands as Admin.
  IF(u.role = 'OWNER'
     AND NOT EXISTS (SELECT 1 FROM altomatehr._owner_orgs w
                      WHERE w.org_id = m.v2_id AND w.user_id <> am.v2_user_id),
     'Owner', 'Admin'),
  -- v1 modules is a JSON array, v2 Modules is a comma-joined string -- but the two
  -- versions do NOT share a module vocabulary, so this is a TRANSLATION, not the plain
  -- shape conversion the settings migration does for Organization.addons.
  --
  --   v1 AdminModuleKey : payroll, leave, hierarchy, company_structure, audit_log,
  --                       settings, claims_personal, claims_company, attendance
  --   v2 module keys    : employees, leave, projects, teams, accounts, policies,
  --                       overtime, claims, attendance
  --
  -- Only 'leave' is spelled the same. Copying v1 names through verbatim (the original
  -- bug) left every grant intersecting the v2 ceiling down to {leave} alone, so admins
  -- 403'd on attendance, employees, claims and everything else -- see RequireModule +
  -- OrgModules.Effective. The two BASE sets are the same entitlement decomposed
  -- differently, so they map as a whole rather than name-by-name; v1 splits Claims into
  -- personal/company where v2 has a single 'claims'.
  --
  -- NULL (no AdminOrganization.modules, or the JSON literal null) stays NULL, which v2
  -- reads as "unrestricted" -> the full org ceiling.
  NULLIF(CONCAT_WS(',',
      IF(JSON_OVERLAPS(a.modules, JSON_ARRAY('payroll', 'leave', 'hierarchy',
                                             'company_structure', 'audit_log', 'settings')),
         'employees,leave,projects,teams,accounts,policies,overtime', NULL),
      IF(JSON_OVERLAPS(a.modules, JSON_ARRAY('claims_personal', 'claims_company')),
         'claims', NULL),
      IF(JSON_CONTAINS(a.modules, '"attendance"'), 'attendance', NULL)
  ), ''),
  a.createdAt,
  UTC_TIMESTAMP(),
  0
FROM hr_prod.AdminOrganization a
JOIN altomatehr._mig_orgmap m   ON m.v1_id = a.organizationId
JOIN altomatehr._mig_adminmap am ON am.v1_user_id = a.adminId
JOIN hr_prod.User u             ON u.id = a.adminId
ON DUPLICATE KEY UPDATE
  Modules = VALUES(Modules),
  UpdatedAt = UTC_TIMESTAMP();

DROP TEMPORARY TABLE IF EXISTS altomatehr._owner_orgs;

-- policyIds on the grant is not carried: every row in hr_prod holds the JSON literal
-- null, and v2's membership has a single PolicyId rather than a list.
