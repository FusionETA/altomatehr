#!/usr/bin/env bash
SP="$(cd "$(dirname "$0")" && pwd)"; source "$SP/lib.sh"
IFS='|' read -r ORG_ID PROJ_ID SHIFT_ID POLICY_ID ACC_ID BANK_ID LT_ID TOKEN STAMP < "$SP/state.txt"
OWNER_TOKEN="$TOKEN"
EMP_EMAIL="qa.emp.$STAMP@altomate.com"; SUP_EMAIL="qa.sup.$STAMP@altomate.com"; PW="Password123!"
IMP_EMAIL="qa.import.$STAMP@altomate.com"
TODAY=$(date +%F); YEAR=$(date +%Y)

# ─────────────────────────────────────────────── P09 Employees
phase "P09 Employees (create + edit)"
req GET /employees;  ok "list employees in the new company" 200
note "employees before setup" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
req POST /employees "{\"email\":\"$SUP_EMAIL\",\"password\":\"$PW\",\"name\":\"Siti Supervisor\",\"employeeNumber\":\"E001\",\"jobTitle\":\"Team Lead\",\"joinDate\":\"2024-01-15\",\"role\":\"Supervisor\",\"policyId\":\"$POLICY_ID\",\"shiftId\":\"$SHIFT_ID\"}"
okin "create a supervisor" "200 201"; SUP_ID=$(jqr '.id')
req POST /employees "{\"email\":\"$EMP_EMAIL\",\"password\":\"$PW\",\"name\":\"Ahmad Employee\",\"employeeNumber\":\"E002\",\"jobTitle\":\"Executive\",\"joinDate\":\"2024-06-01\",\"role\":\"Employee\",\"policyId\":\"$POLICY_ID\",\"shiftId\":\"$SHIFT_ID\"}"
okin "create an employee" "200 201"; EMP_ID=$(jqr '.id')
note "ids" "sup=$SUP_ID emp=$EMP_ID"
req POST /employees "{\"email\":\"$EMP_EMAIL\",\"password\":\"$PW\",\"name\":\"Duplicate\",\"role\":\"Employee\"}"
okin "reject a duplicate employee email" "400 409"
req POST /employees '{"email":"not-an-email","role":"Employee"}'; ok "reject a malformed employee email" 400
req PUT "/employees/$EMP_ID" "{\"name\":\"Ahmad bin Employee\",\"email\":\"$EMP_EMAIL\",\"employeeNumber\":\"E002\",\"jobTitle\":\"Senior Executive\",\"joinDate\":\"2024-06-01\",\"role\":\"Employee\",\"policyId\":\"$POLICY_ID\",\"shiftId\":\"$SHIFT_ID\"}"
okin "edit an employee" "200 204"

phase "P10 Employee profile (payroll master data)"
req GET "/employees/$EMP_ID/profile"; ok "read the employee profile" 200
req PUT "/employees/$EMP_ID/profile" "{\"idType\":\"NRIC\",\"idNumber\":\"900202-14-5501\",\"nationality\":\"Malaysian\",\"gender\":\"MALE\",\"maritalStatus\":\"SINGLE\",\"dateOfBirth\":\"1990-02-02\",\"joinDate\":\"2024-06-01\",\"department\":\"Operations\",\"salaryType\":\"MONTHLY\",\"monthlySalary\":5000,\"epfNumber\":\"12345678\",\"epfEmployeeRate\":11,\"contributeToEpf\":true,\"socsoNumber\":\"900202145501\",\"contributeToEis\":true,\"incomeTaxNumber\":\"SG11111111111\",\"isResident\":true,\"bankName\":\"Maybank\",\"bankAccountNumber\":\"1122334455\",\"bankAccountHolderName\":\"Ahmad bin Employee\",\"paymentMethod\":\"BANK_TRANSFER\"}"
okin "save statutory + bank details" "200 204"
req PUT "/employees/$SUP_ID/profile" "{\"idType\":\"NRIC\",\"idNumber\":\"880303-14-5502\",\"nationality\":\"Malaysian\",\"gender\":\"FEMALE\",\"maritalStatus\":\"MARRIED\",\"dateOfBirth\":\"1988-03-03\",\"joinDate\":\"2024-01-15\",\"department\":\"Operations\",\"salaryType\":\"MONTHLY\",\"monthlySalary\":7000,\"epfNumber\":\"87654321\",\"epfEmployeeRate\":11,\"contributeToEpf\":true,\"socsoNumber\":\"880303145502\",\"contributeToEis\":true,\"incomeTaxNumber\":\"SG22222222222\",\"isResident\":true,\"bankName\":\"CIMB\",\"bankAccountNumber\":\"5566778899\",\"bankAccountHolderName\":\"Siti Supervisor\",\"paymentMethod\":\"BANK_TRANSFER\"}"
okin "save the supervisor's profile too" "200 204"
req GET "/employees/$EMP_ID/profile"; ok "re-read the saved profile" 200
note "saved salary" "$(jqr '.monthlySalary') / bank $(jqr '.bankName')"
req GET "/employees/$EMP_ID/documents"; ok "list employee documents" 200

