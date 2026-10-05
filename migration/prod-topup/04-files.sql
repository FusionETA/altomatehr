USE altomatehr;
-- hr_prod -> altomatehr : point every migrated file reference at a route v2 serves.
--
-- Two kinds of file, handled differently (see memory/README "file storage"):
--   * XERO-HOSTED (v1 stored a Xero Files id): NOT downloaded. The row is pointed at
--     v2's own Xero proxy route, which fetches through the org's v2 Xero connection.
--     They render once the org is connected to Xero in v2 (same tenant as v1).
--   * V1 DISK (/uploads/...): the file is copied into the v2 storage volume by
--     04-copy-files.sh and the row is pointed at v2's local route.
--
--   v1 value                                   v2 value
--   /api/xero/files/<id>/content  (claims)  -> /claims/receipts/xero/<id>  (+ ReceiptXeroFileId)
--   /uploads/receipts/<f>                   -> /claims/receipts/<f>
--   <xero guid>  (attendance selfie)        -> /attendance/photos/xero/<guid>
--   /uploads/attendance-selfies/<f>         -> /attendance/photos/<f>
--   /api/leave/files/<id>/content           -> unchanged; LeaveService derives the v2 url
--                                              from XeroFileId (LeaveService.cs:1640)
--   PayrollProfile.payrollDocuments (camel) -> EmployeeProfiles.PayrollDocumentsJson (Pascal,
--                                              StoredFileName = basename of v1 url)
-- .jpeg -> .jpg: v2's storage whitelists extensions and has no .jpeg.
--
-- Idempotent: every statement matches only v1-shaped values or NULL targets.

DROP TEMPORARY TABLE IF EXISTS _scope;
CREATE TEMPORARY TABLE _scope (v2_id VARCHAR(64) COLLATE utf8mb4_0900_ai_ci PRIMARY KEY)
  SELECT v2_id COLLATE utf8mb4_0900_ai_ci AS v2_id FROM altomatehr._mig_orgmap;

START TRANSACTION;

-- ---------------------------------------------------------------- Claim receipts
UPDATE altomatehr.Claims v JOIN _scope sc ON sc.v2_id = v.OrganizationId
SET v.ReceiptXeroFileId = SUBSTRING_INDEX(SUBSTRING_INDEX(v.ReceiptUrl, '/', 5), '/', -1),
    v.ReceiptUrl = CONCAT('/claims/receipts/xero/', SUBSTRING_INDEX(SUBSTRING_INDEX(v.ReceiptUrl, '/', 5), '/', -1))
WHERE v.ReceiptUrl LIKE '/api/xero/files/%/content';
SELECT 'Claims.ReceiptUrl (xero)' fld, ROW_COUNT() updated;

UPDATE altomatehr.Claims v JOIN _scope sc ON sc.v2_id = v.OrganizationId
SET v.ReceiptUrl = CONCAT('/claims/receipts/', REPLACE(SUBSTRING_INDEX(v.ReceiptUrl, '/', -1), '.jpeg', '.jpg'))
WHERE v.ReceiptUrl LIKE '/uploads/receipts/%';
SELECT 'Claims.ReceiptUrl (disk)' fld, ROW_COUNT() updated;

-- Supporting documents: a JSON array of urls. The proxy authorises a supporting
-- document through ClaimsRepository.GetByReceiptUrlAsync (ReceiptUrl OR
-- SupportingDocumentUrls.Contains), so the same route shapes apply. '/content"' only
-- occurs as the tail of a v1 Xero url, so the string rewrite is exact.
UPDATE altomatehr.Claims v JOIN _scope sc ON sc.v2_id = v.OrganizationId
SET v.SupportingDocumentUrls =
      REPLACE(REPLACE(REPLACE(REPLACE(v.SupportingDocumentUrls,
        '/api/xero/files/', '/claims/receipts/xero/'), '/content"', '"'),
        '/uploads/receipts/', '/claims/receipts/'), '.jpeg"', '.jpg"')
