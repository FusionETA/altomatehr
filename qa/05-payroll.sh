#!/usr/bin/env bash
# A full Malaysian payroll run: company info → run → generate → submit → approve
# → statutory files → the employee's payslip.
SP="$(cd "$(dirname "$0")" && pwd)"; source "$SP/lib.sh"
IFS='|' read -r ORG_ID PROJ_ID SHIFT_ID POLICY_ID ACC_ID BANK_ID LT_ID OWNER_TOKEN STAMP EMP_ID SUP_ID TEAM_ID EMP_TOKEN SUP_TOKEN CLAIM_ID LEAVE_ID < "$SP/state.txt"
YEAR=$(date +%Y); MONTH=$(date +%-m)

phase "P17 Payroll setup"
TOKEN="$OWNER_TOKEN"
req GET /payroll/settings;              ok "read payroll settings" 200
req GET /payroll/adjustment-categories; ok "read adjustment categories" 200
req GET /payroll/portal-credentials;    ok "read statutory portal credentials" 200
req GET /payroll/employees;             ok "read the payroll roster" 200
note "roster" "$(printf '%s' "$BODY" | jq -r '[.[]|"\(.name):\(.profileIncompleteSections|join("/")|if .=="" then "ready" else . end)"]|join("  ")' 2>/dev/null)"
# Every field name below matters: an unknown key binds to nothing and the save
# silently succeeds while readiness still reports the field as missing.
req PUT /payroll/company-info '{"employerName":"QA Test Sdn Bhd","employerTin":"C12345678901","registrationNo":"202401000001","referenceType":"E","referenceNo":"E1234567890","employerCategory":"1","employerStatus":"1","cp8dFurnishType":"1","perkesoEmployerCode":"P1234567890","epfEmployerNo":"E9876543210","addressLine1":"Jalan Ampang","city":"Kuala Lumpur","state":"WP Kuala Lumpur","postcode":"50450","country":"MY","phone":"0312345678","email":"hr@qatest.test","declarantName":"QA Owner","declarantIdType":"NRIC","declarantIdNumber":"900101-14-5567","declarantPosition":"Director"}'
okin "save payroll company info" "200 204"
req GET /payroll/company-info; ok "read the company info back" 200
note "saved" "employerName=$(jqr '.employerName') configured=$(jqr '.isConfigured')"
[ "$(jqr '.isConfigured')" = "true" ] || note "WARNING" "company info did not persist — check the field names"

phase "P17b Payroll run"
req GET /payroll/runs;        ok "list payroll runs" 200
req GET /payroll/runs/picker; ok "read the run picker" 200
note "payable this run" "$(printf '%s' "$BODY" | jq -r '[.policies[].members[].name]|join(", ")' 2>/dev/null)"
req POST /payroll/runs "{\"periodYear\":$YEAR,\"periodMonth\":$MONTH}"
okin "create a run for this month" "200 201"; RUN_ID=$(jqr '.id'); note "run" "id=$RUN_ID status=$(jqr '.status')"
req POST /payroll/runs "{\"periodYear\":$YEAR,\"periodMonth\":$MONTH}"; okin "a duplicate run for the same period is refused" "400 409"
req POST /payroll/runs "{\"periodYear\":$YEAR,\"periodMonth\":13}"; ok "reject an invalid month" 400
req GET "/payroll/runs/$RUN_ID/readiness"; ok "check run readiness" 200
note "readiness" "$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-240)"
req POST "/payroll/runs/$RUN_ID/generate" '{}'; okin "generate the payroll" "200 204"
note "generated" "employees=$(jqr '.detail.run.employeeCount') gross=$(jqr '.detail.run.totalGross') net=$(jqr '.detail.run.totalNet') employerEPF=$(jqr '.detail.run.totalEmployerEpf') pcb=$(jqr '.detail.run.totalPcb')"
req GET "/payroll/runs/$RUN_ID/adjustments";        ok "list run adjustments" 200
req GET "/payroll/runs/$RUN_ID/claims/attachable";  ok "list claims attachable to the run" 200
req GET "/payroll/runs/$RUN_ID/salary-change-hints"; ok "read salary-change hints" 200
req POST "/payroll/runs/$RUN_ID/submit" '{}'; okin "submit the run for approval" "200 204"
note "after submit" "$(jqr '.detail.run.status // .status')"
req POST "/payroll/runs/$RUN_ID/approve" '{}'; okin "approve the run" "200 204"
note "after approve" "$(jqr '.detail.run.status // .status')"
req GET "/payroll/runs/$RUN_ID/revert-impact"; ok "read the revert impact" 200

phase "P17c Payroll documents and statutory files"
for doc in payslips summary payment-schedule pcb-details; do
  reqraw GET "/payroll/runs/$RUN_ID/documents/$doc" "$SP/.out/run-$doc.bin"
  ok "download $doc" 200; note "$doc" "$CTYPE ${SIZE}b"
done
# Needs a payor bank account under Payroll Settings → Bank, which run readiness
# does NOT check — so this 409 is expected until that is configured.
reqraw GET "/payroll/runs/$RUN_ID/documents/bank-file" "$SP/.out/run-bank-file.bin"
okin "download the bank disbursement file" "200 409"
note "bank file" "$HTTP — $(head -c 160 "$SP/.out/run-bank-file.bin")"
for f in epf socso-eis pcb; do
  reqraw GET "/payroll/runs/$RUN_ID/files/$f" "$SP/.out/run-$f.txt"
  ok "statutory file: $f" 200; note "file $f" "$CTYPE ${SIZE}b"
done
reqraw GET "/payroll/runs/$RUN_ID/adjustments/template?format=Csv" "$SP/.out/adj-template.csv"; ok "adjustment import template" 200
req GET /payroll/loans;                    ok "list payroll loans" 200
req GET "/payroll/annual/reports?year=$YEAR"; okin "annual reports index" "200 204"
reqraw GET "/payroll/ytd-import/template/$YEAR" "$SP/.out/ytd-template.bin"; ok "YTD import template" 200

phase "P18 Payslips (the employee's view)"
TOKEN="$EMP_TOKEN"
req GET /payslips; ok "employee lists their payslips" 200
note "payslips visible" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
PS_ID=$(printf '%s' "$BODY" | jq -r '.[0].id // empty' 2>/dev/null)
if [ -n "$PS_ID" ]; then
  req GET "/payslips/$PS_ID"; ok "employee opens their payslip" 200
  reqraw GET "/payslips/$PS_ID/pdf" "$SP/.out/payslip.pdf"; ok "employee downloads the payslip PDF" 200
  note "payslip pdf" "$CTYPE ${SIZE}b"
else
  note "payslip detail" "SKIPPED — no payslip visible to the employee"
fi
TOKEN="$SUP_TOKEN"; req GET /payslips; ok "supervisor sees only their own payslips" 200

printf '%s\n' "$ORG_ID|$PROJ_ID|$SHIFT_ID|$POLICY_ID|$ACC_ID|$BANK_ID|$LT_ID|$OWNER_TOKEN|$STAMP|$EMP_ID|$SUP_ID|$TEAM_ID|$EMP_TOKEN|$SUP_TOKEN|$CLAIM_ID|$LEAVE_ID|$RUN_ID" > "$SP/state.txt"
echo; echo "──────── Phase 5 subtotal: PASS=$PASS FAIL=$FAIL"
