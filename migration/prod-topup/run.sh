#!/usr/bin/env bash
# Runs SQL steps against the DB named in /opt/altomatehr-v2/.env.
# Usage: bash run.sh 01-new-records.sql 02-drift.sql ...
set -euo pipefail
cd "$(dirname "$0")"
CS=$(grep -i '^ConnectionStrings__Default=' /opt/altomatehr-v2/.env | cut -d= -f2-)
f() { echo "$CS" | tr ';' '\n' | grep -i "^$1=" | cut -d= -f2; }
CNF=$(mktemp); trap 'rm -f "$CNF"' EXIT
printf '[client]\nhost=%s\nport=%s\nuser=%s\npassword=%s\nssl\nssl-verify-server-cert=0\n' \
  "$(f Server)" "$(f Port)" "$(f User)" "$(f Password)" > "$CNF"
for s in "$@"; do
  echo "== $s"
  if [[ "$s" == *.sh ]]; then CNF="$CNF" bash "$s"; else mysql --defaults-file="$CNF" --table < "$s"; fi
done
