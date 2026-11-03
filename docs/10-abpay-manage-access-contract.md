# ABPay × AltomateHR: honouring each admin's Manage access

**Status:** built on branch `rachel`, 2026-11-03. Backend only — ABPay's own
changes are separate.

## The problem this solves

ABPay reads a company through that company's `wp_live_` API key, and a company
key sees the **whole** company. Nothing in the key says the signed-in admin's
Manage access in AltomateHR is limited to some employees, so an admin
restricted to (say) Clevemond saw every employee in ABPay, and could see and
post their pay.

AltomateHR now answers "what may this person reach in this company?" on a
master-key endpoint. ABPay keeps reading the company with the company key and
filters what it shows to what that endpoint allows.

## What Manage access actually restricts in AltomateHR

Read this before mapping anything — the model is not "a list of employees".
An **Owner is never restricted**; only an **Admin** can be. The Owner sets
three independent limits per admin (`Settings → Admins → Manage access`):

| Limit | Stored as | What it means |
|---|---|---|
| **Modules** | per module Off / View / Manage | which parts of the app they reach at all |
| **Employees** | a set of **employee policies** | they see only people **on those policies** |
| **Settings** | one switch | may change the company's configuration |

Two consequences that matter to ABPay:

- **Employees are restricted by POLICY, not by a hand-picked list.** A person's
  policy is their own, or the company default when they have none. So "everyone
  on Monthly Workers" includes people who are on it by default. AltomateHR
  resolves this to concrete people for you — you never see policy ids.
- **A limited admin can never run company-wide payroll.** A run must cover
  every employee, so AltomateHR already makes payroll **view-only** for an
  admin limited to some policies, whatever their Payroll module grant.

**There is no per-project / per-outlet restriction.** Projects are not part of
Manage access. The only project-level limit possible is the `projects` **module**
being Off for that admin, which the `modules` map below reports.

## What the endpoints return **today** (before this change)

`POST /auth/verify` and `POST /auth/organizations` already returned **only the
organizations the person is an Owner or Admin of** (deactivated companies
excluded) — so a limited admin never saw a company they don't administer. What
they did **not** say is anything about limits *inside* a company: no role-level
detail beyond `role`, no employee scope, no payroll flag. That is what's new.

Nothing about `GET /employees`, `GET /projects`, `GET /payroll/employees` or the
payroll-run endpoints has changed. Company keys still see the whole company.

---

## The contract

Base URL: the AltomateHR v2 API (`https://…`, same host ABPay uses today).
**ABPay should call all three with the platform master key**
(`Authorization: Bearer wp_master_…`).

Strictly: `/auth/access` accepts the master key *only* (403 for anything else).
`/auth/verify` and `/auth/organizations` do accept a company (`wp_live_`) key —
that is unchanged — but they answer it about its own company only, and they
tell it **nothing** about a person's limits (`allEmployees` and `canRunPayroll`
come back `null`). So use the master key.

**Rate limits.** `/auth/verify` is on the login limiter: **5 requests per
minute per source IP**, shared with `/auth/login`. ABPay calls it from one
address, so six people signing in within a minute will throttle the sixth. It
is deliberately tight — anyone holding a key can use `/auth/verify` as a
password oracle, which is the one thing a limit here protects against.
`/auth/organizations`, `/auth/access` and `/whoami` carry no password and are
on a separate **60 per minute per IP** bucket.

A throttled call returns **`429`** with a *ProblemDetails* body and a
`Retry-After` header:

```json
{ "title": "Too many requests.",
  "detail": "Too many attempts. Please wait before trying again.", "status": 429 }
```

⚠️ Note it has **no `message` field**. ABPay branches on `message` to tell a bad
app credential from a wrong password, so treat a `429` as its own case — retry
after `Retry-After`, never as a sign-in failure shown to the user.

### 1. `POST /auth/verify` — sign-in (unchanged inputs, two new fields per org)

```json
{ "email": "someone@example.com", "password": "…" }
```

`200`:

