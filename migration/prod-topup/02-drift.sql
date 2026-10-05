USE altomatehr;
-- hr_prod -> altomatehr : bring already-migrated records up to their current v1 state.
--
-- ../prod-records is insert-if-free, so a re-run picks up NEW v1 rows but never a
-- later v1 edit (an attendance record clocked out after 09-18, a claim approved
-- since, ...). This pass applies those edits.
--
-- WHO WINS: a row is overwritten only when BOTH hold
--   1. v1 is newer:           v1.updatedAt > v2.UpdatedAt
--   2. v2 never touched it:   MICROSECOND(v2.UpdatedAt) % 1000 = 0
-- (2) works because every migrated row carries v1's MILLISECOND timestamp
-- (Prisma), while anything EF writes uses DateTime.UtcNow at MICROSECOND
-- precision. A v2 edit therefore leaves sub-millisecond digits (1-in-1000
-- false negative: such a row is treated as v2-edited and left alone -- the safe
-- direction). As of 2026-09-25 0 migrated rows had been edited in v2.
--
-- Scope: only rows in the 42 mapped orgs. The hr_dev ZR TEST twins
-- (org cmogup4k90000kukzos5fj1qn) share ids with prod rows and are never touched.
-- Re-runnable: after one pass v2.UpdatedAt = v1.updatedAt, so condition 1 fails.

DROP TEMPORARY TABLE IF EXISTS _scope;
CREATE TEMPORARY TABLE _scope (v2_id VARCHAR(64) COLLATE utf8mb4_0900_ai_ci PRIMARY KEY)
  SELECT v2_id COLLATE utf8mb4_0900_ai_ci AS v2_id FROM altomatehr._mig_orgmap;

START TRANSACTION;

-- ---------------------------------------------------------------- Attendance records
-- Date is the unique key half and never changes in v1 after creation -> not updated.
UPDATE altomatehr.AttendanceRecords v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.AttendanceRecord a ON a.id = v.Id COLLATE utf8mb4_unicode_ci
LEFT JOIN hr_prod.XeroProject xp ON xp.id = a.projectId
LEFT JOIN altomatehr._mig_orgmap pom ON pom.v1_id = xp.organizationId
SET v.TimeIn = a.timeIn, v.TimeOut = a.timeOut, v.DurationMin = a.durationMin,
    v.LateByMin = a.lateByMin, v.Location = LEFT(a.location, 200),
    v.ProjectId = CASE WHEN a.projectId IS NULL THEN NULL
                       ELSE IF(pom.remapped, CONCAT('prod-', a.projectId), a.projectId) END,
    v.Status = a.status, v.Notes = a.notes, v.Remark = a.remark,
    v.ClockInDistanceMeters = a.clockInDistanceMeters, v.ClockInLat = a.clockInLat, v.ClockInLng = a.clockInLng,
    v.ClockOutDistanceMeters = a.clockOutDistanceMeters, v.ClockOutLat = a.clockOutLat, v.ClockOutLng = a.clockOutLng,
    v.UpdatedAt = a.updatedAt
WHERE a.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'AttendanceRecords' tbl, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Sessions
UPDATE altomatehr.AttendanceSessions v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.AttendanceSession s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
SET v.StartedAt = s.startedAt, v.EndedAt = s.endedAt, v.DurationMin = s.durationMin, v.Status = s.status,
    v.ClockInDistanceMeters = s.clockInDistanceMeters, v.ClockInLat = s.clockInLat, v.ClockInLng = s.clockInLng,
    v.ClockOutDistanceMeters = s.clockOutDistanceMeters, v.ClockOutLat = s.clockOutLat, v.ClockOutLng = s.clockOutLng,
    v.UpdatedAt = s.updatedAt
WHERE s.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'AttendanceSessions' tbl, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Breaks
-- v1 BreakSession has no updatedAt (01-attendance.sql reused createdAt), so "newer"
-- is "the break has since ended in v1".
UPDATE altomatehr.AttendanceBreaks v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.BreakSession b ON b.id = v.Id COLLATE utf8mb4_unicode_ci
SET v.EndedAt = b.endedAt, v.EndLat = b.endedAtLat, v.EndLng = b.endedAtLng
WHERE NOT (v.EndedAt <=> b.endedAt) AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'AttendanceBreaks' tbl, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Approval requests
UPDATE altomatehr.AttendanceApprovalRequests v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.ApprovalRequest q ON q.id = v.Id COLLATE utf8mb4_unicode_ci
LEFT JOIN altomatehr._mig_peoplemap rpm ON rpm.v1_user_id COLLATE utf8mb4_unicode_ci = q.reviewerId
SET v.ApprovalStatus = q.status, v.ReviewNotes = q.reviewNotes, v.ReviewerId = rpm.v2_user_id,
    v.EventAt = q.eventAt, v.OriginalEventAt = q.originalEventAt, v.SubmittedAt = q.submittedAt,
    v.DecidedAt = q.reviewedAt, v.Reason = LEFT(q.detail, 1000), v.UpdatedAt = q.updatedAt
