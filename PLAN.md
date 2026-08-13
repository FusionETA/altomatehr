# Decoupled Architecture Sandbox — Plan

A safe place to learn the **two-folder (frontend + backend) architecture** and
**ASP.NET Core (C#)** by rebuilding AltomateHR's backend — *without touching the
real project*.

- **Real project (never modified):** `/Users/chenzirong/Documents/globe-engineering-claim`
- **This sandbox:** `/Users/chenzirong/Documents/altomatehr`

---

## Goal

Two goals at once:
1. See how a **frontend** and **backend** work as two separate apps over HTTP.
2. Learn **ASP.NET Core (C#)** — the backend stack most enterprises use.

```
BEFORE (today — one Next.js app)          AFTER (two folders)
┌──────────────────────────┐             ┌───────────────┐   HTTP    ┌─────────────────┐
│  Next.js                 │             │  frontend/    │  ──────►  │  backend/       │
│  UI + API + DB access    │             │  Vite + React │  ◄──────  │  ASP.NET Core   │
│  all in one              │             │  (SPA, no DB) │   JSON    │  (C#) owns DB   │
└──────────────────────────┘             └───────────────┘           └─────────────────┘
```

---

## Decisions made

| Decision | Choice | Why |
|---|---|---|
| **Backend** | **ASP.NET Core Web API** (C#) | Most-used enterprise backend (banking, corporate, GLC). Learning it is the goal — a marketable skill. Uses **Entity Framework Core** as the ORM. |
| **Frontend** | **Vite + React** (SPA) | Light, fast builds, low RAM. Pure SPA → forced to call the API. Keeps React + Tailwind + shadcn. |
| Type sharing | **OpenAPI / Swagger codegen** | C# types can't be imported into TS, so the backend emits an OpenAPI spec and the frontend generates TS types from it. |
| Approach | **Frontend: port · Backend: rebuild in C#** | React components port over; the C# backend is built fresh (best way to *learn* it). |
| Scope | **Claims module first** | Build it end-to-end before the rest. |
| After JWT | **Authorization / RBAC** | ASP.NET has first-class `[Authorize(Roles=...)]`. AltomateHR is all about ADMIN / EMPLOYEE / SUPERADMIN. |
| Teaching style | **Concept first, then build** | Claude explains how each piece works & *why*, then we build it. Goal = senior-level understanding, plus a working grasp of C#. |

> Not chosen: **NestJS** (would've been a mechanical port, but you want the .NET
> skill) · **Django** (Python) · **Astro** (content sites, not dashboards).

---

## Build order (decided)

**Backend-first, one thin vertical slice at a time — NOT "whole frontend first".**

Why: a decoupled frontend has nothing to talk to until the backend exists, and
your goal is learning C#. So:
1. Build the **Claims backend** in C# → **test it in Swagger** (no frontend needed).
2. Then build the **Claims page** in the frontend that calls it.
3. Repeat per module.

The only frontend work worth doing early is the **shell** (Vite + Tailwind +
shadcn + router skeleton + `api-client.ts`) — not porting all the screens up front.

---

## ⚠️ What changes because it's .NET, not NestJS

- **New language: C#.** You're learning the language *and* the framework — see the
  learning track (C# fundamentals is now step 0).
- **ORM: Entity Framework Core** replaces Prisma. `DbContext` replaces the Prisma
  client; `dotnet ef migrations` replaces `prisma migrate`. EF Core talks to your
  existing **MariaDB** via the `Pomelo.EntityFrameworkCore.MySql` provider — you
  can even scaffold entities from the live schema (`dotnet ef dbcontext scaffold`).
- **No `shared/` folder.** Types cross the language boundary via **OpenAPI**:
  `npx openapi-typescript http://localhost:5001/swagger/v1/swagger.json -o src/lib/api-types.ts`
- **Auth moves to the backend** (a Vite SPA has no server). Browser talks to
  ASP.NET directly; ASP.NET owns login.

---

## Auth — the learning path (Step 1 → 4)

> **Goal: understand this *clearly*, not just copy-paste.** The gap between mid
> and senior is being able to explain **why**. Walk through it with Claude at night.

**Authentication vs Authorization — NOT the same thing:**
- **Authentication (Step 1, JWT)** = *"who are you?"* → login, proves identity.
- **Authorization (Step 2, RBAC)** = *"what may you do?"* → roles/permissions.
- JWT is the *authentication* token, and it **carries the role** authorization reads.
- `401 Unauthorized` = authN failed · `403 Forbidden` = authenticated but not allowed.

**How real orgs store the token (most → least secure):**

| Approach | Security | Who uses it |
|---|---|---|
| Access token in **memory** + refresh token in **httpOnly cookie** | ✅ best | serious production SaaS |
| **httpOnly cookie** session | ✅ good | many solid teams (≈ today's iron-session) |
| JWT in **localStorage** | ⚠️ weak (XSS can steal it) | common in tutorials; *not* OWASP-recommended |

**Bigger truth:** most production orgs don't hand-roll auth — they use a provider
(Microsoft **Entra ID** is the .NET enterprise default, or Auth0 / IdentityServer /
Keycloak). Hand-rolling JWT is a *learning* exercise.

**Climb the ladder — don't skip steps:**

- **Step 1 — plain JWT access token** *(do this first / tonight)*
  Login → backend signs a token → SPA sends `Authorization: Bearer <token>` →
  `[Authorize]` verifies it.
- **Step 2 — refresh tokens + httpOnly cookie** — production-grade version of Step 1.
- **Step 3 — ASP.NET Identity** — the built-in users/roles/password framework.
- **Step 4 — managed providers (know they exist)** — Entra ID / Auth0 / IdentityServer.

**For Step 1 in ASP.NET Core, look up / install:**
- `Microsoft.AspNetCore.Authentication.JwtBearer` — validates the token
- `builder.Services.AddAuthentication().AddJwtBearer(...)` in `Program.cs`
- `[Authorize]` on protected controllers; `[AllowAnonymous]` on login
- official **ASP.NET Core → Security → JWT bearer** docs

**Questions a senior can answer (test yourself):**
1. What's inside a JWT? (`header.payload.signature` — **signed, not encrypted**)
2. Why is `localStorage` vulnerable to **XSS**, and a cookie to **CSRF**?
3. Why short-lived access token + refresh token instead of one long-lived token?
4. What does the `[Authorize]` middleware do on each request, step by step?
5. How do CORS + the token work together when `:5173` calls `:5001`?

---

## Learning Track — topics in order (the senior curriculum)

Two tracks run in parallel: **Build Phases** (what you make) and this **Learning
Track** (what you understand). Don't move past a topic until you can explain *why*.

### Backend (ASP.NET Core / C#) — the main track
0. **C# + .NET fundamentals** *(NEW — you're learning the language)* — types,
   classes, `async`/`await`, LINQ, namespaces, the `dotnet` CLI.
1. **ASP.NET Core fundamentals** — `Program.cs`, the middleware pipeline,
   controllers, built-in **dependency injection**.
2. **Entity Framework Core** — `DbContext`, entities, migrations, LINQ queries,
   connecting to MariaDB via Pomelo.
3. **Build one module (CRUD)** — controller + service + repository + entity for
   **claims**: `GET` / `POST` / `PUT` / `DELETE`.
4. **Validation** — data annotations (`[Required]`, `[Range]`) or FluentValidation;
   `[ApiController]` auto-validates.
5. **Auth — Step 1: JWT**  ← *you start here*
   login → sign token → `[Authorize]` verifies it.
6. **Authorization (RBAC)**  ← ★ **THE NEXT STEP AFTER JWT**
   `[Authorize(Roles = "Admin")]` + policies. First-class in ASP.NET.
7. **Error handling** — exception middleware · `ProblemDetails` · right HTTP codes.
8. **Auth — Step 2: refresh tokens** — short access token + httpOnly refresh cookie.
9. **Config & secrets** — `appsettings.json`, user-secrets, env vars.
10. **Testing** — xUnit unit tests + integration tests.
11. **Deploy** — `dotnet publish`, Kestrel behind a reverse proxy.

### Frontend (Vite + React) — the parallel track
- **A. Setup** — Vite + React + TS + Tailwind + shadcn + **React Router**.
- **B. `api-client.ts`** — one fetch wrapper: base URL + attach the JWT.
- **C. TanStack Query (React Query)** — fetching with loading / error / cache states.
- **D. Client auth** — login form, hold token in memory, protected routes.
- **E. Forms** — react-hook-form + zod → call your API.
- **F. UX polish** — toasts, optimistic updates.

### Cross-cutting — hit the moment the two apps talk
- **OpenAPI / Swagger** — the backend auto-emits an API spec; the frontend
  generates TS types from it (replaces the shared/ folder).
- **CORS** *(the first wall)* — the browser **blocks** `:5173 → :5001` by default.
  Fix in `Program.cs`: `AddCors(...)` + `app.UseCors(...)`. Enforced by the
  **browser**, not the server.
- **Config** — `VITE_API_URL` (frontend) vs `appsettings.json` connection string
  + JWT signing key (backend).

---

## Folder structure  (all inside /Users/chenzirong/Documents/altomatehr)

### `frontend/` — Vite + React (runs on :5173)

```
frontend/
├── index.html
├── src/
│   ├── main.tsx   App.tsx        ← React Router routes
│   ├── routes/                   ← was app/(admin) / app/(employee)
│   ├── components/               ← your shadcn-ui components (ported)
│   ├── lib/
│   │   ├── api-client.ts         ← fetch wrapper → http://localhost:5001
│   │   ├── api-types.ts          ← GENERATED from the backend's OpenAPI spec
│   │   ├── auth.ts               ← client-side session handling
│   │   └── utils.ts decimal.ts   ← pure helpers (port as-is)
│   └── styles/                   ← Tailwind
├── .env                          ← VITE_API_URL=http://localhost:5001
└── package.json
```

### `backend/` — ASP.NET Core Web API, C# (runs on :5001)

```
backend/
├── Program.cs                    ← bootstrap: DI, middleware, CORS, auth, routing
├── appsettings.json              ← config: MariaDB connection string, JWT key
├── appsettings.Development.json
├── AltomateHR.Api.csproj         ← project + NuGet dependencies
├── Modules/                      ← feature folders (modular monolith)
│   └── Claims/
│       ├── ClaimsController.cs   ← was app/api/v1/claims/route.ts
│       ├── IClaimsService.cs     ← interface (for DI)
│       ├── ClaimsService.cs      ← was application/services/*
│       ├── IClaimsRepository.cs
│       ├── ClaimsRepository.cs   ← was infrastructure/*.repository.ts (uses EF Core)
│       ├── Dtos/
│       │   ├── CreateClaimDto.cs
│       │   └── ClaimResponseDto.cs
│       └── Entities/Claim.cs     ← EF Core entity (POCO)
├── Data/
│   ├── AppDbContext.cs           ← EF Core DbContext (replaces the Prisma client)
│   └── Migrations/               ← EF Core migrations (replaces prisma migrate)
└── Auth/                         ← JWT setup; guards via [Authorize]
```

> Note: ASP.NET has no `@Module()` like NestJS. You get a modular monolith via
> **feature folders + DI registration** in `Program.cs` (optionally tidied with
> `services.AddClaimsModule()` extension methods).

---

## How they connect

```
┌──────── frontend :5173 (Vite) ────────┐        ┌──────── backend :5001 (ASP.NET) ──────┐
│ page → lib/api-client.ts               │  HTTP  │ Program.cs (CORS allows :5173)         │
│           fetch(`${VITE_API_URL}/...`) │ ─────► │   ClaimsController  [Authorize]        │
│ renders JSON into shadcn table  ◄──────┼────────┤     → ClaimsService                    │
│ types GENERATED from OpenAPI spec      │  JSON  │       → ClaimsRepository → EF Core → DB │
└────────────────────────────────────────┘        └─────────────────────────────────────────┘
```

Wiring: **1)** `VITE_API_URL`, **2)** CORS in `Program.cs`, **3)** `api-client.ts`,
**4)** OpenAPI codegen for types.

---

## Prerequisites (install before Phase 0)

- [ ] **.NET SDK** (8 or 9) — check with `dotnet --version`
- [ ] **Node.js** (for the Vite frontend) — check with `node --version`
- [ ] MariaDB reachable (your existing dev DB is fine for read-only practice)
- [ ] (optional) VS Code + **C# Dev Kit** extension

---

## Build Phases (to run when ready — NOT started yet)

### Phase 0 — Scaffold  ✅ COMPLETE
- [x] `backend/` : ASP.NET Core Web API (controllers) — builds clean, port set to :5001
- [x] `frontend/` : Vite + React + TS — scaffolded + `npm install` done (:5173)
- [x] frontend: Tailwind v4 + shadcn (radix / nova preset) — `@/` alias working, builds clean
- [x] backend: EF Core 9 + Pomelo 9 + Design installed (EF Core 9 runs fine on the .NET 10 runtime)

### Phase 1 — Stand up the backend
- [ ] `AppDbContext` + connection string to MariaDB; scaffold or define entities.
- [ ] Enable CORS (allow `http://localhost:5173`) + Swagger.
- [ ] Add JWT auth (**Step 1**, see Auth path) + a health endpoint.

### Phase 2 — Build the Claims module end-to-end
- [ ] Backend: `ClaimsController` → `ClaimsService` → `ClaimsRepository` (EF Core),
      with `CreateClaimDto` + `Claim` entity. Endpoints: `GET/POST/PUT/DELETE`.
      **Test in Swagger first — no frontend needed.**
- [ ] Frontend: build the claims page in React, generate types from OpenAPI,
      fetch via `api-client.ts`.

### Phase 3 — Run both & observe
- [ ] Start `backend/` (:5001) and `frontend/` (:5173) together.
- [ ] Watch the round trip: click → fetch → controller → service → repo → EF → DB.
- [ ] Note what you now handle manually: CORS, JWT, OpenAPI codegen, two servers.

### Phase 4 (optional) — Repeat for the other modules.

---

## What this will teach you

- **C# and ASP.NET Core** — a marketable enterprise backend skill.
- Why React-SPA + a separate API is the classic decoupled shape.
- The real cost of decoupling: network hop, CORS, cross-boundary auth, type
  codegen, two deploys.
- That the controller/service/repository/DI concepts are **the same** as NestJS —
  only the language and syntax differ.

---

## Status

**Phase 0 ✅ · EF Core ✅ · Full CRUD ✅ · Claim aligned to real schema (core) ✅.**
Claim now uses a **string id**, C# **enums** (Status/ClaimType/PaymentType/Category — stored &
serialized as strings), + real core fields (claimNumber, title, category, currency, spentAt,
employeeId…). Server controls id/claimNumber/status/timestamps. Full CRUD + validation working.
Deferred (add incrementally): Xero, mileage, org/project/COA relations, approval chain.
**Frontend connected ✅ · JWT Step 1 ✅ · Frontend login ✅.** `[Authorize]` on Claims → 401
without a token. `POST /auth/login` (TEMP demo `admin@altomate.com` / `password123`) issues a
signed JWT (`Jwt:Key` in user-secrets; carries sub/email/role). Frontend: login form → store token
in memory (`setAuthToken`) → attached to every request → protected `/claims` = 200. Verified in
browser (login → claims). FE structure: `lib/api-client.ts` (generic) + `features/auth|claims/api.ts`.
**Note:** token in memory → a page refresh logs you out (Step 2 httpOnly cookie/refresh fixes this).
**RBAC ✅** — `[Authorize(Roles="Admin")]` on DELETE: Employee → 403, Admin → 204 (same request; role
from the JWT decides, no DB). Two demo users (admin@ / employee@). 401 = not authed, 403 = authed-but-forbidden.
**Step 2 (refresh + httpOnly cookie) ✅** — access token (15min, memory) + refresh token (7d,
DB-stored, httpOnly `SameSite=Lax` cookie, Path=/auth). `/auth/refresh` (with rotation) + `/auth/logout`
(revokes). CORS `AllowCredentials`; FE `credentials:"include"` + refresh-on-load. **Verified in browser:
page reload stays logged in.** Migration `AddRefreshTokens`.
**Layering fix ✅** — extracted `AuthService`; `AuthController` is now thin (HTTP/cookies only),
matching the `ClaimsController` controller→service→repo pattern. Added root `CLAUDE.md` documenting
the layering rule + conventions so future sessions don't repeat the mistake.
**Real users + hashing ✅** — `Users` table, seeded on startup (`DbSeeder`) with admin@/employee@
and **BCrypt-hashed** passwords. `AuthService` now looks up the user + `BCrypt.Verify`; roles come
from the DB. `DemoUsers` dictionary removed. Package: `BCrypt.Net-Next`. Migration `AddUsers`.
Login verified: correct→200, wrong→401, unknown→401. (Demo passwords are still `password123` — a
real deploy needs a registration/password-reset flow + strong passwords.)
**Error handling + brute-force protection ✅** — global exception handler returns `ProblemDetails`;
auth endpoints are rate-limited (`/auth/login`: 5/min/IP, `/auth/refresh`: 20/min/IP); auth errors
avoid revealing whether a refresh token is missing/expired/invalid.
**Backend tests ✅** — added `backend.Tests/` with xUnit unit tests for `AuthService` and integration
tests for `/auth/login` + `/auth/refresh` using `WebApplicationFactory` + EF InMemory. Run with:
`dotnet test backend.Tests/AltomateHR.Api.Tests.csproj`.
Next: frontend polish. Optional: registration endpoint · role-aware frontend · create-claim form.

Locked choices:
- Folder = **/Users/chenzirong/Documents/altomatehr** (frontend/ + backend/ inside it)
- Frontend = **Vite + React** (:5173) · Backend = **ASP.NET Core / C#** (:5001)
- ORM = **Entity Framework Core** (MariaDB via Pomelo) · Types via **OpenAPI codegen**
- Build order = **backend-first, Claims slice, verify in Swagger, then the page**
- Auth = **Step 1 JWT** → then **Authorization / RBAC** (`[Authorize(Roles=...)]`)
- Teaching style = **concept first, then build**

Next action: run **Phase 0 (scaffold)** when you say go.