```json
{
  "id": "usr_…",
  "name": "Siti",
  "email": "someone@example.com",
  "role": "Admin",
  "organizationId": "72f6f95a-1f75-4164-be70-8244d63f8c10",
  "organizationName": "EMJ RENOVATION SDN. BHD.",
  "organizations": [
    {
      "id": "72f6f95a-1f75-4164-be70-8244d63f8c10",
      "organizationId": "72f6f95a-1f75-4164-be70-8244d63f8c10",
      "name": "EMJ RENOVATION SDN. BHD.",
      "role": "Admin",
      "allEmployees": false,
      "canRunPayroll": false
    }
  ]
}
```

- `organizations` holds **only** the companies this person owns or administers,
  deactivated ones excluded. Unchanged behaviour.
- `organizationId` duplicates `id` — it was there before; keep using either.
- **`allEmployees`** *(new)* — `false` means this person is limited in that
  company and ABPay must call `/auth/access` for it.
- **`canRunPayroll`** *(new)* — `false` means hide/disable creating, generating
  and posting a run for that company. Both are always `true` for an Owner.
- Both are **`null` when called with a company (`wp_live_`) key**, not `true`.
  These endpoints take a bare email with no password, so telling an org key
  which of its admins are restricted would hand it the directory that
  `/auth/access` 403s it for. Use the master key, as ABPay does.
- `401 { "message": "Invalid credentials for this organization." }` for a wrong
  password, an unknown email, and someone who administers nothing — one answer,
  deliberately.
- `429` when over 5/min from this address — see **Rate limits** above. It is
  not a credential failure.

### 2. `POST /auth/organizations` — refresh (same two new fields)

```json
{ "userId": "usr_…" }          // or { "email": "…" }
```

`200 { "organizations": [ …same objects as above… ] }`

No password, so it is on the 60/min bucket, not the login one — a refresh storm
across several admins will not throttle.

### 3. `POST /auth/access` — **new**: what one person may reach in one company

```json
{ "userId": "usr_…", "organizationId": "72f6f95a-1f75-4164-be70-8244d63f8c10" }
```

`200`, unrestricted admin or owner:

```json
{
  "userId": "usr_…",
  "organizationId": "72f6f95a-…",
  "role": "Owner",
  "allEmployees": true,
  "canRunPayroll": true,
  "canChangeSettings": true,
  "modules": { "employees": "Manage", "payroll": "Manage", "projects": "Manage", "…": "…" }
}
```

`200`, limited admin:

```json
{
  "userId": "usr_…",
  "organizationId": "72f6f95a-…",
  "role": "Admin",
  "allEmployees": false,
  "canRunPayroll": false,
  "canChangeSettings": false,
  "modules": { "employees": "Manage", "payroll": "View", "projects": "Manage" },
  "employees": [
    { "userId": "usr_abc", "employeeProfileId": "emp_123" },
    { "userId": "usr_def", "employeeProfileId": null }
  ]
}
```

| Field | Meaning |
|---|---|
| `role` | `"Owner"` or `"Admin"` in **this** company |
| `allEmployees` | `true` = no employee restriction. **`employees` is then absent** — AltomateHR will not send thousands of ids to say "everything". |
| `employees` | present **only** when `allEmployees` is `false`. May be a short list; see the note below about it never being empty. |
| `employees[].userId` | **the id to match people by.** Exactly the `id` that `GET /employees` returns, and exactly the `userId` that `GET /payroll/employees` returns |
| `employees[].employeeProfileId` | their payroll identity (payslips, run members). **`null`** when their payroll details were never saved. ⚠️ **Do not filter `GET /payroll/employees` by this field** — see below |
| `canRunPayroll` | may create / generate / submit a run. `false` when the Payroll module is below Manage **or** `allEmployees` is `false` |
| `canChangeSettings` | may change the company's configuration (not needed by ABPay today; included because it is part of the same setting) |
| `modules` | the modules this person actually has, and at what level: `"Manage"` or `"View"`. A module that is **off** for them is **absent from the map**. Use `modules.projects` to decide whether to show outlets at all. |

