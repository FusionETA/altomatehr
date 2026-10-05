#!/usr/bin/env bash
# altomatehr-v2-deploy.sh — deploy FusionETA/altomatehr (main) to this droplet.
#
# Called by the GitHub Actions "Deploy" workflow over SSH. The deploy key in
# /root/.ssh/authorized_keys is pinned to this script (command="..."), so that
# key can do nothing else. The workflow passes the commit it expects to ship as
# the SSH command; it arrives here in $SSH_ORIGINAL_COMMAND and is only logged
# and compared, never executed.
#
# Can also be run by hand:  sudo /usr/local/bin/altomatehr-v2-deploy.sh
#
# Steps: fast-forward to origin/main -> rebuild + restart the API container
# (EF migrations run on API startup) -> rebuild the frontend into a side dir and
# swap it in -> health-check both.
set -euo pipefail

# Run the real work detached from the caller and just stream its output. If the
# SSH connection drops (runner cancelled, network blip), the deploy still runs
# to the end instead of dying half-way — e.g. between moving dist/ aside and
# moving the new build in, which would take the site down.
if [ -z "${DEPLOY_DETACHED:-}" ]; then
  OUT=$(mktemp /tmp/altomatehr-v2-deploy.XXXXXX)
  DEPLOY_DETACHED=1 setsid nohup "$0" >"$OUT" 2>&1 </dev/null &
  PID=$!
  tail -n +1 -f --pid="$PID" "$OUT"
  set +e; wait "$PID"; RC=$?; set -e
  rm -f "$OUT"
  exit "$RC"
fi

APP_DIR=/opt/altomatehr-v2
BRANCH=main
COMPOSE_FILE=docker-compose.test.yml   # the file the live stack was started from
LOG=/var/log/altomatehr-v2-deploy.log
LOCK=/var/lock/altomatehr-v2-deploy.lock

# Output goes to the GitHub Actions log AND the server log.
exec > >(tee -a "$LOG") 2>&1

exec 9>"$LOCK"
if ! flock -w 1800 9; then
  echo "!! another deploy held the lock for 30 min, giving up"
  exit 1
fi

ts() { date -u +%FT%TZ; }
echo
echo "==== [$(ts)] deploy start ===="

EXPECTED_SHA=""
if [[ "${SSH_ORIGINAL_COMMAND:-}" =~ ^[0-9a-f]{40}$ ]]; then
  EXPECTED_SHA=$SSH_ORIGINAL_COMMAND
  echo "requested commit: $EXPECTED_SHA"
fi

cd "$APP_DIR"

# Refuse to deploy over hand edits to tracked files — they'd either block the
# merge or be silently shipped alongside main. Commit them first.
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  echo "!! $APP_DIR has uncommitted changes to tracked files; refusing to deploy:"
  git status --short --untracked-files=no
  exit 1
fi
if [ "$(git rev-parse --abbrev-ref HEAD)" != "$BRANCH" ]; then
  echo "!! $APP_DIR is on branch $(git rev-parse --abbrev-ref HEAD), not $BRANCH; refusing to deploy"
  exit 1
fi

OLD_HEAD=$(git rev-parse HEAD)
OLD_LOCK_SUM=$(sha256sum frontend/package-lock.json | awk '{print $1}')

git -c credential.helper= fetch --quiet origin "$BRANCH"
git merge --ff-only --quiet "origin/$BRANCH"
NEW_HEAD=$(git rev-parse HEAD)
NEW_LOCK_SUM=$(sha256sum frontend/package-lock.json | awk '{print $1}')
echo "moved $OLD_HEAD -> $NEW_HEAD"

if [ -n "$EXPECTED_SHA" ] && [ "$EXPECTED_SHA" != "$NEW_HEAD" ]; then
  if git merge-base --is-ancestor "$EXPECTED_SHA" "$NEW_HEAD" 2>/dev/null; then
    echo "note: main has moved past the requested commit; deploying the newer $NEW_HEAD"
  else
    echo "!! requested commit $EXPECTED_SHA is not on origin/$BRANCH; refusing to deploy"
    exit 1
  fi
fi

# --- API -----------------------------------------------------------------------
# `up --build` only recreates the container when the image actually changed.
# Building is memory-heavy, so it takes the droplet-wide build lock.
echo "[$(ts)] building API image"
with-build-lock docker compose -f "$COMPOSE_FILE" build api
docker compose -f "$COMPOSE_FILE" up -d api

echo "[$(ts)] waiting for API"
API_OK=""
for i in $(seq 1 60); do
  # Unauthenticated request to a protected route: 401 = app up, routing + auth working.
  CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://127.0.0.1:8080/employees || echo 000)
  if [ "$CODE" = "401" ]; then API_OK=1; echo "API healthy after ${i}x2s"; break; fi
  sleep 2
done
if [ -z "$API_OK" ]; then
  echo "!! API did not come up (last HTTP $CODE). Recent logs:"
  docker compose -f "$COMPOSE_FILE" logs --tail 40 api
  exit 1
fi

# --- Frontend ------------------------------------------------------------------
# Build into dist.next and swap, so nginx never serves a half-written dist/.
cd "$APP_DIR/frontend"
if [ "$OLD_LOCK_SUM" != "$NEW_LOCK_SUM" ] || [ ! -d node_modules ]; then
  echo "[$(ts)] lockfile changed, running npm ci"
  npm ci --no-audit --no-fund
fi
echo "[$(ts)] building frontend"
rm -rf dist.next
with-build-lock npm run build -- --outDir dist.next
test -f dist.next/index.html
rm -rf dist.prev
[ -d dist ] && mv dist dist.prev
mv dist.next dist
rm -rf dist.prev

CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 https://hr.altomate.io/ || echo 000)
if [ "$CODE" != "200" ]; then
  echo "!! frontend check failed: https://hr.altomate.io/ returned $CODE"
  exit 1
fi

docker image prune -f >/dev/null
echo "==== [$(ts)] deploy done: $NEW_HEAD ===="