WHERE q.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'AttendanceApprovalRequests' tbl, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Claims
-- Same column mapping as ../prod-records/02-claims.sql. ReceiptUrl /
-- SupportingDocumentUrls are copied in v1 shape here and rewritten to v2 routes by
-- 04-files.sql, which runs after this and is idempotent.
-- XeroBillId is UNIQUE: it is only set when no OTHER v2 claim holds it
-- (the derived table dodges MySQL's "can't reference the update target" error).
UPDATE altomatehr.Claims v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.Claim c ON c.id = v.Id COLLATE utf8mb4_unicode_ci
LEFT JOIN hr_prod.XeroProject xp ON xp.id = c.projectId
LEFT JOIN altomatehr._mig_orgmap pom ON pom.v1_id = xp.organizationId
LEFT JOIN (SELECT Id, XeroBillId FROM altomatehr.Claims WHERE XeroBillId IS NOT NULL) other
  ON other.XeroBillId COLLATE utf8mb4_unicode_ci = c.xeroBillId AND other.Id <> v.Id
SET v.Title = LEFT(c.title, 200), v.Description = c.description, v.Category = c.category,
    v.Amount = c.amount, v.Currency = c.currency, v.SpentAt = c.spentAt, v.SubmittedAt = c.submittedAt,
    v.Status = CASE WHEN c.status = 'REVIEWED' THEN 'APPROVED' ELSE c.status END,
    v.ClaimType = c.claimType, v.PaymentType = c.paymentType,
    v.ReceiptUrl = LEFT(c.receiptUrl, 1000), v.ReviewNotes = c.reviewNotes,
    v.ChartOfAccountId = c.chartOfAccountId, v.ExceedsLimit = c.exceedsLimit,
    v.ProjectId = CASE WHEN c.projectId IS NULL THEN NULL
                       ELSE IF(pom.remapped, CONCAT('prod-', c.projectId), c.projectId) END,
    v.Distance = c.distance, v.MileageDestinationAddress = c.mileageDestinationAddress,
    v.MileageOriginAddress = c.mileageOriginAddress, v.MileageRateUsed = c.mileageRateUsed,
    v.MileageUnitUsed = c.mileageUnitUsed, v.PayViaAccountId = c.payViaAccountId,
    v.SpendingAt = LEFT(c.spendingAt, 200), v.SpendingWith = LEFT(c.spendingWith, 200),
    v.SupportingDocumentUrls = (SELECT JSON_ARRAYAGG(sa.fileUrl) FROM hr_prod.ClaimSupportingAttachment sa
                                 WHERE sa.claimId = c.id),
    v.XeroBillId = IF(other.Id IS NULL, c.xeroBillId, v.XeroBillId),
    v.XeroBillRef = c.xeroBillRef, v.XeroSyncError = c.xeroSyncError,
    v.XeroSyncStatus = c.xeroSyncStatus, v.XeroSyncedAt = c.xeroSyncedAt,
    v.UpdatedAt = c.updatedAt
WHERE c.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'Claims' tbl, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Leave applications
UPDATE altomatehr.LeaveApplications v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.LeaveApplication l ON l.id = v.Id COLLATE utf8mb4_unicode_ci
LEFT JOIN altomatehr._mig_peoplemap apm ON apm.v1_user_id COLLATE utf8mb4_unicode_ci = l.appliedByAdminId
SET v.StartDate = l.startDate, v.EndDate = l.endDate, v.TotalDays = l.totalDays, v.Reason = l.reason,
    v.Status = l.status, v.DecidedAt = l.decidedAt, v.CurrentStep = l.currentStep, v.XeroFileId = l.xeroFileId,
    v.AppliedByAdminId = apm.v2_user_id, v.Approvals = CAST(l.approvals AS CHAR),
    v.AttachmentName = LEFT(l.attachmentName, 260), v.Duration = l.duration,
    v.AttachmentUrl = LEFT(l.attachmentUrl, 400), v.UpdatedAt = l.updatedAt
WHERE l.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'LeaveApplications' tbl, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Leave entitlements
UPDATE altomatehr.LeaveEntitlements v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.LeaveEntitlement e ON e.id = v.Id COLLATE utf8mb4_unicode_ci
SET v.EntitledDays = e.entitledDays, v.AccruedDays = e.accruedDays, v.CarriedDays = e.carriedDays,
    v.CarriedExpiresAt = e.carriedExpiresAt, v.CarriedExpired = e.carriedExpired,
    v.CarriedExpiredAt = e.carriedExpiredAt, v.CarriedExpiredDays = e.carriedExpiredDays,
    v.AccrualMethod = e.accrualMethod, v.UpdatedAt = e.updatedAt
WHERE e.updatedAt > v.UpdatedAt AND MICROSECOND(v.UpdatedAt) % 1000 = 0;
SELECT 'LeaveEntitlements' tbl, ROW_COUNT() updated;

COMMIT;
