# AltomateHR (decoupled rebuild) — Documentation

> Snapshot: **2026-08-14**. Repo: **FusionETA/altomatehr**. This documents the
> incremental, module-by-module rebuild of the AltomateHR HR/payroll app into a
> decoupled Vite (React/TS) frontend + ASP.NET Core (C#) backend.

## How this maps to OneNote

Each file below is a **Section**. Inside each file, every **"Page:"** heading is
a **Page** — copy the block under it into a OneNote page of the same name.

| Section (file) | Pages |
|---|---|
| [01-frontend.md](01-frontend.md) | Stack · Structure · API client · Design system · Shared UI · Auth flow · Feature modules |
| [02-backend.md](02-backend.md) | Stack · Layered architecture · Multi-tenancy · Auth · Roles/Employees · Module catalog · Policies · Teams & Approval chain · Data/Migrations · Seeding |
| [03-backend-testing.md](03-backend-testing.md) | Approach · Test doubles · Coverage map |
| [04-ci-cd.md](04-ci-cd.md) | Current manual flow · Git workflow · Proposed pipeline · Deployment |
| [05-architecture-decisions.md](05-architecture-decisions.md) | ADRs (one page per decision) + Deferred list |

## Two-sentence overview

The app is a **multi-tenant** HR platform: organizations have employees who file
**claims**, clock **attendance** (with geofencing), and apply for **leave** — all
approved through a **team-based, layered, multi-step approval chain**, with
per-employee **policies** governing enforcement and entitlements. It's being
migrated **strangler-fig style** (one module at a time) from a Next.js monolith.

## Module status (as of snapshot)

| Module | Backend | Frontend | Notes |
|---|---|---|---|
| Auth + multi-tenancy | ✅ | ✅ | JWT + refresh, org isolation |
| Organizations (settings) | ✅ | ✅ | name, currency, mileage rate, geofence radius |
| Projects | ✅ | ✅ | + geofence coordinates |
| Accounts (chart of accounts) | ✅ | ✅ | spend limits, mileage |
| Claims | ✅ | ✅ | + over-limit flag, chain routing |
| Attendance | ✅ | ✅ | clock in/out, geofencing, off-site remark+photo |
| Leave | ✅ | ✅ | types, apply, balances, chain routing |
| Policies | ✅ | ✅ | rule bundles; retrofits attendance + leave |
| Teams / hierarchy | ✅ | ✅ | layers, memberships, module-aware chain |
| Approval chain routing | ✅ | ✅ (transparent) | multi-step, by seat not role |
| Payroll / payslips | ⬜ | ⬜ | intentionally last (money-critical) |

Deferred details are listed in [05-architecture-decisions.md](05-architecture-decisions.md).
