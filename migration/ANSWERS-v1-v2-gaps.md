# Answers: the three v1→v2 questions

Answered on the droplet, 2026-09-18, against the live `hr_prod` and `altomatehr`
databases and both codebases. Replaces Questions 1–3 of `PROMPT-v2-schema-gaps.md` —
those were written to be asked of a local agent, which cannot see either database.

---

## Q1 — approval chains: what is actually different, and why does a straight load break?

### The one-line version

v1 **stores** each employee's approval chain as rows. v2 **computes** it from the team
roster, and only stores the exceptions. Same table shape, opposite meaning — so loading
v1's rows into v2's table doesn't move the chain across, it overwrites the rule that
generates it.

### 1. The models

**v1 — the chain IS the rows.**

```prisma
model ApprovalChainStep {
  employeeId String    // whose chain
  teamId     String?   // scoped to one team
  step       Int       // 1, 2 — position in the sequence
  approverId String    // one approver
  @@unique([employeeId, teamId, step, approverId])
}
```

`resolveModuleChain()` reads those rows and — this is the important line —

```ts
if (chainSteps.length === 0) return []
```

No rows means **no approvers**. v1 never derives a chain from the team; the rows are the
only source. An admin builds each employee's chain by hand.

**v2 — the chain is derived; the table holds deviations.**

```csharp
// An explicit, admin-picked override of who approves for ONE employee at ONE
// layer of a team — as opposed to the implicit default (everyone else who
// happens to sit at that layer). Only exists where an admin deliberately
// deviated from the default; its absence means "use the default."
public class TeamApprovalOverride { TeamId; EmployeeId; Layer; ApproverIdsJson; }
```

`ApprovalChainService.Build()` walks the team's layers above the employee's own and takes
everyone standing there:

```csharp
for (var layer = membership.Layer + 1; layer < team.LayerCount; layer++)
    // override for this layer? use it (re-filtered). otherwise: everyone else at this layer.
```

### 2. Why that difference breaks a straight load — four distinct failures

**(a) `step` and `Layer` are different axes.** v1's `step` is a position in a sequence
(1 then 2). v2's `Layer` is a rung of the team hierarchy, and v2 derives the order from
it. They coincide almost never — measured across all 492 rows:

| | rows |
|---|---|
| approver's actual layer **equals** the v1 `step` | **8** |
| approver's layer **differs** from `step` | 463 |
| approver isn't on that team at all | 26 |

So `Layer = step` is wrong for 98% of the rows. The correct source for `Layer` is the
approver's own membership layer in that team — which is a join v1 never needed to make.

**(b) v1 layers are 1-based; v2 layers are 0-based.** v1 memberships run `1..layerCount`
(measured: min 1, max 3, never 0). v2 runs `0..LayerCount-1` — its loop condition is
`layer < team.LayerCount`, so `LayerCount` itself is out of range by definition.

This is not theoretical. `altomatehr.Teams` already has the prod teams loaded with
`LayerCount` copied **verbatim** (38 teams at 1, 52 at 2, 65 at 3, 1 at 10 — identical to
v1). The moment team memberships are loaded 1-based against those verbatim counts, every
employee's top approver falls outside the loop, and in a 1-layer team the only member sits
at a layer v2 will never walk. **Everyone silently loses their approvers, and nothing
errors.** 340 of 498 chain rows already point at a layer `> LayerCount-1`.

Worse, the two loads that have happened disagree with each other. The 5 team memberships
already in `altomatehr.TeamMemberships` came from the earlier `hr_dev` ZR TEST slice and
carry raw 1-based layers (1 and 2) — and whoever loaded them compensated by bumping those
teams' `LayerCount` from 2 to 3 and padding `LayerLabels` from `["",""]` to `["","",""]`.
The prod teams got no such padding. So there are currently two incompatible conventions in
one table. Fix: re-base the layer (`Layer = layer - 1`) and leave `LayerCount`/`LayerLabels`
alone — that also lines the labels up, since v2 indexes `LayerLabels[layer]` and v1's
`["Staff","Layer 2","Manager"]` is already in that order.

