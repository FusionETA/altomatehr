#!/usr/bin/env bash
# Shared helpers for the AltomateHR functional smoke test.
API="${API:-http://localhost:5001}"
OUT="${OUT:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/.out}"
RESULTS="$OUT/results.tsv"
BODYDIR="$OUT/bodies"; mkdir -p "$BODYDIR"

PASS=0; FAIL=0
TOKEN=""          # current bearer token
PHASE="-"

phase() { PHASE="$1"; echo; echo "══════ $1"; }

# req METHOD PATH [JSON]   -> sets HTTP and BODY
req() {
  local m="$1" p="$2" data="${3-}"
  local args=(-s -o "$BODYDIR/last.json" -w '%{http_code}' -X "$m" "$API$p")
  [ -n "$TOKEN" ] && args+=(-H "Authorization: Bearer $TOKEN")
  if [ -n "$data" ]; then args+=(-H 'Content-Type: application/json' -d "$data"); fi
  HTTP=$(curl "${args[@]}")
  BODY=$(cat "$BODYDIR/last.json")
}

# reqfile METHOD PATH FIELD FILEPATH   -> multipart upload
reqfile() {
  local m="$1" p="$2" field="$3" fp="$4"
  local args=(-s -o "$BODYDIR/last.json" -w '%{http_code}' -X "$m" "$API$p" -F "$field=@$fp")
  [ -n "$TOKEN" ] && args+=(-H "Authorization: Bearer $TOKEN")
  HTTP=$(curl "${args[@]}")
  BODY=$(cat "$BODYDIR/last.json")
}

# reqraw METHOD PATH  -> for file downloads; sets HTTP, CTYPE, SIZE
reqraw() {
  local m="$1" p="$2" dest="${3:-$BODYDIR/last.bin}"
  local args=(-s -o "$dest" -w '%{http_code}\t%{content_type}\t%{size_download}' -X "$m" "$API$p")
  [ -n "$TOKEN" ] && args+=(-H "Authorization: Bearer $TOKEN")
  IFS=$'\t' read -r HTTP CTYPE SIZE <<< "$(curl "${args[@]}")"
  BODY=""
}

# ok NAME EXPECTED  -> assert last HTTP status
ok() {
  local name="$1" want="$2"
  if [[ "$HTTP" == "$want" ]]; then
    PASS=$((PASS+1)); printf 'PASS\t%s\t%s\t%s\t%s\n' "$PHASE" "$name" "$HTTP" "" >> "$RESULTS"
    printf '  ✓ %-58s %s\n' "$name" "$HTTP"
  else
    FAIL=$((FAIL+1))
    local snip; snip=$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-260)
    printf 'FAIL\t%s\t%s\t%s\t%s\n' "$PHASE" "$name" "$HTTP (want $want)" "$snip" >> "$RESULTS"
    printf '  ✗ %-58s %s (want %s)\n     %s\n' "$name" "$HTTP" "$want" "$snip"
  fi
}

# okin NAME "200 204"  -> assert status is one of
okin() {
  local name="$1" want="$2"
  if [[ " $want " == *" $HTTP "* ]]; then
    PASS=$((PASS+1)); printf 'PASS\t%s\t%s\t%s\t%s\n' "$PHASE" "$name" "$HTTP" "" >> "$RESULTS"
    printf '  ✓ %-58s %s\n' "$name" "$HTTP"
  else
    FAIL=$((FAIL+1))
    local snip; snip=$(printf '%s' "$BODY" | tr -d '\n' | cut -c1-260)
    printf 'FAIL\t%s\t%s\t%s\t%s\n' "$PHASE" "$name" "$HTTP (want $want)" "$snip" >> "$RESULTS"
    printf '  ✗ %-58s %s (want one of %s)\n     %s\n' "$name" "$HTTP" "$want" "$snip"
  fi
}

# note NAME MESSAGE -> record an observation that is not a pass/fail gate
note() { printf 'NOTE\t%s\t%s\t\t%s\n' "$PHASE" "$1" "$2" >> "$RESULTS"; printf '  • %-58s %s\n' "$1" "$2"; }

jqr() { printf '%s' "$BODY" | jq -r "$1" 2>/dev/null; }

login() {  # login EMAIL PASSWORD -> sets TOKEN (backs off through the 5/min login rate limit)
  local e="$1" p="$2" attempt=0
  while :; do
    HTTP=$(curl -s -o "$BODYDIR/last.json" -w '%{http_code}' -X POST "$API/auth/login" \
      -H 'Content-Type: application/json' -d "{\"email\":\"$e\",\"password\":\"$p\"}")
    BODY=$(cat "$BODYDIR/last.json")
    [ "$HTTP" != "429" ] && break
    attempt=$((attempt+1)); [ "$attempt" -gt 4 ] && break
    printf '    (rate limited, waiting 20s before retrying login)\n' >&2; sleep 20
  done
  TOKEN=$(jqr '.token'); [ "$TOKEN" = "null" ] && TOKEN=""
}
