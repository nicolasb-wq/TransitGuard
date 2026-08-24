#!/usr/bin/env bash
# ============================================================================
# TransitGuard QA-Loop — automatischer Prüfen→Beheben→Dokumentieren-Kreislauf.
#
# Pro Durchlauf (Pass) laufen elf Gates. Ein rotes Gate löst die
# deterministischen Fixer-Hooks (scripts/qa-fixes.sh) und GENAU EINEN
# Wiederholungslauf desselben Gates aus. Bericht je Pass nach
# docs/qa/qa-pass-N.md, Maschinen-Summary nach docs/qa/qa-pass-N.json.
#
# Nutzung:  scripts/qa-loop.sh [passes=5] [fix=yes]
# Exit:     0 = alle Gates aller Passes gruen/uebersprungen, 1 = mind. ein Rot.
#
# Gate-Zustaende (bewusst VIER, nicht zwei — eine fehlende Toolchain ist kein
# Beweis fuer Korrektheit und darf niemals als "gruen" verbucht werden):
#   0 gruen · 1 rot→durch Fixer behoben · 2 rot · 3 uebersprungen
#
# Selbstprüfung des Loops (26- und 27-build-log): In dieser Datei steckten
# schon neun Defekte, davon fünf mit FALSCHEM GRÜN. Wer sie ändert, prüfe
# zuerst, ob das geänderte Gate am kaputten Zustand noch rot werden kann.
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PASSES="${1:-5}"; FIX="${2:-yes}"
FLUTTER_BIN="${FLUTTER_BIN:-/home/user/.cache/flutter/bin/flutter}"
CADDY_BIN="${CADDY_BIN:-$(command -v caddy || echo /tmp/caddy)}"
export PATH="/tmp/dotnet:$(dirname "$FLUTTER_BIN"):$PATH"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/home/user/.cache/nuget}" DOTNET_NOLOGO=1
API_URL="${API_URL:-http://127.0.0.1:5099}"
PWA_PORT="${PWA_PORT:-8080}"          # Caddy: PWA + API unter einem Origin
FLT_PORT="${FLT_PORT:-8090}"          # Caddy: Flutter-Web + API unter einem Origin
OUTDIR="$ROOT/docs/qa"; LOGDIR="$OUTDIR/logs"
mkdir -p "$OUTDIR" "$LOGDIR"

# --- Einzelinstanz-Sperre ---------------------------------------------------
# Zwei gleichzeitig laufende Loops streiten um Ports, API und docs/qa/ und
# erzeugen dabei still falsche Ergebnisse: am 22.08.2026 meldete ein Pass fuenf
# rote Gates, weil ein verwaister Zweitlauf die API weggeraeumt hatte. Ein
# Ergebnis, das von einem unbemerkten Nachbarn abhaengt, ist kein Ergebnis.
#
# ZWEITER Anlauf dieser Sperre (24.08.2026). Die erste Fassung war
#   exec 9>"$SPERRE"; flock -n 9
# und hat sich selbst dauerhaft ausgesperrt: `dotnet build` startet MSBuild-Daemons
# mit /nodeReuse:true, die den Dateideskriptor 9 ERBEN und den Loop ueberleben.
# Solange einer dieser Daemons lebt, haelt er die Sperre — und JEDER weitere QA-Lauf
# bricht mit "laeuft bereits" ab, obwohl kein Loop laeuft. Gemessen: nach dem Ende des
# Loops hielten drei MSBuild-Prozesse die Sperre.
#
# Deshalb entscheidet jetzt die LEBENDIGKEIT, nicht der Dateideskriptor: flock bleibt
# als schneller, atomarer Weg, aber ein Fehlschlag wird gegen die Prozessliste geprueft.
# Laeuft nachweislich kein zweiter qa-loop.sh, ist die Sperre verwaist und wird ersetzt.
SPERRE="${TMPDIR:-/tmp}/transitguard-qa-loop.lock"

# Und die URSACHE gleich mit abstellen: ohne Knoten-Wiederverwendung ueberleben keine
# MSBuild-Daemons den Build, also erbt auch keiner den Sperr-Deskriptor. Kostet ein paar
# Sekunden je Build; ein Loop, der sich selbst aussperrt, kostet mehr.
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

laeuft_zweiter_loop() {
  local eigen=$$ p
  for p in $(pgrep -f 'bash .*qa-loop\.sh' 2>/dev/null); do
    [ "$p" = "$eigen" ] && continue
    [ "$p" = "$PPID" ] && continue
    kill -0 "$p" 2>/dev/null && return 0
  done
  return 1
}

