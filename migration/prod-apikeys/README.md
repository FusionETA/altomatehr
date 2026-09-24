# hr_prod → altomatehr : API keys and master keys

Copies all 4 v1 `MasterApiKey` rows into v2 `MasterKeys` and all 56 v1
`ApiIntegration` rows into v2 `ApiKeys`, keeping each token's hash. v1 and v2 hash
the same way (SHA-256 of the full token, lowercase hex), so every token that works
on v1 today works on v2 once copied. New-Altomate only changes its base URL.

```sh
node load-keys.mjs            # dry run: inserts in a transaction, prints the scope sets, ROLLBACK
node load-keys.mjs --commit
```

## Order

1. **`1dd959c` must be deployed first.** A `wp_live_` key acts as an Admin of its
   company. Before that release no payroll endpoint checked a scope, so the 10 keys
   v1 does not let change payroll (9 with no payroll scope at all — 7 Altomate
   Corporate Services keys, Demo-Testing, HRGenie — and 1 read-only) could approve or
   delete payroll runs on v2.
2. **`../prod-keyorgs` must have run.** 26 of the keys belong to companies that
   only that migration creates. The preflight refuses to write anything while any
   key's company is missing, or exists under the same id with a different name.

## Scope mapping

| v1 | v2 |
|---|---|
| `chart-of-accounts:read/write` | `accounts:read/write` |
| `settings:read/write` | `organizations:read/write` |
| `approvals:write` | dropped — covered by `claims:write` / `leave:write`, which every key holding it also has |
| everything else (`employees`, `claims`, `leave`, `attendance`, `projects`, `teams`, `policies`, `payroll`) | same name |
| — | `sso:write` added **only** to keys issued by the "Altomate Corporate Services Sdn Bhd" master key (48 keys): New-Altomate is the one partner that signs users in via `/sso/ticket` |

Every mapped scope is checked against v2's list (`ApiScopes.cs`); anything unknown
aborts the run.

## After cutover

The Altomate Corporate Services master token is hard-coded as a fallback in
New-Altomate (`altomate-payroll/index.js`), so treat it as leaked. Once New-Altomate
is running against v2: issue a new v2 master key, put it in New-Altomate's
`WORKPULSE_MASTER_TOKEN`, remove the hard-coded fallback, and set the copied key's
`Active = 0` in both v1 and v2.
