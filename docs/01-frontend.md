# Section: Front-End

> Each **"Page:"** below → one OneNote page under the **Front-End** section.

---

# Page: Stack & Tooling

**What it's built on**

| Concern | Choice | Why |
|---|---|---|
| Build tool | **Vite** | Instant dev server + HMR, no framework lock-in |
| UI | **React 18 + TypeScript** | Component model, type safety |
| Styling | **Tailwind CSS** | Utility-first; matches monolith look |
| Components | **shadcn/ui (nova theme)** primitives on **Radix** | Accessible, unstyled base we restyle |
| Routing | **React Router** | Client-side routing |
| Icons | **lucide-react** | Same icon set as monolith |

**Dev**
- Runs at **http://localhost:5173**.
- Talks to the backend at **http://localhost:5001** via **VITE_API_URL** (env).
- **@/** path alias → **src/** (configured in **vite.config.ts** + **tsconfig**).

**One TypeScript gotcha we hit repeatedly:** the project uses
**verbatimModuleSyntax**. That means **types must be imported with the type
keyword**, otherwise the build fails:

```ts
import { apiGet, type ApiError } from "@/shared/lib/api-client";
```

---

# Page: Folder Structure & Conventions

The frontend is organised into **two top-level zones** inside **src/**:

```
src/
├── features/          ← one folder per business domain (modularised)
│   ├── auth/
│   ├── claims/
│   ├── attendance/
│   ├── leave/
│   ├── employees/
│   ├── policies/
│   ├── teams/
│   └── settings/
└── shared/            ← everything reused across features
    ├── lib/           ← api-client.ts, geolocation.ts, utils.ts
    ├── components/ui/ ← Select, buttons, inputs, modal shells…
    └── types/         ← session, shared DTO-ish types
```

**The rule of thumb (this is the "Structure" idea from OneNote):**

- **Features folder** = the actual product. Each feature is *modular* — it owns:
  - **api.ts** — the typed calls to the backend for that domain,
  - **components/** — the screens/widgets for that domain,
  - **lib/** (optional) — domain-only helpers.
- **Shared folder** = anything reused by more than one feature. If two features
  need it, it moves to **shared/**. The searchable Select, the API client, and
  formatting helpers all live here.

**Why split this way?** A feature should be *deletable* — remove the folder and
the app still compiles except for its routes. Shared code is the only thing
features are allowed to depend on. Features never import from each other.

---

# Page: The Shared API Client

Every network call goes through **one file**: **src/shared/lib/api-client.ts**.
Nothing else calls **fetch** directly. This is the frontend's equivalent of the
backend's "repositories are the only place EF runs" rule.

**What it gives you:**

```ts
apiGet<T>(path)              // GET  → T
apiPost<T>(path, body)       // POST (JSON) → T
apiPut<T>(path, body)        // PUT  (JSON) → T
apiDelete(path)              // DELETE
apiPostForm<T>(path, form)   // POST multipart/form-data (receipts, selfies)
apiGetBlob(path)             // GET a file (receipt download)
setAuthToken(token | null)   // set/clear the in-memory bearer token
```

**Design points worth remembering:**

1. **Auth token lives in memory**, not localStorage. **setAuthToken()** stores it
   in a module variable; every request attaches **Authorization: Bearer <token>**.
   On a full page reload the token is gone — we re-acquire it via the refresh
   cookie (see the Auth flow page).
2. **credentials: "include"** on every request so the httpOnly refresh cookie
   rides along.
3. **ApiError** — a custom Error subclass. When the backend returns a non-2xx,
   we throw **ApiError** carrying **status** and a machine-readable **code**
   string. That code is how the UI reacts to specific errors without
   string-matching messages — e.g. geofencing throws
   **OFF_SITE_ACTION_REQUIRED**, and the attendance screen catches exactly that
   code to prompt for a remark + photo.
4. **204 No Content → undefined** so callers don't try to parse an empty body.

**Feature api.ts files build on top of this**, e.g. **features/claims/api.ts**:

```ts
export const getMyClaims  = () => apiGet<Claim[]>("/claims/mine");
export const getTeamClaims = () => apiGet<Claim[]>("/claims/team");
export const approveClaim  = (id: string) => apiPost(`/claims/${id}/approve`, {});
```

The component never sees a URL — only these named functions.

---

# Page: Design System

The look is a **custom purple glassmorphism** system layered on shadcn:

- Primary brand purple: **#4c1a86**.
- Cards/surfaces: large radius **rounded-[28px]**, soft borders, frosted panels.
- **No gradients anywhere** — solid backgrounds only. (Radial/linear washes were
  explicitly removed from the body; this is a hard rule for this project.)
- Custom scrollbar utility: **.nice-scrollbar** in **src/index.css**.

**A layout lesson baked into the components** — a scrolling element *cannot clip
its own scrollbar to a border-radius*. So every modal splits into two elements:

- an **outer wrapper** that owns the rounded shape and **overflow-hidden**,
- an **inner** element that does the scrolling (**max-h-[90vh]**, **.nice-scrollbar**).

That's why our modals look like the scrollbar respects the rounded corner — the
round corner is on a different element than the scroll.

---

# Page: Shared UI — the Searchable Select

**src/shared/components/ui/select.tsx** is the one dropdown used everywhere
(settings, claims, leave, teams). It's a **Radix Select** we restyled, with one
key behaviour:

- If the list has **more than 7 items**, it automatically shows a **search box**
  at the top and filters as you type. Under 7, it's a plain dropdown.
- It matches the monolith's dropdown behaviour on purpose, so the two apps feel
  the same.

**A convention that goes with it:** the "nothing selected" option uses a sentinel
constant, **NO_SELECTION = "__none__"**, instead of empty string — Radix treats
empty string specially, so we needed a real value to represent "none" (e.g. "No
supervisor", "Use org default policy").

Because it lives in **shared/**, adding a searchable dropdown to a new feature is
a one-line import — no feature re-implements a dropdown.

---

# Page: Auth & Session flow

**The shape of it:**

1. **Login** → **POST /auth/login** returns a short-lived **access token** (JWT,
   15 min) in the JSON body, and sets a **refresh token** as an **httpOnly
   cookie** (7 days, **Path=/auth**, **SameSite=Lax**).
2. **setAuthToken(accessToken)** puts the JWT in memory; every later request
   carries it as a bearer token.
3. **On app mount / page reload** the in-memory token is gone, so we call the
   refresh endpoint. If the refresh cookie is still valid, we get a fresh access
   token (and the refresh token rotates). If not, we're logged out.
4. **Logout** clears the in-memory token and revokes the refresh token server-side.

**Why in-memory access token + httpOnly refresh cookie?** The short-lived JWT is
never written to disk/localStorage (so XSS can't steal a long-lived credential),
and the long-lived refresh token is httpOnly (so JS can't read it at all). This is
the standard "silent refresh" pattern.

The current user (id, email, role, org) is read from the JWT/session and typed in
**src/shared/types/**. **Important:** the UI uses role for *what menus to show*,
but the backend never trusts the frontend for authorization — every rule is
re-checked server-side.

---

# Page: Feature Modules (walkthrough)

Each feature folder is self-contained. Quick tour:

- **auth/** — login screen, session bootstrap, refresh-on-mount.
- **claims/** — **ClaimsPage** (file a claim: title, category, amount, receipt
  upload, personal-vs-company payment, mileage) + **ClaimsApprovals** (team
  queue). Calls **getMyClaims** / **getTeamClaims** / **approve** / **reject**.
  Shows an over-limit flag when an account's spend limit is exceeded.
- **attendance/** — clock in/out. Requests geolocation, sends coordinates. If the
  backend replies **OFF_SITE_ACTION_REQUIRED**, the screen forces a **remark +
  photo** and retries. Day-bucketed by Kuala Lumpur time.
- **leave/** — apply for leave (type, date range → inclusive day count), see
  **balances** (entitlement − approved, pending shown separately), and a team
  approvals queue. Balances honour per-policy overrides.
- **employees/** — admin assigns each employee a **role**, a **supervisor**, and a
  **policy**. Uses the searchable Select for all three.
- **policies/** — inside settings; create/edit **policy bundles** (module access,
  geofence/selfie enforcement, salary type, OT settings, per-policy leave
  entitlements), mark one default, archive.
- **teams/** — inside settings; build the **team hierarchy**: a team belongs to a
  project, has N **layers** with labels, members sit at a layer, and a **module
  approval matrix** decides which layers approve Claims / Leave / OT / Attendance.
- **settings/** — the shell (**SettingsView**) with tabs: Organization, Employees,
  Policies, Projects, Teams, Accounts, Leave. Each tab is a **\*Settings.tsx**.

**The pattern every feature repeats:** typed **api.ts** → component calls it →
renders. No feature reaches past **shared/** for anything cross-cutting.