sperre_holen() {
  exec 9>"$SPERRE"
  flock -n 9 && return 0
  if laeuft_zweiter_loop; then return 1; fi
  # Verwaiste Sperre: der Halter ist kein QA-Loop (fast immer ein MSBuild-Daemon mit
  # geerbtem Deskriptor). Neue Datei = neuer Inode; der Altlasthalter behaelt seine.
  echo "Hinweis: verwaiste Sperre gefunden (Halter ist kein QA-Loop) — wird ersetzt." >&2
  exec 9>&-
  rm -f "$SPERRE"
  exec 9>"$SPERRE"
  flock -n 9
}

if ! sperre_holen; then
  echo "Es laeuft bereits ein QA-Loop (Sperre: $SPERRE). Abbruch." >&2
  exit 2
fi
echo $$ >&9

API_PID=""; CADDY_PID=""; CADDY_FLT_PID=""
cleanup() {
  for p in "$API_PID" "$CADDY_PID" "$CADDY_FLT_PID"; do [ -n "$p" ] && kill "$p" 2>/dev/null; done
  return 0
}
trap cleanup EXIT INT TERM

belegt() { (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

start_api() {
  [ -n "$API_PID" ] && return 0
  local port="${API_URL##*:}"
  # Ein fremder Prozess auf dem Port ist KEIN Erfolg: der Loop wuerde sonst
  # gegen eine andere (womoeglich veraltete) Instanz pruefen und Gruen melden.
  if belegt "$port"; then
    echo "   (Port $port ist bereits belegt — QA-Loop startet keine eigene API und bricht ab)" \
      > "$LOGDIR/api.log"
    return 1
  fi
  local dll="$ROOT/src/TransitGuard.Api/bin/Release/net8.0/TransitGuard.Api.dll"
  [ -f "$dll" ] || { echo "   (API-DLL fehlt)" > "$LOGDIR/api.log"; return 1; }
  ASPNETCORE_URLS="$API_URL" nohup dotnet "$dll" >"$LOGDIR/api.log" 2>&1 &
  API_PID=$!
  curl -sf --retry 30 --retry-connrefused --retry-delay 1 --max-time 60 \
    "$API_URL/health/ready" >/dev/null 2>&1 && return 0
  kill -0 "$API_PID" 2>/dev/null || API_PID=""
  return 1
}

# start_caddy <port> <wurzel> <pid-variable>
start_caddy() {
  local port="$1" wurzel="$2" var="$3"
  [ -x "$CADDY_BIN" ] || return 3
  [ -d "$wurzel" ] || return 3
  [ -n "$API_PID" ] || return 3
  eval "[ -n \"\${$var}\" ]" && return 0
  belegt "$port" && return 3      # fremder Halter: nicht uebernehmen
  local cfg="$LOGDIR/Caddyfile.$port"
  sed -e "s|^app.example.de {|:$port {|" -e "s|/srv/pwa|$wurzel|" \
      -e "s|127.0.0.1:5080|${API_URL#http://}|" "$ROOT/deploy/Caddyfile" \
    | awk "/^:$port \{/,0" > "$cfg"
  nohup "$CADDY_BIN" run --config "$cfg" --adapter caddyfile >"$LOGDIR/caddy-$port.log" 2>&1 &
  eval "$var=\$!"
  curl -sf --retry 20 --retry-connrefused --retry-delay 1 --max-time 30 \
    "http://127.0.0.1:$port/health/ready" >/dev/null 2>&1 || return 3
  return 0
}

# gate <name> <fn> → 0 gruen | 1 rot-behoben | 2 rot | 3 uebersprungen
#
# Exit 3 eines Werkzeugs heisst im ganzen Repo „uebersprungen" (fehlende Toolchain,
# kein Dienst, kein Netz). Bis zum 24.08.2026 machte diese Funktion daraus ein ROT:
# jeder nicht-null Exit landete im selben Zweig. Damit haette ein Lauf ohne laufende
# API den Vertrags-Gate als Fehlschlag gemeldet — und ein rotes Gate, das nur
# „konnte nicht pruefen" bedeutet, ist genauso irrefuehrend wie ein falsches Gruen.
gate() {
  local name="$1"; shift
  local out rc
  out=$("$@" 2>&1); rc=$?
  if [ $rc -eq 0 ]; then printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 0; fi
  if [ $rc -eq 3 ]; then printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 3; fi
  if [ "$FIX" = "yes" ] && [ -x "$ROOT/scripts/qa-fixes.sh" ]; then
    printf '%s\n' "$out" | "$ROOT/scripts/qa-fixes.sh" "$name" >/dev/null 2>&1 || true
    out=$("$@" 2>&1); rc=$?
    if [ $rc -eq 0 ]; then printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 1; fi
  fi
  printf '%s\n' "$out" > "$LOGDIR/$name.log"; return 2
}

# run_gate <name> <fn> [vorbedingung]
run_gate() {
  local name="$1" fn="$2" pre="${3:-}"
  if [ -n "$pre" ] && ! eval "$pre" >/dev/null 2>&1; then
    echo "SKIP: Vorbedingung nicht erfuellt ($pre)" > "$LOGDIR/$name.log"; return 3
  fi
  gate "$name" "$fn"
}

# --- Gates: immer der ECHTE Exit-Code des Werkzeugs -------------------------
g_build() {
  cd "$ROOT" || return 2
  local out; out=$(dotnet build TransitGuard.sln -c Release --nologo 2>&1); local rc=$?
  printf '%s\n' "$out"
  [ $rc -ne 0 ] && return $rc
  if printf '%s' "$out" | grep -qE '^ +[1-9][0-9]* (Warning\(s\)|Warnung\(en\))'; then
    echo "QA: Build hat Warnungen — Projektregel verlangt 0 Warnungen."; return 1
  fi
  return 0
}

# Backend-Tests OHNE die Postgres-Tests: die bekommen ein eigenes Gate, damit
# ein "uebersprungen" mangels Datenbank nicht als Gruen durchgeht.
g_backendtests() {
  cd "$ROOT" || return 2
  dotnet test TransitGuard.sln -c Release --no-build --nologo \
    --filter "FullyQualifiedName!~Postgres" 2>&1
}

g_pgtests() {
  cd "$ROOT" || return 2
  local cs; cs=$("$ROOT/scripts/pg-dev.sh" env 2>/dev/null)
  local out; out=$(TG_TEST_DB="$cs" dotnet test tests/TransitGuard.Api.Tests -c Release \
    --no-build --nologo --filter "FullyQualifiedName~Postgres" 2>&1); local rc=$?
  printf '%s\n' "$out"
  [ $rc -ne 0 ] && return $rc
  # Ein Lauf, der nur uebersprungen hat, ist KEIN Beweis.
  if printf '%s' "$out" | grep -qE 'Passed: *0,'; then
    echo "QA: kein einziger Postgres-Test wirklich gelaufen."; return 2
  fi
  return 0
}

g_flutteranalyze() { cd "$ROOT/app" || return 2; [ -d .dart_tool ] || "$FLUTTER_BIN" pub get 2>&1; "$FLUTTER_BIN" analyze 2>&1; }
g_fluttertests()  { cd "$ROOT/app" || return 2; [ -d .dart_tool ] || "$FLUTTER_BIN" pub get 2>&1; "$FLUTTER_BIN" test 2>&1; }

g_webbuild() {
  cd "$ROOT/web" || return 2
  [ -d node_modules ] || npm ci 2>&1
  npm run build 2>&1
}

g_acceptance()  { "$ROOT/scripts/acceptance.sh" "$API_URL" 2>&1; }
g_contract()    { "$ROOT/scripts/verify-contract.sh" "$API_URL" 2>&1; }
g_caddy()       { CADDY="$CADDY_BIN" "$ROOT/scripts/verify-caddy.sh" "$API_URL" "$((PWA_PORT + 100))" 2>&1; }
g_pwasmoke()    { BASE="http://127.0.0.1:$PWA_PORT" OUT="$LOGDIR/shots" bash -c 'mkdir -p "$OUT"; node '"$ROOT"'/verifikation/pwa_smoke.mjs' 2>&1; }
g_swoffline()   { BASE="http://127.0.0.1:$PWA_PORT" node "$ROOT/verifikation/sw_offline.mjs" 2>&1; }

# Echtdaten-Ingest von Ende zu Ende: Hangfire + Postgres + echter Feed.
# Braucht Netz zum echten Feed UND eine Postgres-Instanz. Fehlt eines davon,
# liefert das Skript selbst Exit 3 — uebersprungen, nicht gruen und nicht rot.
g_ingest() { TG_E2E_PORT=$((PWA_PORT + 200)) "$ROOT/scripts/verify-ingest-e2e.sh" 2>&1; }

# Fehlerpfade des Poll-Jobs gegen einen lokalen Testserver: unerreichbar,
# HTTP 500, abgeschnitten, leerer Koerper, kaputtes Protobuf, 304, Erholung.
# Braucht KEIN Netz zum echten Feed (der eine Abruf darin ist optional).
g_ingestfehler() {
  cd "$ROOT" || return 2
  dotnet build verifikation/IngestProbe/IngestProbe.csproj -c Release --nologo -v q 2>&1 || return 2
  TG_PROBE_CACHE="${TG_PROBE_CACHE:-${TMPDIR:-/tmp}/tg-probe-qa}" \
    dotnet verifikation/IngestProbe/bin/Release/net8.0/IngestProbe.dll errors 2>&1
}

g_flutterweb() {
  cd "$ROOT/app" || return 2
  # IMMER neu bauen: ein alter build/web-Ordner wuerde einen laengst
  # geaenderten Stand pruefen und faelschlich Gruen melden.
  "$FLUTTER_BIN" build web --release --dart-define=API_BASE= --no-web-resources-cdn 2>&1 | tail -3
  BASE="http://127.0.0.1:$FLT_PORT" OUT="$LOGDIR/shots-flutter" \
    bash -c 'mkdir -p "$OUT"; node '"$ROOT"'/verifikation/flutter_web_smoke.mjs' 2>&1
}

label() {
  case "$1" in
    0) echo "✅ grün" ;;
    1) echo "🔧 rot → durch Fixer behoben" ;;
    3) echo "⏭️ übersprungen" ;;
    *) echo "❌ rot" ;;
  esac
}

