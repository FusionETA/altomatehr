# Section: CI-CD

> Each **"Page:"** below → one OneNote page under the **CI-CD** section.
> **Status: mostly aspirational.** Today the flow is manual; this section
> documents what we do now and the pipeline we intend to add.

---

# Page: Current Manual Flow

There is **no automated CI pipeline yet**. Verification is a manual loop, run
locally before every commit:

**Backend**
```bash
dotnet build      # must compile clean
dotnet test       # 41 tests must pass
```

**Frontend**
```bash
npm run build     # tsc + vite build must pass (verbatimModuleSyntax is strict)
```

**Manual smoke test** — boot both servers and click through the changed module,
or hit endpoints with **curl** against **http://localhost:5001**. We verify with
build/test/curl + a quick click-through, **not** by screenshotting in a browser
(that's slow and wasteful here).

**The rule:** a broken build never gets committed. If build or test fails, we
stop and fix before staging.

---

# Page: Git Workflow

The working discipline around commits (this is followed by hand today, and is
what a pipeline should eventually enforce):

1. **Sync with main first.** Before committing/pushing, fetch and check
   **HEAD..origin/main**; if behind, merge **main** in first.
2. **Build before commit.** Run **npm run build** (and **dotnet build** /
   **dotnet test**) after staging and before committing. Halt on failure — never
   commit a broken build.
3. **Commit locally, then stop.** Never chain commit + push in one step. Commit,
   then wait for an explicit go-ahead before pushing.
4. **Conventional Commits** messages: **feat:**, **fix:**, **refactor:**,
   **chore:**.
5. Feature work happens on the working branch (**Zi-Rong**); **main** is the
   integration branch.

**Secrets never enter git.** The DB connection string and JWT signing key live in
**.NET user-secrets** locally, never in a committed file.

---

# Page: Proposed GitHub Actions Pipeline

The natural next step is to codify the manual loop as a GitHub Actions workflow
that runs on every push / PR:

```yaml
# .github/workflows/ci.yml  (proposed)
name: CI
on: [push, pull_request]

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release   # gate on the 41 tests

  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '20' }
      - run: npm ci
      - run: npm run build                       # tsc + vite build
```

**Gates a PR has to pass (as of 2026-09-02):** backend builds, **154 tests**
green, the EF model snapshot matches the model
(**dotnet ef migrations has-pending-model-changes**), frontend builds.

The snapshot check was added after a clean text merge left
AppDbContextModelSnapshot.cs no longer describing the real model — twice. It
needs no database and costs a few seconds. See ADR-02's amendment and the two
Repair*Snapshot migrations.

Still optional later: linting, **dotnet format --verify-no-changes**.

---

# Page: Deployment (future)

Not built yet — captured here so the target is written down.

- **Database:** already on **DigitalOcean** (managed MySQL/MariaDB). Migrations
  applied with **dotnet ef database update** against the target connection string.
- **Backend:** publish the ASP.NET Core app (container image or DO App Platform).
  Secrets (connection string, JWT key) injected as environment variables /
  platform secrets — never baked into the image.
- **Frontend:** **npm run build** produces static assets → served from a static
  host / CDN, with **VITE_API_URL** pointed at the deployed backend.
- **Sequence:** run migrations → deploy backend → deploy frontend. Keep migrations
  backward-compatible so backend can roll out without breaking the old frontend.

**Target host:** a **DigitalOcean droplet** (a plain VM), not App Platform —
so the pipeline has to do the work App Platform would otherwise handle: build,
ship the artifact to the box, restart the service behind a reverse proxy.

**Open items:** environment separation (staging vs prod), automated migration
step in the pipeline, and a health-check endpoint for deploy verification.

**Parked together — the next CI/CD chunk (2026-09-02):**

1. **Deploy workflow** — build → publish → deploy to the droplet on merge to
   main. Needs: SSH key or DO token in repo secrets, a systemd unit (or
   container) on the box, a reverse proxy, and the migration step run *before*
   the backend swaps over.
2. **Dependabot security alerts** — advisory-driven only, not version churn.
   Repo Settings → Code security. Grouped here because both are repo-settings
   and pipeline work, and both want the same "who approves a deploy" thinking.

Deliberately deferred, not forgotten. Nothing about either is urgent: CI is
green, and `dotnet list package --vulnerable --include-transitive` covers the
advisory question by hand until Dependabot does it automatically.
