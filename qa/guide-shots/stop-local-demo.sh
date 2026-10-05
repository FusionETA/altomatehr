#!/usr/bin/env bash
# Stops only what ./start-local-demo.sh started. Each pid was recorded with its
# start time; a pid whose start time no longer matches has been reused by some
# other process (after a reboot, say) and is left alone.
set -uo pipefail

out="$(cd "$(dirname "$0")" && pwd)/.out"

for name in backend frontend; do
  file="$out/$name.pid"
  [ -f "$file" ] || continue
  while IFS='|' read -r pid started; do
    [ -n "$pid" ] || continue
    now="$(ps -o lstart= -p "$pid" 2>/dev/null)"
    if [ -z "$now" ]; then continue; fi                     # already gone
    if [ "$now" != "$started" ]; then
      echo "· pid $pid is now a different process — left alone"
      continue
    fi
    pkill -P "$pid" 2>/dev/null
    kill "$pid" 2>/dev/null
  done <"$file"
  rm -f "$file"
  echo "✓ stopped $name"
done