HAT_PLAYWRIGHT='node -e "require.resolve(require(\"path\").join(require(\"child_process\").execSync(\"npm root -g\",{encoding:\"utf8\"}).trim(),\"playwright\",\"index.js\"))"'

OVERALL=0
for pass in $(seq 1 "$PASSES"); do
  echo "== QA-Pass $pass/$PASSES =="
  R_BUILD=$(run_gate build g_build; echo $?)
  R_BETESTS=$(run_gate backendtests g_backendtests; echo $?)
  R_PG=$(run_gate pgtests g_pgtests "$ROOT/scripts/pg-dev.sh env"; echo $?)
  R_FANALYZE=$(run_gate flutteranalyze g_flutteranalyze '[ -x "$FLUTTER_BIN" ]'; echo $?)
  R_FTESTS=$(run_gate fluttertests g_fluttertests '[ -x "$FLUTTER_BIN" ]'; echo $?)
  R_WEB=$(run_gate webbuild g_webbuild; echo $?)
  R_IFEHLER=$(run_gate ingestfehler g_ingestfehler; echo $?)
  R_INGEST=$(run_gate ingest g_ingest; echo $?)

  if start_api; then
    R_ACC=$(run_gate acceptance g_acceptance; echo $?)
    R_CONTRACT=$(run_gate contract g_contract; echo $?)
    R_CADDY=$(run_gate caddy g_caddy '[ -x "$CADDY_BIN" ] && [ -d "$ROOT/web/dist" ]'; echo $?)
  else
    R_ACC=2; R_CONTRACT=2; R_CADDY=2
    echo "API konnte nicht gestartet werden — siehe docs/qa/logs/api.log" > "$LOGDIR/acceptance.log"
  fi

  if start_caddy "$PWA_PORT" "$ROOT/web/dist" CADDY_PID; then
    R_PWA=$(run_gate pwasmoke g_pwasmoke "$HAT_PLAYWRIGHT"; echo $?)
    R_SW=$(run_gate swoffline g_swoffline "$HAT_PLAYWRIGHT"; echo $?)
  else R_PWA=3; R_SW=3
    echo "SKIP: kein Caddy-Origin auf $PWA_PORT" > "$LOGDIR/pwasmoke.log"
    cp "$LOGDIR/pwasmoke.log" "$LOGDIR/swoffline.log" 2>/dev/null
  fi

  # Auf einer frischen Maschine gibt es app/build/web noch nicht — dann koennte
  # Caddy nichts ausliefern und das Gate wuerde uebersprungen, obwohl Flutter da
  # ist. Deshalb einmal vorbauen, BEVOR der Origin hochgezogen wird.
  if [ -x "$FLUTTER_BIN" ] && [ ! -f "$ROOT/app/build/web/index.html" ]; then
    (cd "$ROOT/app" && "$FLUTTER_BIN" build web --release --dart-define=API_BASE= \
       --no-web-resources-cdn) >"$LOGDIR/flutterweb-vorbau.log" 2>&1
  fi
  if [ -x "$FLUTTER_BIN" ] && start_caddy "$FLT_PORT" "$ROOT/app/build/web" CADDY_FLT_PID; then
    R_FWEB=$(run_gate flutterweb g_flutterweb "$HAT_PLAYWRIGHT"; echo $?)
  else R_FWEB=3; echo "SKIP: kein Flutter-Web-Origin auf $FLT_PORT" > "$LOGDIR/flutterweb.log"; fi

  STAMP=$(date -u +%FT%TZ)
  {
    echo "# QA-Pass $pass — $STAMP"; echo ""
    echo "| Gate | Ergebnis | prüft |"; echo "|---|---|---|"
    echo "| Backend-Build | $(label "$R_BUILD") | 0 Fehler, 0 Warnungen |"
    echo "| Backend-Tests | $(label "$R_BETESTS") | Core, Ingest, API-Kette |"
    echo "| Postgres-Tests | $(label "$R_PG") | Migrationen, RLS, Partitionen gegen echtes PG |"
    echo "| Flutter-Analyze | $(label "$R_FANALYZE") | statische Analyse |"
    echo "| Flutter-Tests | $(label "$R_FTESTS") | DTO-Verträge, Gate, Copy-Nie-Liste |"
    echo "| Web-Build | $(label "$R_WEB") | tsc gegen den generierten Vertrag |"
    echo "| Acceptance | $(label "$R_ACC") | Vertragskette über HTTP |"
    echo "| Vertrag | $(label "$R_CONTRACT") | Server-Drift + Frische der Generate |"
    echo "| Caddy-Auslieferung | $(label "$R_CADDY") | Same-Origin, no-store, Deep-Links |"
    echo "| PWA-Rauchtest | $(label "$R_PWA") | echter Browser, hell+dunkel, Umstiege |"
    echo "| Service Worker | $(label "$R_SW") | keine Meldungsdaten im Cache, offline |"
    echo "| Flutter-Laufzeit | $(label "$R_FWEB") | echte App im Browser gegen echte API |"
    echo "| Ingest-Fehlerpfade | $(label "$R_IFEHLER") | 7 Fehlerfaelle, Job faengt sich, Metrikzeile je Runde |"
    echo "| Echtdaten-Ingest | $(label "$R_INGEST") | Hangfire+Postgres+echter Feed, Persistenz, API stabil |"
    echo ""
    echo "Vollständige Gate-Ausgaben: \`docs/qa/logs/<gate>.log\` (wird je Pass überschrieben)."
    echo "„Übersprungen\" heißt: Werkzeug oder Dienst fehlt — das ist KEIN Beweis für Korrektheit."
  } > "$OUTDIR/qa-pass-$pass.md"

  printf '{"pass":%d,"utc":"%s","gates":{"build":%d,"backendtests":%d,"pgtests":%d,"flutteranalyze":%d,"fluttertests":%d,"webbuild":%d,"acceptance":%d,"contract":%d,"caddy":%d,"pwasmoke":%d,"swoffline":%d,"flutterweb":%d,"ingestfehler":%d,"ingest":%d}}\n' \
    "$pass" "$STAMP" "$R_BUILD" "$R_BETESTS" "$R_PG" "$R_FANALYZE" "$R_FTESTS" "$R_WEB" \
    "$R_ACC" "$R_CONTRACT" "$R_CADDY" "$R_PWA" "$R_SW" "$R_FWEB" "$R_IFEHLER" "$R_INGEST" > "$OUTDIR/qa-pass-$pass.json"

  echo "   build=$R_BUILD backend=$R_BETESTS pg=$R_PG flutter=$R_FANALYZE/$R_FTESTS web=$R_WEB" \
       "acc=$R_ACC vertrag=$R_CONTRACT caddy=$R_CADDY pwa=$R_PWA sw=$R_SW flutterweb=$R_FWEB" \
       "ingestfehler=$R_IFEHLER ingest=$R_INGEST"
  for v in "$R_BUILD" "$R_BETESTS" "$R_PG" "$R_FANALYZE" "$R_FTESTS" "$R_WEB" \
           "$R_ACC" "$R_CONTRACT" "$R_CADDY" "$R_PWA" "$R_SW" "$R_FWEB" "$R_IFEHLER" "$R_INGEST"; do
    [ "$v" = "2" ] && OVERALL=1
  done
done

GREEN=$(grep -L "❌" "$OUTDIR"/qa-pass-*.md | wc -l)
echo "Passes ohne rotes Gate: $GREEN/$PASSES"
exit $OVERALL
