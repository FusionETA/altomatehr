#!/usr/bin/env bash
# Attendance + overtime, as an employee and their supervisor.
SP="$(cd "$(dirname "$0")" && pwd)"; source "$SP/lib.sh"
IFS='|' read -r ORG_ID PROJ_ID SHIFT_ID POLICY_ID ACC_ID BANK_ID LT_ID OWNER_TOKEN STAMP EMP_ID SUP_ID TEAM_ID EMP_TOKEN SUP_TOKEN CLAIM_ID LEAVE_ID < "$SP/state.txt"
TODAY=$(date +%F); YEAR=$(date +%Y)

phase "P15 Attendance lifecycle"
TOKEN="$EMP_TOKEN"
req GET /attendance/open-session; okin "check for an open session (204 = none)" "200 204"
req GET /attendance/today;        okin "read today's record (204 = none yet)" "200 204"
req POST /attendance/clock-in "{\"projectId\":\"$PROJ_ID\",\"location\":\"HQ Kuala Lumpur\",\"lat\":3.1578,\"lng\":101.7117,\"remark\":\"QA clock-in\"}"
okin "employee clocks in" "200 201"; REC_ID=$(jqr '.id'); note "clocked in at" "$(jqr '.timeIn')"
req GET /attendance/open-session; ok "the open session is now reported" 200
req POST /attendance/clock-in "{\"projectId\":\"$PROJ_ID\"}"; okin "a second clock-in is refused" "400 409"
req POST /attendance/break/start '{}'; okin "employee starts a break" "200 201"
req POST /attendance/break/end '{}';   okin "employee ends the break" "200 201"
req GET "/attendance/$REC_ID/breaks";  ok "read the breaks on the record" 200
req POST /attendance/clock-out '{"remark":"QA clock-out","lat":3.1578,"lng":101.7117}'
okin "employee clocks out" "200 201"; note "clocked out at" "$(jqr '.timeOut')"
req POST /attendance/clock-out '{"remark":"again"}'; okin "clocking out twice is refused" "400 409"
req GET /attendance; ok "employee reads their attendance history" 200
# The correction must land inside the worked window, so anchor it to the record.
req POST /attendance/adjustments "{\"recordId\":\"$REC_ID\",\"requestedTimeIn\":\"${TODAY}T00:30:00Z\",\"reason\":\"Forgot to clock in on arrival\"}"
okin "employee requests a time correction" "200 201"
ADJ_ID=$(printf '%s' "$BODY" | jq -r 'if type=="array" then .[0].id else .id end' 2>/dev/null)
note "adjustment" "$ADJ_ID"
req GET /attendance/hours-summary/me; ok "employee reads their hours summary" 200

TOKEN="$SUP_TOKEN"
req GET /attendance/team;                    ok "supervisor reads the team's attendance" 200
req GET /attendance/team/today;              ok "supervisor reads who is in today" 200
req GET /attendance/team/breaks;             ok "supervisor reads team breaks" 200
req GET /attendance/pending-approvals/digest; ok "supervisor reads the pending digest" 200
if [ -n "$ADJ_ID" ] && [ "$ADJ_ID" != "null" ]; then
  req POST "/attendance/$ADJ_ID/approve" '{}'; okin "supervisor approves the correction" "200 204"
  note "corrected clock-in" "$(jqr '.timeIn')"
fi

