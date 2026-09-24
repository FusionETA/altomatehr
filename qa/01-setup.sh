#!/usr/bin/env bash
# AltomateHR — end-to-end functional smoke test, normal-user perspective.
# Walks a brand-new company from first login to an approved payroll run.
SP="$(cd "$(dirname "$0")" && pwd)"
source "$SP/lib.sh"
: > "$RESULTS"
STAMP=$(date +%H%M%S)
ADMIN_EMAIL="admin@altomate.com"; ADMIN_PW="password123"
NEW_ADMIN="qa.owner.$STAMP@altomate.com"
EMP_EMAIL="qa.emp.$STAMP@altomate.com"; EMP_PW="Password123!"
SUP_EMAIL="qa.sup.$STAMP@altomate.com"; SUP_PW="Password123!"
TODAY=$(date +%F); YEAR=$(date +%Y)

# ─────────────────────────────────────────────── P01 Authentication
phase "P01 Authentication"
login "$ADMIN_EMAIL" "wrong-password"; ok "login rejects a wrong password" 401
login "nobody@nowhere.test" "password123"; ok "login rejects an unknown email" 401
login "$ADMIN_EMAIL" "$ADMIN_PW";        ok "login as seeded owner" 200
OWNER_TOKEN="$TOKEN"
[ -n "$TOKEN" ] && note "token issued" "role=$(jqr '.role') org=$(jqr '.activeOrganizationId')"
req GET /auth/orgs;                       ok "list the orgs I belong to" 200
TOKEN=""; req GET /employees;             ok "protected endpoint refuses an anonymous caller" 401
TOKEN="bad.token.value"; req GET /employees; ok "protected endpoint refuses a malformed token" 401
TOKEN="$OWNER_TOKEN"
req POST /auth/forgot-password '{"email":"nobody@nowhere.test"}'
okin "forgot-password does not leak whether an email exists" "200 204 202"

# ─────────────────────────────────────────────── P02 Create a company
phase "P02 Create a company"
req POST /organizations "{\"name\":\"QA Test Sdn Bhd $STAMP\"}"; ok "create a new company" 200
ORG_ID=$(jqr '.id'); note "new org id" "$ORG_ID"
req POST "/auth/switch-org/$ORG_ID" '{}'; ok "switch into the new company" 200
NEW_TOKEN=$(jqr '.token'); [ -n "$NEW_TOKEN" ] && [ "$NEW_TOKEN" != "null" ] && TOKEN="$NEW_TOKEN"
req GET /organizations/current;           ok "read the new company profile" 200
note "active org after switch" "$(jqr '.name') / $(jqr '.id')"
req GET /organizations/modules;           ok "read enabled modules" 200
req PUT /organizations/current '{"name":"QA Test Sdn Bhd (renamed)","defaultCurrency":"MYR","mileageUnit":"KM","geofenceRadiusMeters":250}'
okin "edit company settings" "200 204"
req GET /organizations/admins;            ok "list company admins (Owner only)" 200

# ─────────────────────────────────────────────── P03 Chart of accounts
phase "P03 Chart of accounts"
req GET /accounts;                        ok "list accounts (empty new company)" 200
req POST /accounts '{"code":"6100","name":"Travel & Meals","type":"EXPENSE","isSelectable":true,"limitAmount":500,"allowMileageClaim":true,"mileageRate":0.60}'
okin "create an expense account" "200 201"
ACC_ID=$(jqr '.id')
req POST /accounts '{"code":"1100","name":"Maybank Current","type":"BANK","isSelectable":true}'
okin "create a bank account" "200 201"
BANK_ID=$(jqr '.id')
req POST /accounts '{"code":"","name":"No code","type":"EXPENSE"}'; ok "reject an account with no code" 400
req POST /accounts '{"code":"9000","name":"Bad type","type":"REVENUE"}'; ok "reject an invalid account type" 400
req PUT "/accounts/$ACC_ID" '{"code":"6100","name":"Travel, Meals & Mileage","type":"EXPENSE","isSelectable":true,"limitAmount":800,"allowMileageClaim":true,"mileageRate":0.70}'
okin "edit an account" "200 204"

# ─────────────────────────────────────────────── P04 Projects / sites
phase "P04 Projects and sites"
req GET /projects;                        ok "list projects" 200
req POST /projects '{"name":"HQ Kuala Lumpur","location":"Jalan Ampang, KL","latitude":3.1578,"longitude":101.7117,"workingHoursStart":"09:00","workingHoursEnd":"18:00","workingDays":"1,2,3,4,5","lunchBreakMinutes":60,"geofencePoints":[{"label":"Main gate","latitude":3.1578,"longitude":101.7117}],"allowedIpEntries":[{"label":"Office WiFi","cidr":"203.0.113.0/24"}]}'
okin "create a project with a geofence and IP allowlist" "200 201"
PROJ_ID=$(jqr '.id'); note "project id" "$PROJ_ID"
req GET "/projects/$PROJ_ID";             ok "read one project" 200
req GET /projects/my-ip;                  ok "read the caller's IP (allowlist helper)" 200
req POST /projects '{"name":"Bad coords","latitude":999,"longitude":0}'; ok "reject an out-of-range latitude" 400
req PUT "/projects/$PROJ_ID" '{"name":"HQ Kuala Lumpur","location":"Jalan Ampang, KL","latitude":3.1578,"longitude":101.7117,"workingHoursStart":"09:00","workingHoursEnd":"18:30","workingDays":"1,2,3,4,5","lunchBreakMinutes":45,"geofencePoints":[{"label":"Main gate","latitude":3.1578,"longitude":101.7117}],"allowedIpEntries":[{"label":"Office WiFi","cidr":"203.0.113.0/24"}]}'
okin "edit a project" "200 204"

