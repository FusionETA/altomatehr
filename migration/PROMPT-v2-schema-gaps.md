> **SUPERSEDED 2026-09-18.** Do not paste this file into a local agent any more.
>
> - Task 1 (geofence) + the new multi-IP task → `PROMPT-v2-geofence-and-ip.md`, which
>   carries the v1 source inline.
> - Questions 1–3 were answered server-side → `ANSWERS-v1-v2-gaps.md`.
> - Project managers: staged and verified → `prod-projectmanagers/`.
>
> Two claims below are now known to be wrong: v1's project-manager data lives in a
> `ProjectManager` **join table**, not only the scalar `projectManagerId`; and the
> allowlist row in the "hold no data" table reads the JSON column with `IS NOT NULL`,
> which counts 129 rows that are the JSON literal `null`. The real count of configured
> allowlists is 0 — the conclusion held, the measurement didn't.

# Prompt: v1→v2 schema gaps — one fix, three questions

Paste everything below the line into Claude Code in a local checkout of the v2 repo.
It is self-contained — it carries the v1 schema facts and row counts inline, because a
local agent cannot reach the production databases.

Row counts were measured on 2026-09-18 against `hr_prod`, scoped to the 42 migrated orgs.

Scope changed 2026-09-18 after review: only the geofence gap is a build task. The
approval chain, project manager and `isCustom` items are **questions to answer first** —
we don't want code for them until we understand what they are. Currency is out of scope
entirely (we'll wire it ourselves; v2 already pulls currencies from Xero).

---

You are working on AltomateHR v2 (ASP.NET 8 + EF Core + MySQL, React/Vite frontend).
Backend: `backend/`, modules under `backend/Modules/<Area>/`, entities in
`backend/Modules/<Area>/Entities/`, DbSets in `backend/Data/AppDbContext.cs`, EF
migrations in `backend/Migrations/`. Tenant-scoped entities implement
`AltomateHR.Api.Common.ITenantScoped` (a single `string OrganizationId`), which the
DbContext auto-stamps on insert and auto-filters on read.

We are migrating production data from AltomateHR v1 (Next.js + Prisma + MySQL, db
`hr_prod`) into v2. Some v1 data has no column in v2 and is being dropped on import.

**There is one task and three questions. Answer the three questions BEFORE writing any
code for them — we do not understand these three well enough to have decided yet.**

## TASK 1 — multi-point project geofences (build this)

**v2 currently supports only ONE geofence point per project. That is the bug. Fix it.**

v1 has a `ProjectGeoLocation` table: many geofence points per project.

```
ProjectGeoLocation(id varchar(191) PK, projectId varchar(191), label varchar(191) NOT NULL,
                   latitude double NOT NULL, longitude double NOT NULL,
                   createdAt datetime(3), updatedAt datetime(3))
```

v2's `Project` entity (`backend/Modules/Projects/Entities/Project.cs`) holds a single
nullable `Latitude`/`Longitude`, so only one point per project survives import, and the
point loses its label.

Production data: **250 points across 138 projects. 89 of those projects have more than
one point** (73 have 2, 11 have 3, 3 have 4, 2 have 5). So the current model silently
drops ~112 geofence points. A real site often has several valid clock-in locations
(gate, site office, warehouse) and an employee at any of them must be able to clock in.

Do this:

1. Add a `ProjectGeoLocation` entity in `backend/Modules/Projects/Entities/`,
   implementing `ITenantScoped`: `Id`, `OrganizationId`, `ProjectId`, `Label`,
   `Latitude` (double), `Longitude` (double), `CreatedAt`. Index on
   `(OrganizationId, ProjectId)`.
2. Register the DbSet in `AppDbContext`.
3. Keep `Project.Latitude`/`Longitude` as-is for now — do NOT remove them. Treat them as
   the project's primary point so nothing that reads them breaks. Note in a comment that
   they are the primary point and the table is the full set.
4. Find every place that currently does geofence/radius checking against
   `Project.Latitude`/`Longitude` (search the Attendance module for the clock-in
   location check and for `GeofenceRadiusMeters` on `Organization`). Change the check to
   pass if the employee is within the radius of **ANY** of the project's points, falling
   back to the primary point when the project has no rows in the new table.
   If v2 has not implemented radius checking at all yet, **say so and stop** — do not
   invent a geofence engine. Report what you found.
5. Surface the points in the project settings UI: list/add/remove points with labels.
   Match the existing settings screens' conventions.
6. Generate an EF migration (`dotnet ef migrations add ProjectGeoLocations`). Do not
   hand-write SQL and do not edit the model snapshot by hand.

Do NOT write a data-migration script for the 250 rows — that runs server-side against
`hr_prod` and is not your job here. Just make the schema and the app ready for it.

## QUESTION 1 — per-employee approval chains: what is the actual problem here?

**Explain this one in plain language before proposing anything. We don't follow it yet.**

v1: `ApprovalChainStep(id, employeeId, teamId, step, approverId, createdAt, updatedAt)` —
one row per approver, per step, per employee, per team. **492 rows** in production,
`step` values 1 and 2 only, no null `teamId`.

v2: `TeamApprovalOverride` (`backend/Modules/Teams/Entities/TeamApprovalOverride.cs`) —
`(Id, OrganizationId, TeamId, EmployeeId, Layer, ApproverIdsJson, CreatedAt, UpdatedAt)`,
one row per (team, employee, layer) with approvers collapsed into a JSON array.
**0 rows today.** Grouping v1 by `(employeeId, teamId, step)` gives **444** v2 rows.

Read `TeamApprovalOverride.cs` and `backend/Modules/Teams/ApprovalChainService.cs`, then
answer these, quoting the code you base each answer on:

1. In v2, what does the **presence** of a `TeamApprovalOverride` row mean, versus its
   **absence**? The entity comment says absence means "fall back to the team's default:
   everyone else at that layer". So is an override a deliberate deviation from the
   default, or just the normal way a chain is stored?
2. In v1, is `ApprovalChainStep` the *same* thing — a deviation — or is it how v1 stores
   **every** employee's chain, deviation or not?
3. If those two answers differ, what breaks when we load 444 rows into v2? Specifically:
   does turning an implicit default into an explicit frozen override mean the chain
   **stops following team membership changes** — i.e. someone leaves the team or changes
   layer and the old approver is still baked into `ApproverIdsJson` forever?
4. The table has **never held a row**, so the whole read/write path is unexercised. What
   in `ApprovalChainService.Build()`, the API that edits overrides, and the settings UI
   would misbehave the moment the table is suddenly populated for real?

Give us the recommendation at the end: load all 444, load only the ones that are genuine
deviations from what v2 would compute by default, or don't load any. **Do not change any
code for this yet.** If writing tests is what it takes to answer #4 honestly, write the
tests — that's fine, tests are the deliverable here, not a fix.

## QUESTION 2 — `projectManagerId`: what is this for?

**We don't know what this field does. Explain it, don't add it.**

v1 `XeroProject.projectManagerId` (nullable varchar) is set on **84 of 730** projects.
It points at a v1 user id — in v2 that is a `User.Id`. v2's `Project` has no equivalent.

Answer:

1. Search the v2 repo (backend and frontend) for anything that already names a person as
   responsible for a project — a project owner, a manager, a supervisor, a team lead on
   the project's team. Does one of these already fill this role under a different name?
   `Team.ProjectId` exists and `ApprovalChainService` resolves a team from a project, so
   check whether the project's team leader is effectively the project manager already.
2. What would a `Project.ProjectManagerId` actually **do** in v2 — who reads it, does it
   affect approvals, notifications, reporting, or is it a label on a settings screen?
   If nothing in v2 would read it, say that plainly.
3. Is the v1 value even trustworthy — is it a live routing field, or a leftover from an
   older design? Say what evidence you have either way, and say when you have none.

Recommend: add it, map it onto something v2 already has, or drop those 84 values.
**No code.**

## QUESTION 3 — `ChartOfAccount.isCustom`: why can't we migrate it?

**We were told this can't be migrated. Explain why, because on the face of it, it's just
a boolean.**

v1 `ChartOfAccount.isCustom` is true on **15** accounts, and marks an account typed by
hand in the app rather than synced from Xero. v2's `ChartOfAccount`
(`backend/Modules/Accounts/Entities/ChartOfAccount.cs`) has no such column — so the
2026-09-18 chart-of-accounts migration dropped the flag for all 672 rows it loaded.

Answer:

1. Is there any reason this **can't** be migrated, or is the only reason that the column
   doesn't exist yet? Be blunt — if it's just a missing column, say so and say that the
   earlier "cannot migrate" framing was wrong.
2. Can v2 already tell a hand-made account from a Xero-synced one without the flag?
   `XeroAccountId` is null for hand-made accounts — is that reliable, or can it be null
   on a Xero-sourced row too (e.g. after a disconnect, or for an account created before
   the org connected Xero)? Check how `XeroService` sets and clears it.
3. If we add `IsCustom` (bool, default false), what in v2 would actually **use** it —
   does anything need to treat a hand-made account differently on sync, archive or
   delete? If a Xero re-sync would wipe or overwrite hand-made accounts without it,
   that's the real argument for adding it — say so.

Recommend add or skip, with the reason. **No code until we say go.**

## OUT OF SCOPE — currency

v1's `Organization.allowedCurrencies` (a JSON array, non-default on 7 orgs) is **not**
being migrated and you should not add a column for it. v2 already sources currencies
from Xero live — `XeroClient.GetCurrenciesAsync()` → `GET /xero/currencies` →
`OrgCurrencyCard` in `frontend/src/features/settings/components/OrgFieldCards.tsx`,
with the choice stored as `Organization.DefaultCurrency`. We'll wire the per-org allowed
set to that ourselves later. Don't touch it.

## Do NOT add these — they hold no data

I measured each one in production. Adding columns for them is pure churn:

| v1 field | why not |
|---|---|
| `XeroProject.allowedIps`, `allowedIpsList` | **0 of 730** projects have either set (the list column is JSON `null` everywhere), and `Project.AllowedIps` already exists in v2 anyway. Nothing to do. |
| `ChartOfAccount.limitPeriod`, `limitScope` | **0 rows** set either. `limitAmount` and `mileageRate` are also empty across all 1169 accounts, so v1's whole spend-limit model carries no data. |
| `Organization.timezone` | All 42 orgs are `Asia/Kuala_Lumpur`, the v1 default. Adding the column would only re-materialise the default. (Timezone handling in v2 is a real concern, but it is a design question, not a data-migration gap.) |
| `Organization.otEnabled`, `supervisorReportEnabled` | All 42 orgs are `1`, the v1 default. |
| `Organization.allowForecastedLeaveApply` | 1 org deviates. Not worth a column and a UI toggle. |
| `Team.requireClockInApproval`, `requireClockOutApproval` | All 158 teams are at the default `1`. |
| `Team.requireBreakStartApproval`, `requireBreakEndApproval` | Only **3 of 158** teams deviate from the default. Skip unless you are adding break approval as a feature anyway. |
| `ChartOfAccount.isBankAccount` | Redundant with `Type='BANK'`. Exactly 1 row of 1169 disagrees, and v2's own Xero importer derives type the same way. |
| `ChartOfAccount.archivedByXeroConnect`, `updatedAt` | No consumer in v2. |

## Separately: the 497 skipped liability accounts are NOT a missing-field problem

`migration/prod-coa/` deliberately skips 497 `CURRLIAB`/`TERMLIAB`/`LIABILITY` accounts.
That is not a schema gap — `ChartOfAccount.Type` exists. v2 models exactly two account
types, and `XeroService.ShouldImportAccount()` imports only claimable types
(EXPENSE/DIRECTCOSTS/OVERHEADS) plus BANK, so a Xero reconnect would never deliver a
liability account either. All 497 are non-selectable in v1 with zero claims against them.

Supporting more account types is a product decision (it needs new UI tabs in
`frontend/src/features/settings/components/AccountsSettings.tsx`, which filters on an
exact string match against `"EXPENSE"` and `"BANK"`). Do not do it as part of this work.

## Constraints

- Generate EF migrations with `dotnet ef migrations add <Name>`. Never hand-edit
  `AppDbContextModelSnapshot.cs`.
- Every new tenant-scoped entity must implement `ITenantScoped` or it will leak across
  organizations — the global query filter is what enforces isolation.
- v2 stores enums as strings and EF throws `Cannot convert string value …` on **read**
  if a value is not a member of the C# enum. The row inserts fine and breaks later, so
  prefer a plain string with a documented value set over an enum for anything sourced
  from v1.
- Additive columns only. Do not drop or rename existing columns — the live database is
  shared and already holds migrated production data.
- Answer the three questions in plain English with code references. Where you don't know,
  say you don't know rather than guessing.
