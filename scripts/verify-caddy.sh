#!/usr/bin/env bash
# ============================================================================
# verify-caddy.sh — beweist lokal, dass deploy/Caddyfile die API unter DEMSELBEN
# Origin ausliefert wie die PWA.
#
# Warum das ein eigenes Skript verdient: Caddy ordnet Direktiven nach einer
# festen Reihenfolge, in der `try_files` (Rewrite) VOR `reverse_proxy` laeuft.
# Ein blosser Pfad-Matcher greift deshalb zu spaet und jeder /v1/-Aufruf bekommt
# die HTML-Seite statt JSON — ein Fehler, den die App erst in Produktion zeigt
# und den weder acceptance.sh noch die Tests sehen. (Fund 21.08.2026.)
#
# Nutzung: scripts/verify-caddy.sh [api-url=http://127.0.0.1:5099] [port=8080]
#          CADDYFILE=<pfad> setzt eine andere Konfiguration (fuer Gegenproben).
# Exit: 0 = in Ordnung · 1 = Befund · 3 = uebersprungen (caddy/dist fehlt)
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="${1:-http://127.0.0.1:5099}"; PORT="${2:-8080}"
CADDY="${CADDY:-$(command -v caddy || echo /tmp/caddy)}"
DIST="$ROOT/web/dist"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"; [ -n "${CPID:-}" ] && kill "$CPID" 2>/dev/null' EXIT

[ -x "$CADDY" ] || { echo "⏭️  uebersprungen: caddy nicht gefunden (CADDY=<pfad> setzen)"; exit 3; }
[ -d "$DIST" ]  || { echo "⏭️  uebersprungen: web/dist fehlt (erst 'npm --prefix web run build')"; exit 3; }
curl -sf --max-time 5 "$API/health/ready" >/dev/null \
  || { echo "⏭️  uebersprungen: API unter $API nicht erreichbar"; exit 3; }

# Den echten app-Block uebernehmen, nur Host/Port/Pfade auf lokal biegen.
sed -e "s|^app.example.de {|:$PORT {|" -e "s|/srv/pwa|$DIST|" \
    -e "s|127.0.0.1:5080|${API#http://}|" "${CADDYFILE:-$ROOT/deploy/Caddyfile}" \
  | awk "/^:$PORT \{/,0" > "$TMP/Caddyfile"

"$CADDY" validate --config "$TMP/Caddyfile" --adapter caddyfile >/dev/null 2>&1 \
  || { echo "❌ Caddyfile ist nicht gueltig"; "$CADDY" validate --config "$TMP/Caddyfile" --adapter caddyfile; exit 1; }

"$CADDY" run --config "$TMP/Caddyfile" --adapter caddyfile >"$TMP/caddy.log" 2>&1 &
CPID=$!
curl -sf --retry 20 --retry-connrefused --retry-delay 1 --max-time 30 "http://127.0.0.1:$PORT/" >/dev/null \
  || { echo "❌ Caddy startete nicht"; tail -5 "$TMP/caddy.log"; exit 1; }

FAIL=0
chk() {  # chk <name> <url> <muster-das-passen-muss>
  # Ganze Antwort pruefen, nur die Fehlermeldung kuerzen — sonst faellt ein
  # Treffer hinter den ersten Zeilen faelschlich durch.
  local got; got=$(curl -s --max-time 5 "http://127.0.0.1:$PORT$2")
  if printf '%s' "$got" | grep -q "$3"; then echo "  ✔ $1"
  else echo "  ✘ $1 — erwartet '$3', bekam: $(printf '%s' "$got" | tr -d '\n' | head -c 70)"; FAIL=1; fi
}
chk "PWA-Wurzel liefert HTML"        "/"                     '<!doctype html>'
chk "Deep-Link faellt auf die App"   "/beliebig/tief"        '<!doctype html>'
chk "/v1 geht an die API (JSON)"     "/v1/cities"            '^{'
chk "/health geht an die API"        "/health/ready"         '"status":"ready"'
chk "Manifest ist ausgeliefert"      "/manifest.webmanifest" '"display"'
H=$(curl -sI --max-time 5 "http://127.0.0.1:$PORT/v1/cities")
echo "$H" | grep -qi 'cache-control: no-store' && echo "  ✔ /v1 mit Cache-Control: no-store" \
  || { echo "  ✘ /v1 ohne Cache-Control: no-store (Kill-Switch!)"; FAIL=1; }

[ $FAIL = 0 ] && echo "Caddy-Auslieferung in Ordnung." || echo "Caddy-Auslieferung FEHLERHAFT."
exit $FAIL
