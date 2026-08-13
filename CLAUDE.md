# AltomateHR (decoupled rebuild) — context for Claude

This is a **learning-first, incremental migration** of AltomateHR from a Next.js
monolith to a **decoupled** app: a Vite + React frontend calling an ASP.NET Core
(C#) backend. The original app lives separately at
`/Users/chenzirong/Documents/globe-engineering-claim` — **never modify it**; only
read it as a reference for the real schema/behaviour. Progress + decisions are
tracked in [PLAN.md](./PLAN.md); read it first when starting cold.

```
altomatehr/
├── backend/    ASP.NET Core Web API (C#), EF Core + Pomelo/MySQL   → http://localhost:5001
├── frontend/   Vite + React + TS, Tailwind + shadcn                → http://localhost:5173
└── PLAN.md
```

## ⭐ The layering rule (do NOT break this)

Backend follows **Controller → Service → Repository**, one direction only:

- **Controllers** handle HTTP only: routing, model binding, status codes, and
  cookies (cookies need `Request`/`Response`, so they legitimately live here).
  A controller calls **services** — it must **NEVER** inject or call a repository
  directly, and must hold **no** business logic.
- **Services** (`*Service.cs` + `I*Service.cs`) hold business logic /
  orchestration. They call repositories and other services. No HTTP types, no
  `DbContext`.
- **Repositories** (`*Repository.cs` + `I*Repository.cs`) are the ONLY place that
  touches the database (EF Core / `AppDbContext`).

`ClaimsController` and `AuthController` are the reference implementations. If you
find a controller calling a repository, that's a bug — extract a service. (This
happened once with `AuthController` and was fixed by adding `AuthService`.)

Everything is wired with **dependency injection** — register each interface →
implementation in `Program.cs` (`builder.Services.AddScoped<IFoo, Foo>()`), and
inject via constructors. Never `new` a service/repo yourself.

## Feature-folder structure (modular monolith)

Group by **feature**, not by technical type:
```
backend/Modules/<Feature>/
├── <Feature>Controller.cs
├── I<Feature>Service.cs   +  <Feature>Service.cs
├── I<Feature>Repository.cs +  <Feature>Repository.cs
├── Dtos/         request/response shapes (with validation attributes)
└── Entities/     EF Core entities + enums
```
- Keep each interface **next to its implementation** — do NOT make a separate
  `Interfaces/` folder.
- `AppDbContext` lives in `backend/Data/`.

## Backend conventions

- **Entities**: string ids (`= Guid.NewGuid().ToString()`, mirrors the real app's
  cuid). Enums stored as strings (`.HasConversion<string>()` in `AppDbContext`).
  Money = `decimal` with `[Precision(12, 2)]`. Align field names to the real
  Prisma schema in `globe-engineering-claim/prisma/schema.prisma`.
- **DTOs vs entities**: clients send/receive DTOs (no `Id`/`Status`/timestamps —
  the server sets those). Never bind requests straight to entities.
- **JSON**: enums serialize as strings (`JsonStringEnumConverter` in `Program.cs`).
- **Secrets**: `Jwt:Key` and `ConnectionStrings:Default` live in **user-secrets**,
  never in `appsettings.json` or code. Non-secret config (Issuer, Audience,
  token lifetimes) goes in `appsettings.json`.
- **Auth**: access token = short-lived JWT (memory on the client). Refresh token =
  opaque, DB-stored, delivered as an **httpOnly** cookie (`AuthController.SetRefreshCookie`),
  rotated on `/auth/refresh`, revoked on `/auth/logout`. Protect endpoints with
  `[Authorize]`; role-gate with `[Authorize(Roles = "Admin")]`.

## ⭐ Multi-tenancy (do NOT break tenant isolation)

The app is multi-tenant. Every request carries its tenant in the JWT `org` claim;
`ICurrentUser` (`Common/`) exposes `UserId` / `OrganizationId` / `Role` from it.

- Any entity that belongs to an org **must implement `ITenantScoped`** (`Common/`) —
  i.e. have an `OrganizationId`. `AppDbContext` then **auto-stamps** it on insert and
  **auto-filters** every query to the current org (global query filter). So you never
  write `WHERE OrganizationId = ...` yourself, and a query can't leak another tenant's data.
- Adding a new business entity → implement `ITenantScoped`, add its global query filter
  in `AppDbContext.OnModelCreating`, add a migration. **Forgetting this = a data leak.**
- Do NOT filter `Organization` itself, or `RefreshToken` (refresh runs unauthenticated,
  before the org is known). The filter is a no-op when there's no current org
  (startup/seeding, login/refresh) so those flows work.
- The `org` claim is set in `TokenService.CreateToken`; `AuthService` reads the user's
  `OrganizationId` on login and re-mints it on refresh (stored on `RefreshToken`).

## EF Core / database

- This backend uses its **own dedicated MySQL database** (DigitalOcean) — it is
  NOT the real app's Prisma DB. Code-first migrations are fine here.
- Workflow: change entity → `dotnet ef migrations add <Name>` → `dotnet ef database update`.
- `dotnet-ef` is a **local** tool (`dotnet tool run dotnet-ef` / `dotnet ef`).

## Frontend conventions

- Group frontend code by feature first. Keep each feature root small and predictable:
  ```
  frontend/src/features/<feature>/
  ├── api.ts          feature API entrypoint (optional, but keep it at root)
  ├── components/     React UI for that feature
  └── lib/            feature-only helpers, constants, options, formatters, types
  ```
- Do **not** leave random helper/config/type files directly in a feature root.
  Examples: `claim-status.ts`, `claim-options.ts`, `employee-formatters.ts`,
  `nav.ts`, and feature-only `types.ts` belong in that feature's `lib/` folder.
- **Two-layer API**: `src/shared/lib/api-client.ts` is the generic HTTP engine
  (base URL, `setAuthToken`, `apiGet/apiPost/...`, `credentials: "include"`,
  error handling — nothing feature-specific). Per-feature calls live in
  `src/features/<feature>/api.ts` and use the generic client.
- `src/shared/` is for truly cross-feature code only:
  - `shared/components/` app-wide reusable UI
  - `shared/lib/` app-wide helpers/infrastructure
  - `shared/types/` app-wide types
  Do not move feature-specific logic into `shared`; that turns it into a dumping
  ground.
- Access token is held in memory (`setAuthToken`); the refresh cookie is httpOnly
  (JS never touches it). On app load, call `/auth/refresh` to restore the session.
- `@/` is the alias for `src/`. Use the shadcn theme tokens (`bg-background`,
  `text-muted-foreground`, etc.), not hardcoded colours.

## After each response: list the files you changed

At the END of every response that creates, edits, renames, or deletes files, add a
short **"Changed files"** section listing each path touched **in that response**
(repo-relative) with a one-word note (added / edited / deleted). This lets the user
review exactly what moved before accepting it. Only list files changed in that turn.

## Verify before saying done

- Backend: `cd backend && dotnet build` (0 errors).
- Frontend: `cd frontend && npm run build`.
- Run both: `dotnet run --launch-profile http` (:5001) + `npm run dev` (:5173).

## Don't

- Don't let a controller call a repository or hold business logic.
- Don't put secrets in `appsettings.json` or commit them.
- Don't run EF migrations against the real app's Prisma database.
- Don't add a separate `Interfaces/` folder — co-locate interfaces.
- Don't modify the real app at `globe-engineering-claim`.
