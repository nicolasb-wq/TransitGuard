#!/usr/bin/env bash
# ============================================================================
# flutter-e2e.sh — Laufzeittest der ECHTEN Flutter-App im echten Browser.
#
# Startet chromedriver, faehrt `flutter drive` gegen eine LAUFENDE API und
# bedient die Oberflaeche (integration_test/app_test.dart).
#
# EHRLICHE GRENZE, die auch im Bericht steht: Das ist das WEB-Ziel von Flutter.
# Es belegt, dass App-Code, Plugins und HTTP-Kette zur Laufzeit zusammenspielen.
# Es belegt NICHT das Verhalten des Android-Release-Artefakts unter R8 — dafuer
# braeuchte es ein Geraet oder einen Emulator, und im Container gibt es weder
# /dev/kvm noch CPU-Virtualisierungsflags (am 22.08.2026 geprueft).
#
# Nutzung: scripts/flutter-e2e.sh [api-url=http://127.0.0.1:5099]
# Exit: 0 = bestanden · 1 = Fehlschlag · 3 = uebersprungen (Werkzeug fehlt)
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="${1:-${API:-http://127.0.0.1:5099}}"
FLUTTER="${FLUTTER:-/home/user/.cache/flutter/bin/flutter}"

[ -x "$FLUTTER" ] || { echo "⏭️  uebersprungen: Flutter fehlt ($FLUTTER)"; exit 3; }

curl -sf --max-time 5 "$API/health/ready" >/dev/null 2>&1 \
  || { echo "⏭️  uebersprungen: keine API unter $API"; exit 3; }

CHROME="${CHROME:-$(command -v google-chrome || ls -d /opt/pw-browsers/chromium-*/chrome-linux/chrome 2>/dev/null | head -1)}"
[ -x "${CHROME:-}" ] || { echo "⏭️  uebersprungen: kein Chromium gefunden (CHROME=<pfad> setzen)"; exit 3; }
export CHROME_EXECUTABLE="$CHROME"

# -d chrome startet den Browser selbst; ein separater chromedriver (und dessen
# Versionsabgleich) entfaellt damit.


cd "$ROOT/app" || exit 1

# Fester Web-Port: nur so laesst sich der Origin vorher kennen und der API
# als enge CORS-Allowlist mitgeben. Produktion braucht das NICHT — dort liegen
# PWA und API unter einem Origin (deploy/Caddyfile). Der Schalter ist im
# Auslieferungszustand aus, festgehalten in T-CORS.
WEBPORT="${E2E_WEB_PORT:-4460}"
echo "  Hinweis: die API muss mit Cors__AllowedOrigins=http://localhost:$WEBPORT laufen,"
echo "           sonst blockt der Browser die Aufrufe der App (anderer Origin)."

# --no-web-resources-cdn ist Voraussetzung: sonst holt Flutter-Web CanvasKit von
# www.gstatic.com, der Container-Proxy blockt das mit ERR_CONNECTION_RESET, die
# App bootet gar nicht — und `flutter drive` wartet endlos auf eine Verbindung,
# die eine nie gestartete App nie aufbaut. Am 22.08.2026 als Ursache gemessen.
#
# --no-proxy-server ist hier entscheidend: der Container setzt HTTPS_PROXY, und
# Chrome leitet sonst auch den Rueckkanal zum Dart-Debug-Dienst auf localhost
# ueber den Proxy — `flutter drive` wartet dann ewig auf eine Verbindung, die
# nie ankommt (am 22.08.2026 dreimal so gemessen).
#
# Chrome laeuft unter einem virtuellen Display statt headless: im Headless-Modus
# meldete sich der Debug-Dienst nicht zurueck und `flutter drive` haengte
# unbegrenzt (am 22.08.2026 zweimal gemessen, je >13 min ohne Testausgabe).
# --no-sandbox ist im Container Pflicht (kein User-Namespace fuer Chrome).
# Wortweise aufteilen wuerde die Server-Argumente zerreissen — deshalb als Array.
XVFB=()
command -v xvfb-run >/dev/null && XVFB=(xvfb-run -a --server-args="-screen 0 1280x900x24")
"${XVFB[@]}" "$FLUTTER" drive \
  --driver=test_driver/integration_test.dart \
  --target=integration_test/app_test.dart \
  -d chrome --web-port="$WEBPORT" \
  --web-browser-flag=--no-sandbox \
  --web-browser-flag=--disable-dev-shm-usage \
  --web-browser-flag=--disable-gpu \
  --web-browser-flag=--no-proxy-server \
  --no-web-resources-cdn \
  --dart-define=API_BASE="$API" \
  2>&1 | tee /tmp/flutter-e2e.log | grep -vE "Woah|superuser|^ *$|📎|^ */$"
RC=${PIPESTATUS[0]}

if grep -q "All tests passed" /tmp/flutter-e2e.log; then
  echo "Flutter-Laufzeittest bestanden."; exit 0
fi
echo "Flutter-Laufzeittest FEHLGESCHLAGEN (rc=$RC)."; exit 1
