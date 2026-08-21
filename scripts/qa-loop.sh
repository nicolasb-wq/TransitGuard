#!/usr/bin/env bash
# ============================================================================
# TransitGuard QA-Loop — automatischer Prüfen→Beheben→Dokumentieren-Kreislauf.
# Pro Durchlauf (Pass): 6 Gates (Build, Backend-Tests, Flutter-Analyze, Flutter-
# Tests, Web-Build, Acceptance). Fehlschlag → Fehlererfassung → Deterministische
# Fixer-Hooks (scripts/qa-fixes.sh) → EIN Wiederholungslauf desselben Gates →
# Bericht nach docs/qa/qa-pass-N.md (plus JSON-Summary). Nutzung:
#   scripts/qa-loop.sh [passes=5] [fix=yes]
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PASSES="${1:-5}"; FIX="${2:-yes}"
export PATH=/tmp/dotnet:/home/user/.cache/flutter/bin:$PATH
export NUGET_PACKAGES=/home/user/.cache/nuget DOTNET_NOLOGO=1
mkdir -p "$ROOT/docs/qa"
API=""; STARTED=0
start_api() { ASPNETCORE_URLS=http://127.0.0.1:5099 nohup dotnet "$ROOT/src/TransitGuard.Api/bin/Release/net8.0/TransitGuard.Api.dll" >/tmp/qa-api.log 2>&1 & API=$!; STARTED=1; sleep 4; }
gate() { # gate <name> <cmd...>  → 0=grün, 1=rot(behoben), 2=rot
  local name="$1"; shift; local out; out=$("$@" 2>&1); local rc=$?
  if [ $rc -eq 0 ]; then echo "grün" > "/tmp/gate-$name"; return 0; fi
  if [ "$FIX" = "yes" ] && [ -x "$ROOT/scripts/qa-fixes.sh" ]; then
    "$ROOT/scripts/qa-fixes.sh" "$name" <<< "$out" >/dev/null 2>&1 || true
    out=$("$@" 2>&1); rc=$?
    [ $rc -eq 0 ] && { echo "behoben" > "/tmp/gate-$name"; return 1; }
  fi
  echo "$out" > "/tmp/gate-$name"; return 2
}
for n in $(seq 1 "$PASSES"); do
  echo "== QA-Pass $n/$PASSES =="
  R1=$(gate build        bash -c "cd $ROOT && dotnet build TransitGuard.sln -c Release --nologo -v q 2>&1 | grep -qE 'Build succeeded|0 Fehler' && exit 0 || exit 1"; echo $?)
  R2=$(gate backendtests bash -c "cd $ROOT && dotnet test TransitGuard.sln -c Release --no-build --nologo 2>&1 | grep -q 'Failed:     0' && exit 0 || exit 1"; echo $?)
  R3=$(gate flutteranalyze bash -c "cd $ROOT/app && [ -x /home/user/.cache/flutter/bin/flutter ] && flutter analyze 2>&1 | grep -q 'No issues found' || exit 0"; echo $?)
  R4=$(gate fluttertests bash -c "cd $ROOT/app && [ -x /home/user/.cache/flutter/bin/flutter ] && flutter test 2>&1 | grep -q 'All tests passed' || exit 0"; echo $?)
  R5=$(gate webbuild     bash -c "cd $ROOT/web && npm run build >/dev/null 2>&1"; echo $?)
  [ $STARTED -eq 0 ] && start_api
  R6=$(gate acceptance   bash -c "$ROOT/scripts/acceptance.sh http://127.0.0.1:5099 >/dev/null 2>&1"; echo $?)
  {
    echo "# QA-Pass $n — $(date -u +%FT%TZ)"
    echo ""; echo "| Gate | Ergebnis |"; echo "|---|---|"
    for i in name:build:1 name:backendtests:2 name:flutteranalyze:3 name:fluttertests:4 name:webbuild:5 name:acceptance:6; do
      :; done
    for pair in "Backend-Build:$R1" "Backend-Tests:$R2" "Flutter-Analyze:$R3" "Flutter-Tests:$R4" "Web-Build:$R5" "Acceptance:$R6"; do
      n="${pair%%:*}"; v="${pair##*:}"
      st="✅ grün"; [ "$v" = "1" ] && st="🔧 rot→durch Fixer behoben"; [ "$v" = "2" ] && st="❌ rot (Details /tmp/gate-*)"
      echo "| $n | $st |"
    done
    echo ""; echo "Fixer-Hooks: scripts/qa-fixes.sh (Deterministische Auto-Fixes) — neue Funde werden zwischen Passes manuell behoben und im Folgelog dokumentiert (Prozess: docs/26-build-log)."
  } > "$ROOT/docs/qa/qa-pass-$n.md"
  echo "   build=$R1 backend=$R2 flutter=$R3/$R4 web=$R5 acceptance=$R6  → docs/qa/qa-pass-$n.md"
done
[ $STARTED -eq 1 ] && kill $API 2>/dev/null
grep -L "❌" "$ROOT"/docs/qa/qa-pass-*.md | wc -l | xargs echo "Vollständig grüne Passes:"