**(c) v2 re-validates overrides against the live roster, and silently empties them.**

```csharp
var validAtLayer = roster.Where(m => m.Layer == layer).Select(m => m.EmployeeId).ToHashSet();
approvers = DeserializeList(ov.ApproverIdsJson)
    .Where(id => validAtLayer.Contains(id) && !administrative.Contains(id)).ToList();
...
if (approvers.Count == 0) continue;   // step skipped
```

An override naming an approver who isn't at that exact layer of that exact team resolves
to an empty list, and the step is **skipped, not failed**. v1 deliberately does the
opposite — it keeps cross-team approvers ("preserve the routing target"). 26 of our rows
name an approver who isn't on the team; 23 more name one at or below the employee's own
layer, which v2's loop never reaches. Counting every cause, **381 of 498 rows cannot be
represented in v2 as they stand** — and every one of them fails by quietly producing an
employee with no approver rather than by raising an error anyone would notice.

Also note v2 filters out admins and owners entirely ("oversight, not links in the chain").
v1 does not. Any v1 chain whose approver is an admin dissolves on arrival.

**(d) The semantics invert in both directions.** Load all 444 grouped rows and every
employee's chain becomes an explicit frozen override: change a team's membership
afterwards and those chains keep pointing at the old approvers forever, because an
override doesn't track the roster. Meanwhile the ~200 employees with *no* v1 chain rows —
who in v1 have **no approvers at all** — get v2's implicit default instead, so approvals
appear where v1 had none. A straight load gets it wrong in both directions at once.

### 3. The precondition nobody has hit yet

`altomatehr.TeamMemberships` holds **7 rows** — 2 demo, 5 from the dev slice. Team
*rosters* have not been migrated. `Teams` (156 from v1) and `Projects` (737) are in;
who stands on which rung is not.

Until that lands, `Build()` returns `[]` for everyone (`if (mine.Count == 0) return []`),
every override loaded would be inert, and there is nothing to validate approver layers
against. **Team memberships, correctly re-based, have to be migrated before the approval
chain can be looked at at all.** `TeamApprovalOverrides` is empty today (0 rows), so
nothing is broken yet — this is a sequencing decision, not a repair.

### 4. Recommendation

1. Migrate `EmployeeTeamMembership` → `TeamMemberships` with `Layer = layer - 1`, leaving
   `LayerCount` and `LayerLabels` as they are. Fix the 5 dev-slice rows and the 2 padded
   teams to match while you're there.
2. Then compute, for each of the 444 (employee, team, layer) groups, what v2's implicit
   default *would* produce, and diff it against v1's explicit chain.
3. Load only the groups that genuinely differ. Those are the real overrides; the rest are
   v1 writing down what v2 already derives, and freezing them into JSON buys nothing and
   costs you every future team change.
4. The ~200 employees with no v1 chain need a product decision, not a script: v1 gives
   them no approvers, v2 will give them the team default. That's a behaviour change either
   way, and it should be a deliberate one.

The table has never held a row in v2, so its whole read/write path is unexercised. Worth
tests before the first real load, not after.

---

## Q2 — set the project managers from the migrated users

### Short answer: done and staged — but v2 has nowhere to put it yet.

No name matching was needed. `_mig_peoplemap` already maps every v1 user id to its v2
`Users.Id`, so this resolves by id, which is exact — name matching on this data would have
been the worse option anyway (two managers share a surname and several names carry
`A/L` / `BIN` particles that normalise badly).

Built `altomatehr._mig_projectmanagers` (`migration/prod-projectmanagers/00-stage.sql`):

| | |
|---|---|
| assignments staged | **84** |
| distinct managers | 20 |
| distinct projects | 84 |
| projects not found in v2 | **0** |
| managers not found in v2 `Users` | **0** |

Every row resolves. Biggest holders: MUHAIMIN BIN MUSTAPHA (13 projects), MUVETHEN A/L
BALASUBRAMANIAM (12), NUR ZAIRULLIZAM BIN ZAINI (7) — all Globe.

### What blocks the load

