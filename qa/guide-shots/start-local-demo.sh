#!/usr/bin/env bash
# Starts the LOCAL demo stack the user-guide screenshots are taken against:
#   backend  :5001  on the local MySQL database `altomatehr_local`, demo data
#                   seeded, email / Gemini / Xero switched off
#   frontend :5173  Vite, told explicitly to call http://localhost:5001
#
# Never the shared database: user-secrets' ConnectionStrings:Default points at
# DigitalOcean, so the backend is started with an explicit localhost override.
# A process already on :5001 or :5173 is reused only if its own environment
# shows it was started by this script (same database, keys blanked, same API
# target). Anything else → refuse, and start nothing.
#
# Ports are fixed: CORS only allows :5173 and the frontend calls :5001.
# Stop with ./stop-local-demo.sh (it only stops what this script started).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$here/.out"
mkdir -p "$out"

LOCAL_DB="Server=localhost;Port=3306;Database=altomatehr_local;User=root;Password=;AllowPublicKeyRetrieval=True;SslMode=None"
LOCAL_API="http://localhost:5001"

listener() { lsof -nP -tiTCP:"$1" -sTCP:LISTEN 2>/dev/null | head -1 || true; }

# One token per line: the process's arguments and environment. Captured into a
# variable and grepped through a here-string, never `… | grep -q`: under
# pipefail the writer's SIGPIPE turns a match into "no match" (and a negated
# check into a pass).
tokens_of() { ps eww -o command= -p "$1" 2>/dev/null | tr ' ' '\n' || true; }
has_token() { grep -Fxq -- "$2" <<<"$1"; }

# Safe only if started with exactly the local database and the outbound
# integrations blanked, and nothing on its command line overrides that.
backend_is_ours() {
  local pid t; pid="$(listener 5001)"; [ -n "$pid" ] || return 1
  t="$(tokens_of "$pid")"
  has_token "$t" "ConnectionStrings__Default=$LOCAL_DB" &&
  has_token "$t" "EngineMailer__ApiKey=" &&
  has_token "$t" "Gemini__ApiKey=" &&
  has_token "$t" "Xero__ClientSecret=" &&
  ! grep -Eiq -- '^(-+ConnectionStrings[:_]|ConnectionStrings:)' <<<"$t"
}

# Safe only if told explicitly to call the local backend.
frontend_is_ours() {
  local pid t; pid="$(listener 5173)"; [ -n "$pid" ] || return 1
  t="$(tokens_of "$pid")"
  has_token "$t" "VITE_API_URL=$LOCAL_API" &&
  has_token "$t" "VITE_DEV_API_TARGET=$LOCAL_API"
}

# "pid|start time", so stop-local-demo.sh never kills a recycled pid.
record() { echo "$1|$(ps -o lstart= -p "$1" 2>/dev/null)" >>"$2"; }

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

# Check both ports before starting anything.
if [ -n "$(listener 5001)" ] && ! backend_is_ours; then
  echo "✗ Something is on :5001 that wasn't started by this script (it may be on the shared database)." >&2
  echo "  Stop it yourself, then rerun. Nothing was started." >&2
  exit 1
fi
if [ -n "$(listener 5173)" ] && ! frontend_is_ours; then
  echo "✗ Something is on :5173 that wasn't started by this script (it may call another API)." >&2
  echo "  Stop it yourself, then rerun. Nothing was started." >&2
  exit 1
fi

# ── Backend ──────────────────────────────────────────────────────────────
if [ -n "$(listener 5001)" ]; then
  echo "✓ backend :5001 already running on the local demo database — reusing it"
else
  rm -f "$out/backend.pid"
  (
    cd "$root/backend"
    env ConnectionStrings__Default="$LOCAL_DB" \
        Seed__DemoData=true \
        ASPNETCORE_ENVIRONMENT=Development \
        EngineMailer__ApiKey= \
        Gemini__ApiKey= \
        Xero__ClientId= Xero__ClientSecret= \
        nohup dotnet run --launch-profile http >"$out/backend.log" 2>&1 &
    record $! "$out/backend.pid"
  )
  wait_for 5001 backend "$out/backend.log"
  record "$(listener 5001)" "$out/backend.pid"
  if ! backend_is_ours; then
    echo "✗ The backend on :5001 doesn't show the local-database settings — stopping it." >&2
    "$here/stop-local-demo.sh"
    exit 1
  fi
  echo "✓ backend :5001 started on the local demo database (log: .out/backend.log)"
fi

# ── Frontend ─────────────────────────────────────────────────────────────
if [ -n "$(listener 5173)" ]; then
  echo "✓ frontend :5173 already running against the local backend — reusing it"
else
  rm -f "$out/frontend.pid"
  (
    cd "$root/frontend"
    env VITE_API_URL="$LOCAL_API" VITE_DEV_API_TARGET="$LOCAL_API" \
        nohup npm run dev -- --port 5173 --strictPort >"$out/frontend.log" 2>&1 &
    record $! "$out/frontend.pid"
  )
  wait_for 5173 frontend "$out/frontend.log"
  record "$(listener 5173)" "$out/frontend.pid"
  echo "✓ frontend :5173 started (log: .out/frontend.log)"
fi
