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

**Gates a PR should have to pass:** backend builds, 41 tests green, frontend
builds. Optional additions later: linting, **dotnet format --verify-no-changes**,
and a migrations check (**dotnet ef migrations has-pending-model-changes**).

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

**Open items:** environment separation (staging vs prod), automated migration
step in the pipeline, and a health-check endpoint for deploy verification.
