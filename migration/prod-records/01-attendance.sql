-- hr_prod -> altomatehr : attendance (records, sessions, breaks, approval requests)
--
-- Depends on: ../prod-settings (orgmap + Projects), ../prod-employees (peoplemap + Users).
-- Independent of team rosters / approval chains — these rows carry their own
-- decided state. PENDING rows load fine but cannot be ACTED on until
-- TeamMemberships are migrated (see ../ANSWERS-v1-v2-gaps.md, Q1).
--
-- CONFLICT POLICY: insert-if-the-unique-key-is-free. Nothing already in v2 is
-- ever updated or deleted. A handful of rows are therefore SKIPPED because a
-- pre-existing v2 row from the earlier hr_dev ZR TEST slice already occupies
-- their unique key; 04-verify.sql lists them. Re-running is safe and is a no-op
-- (it does NOT pick up later v1 edits — that is a cutover decision, not this).
--
-- Collation: altomatehr is utf8mb4_0900_ai_ci, hr_prod is utf8mb4_unicode_ci.
-- Every cross-database comparison coerces the altomatehr side explicitly.

-- Precheck.
SELECT IF((SELECT COUNT(*) FROM altomatehr._mig_orgmap) = 0
          OR (SELECT COUNT(*) FROM altomatehr._mig_peoplemap) = 0,
          'ABORT: run ../prod-settings and ../prod-employees first',
          'deps ok') AS precheck;

-- ---------------------------------------------------------------- 1. Records
-- Org is the record's PROJECT's org; the 5 project-less rows fall back to the
-- employee's (single) organization membership.
-- Date is copied VERBATIM: v1's `date` is already the UTC-midnight local-day key
-- v2 expects (all 893 rows are exactly 00:00:00). 13 rows are mis-filed one day
-- behind in v1 itself -- that is pre-existing bad data, NOT a conversion, and it
-- is deliberately carried across unchanged. 9 of the 13 would collide with a
-- real row if "corrected", so fixing them needs merge logic and a separate run.
INSERT INTO altomatehr.AttendanceRecords
  (Id, OrganizationId, EmployeeId, Date, TimeIn, TimeOut, DurationMin, LateByMin,
   Location, ProjectId, Status, Notes, Remark, CreatedAt, UpdatedAt,
   ClockInDistanceMeters, ClockInLat, ClockInLng,
   ClockOutDistanceMeters, ClockOutLat, ClockOutLng)
SELECT
  a.id, org.v2_id, pm.v2_user_id, a.date, a.timeIn, a.timeOut, a.durationMin, a.lateByMin,
  LEFT(a.location, 200),
  CASE WHEN a.projectId IS NULL THEN NULL
       ELSE IF(pom.remapped, CONCAT('prod-', a.projectId), a.projectId) END,
  a.status, a.notes, a.remark, a.createdAt, a.updatedAt,
  a.clockInDistanceMeters, a.clockInLat, a.clockInLng,
  a.clockOutDistanceMeters, a.clockOutLat, a.clockOutLng
FROM hr_prod.AttendanceRecord a
JOIN altomatehr._mig_peoplemap pm
  ON pm.v1_user_id COLLATE utf8mb4_unicode_ci = a.employeeId
LEFT JOIN hr_prod.XeroProject xp ON xp.id = a.projectId
LEFT JOIN altomatehr._mig_orgmap pom
  ON pom.v1_id COLLATE utf8mb4_unicode_ci = xp.organizationId
JOIN altomatehr._mig_orgmap org
  ON org.v1_id COLLATE utf8mb4_unicode_ci = COALESCE(
       xp.organizationId,
       (SELECT eo.organizationId FROM hr_prod.EmployeeOrganization eo
         WHERE eo.userId = a.employeeId ORDER BY eo.createdAt LIMIT 1))
WHERE NOT EXISTS (
  SELECT 1 FROM altomatehr.AttendanceRecords v2
   WHERE v2.EmployeeId COLLATE utf8mb4_unicode_ci = pm.v2_user_id
     AND v2.Date = a.date)
AND NOT EXISTS (
  SELECT 1 FROM altomatehr.AttendanceRecords v3
   WHERE v3.Id COLLATE utf8mb4_unicode_ci = a.id);

-- ---------------------------------------------------------------- 2. Sessions
-- Parent-keyed: a session whose record was skipped above is skipped here too.
-- Dropped, no v2 column: clockInNotes, clockOutNotes, xeroSelfieFileId,
-- selfieUploadedAt, isAutoClockOut, clockInIpAddress, clockInIpAllowed,
-- clockInApprovalRequestId, clockOutApprovalRequestId, project/projectId
-- (the record carries the project).
INSERT INTO altomatehr.AttendanceSessions
  (Id, OrganizationId, AttendanceRecordId, EmployeeId, StartedAt, EndedAt,
   CreatedAt, UpdatedAt, ClockInDistanceMeters, ClockInLat, ClockInLng,
   ClockOutDistanceMeters, ClockOutLat, ClockOutLng, DurationMin, Status)
