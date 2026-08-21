#!/usr/bin/env bash
# ============================================================================
# TransitGuard QA-Loop — automatischer Prüfen→Beheben→Dokumentieren-Kreislauf.
#
# Pro Durchlauf (Pass) laufen 6 Gates: Backend-Build, Backend-Tests,
# Flutter-Analyze, Flutter-Tests, Web-Build, Acceptance. Ein rotes Gate löst
# die deterministischen Fixer-Hooks (scripts/qa-fixes.sh) und GENAU EINEN
# Wiederholungslauf desselben Gates aus. Bericht je Pass nach
# docs/qa/qa-pass-N.md, Maschinen-Summary nach docs/qa/qa-pass-N.json.
#
# Nutzung:  scripts/qa-loop.sh [passes=5] [fix=yes]
# Exit:     0 = alle Gates aller Passes gruen/uebersprungen, 1 = mind. ein Rot.
#
# Gate-Zustaende (bewusst VIER, nicht zwei — eine fehlende Toolchain ist kein
# Beweis fuer Korrektheit und darf niemals als "gruen" verbucht werden):
#   0 gruen · 1 rot→durch Fixer behoben · 2 rot · 3 uebersprungen (Toolchain fehlt)
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PASSES="${1:-5}"; FIX="${2:-yes}"
FLUTTER_BIN="${FLUTTER_BIN:-/home/user/.cache/flutter/bin/flutter}"
export PATH="/tmp/dotnet:$(dirname "$FLUTTER_BIN"):$PATH"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/home/user/.cache/nuget}" DOTNET_NOLOGO=1
API_URL="${API_URL:-http://127.0.0.1:5099}"
OUTDIR="$ROOT/docs/qa"; LOGDIR="$OUTDIR/logs"
mkdir -p "$OUTDIR" "$LOGDIR"

API_PID=""
cleanup() { [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null; return 0; }
trap cleanup EXIT INT TERM

start_api() {
  [ -n "$API_PID" ] && return 0
  local dll="$ROOT/src/TransitGuard.Api/bin/Release/net8.0/TransitGuard.Api.dll"
  [ -f "$dll" ] || { echo "   (API-DLL fehlt — Acceptance kann nicht laufen)"; return 1; }
  ASPNETCORE_URLS="$API_URL" nohup dotnet "$dll" >"$LOGDIR/api.log" 2>&1 &
  API_PID=$!
  # Aktiv auf Bereitschaft warten statt blind zu schlafen: curl uebernimmt das
  # Takten (--retry-connrefused), damit der Loop auch in Umgebungen laeuft,
  # die freistehende sleep-Prozesse unterbinden.
  if curl -sf --retry 30 --retry-connrefused --retry-delay 1 --max-time 60 \
       "$API_URL/health/ready" >/dev/null 2>&1; then
    return 0
  fi
  kill -0 "$API_PID" 2>/dev/null || { echo "   (API-Prozess vorzeitig beendet)"; API_PID=""; }
  return 1
}

# gate <name> <cmd...> → 0 gruen | 1 rot-behoben | 2 rot
# Der VOLLSTAENDIGE Gate-Output wird immer nach docs/qa/logs/<name>.log
# geschrieben — auch bei Gruen, damit Belege nachvollziehbar bleiben.
gate() {
  local name="$1"; shift
  local out rc
  out=$("$@" 2>&1); rc=$?
  if [ $rc -eq 0 ]; then printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 0; fi
  if [ "$FIX" = "yes" ] && [ -x "$ROOT/scripts/qa-fixes.sh" ]; then
    printf '%s\n' "$out" | "$ROOT/scripts/qa-fixes.sh" "$name" >/dev/null 2>&1 || true
    out=$("$@" 2>&1); rc=$?
    if [ $rc -eq 0 ]; then printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 1; fi
  fi
  printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 2
}

# --- Gate-Implementierungen: immer der ECHTE Exit-Code des Werkzeugs. --------
g_build() {
  cd "$ROOT" || return 2
  local out; out=$(dotnet build TransitGuard.sln -c Release --nologo 2>&1); local rc=$?
  printf '%s\n' "$out"
  [ $rc -ne 0 ] && return $rc
  # Projektregel: 0 Fehler UND 0 Warnungen (CLAUDE.md, 25-build-log).
  if printf '%s' "$out" | grep -qE '^ +[1-9][0-9]* (Warning\(s\)|Warnung\(en\))'; then
    echo "QA: Build hat Warnungen — Projektregel verlangt 0 Warnungen."; return 1
  fi
  return 0
}
g_backendtests() {
  cd "$ROOT" || return 2
  # Exit-Code von dotnet test ist massgeblich. Ein grep auf 'Failed:     0'
  # waere falsch: bei zwei Testprojekten wuerde die Zeile des GRUENEN Projekts
  # ein rotes Projekt maskieren.
  dotnet test TransitGuard.sln -c Release --no-build --nologo 2>&1
}
g_flutteranalyze() {
  cd "$ROOT/app" || return 2
  [ -d .dart_tool ] || "$FLUTTER_BIN" pub get 2>&1
  "$FLUTTER_BIN" analyze 2>&1
}
g_fluttertests() {
  cd "$ROOT/app" || return 2
  [ -d .dart_tool ] || "$FLUTTER_BIN" pub get 2>&1
  "$FLUTTER_BIN" test 2>&1
}
g_webbuild() {
  cd "$ROOT/web" || return 2
  [ -d node_modules ] || npm ci 2>&1
  npm run build 2>&1
}
g_acceptance() {
  "$ROOT/scripts/acceptance.sh" "$API_URL" 2>&1
}

# run_gate <name> <fn> [precondition-cmd]
# Ist die Vorbedingung gesetzt und nicht erfuellt, wird das Gate uebersprungen
# (rc 3) — OHNE es auszufuehren und ohne die Fixer-Logik zu bemuehen. Eine
# fehlende Toolchain ist kein Beweis fuer Korrektheit.
run_gate() {
  local name="$1" fn="$2" pre="${3:-}"
  if [ -n "$pre" ] && ! eval "$pre"; then
    echo "SKIP: Vorbedingung nicht erfuellt ($pre)" > "$LOGDIR/$name.log"; return 3
  fi
  gate "$name" "$fn"
}

label() {
  case "$1" in
    0) echo "✅ grün" ;;
    1) echo "🔧 rot → durch Fixer behoben" ;;
    3) echo "⏭️ übersprungen (Toolchain fehlt)" ;;
    *) echo "❌ rot" ;;
  esac
}

