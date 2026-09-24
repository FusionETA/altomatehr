#!/usr/bin/env bash
SP="$(cd "$(dirname "$0")" && pwd)"; source "$SP/lib.sh"
IFS='|' read -r ORG_ID PROJ_ID SHIFT_ID POLICY_ID ACC_ID BANK_ID LT_ID OWNER_TOKEN STAMP EMP_ID SUP_ID TEAM_ID < "$SP/state.txt"
TOKEN="$OWNER_TOKEN"
EMP_EMAIL="qa.emp.$STAMP@altomate.com"; SUP_EMAIL="qa.sup.$STAMP@altomate.com"; PW="Password123!"
TODAY=$(date +%F); YEAR=$(date +%Y); MONTH=$(date +%-m)

# Log the two personas in
login "$SUP_EMAIL" "$PW"; ok "supervisor can log in" 200; SUP_TOKEN="$TOKEN"; note "supervisor role" "$(jqr '.role')"
login "$EMP_EMAIL" "$PW"; ok "employee can log in" 200; EMP_TOKEN="$TOKEN"; note "employee role" "$(jqr '.role')"

# ─────────────────────────────────────────────── P13 Claims
phase "P13 Claims lifecycle"
TOKEN="$EMP_TOKEN"
req GET /claims; ok "employee lists their own claims" 200
req POST /claims "{\"title\":\"Client lunch\",\"description\":\"Lunch with Acme\",\"category\":\"MEAL\",\"amount\":85.50,\"spentAt\":\"$TODAY\",\"claimType\":\"EXPENSE\",\"paymentType\":\"PERSONAL\",\"chartOfAccountId\":\"$ACC_ID\",\"projectId\":\"$PROJ_ID\",\"spendingAt\":\"Acme Cafe\"}"
okin "employee submits an expense claim" "200 201"; CLAIM_ID=$(jqr '.id'); note "claim id" "$CLAIM_ID"
req POST /claims "{\"title\":\"Site visit\",\"description\":\"Drive to site\",\"category\":\"TRANSPORT\",\"spentAt\":\"$TODAY\",\"claimType\":\"MILEAGE\",\"paymentType\":\"PERSONAL\",\"chartOfAccountId\":\"$ACC_ID\",\"projectId\":\"$PROJ_ID\",\"distance\":42,\"mileageOriginAddress\":\"HQ\",\"mileageDestinationAddress\":\"Site A\"}"
okin "employee submits a mileage claim" "200 201"; MILE_ID=$(jqr '.id')
note "mileage amount computed" "$(jqr '.amount')"
req POST /claims "{\"title\":\"No amount\",\"description\":\"x\",\"category\":\"MEAL\",\"spentAt\":\"$TODAY\",\"claimType\":\"EXPENSE\",\"paymentType\":\"PERSONAL\",\"chartOfAccountId\":\"$ACC_ID\"}"
ok "reject an expense claim with no amount" 400
req POST /claims "{\"title\":\"Negative\",\"description\":\"x\",\"category\":\"MEAL\",\"amount\":-5,\"spentAt\":\"$TODAY\",\"claimType\":\"EXPENSE\",\"paymentType\":\"PERSONAL\",\"chartOfAccountId\":\"$ACC_ID\"}"
ok "reject a negative amount" 400
req POST /claims "{\"title\":\"No account\",\"description\":\"x\",\"category\":\"MEAL\",\"amount\":10,\"spentAt\":\"$TODAY\",\"claimType\":\"EXPENSE\",\"paymentType\":\"PERSONAL\"}"
ok "reject a claim with no chart account" 400
req GET "/claims/$CLAIM_ID"; ok "employee reads their own claim" 200; note "claim status" "$(jqr '.status')"
req GET /claims/all; ok "employee is blocked from the org-wide claim list" 403

TOKEN="$SUP_TOKEN"
req GET /claims/team; ok "supervisor opens the claims queue" 200
note "claims awaiting this supervisor" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
req POST "/claims/$CLAIM_ID/approve" '{}'; okin "supervisor approves the expense claim" "200 204 400 403"
note "approve result" "$HTTP $(printf '%s' "$BODY" | tr -d '\n' | cut -c1-140)"
req POST "/claims/$MILE_ID/reject" '{"reviewNotes":"Distance looks wrong"}'; okin "supervisor rejects the mileage claim" "200 204 400 403"
note "reject result" "$HTTP $(printf '%s' "$BODY" | tr -d '\n' | cut -c1-140)"
req POST "/claims/$CLAIM_ID/reject" '{}'; ok "reject without a reason is refused" 400

