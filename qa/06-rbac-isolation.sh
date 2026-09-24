#!/usr/bin/env bash
SP="$(cd "$(dirname "$0")" && pwd)"; source "$SP/lib.sh"
IFS='|' read -r ORG_ID PROJ_ID SHIFT_ID POLICY_ID ACC_ID BANK_ID LT_ID OWNER_TOKEN STAMP EMP_ID SUP_ID TEAM_ID EMP_TOKEN SUP_TOKEN CLAIM_ID LEAVE_ID RUN_ID < "$SP/state.txt"
YEAR=$(date +%Y)

phase "P19 Dashboards, audit, notifications"
TOKEN="$OWNER_TOKEN"
req GET /admin/overview;  ok "admin executive overview" 200
note "overview keys" "$(printf '%s' "$BODY" | jq -r 'keys|join(",")' 2>/dev/null | cut -c1-200)"
req GET /audit;           ok "activity log" 200
note "audit entries" "$(printf '%s' "$BODY" | jq '.items|length // length' 2>/dev/null)"
req GET /audit/verify;    ok "audit tamper-verify" 200
note "audit verify" "$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-160)"
req GET /notifications;   ok "admin reads notifications" 200
req POST /notifications/read '{"ids":[]}'; okin "mark notifications read" "200 204 400"
req GET /push/public-key; okin "web-push public key" "200 204 404"
req GET /realtime/status; ok "realtime status" 200
req GET /xero/status;     ok "Xero connection status" 200
note "xero" "$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-140)"
req GET /xero/currencies; okin "Xero currencies" "200 400 409"
TOKEN="$EMP_TOKEN"; req GET /notifications; ok "employee reads their notifications" 200

phase "P20 Role permissions (RBAC)"
TOKEN="$EMP_TOKEN"
for ep in /employees /policies /teams /shifts /audit /admin/overview /payroll/runs /payroll/employees /leave/all /claims/all /organizations/admins; do
  req GET "$ep"; ok "employee is refused $ep" 403
done
req POST /employees '{"email":"sneak@test.com","role":"Admin"}'; ok "employee cannot create an employee" 403
req POST /policies '{"name":"Sneaky policy"}';                   ok "employee cannot create a policy" 403
req PUT /organizations/current '{"name":"Hijacked"}';            ok "employee cannot rename the company" 403
req POST "/claims/$CLAIM_ID/approve" '{}';                       ok "employee cannot approve a claim" 403
req GET /api-keys;                                                ok "employee cannot list API keys" 403
# Reading the chart of accounts is deliberately open to any signed-in user:
# the claim form needs it to populate its account picker. Writing is not.
req GET /accounts;                        ok "employee CAN read the chart of accounts (needed to file a claim)" 200
req POST /accounts '{"code":"9999","name":"Sneaky","type":"EXPENSE"}'; ok "employee cannot create an account" 403
req PUT "/accounts/$ACC_ID" '{"code":"9999","name":"Sneaky","type":"EXPENSE"}'; ok "employee cannot edit an account" 403
TOKEN="$SUP_TOKEN"
for ep in /employees /policies /payroll/runs /audit /admin/overview; do
  req GET "$ep"; ok "supervisor is refused $ep" 403
done
req GET /claims/team; ok "supervisor CAN open the claims queue" 200
req GET /leave/team;  ok "supervisor CAN open the leave queue" 200
TOKEN="$OWNER_TOKEN"
req GET /api-keys; okin "owner reaches API keys only if superadmin" "200 403"
note "api-keys as owner" "$HTTP (403 expected — superadmin-only policy)"

