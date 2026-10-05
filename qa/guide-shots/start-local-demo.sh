#!/usr/bin/env bash
# Starts the LOCAL demo stack the user-guide screenshots are taken against:
#   backend  :5001  on the local MySQL database `altomatehr_local`, demo data
#                   seeded, email / Gemini / Xero switched off
#   frontend :5173  Vite (its .env already points at :5001)
#
# Never the shared database: user-secrets' ConnectionStrings:Default points at
# DigitalOcean, so the backend is started with an explicit localhost override,
# and an already-running backend is only reused when its own environment shows
# that same localhost override. Anything else on :5001 → refuse.
#
# Ports are fixed: CORS only allows :5173 and the frontend calls :5001.
# Stop with ./stop-local-demo.sh (it only stops what this script started).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$here/.out"
mkdir -p "$out"

LOCAL_DB="Server=localhost;Port=3306;Database=altomatehr_local;User=root;Password=;AllowPublicKeyRetrieval=True;SslMode=None"

listener() { lsof -nP -tiTCP:"$1" -sTCP:LISTEN 2>/dev/null | head -1; }

# The backend on :5001 is safe only if it was started against localhost.
backend_is_local() {
  local pid; pid="$(listener 5001)"
  [ -n "$pid" ] && ps eww -o command= -p "$pid" | tr ' ' '\n' \
    | grep -Eq '^ConnectionStrings__Default=Server=(localhost|127\.0\.0\.1);'
}

wait_for() { # port, label, log
  for _ in $(seq 1 120); do
    [ -n "$(listener "$1")" ] && return 0
    sleep 1
  done
  echo "✗ $2 didn't start on :$1 within 2 minutes — see $3" >&2
  exit 1
}

mysql -uroot -e 'SELECT 1' >/dev/null 2>&1 \
  || { echo "✗ Local MySQL isn't reachable as root@localhost. Start it (brew services start mysql) and retry." >&2; exit 1; }

# ── Backend ──────────────────────────────────────────────────────────────
if [ -n "$(listener 5001)" ]; then
  if backend_is_local; then
    echo "✓ backend :5001 already running on the local demo database — reusing it"
  else
    echo "✗ Something else is on :5001 and it is NOT the local demo backend (it may be on the shared database)." >&2
    echo "  Stop it first, then rerun this script. Nothing was started." >&2
    exit 1
  fi
else
  (
    cd "$root/backend"
    env ConnectionStrings__Default="$LOCAL_DB" \
        Seed__DemoData=true \
        ASPNETCORE_ENVIRONMENT=Development \
        EngineMailer__ApiKey= \
        Gemini__ApiKey= \
        Xero__ClientId= Xero__ClientSecret= \
        nohup dotnet run --launch-profile http >"$out/backend.log" 2>&1 &
    echo $! >"$out/backend.pid"
  )
  wait_for 5001 backend "$out/backend.log"
  backend_is_local || { echo "✗ The backend on :5001 isn't on the local database — stopping." >&2; "$here/stop-local-demo.sh"; exit 1; }
  listener 5001 >>"$out/backend.pid"
  echo "✓ backend :5001 started on the local demo database (log: .out/backend.log)"
fi

# ── Frontend ─────────────────────────────────────────────────────────────
if [ -n "$(listener 5173)" ]; then
  echo "✓ frontend :5173 already running — reusing it"
else
  (
    cd "$root/frontend"
    nohup npm run dev -- --port 5173 --strictPort >"$out/frontend.log" 2>&1 &
    echo $! >"$out/frontend.pid"
  )
  wait_for 5173 frontend "$out/frontend.log"
  listener 5173 >>"$out/frontend.pid"
  echo "✓ frontend :5173 started (log: .out/frontend.log)"
fi