OVERALL=0
for pass in $(seq 1 "$PASSES"); do
  echo "== QA-Pass $pass/$PASSES =="
  R_BUILD=$(run_gate build g_build; echo $?)
  R_BETESTS=$(run_gate backendtests g_backendtests; echo $?)
  R_FANALYZE=$(run_gate flutteranalyze g_flutteranalyze '[ -x "$FLUTTER_BIN" ]'; echo $?)
  R_FTESTS=$(run_gate fluttertests g_fluttertests '[ -x "$FLUTTER_BIN" ]'; echo $?)
  R_WEB=$(run_gate webbuild g_webbuild; echo $?)
  if start_api; then R_ACC=$(run_gate acceptance g_acceptance; echo $?)
  else R_ACC=2; echo "API konnte nicht gestartet werden — siehe docs/qa/logs/api.log" > "$LOGDIR/acceptance.log"; fi

  STAMP=$(date -u +%FT%TZ)
  {
    echo "# QA-Pass $pass — $STAMP"
    echo ""
    echo "| Gate | Ergebnis |"
    echo "|---|---|"
    echo "| Backend-Build | $(label "$R_BUILD") |"
    echo "| Backend-Tests | $(label "$R_BETESTS") |"
    echo "| Flutter-Analyze | $(label "$R_FANALYZE") |"
    echo "| Flutter-Tests | $(label "$R_FTESTS") |"
    echo "| Web-Build | $(label "$R_WEB") |"
    echo "| Acceptance | $(label "$R_ACC") |"
    echo ""
    echo "Vollständige Gate-Ausgaben: \`docs/qa/logs/<gate>.log\` (wird je Pass überschrieben)."
    echo "Fixer-Hooks: \`scripts/qa-fixes.sh\` — ausschließlich deterministische Korrekturen;"
    echo "alles andere wird rot gemeldet und zwischen den Passes von Hand behoben."
  } > "$OUTDIR/qa-pass-$pass.md"

  printf '{"pass":%d,"utc":"%s","gates":{"build":%d,"backendtests":%d,"flutteranalyze":%d,"fluttertests":%d,"webbuild":%d,"acceptance":%d}}\n' \
    "$pass" "$STAMP" "$R_BUILD" "$R_BETESTS" "$R_FANALYZE" "$R_FTESTS" "$R_WEB" "$R_ACC" \
    > "$OUTDIR/qa-pass-$pass.json"

  echo "   build=$R_BUILD backend=$R_BETESTS flutter=$R_FANALYZE/$R_FTESTS web=$R_WEB acceptance=$R_ACC  → docs/qa/qa-pass-$pass.md"
  for v in "$R_BUILD" "$R_BETESTS" "$R_FANALYZE" "$R_FTESTS" "$R_WEB" "$R_ACC"; do
    [ "$v" = "2" ] && OVERALL=1
  done
done

GREEN=$(grep -L "❌" "$OUTDIR"/qa-pass-*.md | wc -l)
echo "Passes ohne rotes Gate: $GREEN/$PASSES"
exit $OVERALL