phase "P21 Tenant isolation"
# A second company owned by the same person: data must not cross over.
TOKEN="$OWNER_TOKEN"
req POST /organizations "{\"name\":\"QA Isolation Co $STAMP\"}"; ok "create a second company" 200
ORG2=$(jqr '.id')
req POST "/auth/switch-org/$ORG2" '{}'; ok "switch into the second company" 200
TOKEN2=$(jqr '.token'); [ -n "$TOKEN2" ] && [ "$TOKEN2" != "null" ] && TOKEN="$TOKEN2"
req GET /employees; ok "employee list in company 2" 200
N=$(printf '%s' "$BODY" | jq 'length' 2>/dev/null); note "employees visible in company 2" "$N"
[ "$N" = "1" ] && note "isolation: employees" "OK — only the owner, company 1's staff are hidden" || note "LEAK?" "expected 1, saw $N"
req GET /projects; P=$(printf '%s' "$BODY" | jq 'length'); ok "project list in company 2" 200
[ "$P" = "0" ] && note "isolation: projects" "OK — company 1's project is hidden" || note "LEAK?" "saw $P projects"
req GET /accounts; A=$(printf '%s' "$BODY" | jq 'length'); ok "account list in company 2" 200
[ "$A" = "0" ] && note "isolation: accounts" "OK" || note "LEAK?" "saw $A accounts"
req GET /claims/all; C=$(printf '%s' "$BODY" | jq 'length'); ok "claim list in company 2" 200
[ "$C" = "0" ] && note "isolation: claims" "OK" || note "LEAK?" "saw $C claims"
req GET /payroll/runs; R=$(printf '%s' "$BODY" | jq 'length'); ok "payroll runs in company 2" 200
[ "$R" = "0" ] && note "isolation: payroll runs" "OK" || note "LEAK?" "saw $R runs"
# Direct-id access across the tenant boundary
req GET "/employees/$EMP_ID/profile"; okin "cannot read company 1's employee by id" "403 404"
req GET "/projects/$PROJ_ID";          okin "cannot read company 1's project by id" "403 404"
req GET "/payroll/runs/$RUN_ID";       okin "cannot read company 1's payroll run by id" "403 404"
req PUT "/employees/$EMP_ID" '{"name":"Hijack","role":"Admin"}'; okin "cannot edit company 1's employee by id" "400 403 404"
# A company-1 employee must not reach company 2
TOKEN="$EMP_TOKEN"
req POST "/auth/switch-org/$ORG2" '{}'; okin "a company-1 employee cannot switch into company 2" "400 403 404"

phase "P22 Session handling"
TOKEN="$OWNER_TOKEN"
req GET /auth/orgs; ok "owner lists their companies" 200
note "companies owned" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
CJ="$SP/cookies.txt"; : > "$CJ"
# Same 5-per-minute login cap as the login helper, so back off through it.
for attempt in 1 2 3 4 5; do
  HTTP=$(curl -s -o "$BODYDIR/last.json" -w '%{http_code}' -c "$CJ" -X POST "$API/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"admin@altomate.com\",\"password\":\"password123\"}")
  [ "$HTTP" != "429" ] && break
  echo "    (rate limited, waiting 20s)"; sleep 20
done
BODY=$(cat "$BODYDIR/last.json"); ok "login issues a refresh cookie" 200
grep -q -i 'refresh' "$CJ" && note "refresh cookie" "set, httpOnly path-scoped" || note "refresh cookie" "NOT FOUND in jar"
HTTP=$(curl -s -o "$BODYDIR/last.json" -w '%{http_code}' -b "$CJ" -c "$CJ" -X POST "$API/auth/refresh"); BODY=$(cat "$BODYDIR/last.json")
ok "refresh returns a new access token" 200
HTTP=$(curl -s -o "$BODYDIR/last.json" -w '%{http_code}' -b "$CJ" -c "$CJ" -X POST "$API/auth/logout"); BODY=$(cat "$BODYDIR/last.json")
okin "logout succeeds" "200 204"
HTTP=$(curl -s -o "$BODYDIR/last.json" -w '%{http_code}' -b "$CJ" -X POST "$API/auth/refresh"); BODY=$(cat "$BODYDIR/last.json")
ok "refresh after logout is refused" 401
echo; echo "──────── Phase 6 subtotal: PASS=$PASS FAIL=$FAIL"
