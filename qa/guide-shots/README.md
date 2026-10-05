# User-guide screenshots

Playwright scripts that retake the screenshots in `user-guide/shots/` from the
**local demo company**, at the sizes the guides already use. The `user-guide`
agent (`.claude/agents/user-guide.md`) runs these; you can too.

## Never the shared database

`ConnectionStrings:Default` in user-secrets points at the shared DigitalOcean
database. `start-local-demo.sh` starts the backend with an explicit override to
the local MySQL database `altomatehr_local` (demo data seeded, email, Gemini
and Xero switched off). It reuses a backend already on `:5001` only if that
process's environment shows the same localhost override. Otherwise it refuses.
`setup.mjs` refuses any API that isn't localhost.

## Run

```bash
cd qa/guide-shots
npm install                     # once; Chromium: npx playwright install chromium
./start-local-demo.sh           # backend :5001 (local DB) + frontend :5173
node setup.mjs work-permits     # only the demo data a shot needs (see the steps)
node shots.mjs executive-overview employee-work-permit   # or a group: admin | evan | sara | all
node check-guide.mjs            # every <img> in both guides loads
./stop-local-demo.sh            # stops only what start-local-demo.sh started
```

Shots land in `.out/shots/<admin|employee>/<name>.png`, named as in
`user-guide/shots/`. Look at each one before copying it across. A failed
step leaves `.out/shots/debug-<name>.png`.

## Adding a shot

Add a recipe to `shots.mjs` in the right group: `admin` (desktop, Demo Admin),
`evan` (phone, Evan Employee) or `sara` (phone, Sara Supervisor). Name it after
its file. If the seed doesn't have the data the screen needs, add a step to
`setup.mjs`. Use made-up demo values only, because this repo is public.

Sign-in is rate limited to 5 a minute. Both scripts back off and retry, so a
run can pause for about 20 seconds.
