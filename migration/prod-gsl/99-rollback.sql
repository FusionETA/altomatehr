-- Removes exactly what 01-gsl.sql inserted (matched by v1 id). The Projects geo UPDATE is
-- not undone here - restore it from the backup if needed (it was NULL before).
SET @org := 'cmpcs5z6300006qkzpew0mymt';
START TRANSACTION;
DELETE v FROM altomatehr.PayrollRunAdjustments v WHERE v.OrganizationId=@org AND v.PayrollRunId IN (SELECT id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.PayrollRun WHERE organizationId=@org);
DELETE v FROM altomatehr.PayslipLineItems v WHERE v.OrganizationId=@org AND v.PayslipId IN (SELECT s.id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.Payslip s JOIN hr_prod.PayrollRun r ON r.id=s.payrollRunId WHERE r.organizationId=@org);
DELETE v FROM altomatehr.Payslips v WHERE v.OrganizationId=@org AND v.PayrollRunId IN (SELECT id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.PayrollRun WHERE organizationId=@org);
DELETE v FROM altomatehr.PayrollRuns v WHERE v.OrganizationId=@org AND v.Id IN (SELECT id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.PayrollRun WHERE organizationId=@org);
DELETE v FROM altomatehr.SalaryChanges v WHERE v.OrganizationId=@org AND v.Id IN (SELECT s.id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.SalaryChange s JOIN hr_prod.EmployeeProfile e ON e.id=s.employeeProfileId WHERE e.organizationId=@org);
DELETE v FROM altomatehr.ProjectGeofencePoints v WHERE v.OrganizationId=@org AND v.Id IN (SELECT g.id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.ProjectGeoLocation g JOIN hr_prod.XeroProject x ON x.id=g.projectId WHERE x.organizationId=@org);
DELETE v FROM altomatehr.ChartOfAccounts v WHERE v.OrganizationId=@org AND v.Id IN (SELECT id COLLATE utf8mb4_0900_ai_ci FROM hr_prod.ChartOfAccount WHERE organizationId=@org);
COMMIT;