TOKEN="$OWNER_TOKEN"
req GET /attendance/hours-summary/org;                ok "admin reads org hours" 200
req GET "/attendance/hours-summary/employees/$EMP_ID"; ok "admin reads one employee's hours" 200
req GET /attendance/warnings/still-clocked-in;         ok "admin sees still-clocked-in warnings" 200
req GET /attendance/supervisor-performance;            ok "admin reads supervisor performance" 200
req GET /attendance/approval-audit;                    ok "admin reads the approval audit" 200
req GET /attendance/audit-log;                         ok "admin reads the attendance audit log" 200
req GET /attendance/selfie-storage;                    ok "admin reads selfie storage usage" 200
req GET /attendance/selfies/stats;                     ok "admin reads selfie stats" 200
reqraw GET "/attendance/export/summary?from=$YEAR-01-01&to=$YEAR-12-31&format=Csv" "$SP/.out/att-export.csv"
ok "export the attendance summary" 200; note "attendance export" "$CTYPE ${SIZE}b"
reqraw GET "/attendance/export/approval-audit?format=Csv" "$SP/.out/att-audit.csv"; ok "export the approval audit" 200
reqraw GET "/attendance/import/template?format=Csv" "$SP/.out/att-template.csv"; ok "download the attendance import template" 200

phase "P16 Overtime lifecycle"
TOKEN="$EMP_TOKEN"
req GET /overtime/rate; ok "employee reads their OT rate" 200
req GET /overtime;      ok "employee lists their OT requests" 200
# The before-photo must be a file this API stored; an arbitrary URL is refused.
reqfile POST /overtime/photo photo "$SP/pixel.png"; ok "employee uploads the before-work photo" 200
OT_PHOTO=$(jqr '.photoUrl')
req POST /overtime "{\"projectId\":\"$PROJ_ID\",\"workDate\":\"$TODAY\",\"startAt\":\"${TODAY}T18:30:00Z\",\"endAt\":\"${TODAY}T21:00:00Z\",\"reason\":\"Month-end closing\",\"beforePhotoUrl\":\"$OT_PHOTO\"}"
okin "employee submits an OT request" "200 201"; OT_ID=$(jqr '.id')
note "ot" "id=$OT_ID minutes=$(jqr '.requestedMinutes') status=$(jqr '.status')"
req POST /overtime "{\"workDate\":\"$TODAY\",\"startAt\":\"${TODAY}T21:00:00Z\",\"endAt\":\"${TODAY}T18:30:00Z\",\"reason\":\"Backwards\",\"beforePhotoUrl\":\"$OT_PHOTO\"}"
okin "reject OT that ends before it starts" "400 422"
req POST /overtime "{\"workDate\":\"$TODAY\",\"startAt\":\"${TODAY}T18:30:00Z\",\"endAt\":\"${TODAY}T21:00:00Z\",\"reason\":\"No photo\"}"
ok "reject OT with no before-photo" 400
req POST /overtime "{\"workDate\":\"$TODAY\",\"startAt\":\"${TODAY}T18:30:00Z\",\"endAt\":\"${TODAY}T21:00:00Z\",\"reason\":\"Made-up photo\",\"beforePhotoUrl\":\"https://evil.test/x.png\"}"
ok "reject OT whose photo was never uploaded here" 400
reqfile POST /overtime/photo photo "$SP/pixel.png"; OT_AFTER=$(jqr '.photoUrl')
req POST "/overtime/$OT_ID/after-photo" "{\"afterPhotoUrl\":\"$OT_AFTER\"}"; okin "attach the after-work photo" "200 204"
req GET "/overtime/$OT_ID"; ok "read the OT request back" 200
TOKEN="$SUP_TOKEN"
req GET /overtime/team; ok "supervisor opens the OT queue" 200
note "OT awaiting supervisor" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
req POST "/overtime/$OT_ID/approve" '{}'; okin "supervisor approves the OT" "200 204"
note "ot after approval" "$(jqr '.status')"
TOKEN="$OWNER_TOKEN"; req GET /overtime/all; ok "admin lists all OT" 200

printf '%s\n' "$ORG_ID|$PROJ_ID|$SHIFT_ID|$POLICY_ID|$ACC_ID|$BANK_ID|$LT_ID|$OWNER_TOKEN|$STAMP|$EMP_ID|$SUP_ID|$TEAM_ID|$EMP_TOKEN|$SUP_TOKEN|$CLAIM_ID|$LEAVE_ID" > "$SP/state.txt"
echo; echo "──────── Phase 4 subtotal: PASS=$PASS FAIL=$FAIL"
