-- Every `bad` must be 0.
SET @org := 'cmpcs5z6300006qkzpew0mymt';
SELECT 'coa missing' chk, COUNT(*) bad FROM hr_prod.ChartOfAccount c
  LEFT JOIN altomatehr.ChartOfAccounts v ON v.Id = c.id COLLATE utf8mb4_0900_ai_ci
  WHERE c.organizationId=@org AND UPPER(c.type) IN ('BANK','EXPENSE','DIRECTCOSTS','OVERHEADS') AND v.Id IS NULL
UNION ALL SELECT 'coa liability leaked', COUNT(*) FROM altomatehr.ChartOfAccounts v
  JOIN hr_prod.ChartOfAccount c ON c.id = v.Id COLLATE utf8mb4_unicode_ci WHERE c.organizationId=@org AND UPPER(c.type) LIKE '%LIAB%'
UNION ALL SELECT 'coa bad type', COUNT(*) FROM altomatehr.ChartOfAccounts WHERE OrganizationId=@org AND Type NOT IN ('EXPENSE','BANK')
UNION ALL SELECT 'geofence missing', COUNT(*) FROM hr_prod.ProjectGeoLocation g JOIN hr_prod.XeroProject x ON x.id=g.projectId
  LEFT JOIN altomatehr.ProjectGeofencePoints v ON v.Id = g.id COLLATE utf8mb4_0900_ai_ci WHERE x.organizationId=@org AND v.Id IS NULL
UNION ALL SELECT 'project geo diff', COUNT(*) FROM hr_prod.XeroProject x JOIN altomatehr.Projects p ON p.Id = x.id COLLATE utf8mb4_0900_ai_ci
  WHERE x.organizationId=@org AND NOT (p.Latitude <=> x.latitude AND p.Longitude <=> x.longitude)
UNION ALL SELECT 'salarychange missing', COUNT(*) FROM hr_prod.SalaryChange s JOIN hr_prod.EmployeeProfile e ON e.id=s.employeeProfileId
  LEFT JOIN altomatehr.SalaryChanges v ON v.Id = s.id COLLATE utf8mb4_0900_ai_ci WHERE e.organizationId=@org AND v.Id IS NULL
UNION ALL SELECT 'run missing/diff', COUNT(*) FROM hr_prod.PayrollRun r LEFT JOIN altomatehr.PayrollRuns v ON v.Id = r.id COLLATE utf8mb4_0900_ai_ci
  WHERE r.organizationId=@org AND (v.Id IS NULL OR v.TotalGross<>r.totalGross OR v.TotalNet<>r.totalNet OR v.TotalPcb<>r.totalPcb OR v.Status<>r.status COLLATE utf8mb4_0900_ai_ci)
UNION ALL SELECT 'payslip missing/diff', COUNT(*) FROM hr_prod.Payslip s JOIN hr_prod.PayrollRun r ON r.id=s.payrollRunId
  LEFT JOIN altomatehr.Payslips v ON v.Id = s.id COLLATE utf8mb4_0900_ai_ci
  WHERE r.organizationId=@org AND (v.Id IS NULL OR v.GrossPay<>s.grossPay OR v.NetPay<>s.netPay OR v.Pcb<>s.pcb OR v.EpfEmployee<>s.epfEmployee
        OR v.SocsoEmployee<>s.socsoEmployee OR v.EisEmployee<>s.eisEmployee OR v.PcbNormal + v.PcbAdditional <> v.Pcb)
UNION ALL SELECT 'run totals != payslip sums', COUNT(*) FROM altomatehr.PayrollRuns r
  WHERE r.OrganizationId=@org AND (r.TotalGross, r.TotalNet) <> (SELECT SUM(GrossPay), SUM(NetPay) FROM altomatehr.Payslips p WHERE p.PayrollRunId=r.Id)
UNION ALL SELECT 'lineitem missing', COUNT(*) FROM hr_prod.PayslipLineItem li JOIN hr_prod.Payslip s ON s.id=li.payslipId JOIN hr_prod.PayrollRun r ON r.id=s.payrollRunId
  LEFT JOIN altomatehr.PayslipLineItems v ON v.Id = li.id COLLATE utf8mb4_0900_ai_ci WHERE r.organizationId=@org AND v.Id IS NULL
UNION ALL SELECT 'adjustment missing', COUNT(*) FROM hr_prod.PayrollRunAdjustment a JOIN hr_prod.PayrollRun r ON r.id=a.payrollRunId
  LEFT JOIN altomatehr.PayrollRunAdjustments v ON v.Id = a.id COLLATE utf8mb4_0900_ai_ci WHERE r.organizationId=@org AND v.Id IS NULL
UNION ALL SELECT 'payslip orphan profile/user', COUNT(*) FROM altomatehr.Payslips p
  LEFT JOIN altomatehr.EmployeeProfiles e ON e.Id=p.EmployeeProfileId LEFT JOIN altomatehr.Users u ON u.Id=p.UserId
  WHERE p.OrganizationId=@org AND (e.Id IS NULL OR u.Id IS NULL OR e.OrganizationId<>@org)
UNION ALL SELECT 'enum: run', COUNT(*) FROM altomatehr.PayrollRuns WHERE OrganizationId=@org AND (Status NOT IN ('DRAFT','PENDING_APPROVAL','SUBMITTED') OR Source NOT IN ('COMPUTED','IMPORTED') OR XeroSyncStatus NOT IN ('NOT_SYNCED','SYNCED','ERROR'))
UNION ALL SELECT 'enum: payslip salarytype', COUNT(*) FROM altomatehr.Payslips WHERE OrganizationId=@org AND SnapshotSalaryType NOT IN ('HOURLY','MONTHLY')
UNION ALL SELECT 'enum: line kind', COUNT(*) FROM altomatehr.PayslipLineItems WHERE OrganizationId=@org AND Kind NOT IN ('ALLOWANCE','DEDUCTION','REIMBURSEMENT')
UNION ALL SELECT 'enum: salarychange', COUNT(*) FROM altomatehr.SalaryChanges WHERE OrganizationId=@org AND (Reason NOT IN ('RAISE','PROMOTION','DEMOTION','RESTRUCTURE','OTHER') OR PreviousSalaryType NOT IN ('HOURLY','MONTHLY') OR NewSalaryType NOT IN ('HOURLY','MONTHLY'))
UNION ALL SELECT 'xero connection copied (must be 0)', COUNT(*) FROM altomatehr.XeroConnections WHERE OrganizationId=@org;
