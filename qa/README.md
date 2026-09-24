# Functional smoke test

An end-to-end, black-box pass over every module from a normal user's point of
view: sign in → create a company → configure it → hire people → file claims,
leave, attendance and overtime → run payroll → check permissions and tenant
isolation. It talks to the HTTP API only, exactly as the frontend does.

The goal is **"can a real user get through the app without hitting a wall"**,
not exhaustive unit coverage. For unit/integration tests see `backend.Tests/`.

## Point it at a throwaway database first

`ConnectionStrings:Default` in user-secrets points at the **shared DigitalOcean
database**. This suite creates companies, employees, claims and payroll runs, so
do not run it against that. Start the API against the local MySQL instead:

```bash
cd backend && ConnectionStrings__Default="$(dotnet user-secrets list | sed -n 's/^ConnectionStrings:Local = //p')" Seed__DemoData=true ASPNETCORE_ENVIRONMENT=Development dotnet run --launch-profile http
```

`Seed__DemoData=true` creates the demo tenant and its three sign-ins
(`admin@` / `supervisor@` / `employee@altomate.com`, password `password123`),
which phase 1 uses to bootstrap.

## Run it

```bash
cd qa && ./run-all.sh
```

Phases share state through `state.txt`, so run them in order. To re-run one
phase while iterating, run the earlier ones first (or keep the existing
`state.txt`). Results land in `.out/results.tsv`; response bodies and every
generated PDF/CSV land in `.out/`.

| Script | Covers |
|---|---|
| `01-setup.sh` | login, bad credentials, create company, org settings, chart of accounts, projects + geofence + IP allowlist, shifts, holidays, leave types, **policies** |
| `02-people.sh` | create employees, employee profiles (statutory + bank), CSV import, teams and the approval chain |
| `03-claims-leave.sh` | claim submit → approve/reject → export; leave entitlements → apply → approve → balances → exports |
| `04-attendance-overtime.sh` | clock in/out, breaks, time corrections, supervisor approvals, admin reports and exports; overtime with photo evidence |
| `05-payroll.sh` | payroll company info, run → generate → submit → approve, payslips, EPF/SOCSO/PCB files, the employee's payslip PDF |
| `06-rbac-isolation.sh` | dashboards and audit log, role permissions, **cross-tenant isolation**, refresh-cookie session handling |

## Notes

- Login is rate limited to 5/minute. The helpers back off automatically; if you
  run phases by hand in quick succession you may still see a `429`.
- `pixel.png` is a 1x1 PNG used for the photo-upload endpoints. Overtime
  rejects any photo URL this API did not store itself, so the upload is real.
