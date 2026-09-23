#!/usr/bin/env bash
# Runs the whole functional smoke test in order. Each phase feeds the next
# through state.txt, so run them in this order (or run one at a time while
# iterating — the later ones need the earlier ones' data).
SP="$(cd "$(dirname "$0")" && pwd)"
for s in 01-setup.sh 02-people.sh 03-claims-leave.sh 04-attendance-overtime.sh 05-payroll.sh 06-rbac-isolation.sh; do
  echo; echo "████ $s"
  bash "$SP/$s" || echo "(phase exited non-zero)"
done
echo; echo "════════ TOTAL"
printf 'PASS %s   FAIL %s\n' "$(grep -c '^PASS' "$SP/.out/results.tsv")" "$(grep -c '^FAIL' "$SP/.out/results.tsv")"
echo; echo "Failures:"; grep '^FAIL' "$SP/.out/results.tsv" | cut -f2,3,4 | sed 's/\t/ | /g'