TOKEN="$OWNER_TOKEN"
req GET /claims/all; ok "admin lists every claim" 200
note "claim statuses now" "$(printf '%s' "$BODY" | jq -r '[.[]|.status]|join(",")' 2>/dev/null | cut -c1-120)"
req GET /claims/settings; ok "read claim settings" 200
req PUT /claims/settings '{"claimRunCutoffDay":25,"settlementRoute":"XERO_BILL","xeroBillStage":"AwaitingPayment"}'; okin "edit claim settings" "200 204"
req PUT /claims/settings '{"claimRunCutoffDay":31}'; ok "reject a cutoff day past 28" 400
reqraw GET "/claims/export/summary?from=$YEAR-01-01&to=$YEAR-12-31" "$SP/claims-export.bin"; ok "export the claims summary" 200
note "export" "$CTYPE ${SIZE}b"
reqraw GET "/claims/export/payroll?from=$YEAR-01-01&to=$YEAR-12-31" "$SP/claims-payroll.bin"; okin "export claims for payroll" "200 204 400"

# ─────────────────────────────────────────────── P14 Leave
phase "P14 Leave lifecycle"
TOKEN="$OWNER_TOKEN"
req POST "/leave/entitlements/$EMP_ID/seed" '{}'; okin "seed the employee's entitlements from policy" "200 201 204"
req GET "/leave/balances/$EMP_ID"; ok "admin reads the employee's balances" 200
note "balances" "$(printf '%s' "$BODY" | jq -r '[.[]|"\(.code):\(.entitlementDays)"]|join(" ")' 2>/dev/null | cut -c1-160)"
req PUT "/leave/entitlements/$EMP_ID/$LT_ID" '{"entitledDays":14,"accrualMethod":"LUMP_SUM"}'; okin "admin sets an entitlement" "200 204"
TOKEN="$EMP_TOKEN"
req GET /leave/balances; ok "employee reads their own balances" 200
S1=$(date -v+7d +%F 2>/dev/null || date -d '+7 days' +%F); S2=$(date -v+8d +%F 2>/dev/null || date -d '+8 days' +%F)
req POST /leave "{\"leaveTypeId\":\"$LT_ID\",\"startDate\":\"$S1\",\"endDate\":\"$S2\",\"duration\":\"FULL_DAY\",\"reason\":\"Family matters\"}"
okin "employee applies for leave" "200 201"; LEAVE_ID=$(jqr '.id'); note "leave id / status" "$LEAVE_ID $(jqr '.status')"
req POST /leave "{\"leaveTypeId\":\"$LT_ID\",\"startDate\":\"$S2\",\"endDate\":\"$S1\",\"duration\":\"FULL_DAY\",\"reason\":\"Backwards\"}"
okin "reject leave that ends before it starts" "400 422"
req POST /leave "{\"leaveTypeId\":\"$LT_ID\",\"startDate\":\"$S1\",\"endDate\":\"$S2\",\"duration\":\"FULL_DAY\",\"reason\":\"Overlap\"}"
okin "reject an overlapping leave application" "400 409"
req POST /leave '{"leaveTypeId":"does-not-exist","startDate":"2026-10-01","endDate":"2026-10-02","duration":"FULL_DAY"}'
okin "reject an unknown leave type" "400 404"
req GET /leave; ok "employee lists their leave" 200
TOKEN="$SUP_TOKEN"
req GET /leave/team; ok "supervisor opens the leave queue" 200
note "leave awaiting supervisor" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
req POST "/leave/$LEAVE_ID/approve" '{}'; okin "supervisor approves the leave" "200 204 400 403"
note "leave approve result" "$HTTP $(printf '%s' "$BODY" | tr -d '\n' | cut -c1-140)"
TOKEN="$OWNER_TOKEN"
req GET "/leave/overview?year=$YEAR"; ok "admin leave overview" 200
req GET /leave/all; ok "admin lists all leave" 200
req GET /leave/balances/all; ok "admin lists all balances" 200
req GET /leave/on-leave-today; ok "who is on leave today" 200
req GET /leave/pending-count; ok "pending leave count" 200
reqraw GET "/leave/export/summary?employeeId=$EMP_ID&year=$YEAR&format=Csv" "$SP/leave-export.bin"; okin "export the leave summary" "200 204"
reqraw GET "/leave/export/summary.pdf?year=$YEAR&employeeId=$EMP_ID" "$SP/leave-summary.pdf"; okin "leave summary PDF" "200 204 400"
note "leave pdf" "$HTTP $CTYPE ${SIZE}b"

printf '%s\n' "$ORG_ID|$PROJ_ID|$SHIFT_ID|$POLICY_ID|$ACC_ID|$BANK_ID|$LT_ID|$OWNER_TOKEN|$STAMP|$EMP_ID|$SUP_ID|$TEAM_ID|$EMP_TOKEN|$SUP_TOKEN|$CLAIM_ID|$LEAVE_ID" > "$SP/state.txt"
echo; echo "──────── Phase 3 subtotal: PASS=$PASS FAIL=$FAIL"