`altomatehr.Projects` has no project-manager column, and there is no `ProjectManagers`
table. The v1 side is **not** just the scalar `XeroProject.projectManagerId` that the
earlier gap analysis described — v1 has a full join table:

```prisma
model ProjectManager {
  projectId String
  userId    String
  @@unique([projectId, userId])
}
```

…and v1's own domain model says the scalar is vestigial:

```ts
/// Legacy single-PM column (XeroProject.projectManagerId). Kept only to
/// avoid breaking older callers; all new code reads from projectManagers
/// (the join-table-backed array) below.
```

v1 writes the scalar as a mirror of "first picked PM" and validates that every manager is
a SUPERVISOR, ADMIN or OWNER. Today all 84 assignments are one-manager-per-project, so a
scalar column would hold the current data without loss — but it would cap a capability v1
already ships.

**So: add `ProjectManagers` as a child table** (`Id, OrganizationId, ProjectId, UserId,
CreatedAt`, unique on `(OrganizationId, ProjectId, UserId)`, `ITenantScoped`), mirroring
v1. `migration/prod-projectmanagers/01-load.sql.PENDING` has the load ready for either
shape — uncomment the block that matches and run it. It is a 2-minute job once the column
exists.

One caveat worth deciding before you build UI on it: nothing in v2 currently reads a
project manager. It does not affect approvals (those route through team layers), and v2's
`ApprovalChainService` resolves a team from a project without ever consulting one. So on
arrival this is a label on a settings screen with 84 real values behind it. That's a fine
reason to migrate it — it's data an admin typed and expects to still see — but it isn't
wiring anything up by itself.

---

## Q3 — "after I add another column, will `isCustom` work?"

**Adding the column is necessary and sufficient to hold the flag — but it does nothing on
its own, and one of the four follow-ups is easy to miss.**

To actually get those 15 flags into v2:

1. **Add `IsCustom` (bool, default false)** to `ChartOfAccount` and generate an EF
   migration. Nothing stops this — the earlier "cannot be migrated" framing was wrong. It
   is a boolean on 15 rows.
2. **Re-run the COA migration**, and add `IsCustom` to the `ON DUPLICATE KEY UPDATE` list
   in `prod-coa/01-coa.sql`. This is the step people miss: the 672 accounts are already
   loaded, so a new column arrives `false` for all of them and no amount of re-running
   fixes that unless the upsert names the column. The migration is idempotent, so the
   re-run itself is safe.
3. **Expose it** in `ChartOfAccountDto` / the accounts settings UI if an admin is meant to
   see it. A column nobody renders is invisible.
4. **Decide what reads it** — see below.

### Does v2 already know, without the flag?

Yes, on this data. Measured across all 1170 v1 accounts in scope:

| `isCustom` | rows | of which `xeroAccountId IS NULL` |
|---|---|---|
| 1 | 15 | **15** |
| 0 | 1155 | **0** |

The correlation is exact: `isCustom` ⟺ `xeroAccountId IS NULL`. v2 can already tell a
hand-typed account from a Xero-synced one with `XeroAccountId IS NULL`, and that is how
v2's own sync behaves — `XeroService.SyncAccountsAsync` matches on
`GetAccountByXeroIdAsync(orgId, xeroAccount.AccountId)`, so a row with a null
`XeroAccountId` is never matched, never updated, never archived by a sync. **A Xero
re-sync will not wipe hand-made accounts**, with or without the flag. That was the one
argument that would have made this column load-bearing, and it doesn't hold.

There's also no unique constraint on `(OrganizationId, Code)` in v2 — the only index is
`(OrganizationId, XeroAccountId)` — so a hand-made account sharing a code with an incoming
Xero account won't collide either. (Unlike v1, where `ChartOfAccount_organizationId_code_key`
is exactly what trips on a tenant switch.)

### Recommendation

Add it if you want the 15 flags preserved as data an admin set — it is cheap and harmless,
and provenance is worth keeping. Don't add it expecting it to change any behaviour: today
nothing would read it, and `XeroAccountId IS NULL` already answers the only question it
was asked. If you add it, do step 2 or you'll have a column of 672 `false`s and think the
migration ran.