SELECT
  s.id, r.OrganizationId, r.Id, r.EmployeeId, s.startedAt, s.endedAt,
  s.createdAt, s.updatedAt, s.clockInDistanceMeters, s.clockInLat, s.clockInLng,
  s.clockOutDistanceMeters, s.clockOutLat, s.clockOutLng, s.durationMin, s.status
FROM hr_prod.AttendanceSession s
JOIN altomatehr.AttendanceRecords r
  ON r.Id COLLATE utf8mb4_unicode_ci = s.attendanceRecordId
WHERE NOT EXISTS (
  SELECT 1 FROM altomatehr.AttendanceSessions v2
   WHERE v2.Id COLLATE utf8mb4_unicode_ci = s.id);

-- ---------------------------------------------------------------- 3. Breaks
-- v2's AttendanceBreaks.AttendanceSessionId is NOT NULL, so the INNER JOIN to
-- sessions is also the filter: 5 of 18 v1 breaks hang off a record with ZERO
-- sessions and cannot be represented. They are skipped, not faked.
-- v1 BreakSession has no updatedAt -> createdAt is reused.
INSERT INTO altomatehr.AttendanceBreaks
  (Id, OrganizationId, AttendanceSessionId, AttendanceRecordId, EmployeeId,
   StartedAt, EndedAt, StartLat, StartLng, EndLat, EndLng, CreatedAt, UpdatedAt)
SELECT
  b.id, r.OrganizationId, sn.Id, r.Id, r.EmployeeId,
  b.startedAt, b.endedAt, b.startedAtLat, b.startedAtLng, b.endedAtLat, b.endedAtLng,
  b.createdAt, b.createdAt
FROM hr_prod.BreakSession b
JOIN altomatehr.AttendanceRecords r
  ON r.Id COLLATE utf8mb4_unicode_ci = b.attendanceRecordId
JOIN altomatehr.AttendanceSessions sn
  ON sn.Id COLLATE utf8mb4_unicode_ci = b.attendanceSessionId
WHERE NOT EXISTS (
  SELECT 1 FROM altomatehr.AttendanceBreaks v2
   WHERE v2.Id COLLATE utf8mb4_unicode_ci = b.id);

-- ------------------------------------------------- 4. Approval requests
-- v1 ApprovalRequest has no FK to the attendance record -- it is keyed by
-- (employeeId, date), which is exactly AttendanceRecord's unique key, so the
-- link is resolved through it. 13 CLOCK_OUT rows point at an (employee, date)
-- with no record and are skipped.
--
-- Kind: v1's single BREAK kind splits into v2's BREAK_START / BREAK_END. v1
-- stores the subtype only in the title ("Break start 09:53" / "Break end 09:53")
-- -- there is no column for it -- so the title is what decides. All 35 rows
-- match one of the two patterns; anything that didn't would be dropped by the
-- CASE's NULL and rejected by the NOT NULL column rather than mislabelled.
--
-- OT is EXCLUDED. v2 routes overtime through its own OvertimeRequests table,
-- whose StartAt/EndAt/RequestedMinutes/Reason/BeforePhotoUrl are all NOT NULL,
-- and 2 of v1's 3 OT rows are threshold-derived with NULL otStartAt/otEndAt.
-- Migrating them would mean inventing times and a photo. See the README.
--
-- Dropped, no v2 column: title, detail (kept as Reason), location, lateMinutes,
-- offsetRef, project, chainHistory (v2 has no per-step history for attendance).
INSERT INTO altomatehr.AttendanceApprovalRequests
  (Id, OrganizationId, EmployeeId, Kind, AttendanceRecordId, EventAt,
   ApprovalStatus, CurrentStep, ReviewNotes, ReviewerId, SubmittedAt, DecidedAt,
   CreatedAt, UpdatedAt, OriginalEventAt, Reason)
SELECT
  q.id, r.OrganizationId, r.EmployeeId,
  CASE WHEN q.kind = 'BREAK' AND q.title LIKE 'Break start%' THEN 'BREAK_START'
       WHEN q.kind = 'BREAK' AND q.title LIKE 'Break end%'   THEN 'BREAK_END'
       WHEN q.kind = 'BREAK'                                 THEN NULL
       ELSE q.kind END,
  r.Id, q.eventAt, q.status, 0, q.reviewNotes, rpm.v2_user_id,
  q.submittedAt, q.reviewedAt, q.createdAt, q.updatedAt, q.originalEventAt,
  LEFT(q.detail, 1000)
FROM hr_prod.ApprovalRequest q
JOIN hr_prod.AttendanceRecord ar
  ON ar.employeeId = q.employeeId AND ar.date = q.date
JOIN altomatehr.AttendanceRecords r
  ON r.Id COLLATE utf8mb4_unicode_ci = ar.id
LEFT JOIN altomatehr._mig_peoplemap rpm
  ON rpm.v1_user_id COLLATE utf8mb4_unicode_ci = q.reviewerId
WHERE q.kind <> 'OT'
AND NOT EXISTS (
  SELECT 1 FROM altomatehr.AttendanceApprovalRequests v2
   WHERE v2.Id COLLATE utf8mb4_unicode_ci = q.id);