# ─────────────────────────────────────────────── P11 Import employees
phase "P11 Import employees (CSV)"
reqraw GET "/payroll/employees/template?format=Csv" "$SP/tpl.csv"; ok "download the import template" 200
HDR=$(head -1 "$SP/tpl.csv")
printf '%s\n' "$HDR" > "$SP/import-ok.csv"
printf '%s\n' "$IMP_EMAIL,Imported Person,910404-14-5503,NRIC,Malaysian,MALE,SINGLE,1991-04-04,2024-02-01,,Finance,MONTHLY,4500.00,,11223344,11,Yes,910404145503,Yes,SG33333333333,Public Bank,9988776655,Imported Person" >> "$SP/import-ok.csv"
reqfile POST "/payroll/employees/import" file "$SP/import-ok.csv"; ok "import a valid employee row" 200
note "import result" "$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-200)"
printf '%s\n' "$HDR" > "$SP/import-bad.csv"
printf '%s\n' ",No Email Person,,NRIC,Malaysian,MALE,SINGLE,not-a-date,2024-02-01,,Finance,MONTHLY,abc,,,,,,,,,," >> "$SP/import-bad.csv"
reqfile POST "/payroll/employees/import" file "$SP/import-bad.csv"; okin "a bad row is reported, not crashed on" "200 400"
note "bad-row result" "$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-240)"
req POST /payroll/employees/import ''; ok "reject an import with no file" 400
req GET /payroll/employees; ok "payroll roster reflects the import" 200
note "roster size" "$(printf '%s' "$BODY" | jq 'length' 2>/dev/null)"
reqraw GET "/payroll/employees/export?format=Csv" "$SP/emp-export.csv"; ok "export the roster" 200
note "export rows" "$(( $(wc -l < "$SP/emp-export.csv") - 1 ))"

# ─────────────────────────────────────────────── P12 Company structure
phase "P12 Company structure (teams + approval chain)"
req GET /teams; ok "list teams" 200
# Layers are 0-based and an approver must sit ABOVE the member, so an
# approval chain needs at least 2 layers: staff at 0, supervisor at 1.
req POST /teams "{\"projectId\":\"$PROJ_ID\",\"name\":\"Operations Team\",\"layerCount\":2,\"layerLabels\":[\"Staff\",\"Supervisor\"],\"moduleApprovalConfig\":{\"CLAIM\":[1],\"LEAVE\":[1],\"ATTENDANCE\":[1],\"OVERTIME\":[1]}}"
okin "create a team with a 2-layer approval chain" "200 201"; TEAM_ID=$(jqr '.id')
req POST "/teams/$TEAM_ID/members" "{\"employeeId\":\"$EMP_ID\",\"layer\":0}"; okin "add the employee at layer 0" "200 201 204"
req POST "/teams/$TEAM_ID/members" "{\"employeeId\":\"$SUP_ID\",\"layer\":1}"; okin "add the supervisor at layer 1" "200 201 204"
req POST "/teams/$TEAM_ID/members" "{\"employeeId\":\"$SUP_ID\",\"layer\":9}"; ok "reject a layer the team does not have" 400
req GET "/teams/chain/$EMP_ID"; ok "read the employee's approval chain" 200
CHAIN_LEN=$(printf '%s' "$BODY" | jq 'length' 2>/dev/null); note "chain steps" "$CHAIN_LEN"
[ "$CHAIN_LEN" = "0" ] && note "WARNING" "approval chain is empty — approvals will auto-approve"
req GET "/teams/$TEAM_ID/members/$EMP_ID/approver-options"; ok "read approver options for the member" 200
req PUT "/teams/$TEAM_ID" '{"name":"Operations Team (KL)","layerCount":2,"layerLabels":["Staff","Supervisor"],"moduleApprovalConfig":{"CLAIM":[1],"LEAVE":[1],"ATTENDANCE":[1],"OVERTIME":[1]}}'
okin "edit the team" "200 204"

printf '%s\n' "$ORG_ID|$PROJ_ID|$SHIFT_ID|$POLICY_ID|$ACC_ID|$BANK_ID|$LT_ID|$OWNER_TOKEN|$STAMP|$EMP_ID|$SUP_ID|$TEAM_ID" > "$SP/state.txt"
echo; echo "──────── Phase 2 subtotal: PASS=$PASS FAIL=$FAIL"
