#!/usr/bin/env bash
# Copies every v1 DISK file a migrated row references into the v2 storage volume.
# (Xero-hosted files are NOT copied -- 04-files.sql points them at v2's Xero proxy.)
# Source: v1 prod public/uploads, falling back to v1 dev (one OT photo on 09-23 only
# existed on dev). Never overwrites a file already in the volume. $CNF from run.sh.
set -euo pipefail
VOL=/var/lib/docker/volumes/altomatehr-v2_api_storage/_data
SRC=(/var/www/AltomateHR/prod/public /var/www/AltomateHR/dev/public)
OWNER=$(stat -c %u:%g "$VOL/receipts")
q() { mysql --defaults-file="$CNF" -N -B -e "$1"; }

# <v1 path> <v2 relative dest>
LIST=$(q "
SELECT receiptUrl, CONCAT('receipts/', REPLACE(SUBSTRING_INDEX(receiptUrl,'/',-1),'.jpeg','.jpg'))
  FROM hr_prod.Claim WHERE receiptUrl LIKE '/uploads/receipts/%'
UNION SELECT fileUrl, CONCAT('receipts/', REPLACE(SUBSTRING_INDEX(fileUrl,'/',-1),'.jpeg','.jpg'))
  FROM hr_prod.ClaimSupportingAttachment WHERE fileUrl LIKE '/uploads/receipts/%'
UNION SELECT x, CONCAT('attendance-photos/', REPLACE(SUBSTRING_INDEX(x,'/',-1),'.jpeg','.jpg')) FROM (
        SELECT xeroSelfieFileId x FROM hr_prod.AttendanceRecord
  UNION SELECT clockOutXeroSelfieFileId FROM hr_prod.AttendanceRecord
  UNION SELECT xeroSelfieFileId FROM hr_prod.AttendanceSession) s
  WHERE x LIKE '/uploads/attendance-selfies/%'
UNION SELECT j.url, CONCAT('employee-documents/', SUBSTRING_INDEX(SUBSTRING_INDEX(j.url,'/',-2),'/',1), '/',
                           REPLACE(SUBSTRING_INDEX(j.url,'/',-1),'.jpeg','.jpg'))
  FROM hr_prod.PayrollProfile p,
       JSON_TABLE(p.payrollDocuments, '\$[*]' COLUMNS (url VARCHAR(600) PATH '\$.url')) j
  WHERE j.url LIKE '/uploads/payroll-documents/%';")

copied=0; present=0; missing=0
while IFS=$'\t' read -r v1 dest; do
  [ -z "$v1" ] && continue
  if [ -f "$VOL/$dest" ]; then present=$((present+1)); continue; fi
  found=""
  for s in "${SRC[@]}"; do [ -f "$s$v1" ] && { found="$s$v1"; break; }; done
  if [ -z "$found" ]; then echo "MISSING  $v1"; missing=$((missing+1)); continue; fi
  mkdir -p "$(dirname "$VOL/$dest")"
  cp --update=none "$found" "$VOL/$dest"
  chown "$OWNER" "$VOL/$dest" "$(dirname "$VOL/$dest")"
  echo "copied   $found -> $dest"; copied=$((copied+1))
done <<< "$LIST"
echo "files: copied=$copied already_present=$present missing=$missing"
