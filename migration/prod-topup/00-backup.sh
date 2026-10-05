#!/usr/bin/env bash
# Dumps every table this top-up writes. $CNF comes from run.sh.
set -euo pipefail
OUT=/var/backups/altomatehr-v2/altomatehr-pre-topup-$(date +%Y%m%d-%H%M%S).sql
mysqldump --defaults-file="$CNF" --single-transaction --no-tablespaces --set-gtid-purged=OFF altomatehr \
  AttendanceRecords AttendanceSessions AttendanceBreaks AttendanceApprovalRequests \
  Claims LeaveApplications LeaveEntitlements \
  PayrollRuns PayrollRunMembers Payslips PayslipLineItems PayrollRunAdjustments SalaryChanges EmployeeLoans \
  EmployeeProfiles > "$OUT" 2>/dev/null || \
mysqldump --defaults-file="$CNF" --single-transaction --no-tablespaces altomatehr \
  AttendanceRecords AttendanceSessions AttendanceBreaks AttendanceApprovalRequests \
  Claims LeaveApplications LeaveEntitlements \
  PayrollRuns PayrollRunMembers Payslips PayslipLineItems PayrollRunAdjustments SalaryChanges EmployeeLoans \
  EmployeeProfiles > "$OUT"
gzip -k "$OUT"; ls -la "$OUT" "$OUT.gz"