> **Match the payroll roster by `userId`, not `employeeProfileId`.**
> `GET /payroll/employees` deliberately lists a member who has **no** payroll
> record yet (that is its job — to show who is not ready to file). It stands a
> blank profile in for them, so the `employeeProfileId` on that row is a
> throwaway value that is **different on every call**; the row says so with
> `hasPayrollProfile: false`. Filtering the roster on profile ids would drop
> exactly those people, while AltomateHR's own screen still shows them to that
> same admin. `userId` is on both endpoints and is stable — use it.

Status codes:

| Code | When |
|---|---|
| `200` | resolved |
| `400` | `userId` or `organizationId` missing |
| `401` | no key, or an invalid/revoked one |
| `403` | a valid **company** (`wp_live_`) key — this endpoint is master-key only |
| `404` | no such user, the user is not an Owner/Admin of that company, or the company is deactivated — one answer for all three |
| `429` | over 60 requests a minute from one address |

### Rules this endpoint keeps

- **Live, never cached.** Every call re-reads Manage access. An Owner who
  narrows an admin at 10:00 is honoured on ABPay's next call.
- **Advisory, not enforcing.** This endpoint *reports* a person's access; it
  does not police the company key ABPay reads and posts with. Treat every flag
  here as something ABPay must act on itself.
- **An empty restriction is not an error.** An admin whose policies cover
  nobody comes back `allEmployees: false` with a list containing only
  themselves — not a 404, not an error.
- **The person themselves is in the list.** AltomateHR's own `GET /employees`
  shows a limited admin to themselves, so the list matches it exactly. An admin
  is never on the payroll roster, so they can never match a timesheet row.
- **Master key only.** A company key may already read its own company; letting
  it ask what *another person* may reach would turn it into a directory of who
  is restricted.
- **Nothing ABPay already uses has changed.** `GET /employees`,
  `GET /projects`, `GET /payroll/employees` and the run endpoints behave exactly
  as before, and a company key still sees the whole company. ABPay does the
  filtering.

### Probing a deployment

`GET /whoami` lists `features`; this endpoint is announced as **`auth.access`**.
Check for it before relying on it, so ABPay and AltomateHR can ship
independently.

It answers a **company key or the master key**. A master caller gets
`kind: "master"`, the `features` list, and nothing about any company
(`organizationId`, `apiKeyId`, `tokenName`, `role` all `null`, `scopes` empty)
— it belongs to no company, so there is nothing else to tell it. A company key
gets `kind: "key"` with its own company and scopes as before, and a signed-in
human `kind: "user"`.

---

## How ABPay should use it

1. **Sign-in** — `POST /auth/verify`. Show only the companies in
   `organizations`. For each, remember `allEmployees` and `canRunPayroll`.
2. **Per company opened** — if `allEmployees` is `false`, `POST /auth/access`
   and keep the `employees` list. Filter **both** `GET /employees` and
   `GET /payroll/employees` by **`userId`** (`id` on the first, `userId` on the
   second). Keep `employeeProfileId` for payslips and run members, where a
   person without one has nothing to reference anyway.
3. **Refresh** — `POST /auth/organizations`, then `/auth/access` again for each
   limited company.
4. **Before posting a run** — `POST /auth/access` once more and refuse if
   `canRunPayroll` is `false` or the run would include anyone outside
   `employees`.

   ⚠️ **ABPay is the only gate here.** It posts runs with the **company's**
   `wp_live_` key, and that key is not a person — AltomateHR sees a credential
   with full access to the company and cannot tell which admin is driving it.
   The in-app rule that stops a limited admin running payroll
   (`RequireFullEmployeeScope`) applies to a signed-in *human*, so it will
   never fire on ABPay's request. Nothing downstream will catch a run ABPay
   posts for an admin who should not have posted it. Re-check right before
   posting, because the access may have changed since the screen was drawn.
5. **Outlets** — hide them if `modules.projects` is absent. There is no
   per-project restriction to apply beyond that.
