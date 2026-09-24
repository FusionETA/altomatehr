#!/usr/bin/env bash
# Runs the admin migration against the DB named in /opt/altomatehr-v2/.env.
# Usage: bash run.sh [00-adminmap.sql 01-admins.sql 02-verify.sql]   (default: all three)
set -euo pipefail
cd "$(dirname "$0")"
CS=$(grep -i '^ConnectionStrings__Default=' /opt/altomatehr-v2/.env | cut -d= -f2-)
f() { echo "$CS" | tr ';' '\n' | grep -i "^$1=" | cut -d= -f2; }
CNF=$(mktemp); trap 'rm -f "$CNF"' EXIT
printf '[client]\nhost=%s\nport=%s\nuser=%s\npassword=%s\nssl\nssl-verify-server-cert=0\n' \
  "$(f Server)" "$(f Port)" "$(f User)" "$(f Password)" > "$CNF"
STEPS=("$@"); [ ${#STEPS[@]} -eq 0 ] && STEPS=(00-adminmap.sql 01-admins.sql 02-verify.sql)
for s in "${STEPS[@]}"; do
  echo "== $s"
  mysql --defaults-file="$CNF" --table < "$s"
done