WHERE v.SupportingDocumentUrls LIKE '%/api/xero/files/%' OR v.SupportingDocumentUrls LIKE '%/uploads/receipts/%';
SELECT 'Claims.SupportingDocumentUrls' fld, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Attendance selfies
-- v1: AttendanceRecord.xeroSelfieFileId (clock-in), .clockOutXeroSelfieFileId (clock-out),
--     AttendanceSession.xeroSelfieFileId (that session's clock-in). v1 sessions have no
--     clock-out selfie, so the record's clock-out selfie goes on the LAST session --
--     which is exactly how v2 derives the record's photos (AttendanceService.cs:330-336).
-- Only NULL targets are filled: a photo taken in v2 is never replaced.
UPDATE altomatehr.AttendanceRecords v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.AttendanceRecord a ON a.id = v.Id COLLATE utf8mb4_unicode_ci
SET v.ClockInPhotoUrl = COALESCE(v.ClockInPhotoUrl, CASE
      WHEN a.xeroSelfieFileId IS NULL THEN NULL
      WHEN a.xeroSelfieFileId LIKE '/uploads/attendance-selfies/%'
        THEN CONCAT('/attendance/photos/', REPLACE(SUBSTRING_INDEX(a.xeroSelfieFileId, '/', -1), '.jpeg', '.jpg'))
      WHEN a.xeroSelfieFileId LIKE '/%' THEN NULL
      ELSE CONCAT('/attendance/photos/xero/', a.xeroSelfieFileId) END),
    v.ClockOutPhotoUrl = COALESCE(v.ClockOutPhotoUrl, CASE
      WHEN a.clockOutXeroSelfieFileId IS NULL THEN NULL
      WHEN a.clockOutXeroSelfieFileId LIKE '/uploads/attendance-selfies/%'
        THEN CONCAT('/attendance/photos/', REPLACE(SUBSTRING_INDEX(a.clockOutXeroSelfieFileId, '/', -1), '.jpeg', '.jpg'))
      WHEN a.clockOutXeroSelfieFileId LIKE '/%' THEN NULL
      ELSE CONCAT('/attendance/photos/xero/', a.clockOutXeroSelfieFileId) END)
WHERE (v.ClockInPhotoUrl IS NULL AND a.xeroSelfieFileId IS NOT NULL)
   OR (v.ClockOutPhotoUrl IS NULL AND a.clockOutXeroSelfieFileId IS NOT NULL);
SELECT 'AttendanceRecords photos' fld, ROW_COUNT() updated;

UPDATE altomatehr.AttendanceSessions v
JOIN _scope sc ON sc.v2_id = v.OrganizationId
JOIN hr_prod.AttendanceSession s ON s.id = v.Id COLLATE utf8mb4_unicode_ci
SET v.ClockInPhotoUrl = CASE
      WHEN s.xeroSelfieFileId LIKE '/uploads/attendance-selfies/%'
        THEN CONCAT('/attendance/photos/', REPLACE(SUBSTRING_INDEX(s.xeroSelfieFileId, '/', -1), '.jpeg', '.jpg'))
      WHEN s.xeroSelfieFileId LIKE '/%' THEN NULL
      ELSE CONCAT('/attendance/photos/xero/', s.xeroSelfieFileId) END
WHERE v.ClockInPhotoUrl IS NULL AND s.xeroSelfieFileId IS NOT NULL;
SELECT 'AttendanceSessions clock-in photos' fld, ROW_COUNT() updated;

-- A record's first session with no clock-in selfie of its own inherits the record's
-- (v1 sometimes kept it only on the record).
UPDATE altomatehr.AttendanceSessions v
JOIN (SELECT AttendanceRecordId, MIN(StartedAt) firstStart FROM altomatehr.AttendanceSessions
       GROUP BY AttendanceRecordId) f
  ON f.AttendanceRecordId = v.AttendanceRecordId AND f.firstStart = v.StartedAt
JOIN altomatehr.AttendanceRecords r ON r.Id = v.AttendanceRecordId
JOIN _scope sc ON sc.v2_id = r.OrganizationId
SET v.ClockInPhotoUrl = r.ClockInPhotoUrl
WHERE v.ClockInPhotoUrl IS NULL AND r.ClockInPhotoUrl IS NOT NULL;
SELECT 'AttendanceSessions first-session photo' fld, ROW_COUNT() updated;

UPDATE altomatehr.AttendanceSessions v
JOIN (SELECT AttendanceRecordId, MAX(StartedAt) lastStart FROM altomatehr.AttendanceSessions
       GROUP BY AttendanceRecordId) l
  ON l.AttendanceRecordId = v.AttendanceRecordId AND l.lastStart = v.StartedAt
JOIN altomatehr.AttendanceRecords r ON r.Id = v.AttendanceRecordId
JOIN _scope sc ON sc.v2_id = r.OrganizationId
SET v.ClockOutPhotoUrl = r.ClockOutPhotoUrl
WHERE v.ClockOutPhotoUrl IS NULL AND r.ClockOutPhotoUrl IS NOT NULL;
SELECT 'AttendanceSessions last-session clock-out photo' fld, ROW_COUNT() updated;

-- ---------------------------------------------------------------- Employee documents
-- EmployeeDocumentService deserialises with bare System.Text.Json: PascalCase and
-- CASE-SENSITIVE. v1's camelCase shape binds every field to empty. Only rows still in
-- v1 shape (have "url", lack "StoredFileName") are rewritten.
UPDATE altomatehr.EmployeeProfiles ep
JOIN _scope sc ON sc.v2_id = ep.OrganizationId
SET ep.PayrollDocumentsJson = (
  SELECT JSON_ARRAYAGG(JSON_OBJECT(
           'Id', j.id, 'Name', j.name, 'MimeType', j.mimeType, 'SizeBytes', j.sizeBytes,
           'UploadedAt', j.uploadedAt,
           'StoredFileName', REPLACE(SUBSTRING_INDEX(j.url, '/', -1), '.jpeg', '.jpg')))
  FROM JSON_TABLE(ep.PayrollDocumentsJson, '$[*]' COLUMNS (
         id VARCHAR(64) PATH '$.id', name VARCHAR(260) PATH '$.name',
         mimeType VARCHAR(100) PATH '$.mimeType', sizeBytes BIGINT PATH '$.sizeBytes',
         uploadedAt VARCHAR(40) PATH '$.uploadedAt', url VARCHAR(600) PATH '$.url')) j)
WHERE JSON_VALID(ep.PayrollDocumentsJson)
  AND JSON_CONTAINS_PATH(ep.PayrollDocumentsJson, 'one', '$[0].url')
  AND NOT JSON_CONTAINS_PATH(ep.PayrollDocumentsJson, 'one', '$[0].StoredFileName');
SELECT 'EmployeeProfiles.PayrollDocumentsJson' fld, ROW_COUNT() updated;

COMMIT;
