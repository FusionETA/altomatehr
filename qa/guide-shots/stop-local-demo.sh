#!/usr/bin/env bash
# Stops only what ./start-local-demo.sh started (the pids it recorded).
# A backend or frontend that was already running is left alone.
set -uo pipefail

out="$(cd "$(dirname "$0")" && pwd)/.out"

for name in backend frontend; do
  file="$out/$name.pid"
  [ -f "$file" ] || continue
  while read -r pid; do
    [ -n "$pid" ] || continue
    pkill -P "$pid" 2>/dev/null
    kill "$pid" 2>/dev/null
  done <"$file"
  rm -f "$file"
  echo "✓ stopped $name"
done
