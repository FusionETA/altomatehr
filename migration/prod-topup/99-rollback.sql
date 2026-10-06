USE altomatehr;
-- Removes the PAYROLL rows 03-payroll.sql inserted (2026-09-25), and nothing else.
--   * New attendance / claim / leave rows: ../prod-records/99-rollback.sql removes
--     every migrated record (09-18 load + this top-up) in one go.
--   * UPDATES (02-drift, 04-files) have no inverse here -- restore those columns from
--     /var/backups/altomatehr-v2/altomatehr-pre-topup-20260925-000857.sql(.gz).
--     Do NOT restore whole tables blindly: v2 is live and users write to them.
-- GSL's 3 runs predate this top-up (../prod-gsl) and are kept; so is every
-- v2-native run (uuid ids never match a v1 cuid).
DROP TEMPORARY TABLE IF EXISTS _rb;
CREATE TEMPORARY TABLE _rb (Id VARCHAR(64) COLLATE utf8mb4_0900_ai_ci PRIMARY KEY)
SELECT v.Id COLLATE utf8mb4_0900_ai_ci AS Id FROM altomatehr.PayrollRuns v
JOIN hr_prod.PayrollRun r ON v.Id IN (r.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', r.id) COLLATE utf8mb4_0900_ai_ci)
WHERE v.Id NOT IN ('cmpdg6afs0098tnm8ulr8pipd','cmpdgdotl009rtnm884c20rmh','cmpdnfrp00000zgm8pyrzm8my');

START TRANSACTION;
DELETE li FROM altomatehr.PayslipLineItems li JOIN altomatehr.Payslips p ON p.Id = li.PayslipId JOIN _rb ON _rb.Id = p.PayrollRunId;
DELETE p  FROM altomatehr.Payslips p JOIN _rb ON _rb.Id = p.PayrollRunId;
DELETE a  FROM altomatehr.PayrollRunAdjustments a JOIN _rb ON _rb.Id = a.PayrollRunId;
DELETE m  FROM altomatehr.PayrollRunMembers m JOIN _rb ON _rb.Id = m.PayrollRunId WHERE m.Id LIKE 'mig-%';
DELETE r  FROM altomatehr.PayrollRuns r JOIN _rb ON _rb.Id = r.Id;
DELETE s  FROM altomatehr.SalaryChanges s JOIN hr_prod.SalaryChange v1
  ON s.Id IN (v1.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', v1.id) COLLATE utf8mb4_0900_ai_ci)
  ;  -- also removes GSL's 1 row from ../prod-gsl; re-run that script to restore it
DELETE l  FROM altomatehr.EmployeeLoans l JOIN hr_prod.EmployeeLoan v1
  ON l.Id IN (v1.id COLLATE utf8mb4_0900_ai_ci, CONCAT('prod-', v1.id) COLLATE utf8mb4_0900_ai_ci);
COMMIT;