# ─────────────────────────────────────────────── P05 Shifts
phase "P05 Work schedule (shifts)"
req GET /shifts;                          ok "list shifts" 200
req POST /shifts "{\"projectId\":\"$PROJ_ID\",\"name\":\"Normal 9-6\",\"startTime\":\"09:00\",\"endTime\":\"18:00\",\"workingDays\":\"1,2,3,4,5\",\"lunchBreakMinutes\":60}"
okin "create a shift" "200 201"
SHIFT_ID=$(jqr '.id')
req POST /shifts "{\"projectId\":\"$PROJ_ID\",\"name\":\"Bad time\",\"startTime\":\"9am\",\"endTime\":\"18:00\"}"
ok "reject a badly formatted shift time" 400
req POST "/shifts/$SHIFT_ID/default" '{}'; okin "set the default shift" "200 204"
req PUT "/shifts/$SHIFT_ID" '{"name":"Normal 9-6 (updated)","startTime":"09:00","endTime":"18:00","workingDays":"1,2,3,4,5","lunchBreakMinutes":45}'
okin "edit a shift" "200 204"

# ─────────────────────────────────────────────── P06 Holidays
phase "P06 Public holidays"
req GET "/holidays?year=$YEAR";           ok "list holidays" 200
req POST /holidays "{\"date\":\"$YEAR-12-25\",\"name\":\"Christmas Day\"}"; okin "create an org-wide holiday" "200 201"
HOL_ID=$(jqr '.id')
req PUT "/holidays/$HOL_ID" "{\"date\":\"$YEAR-12-25\",\"name\":\"Christmas\"}"; okin "edit a holiday" "200 204"

# ─────────────────────────────────────────────── P07 Leave types
phase "P07 Leave types"
req GET /leave-types;                     ok "list leave types (auto-seeded with the company)" 200
LT_COUNT=$(printf '%s' "$BODY" | jq 'length' 2>/dev/null); note "leave types present" "$LT_COUNT"
LT_ID=$(printf '%s' "$BODY" | jq -r '.[0].id' 2>/dev/null)
req POST /leave-types '{"code":"QA","name":"QA Special Leave","paid":true,"defaultDays":3,"accrualMethod":"LUMP_SUM"}'
okin "create a custom leave type" "200 201 400"
CUSTOM_LT=$(jqr '.id')
req GET /leave-types/count;               ok "count leave types" 200

# ─────────────────────────────────────────────── P08 Policies
phase "P08 Employee policies"
req GET /policies;                        ok "list policies" 200
req POST /policies "{\"name\":\"Standard Staff\",\"description\":\"Default policy for office staff\",\"canAccessAttendance\":true,\"canAccessClaims\":true,\"canAccessLeave\":true,\"requireGeofence\":false,\"requireSelfie\":false,\"requireIpWhitelist\":false,\"geolocationEnabled\":true,\"salaryType\":\"MONTHLY\",\"otEnabled\":true,\"otDailyThresholdMinutes\":480,\"otMethod\":\"CASH\",\"otRateNormalDay\":1.5,\"otRateRestDay\":2.0,\"otRatePublicHoliday\":3.0,\"otRateRestDayInShift\":1.0,\"otRatePublicHolidayInShift\":2.0,\"leaveEntitlements\":[{\"leaveTypeId\":\"$LT_ID\",\"defaultDays\":14}]}"
okin "create a policy with leave entitlements" "200 201"
POLICY_ID=$(jqr '.id'); note "policy id" "$POLICY_ID"
req POST /policies '{"name":"","canAccessClaims":true}';  ok "reject a policy with no name" 400
req POST /policies '{"name":"Bad OT","otRateNormalDay":500}'; ok "reject an out-of-range OT rate" 400
req PUT "/policies/$POLICY_ID" "{\"name\":\"Standard Staff\",\"description\":\"Edited during QA\",\"canAccessAttendance\":true,\"canAccessClaims\":true,\"canAccessLeave\":true,\"requireGeofence\":false,\"requireSelfie\":false,\"requireIpWhitelist\":false,\"geolocationEnabled\":true,\"salaryType\":\"MONTHLY\",\"otEnabled\":true,\"otDailyThresholdMinutes\":480,\"otMethod\":\"CASH\",\"otRateNormalDay\":2.0,\"otRateRestDay\":2.0,\"otRatePublicHoliday\":3.0,\"otRateRestDayInShift\":1.0,\"otRatePublicHolidayInShift\":2.0,\"leaveEntitlements\":[{\"leaveTypeId\":\"$LT_ID\",\"defaultDays\":16}]}"
okin "edit the policy (the flow you asked about)" "200 204"
req POST "/policies/$POLICY_ID/default" '{}'; okin "make it the default policy" "200 204"
req GET /policies; ok "re-list policies after edit" 200
note "default policy is" "$(printf '%s' "$BODY" | jq -r '[.[]|select(.isDefault)]|.[0].name' 2>/dev/null)"

printf '%s\n' "$ORG_ID|$PROJ_ID|$SHIFT_ID|$POLICY_ID|$ACC_ID|$BANK_ID|$LT_ID|$TOKEN|$STAMP" > "$SP/state.txt"
echo; echo "──────── Phase 1 subtotal: PASS=$PASS FAIL=$FAIL"
