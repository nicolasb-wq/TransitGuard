#!/usr/bin/env bash
# ============================================================================
# verify-contract.sh — Vertragsdrift zwischen Server und Clients sichtbar machen.
#
# Drei der vier Launch-Blocker vom 21.08.2026 waren derselbe Fehlertyp: Server
# und Client waren sich über einen Feldvertrag uneinig, und nur ein manueller
# Rauchtest hat es gefunden. Dieses Gate schliesst die FEHLERKLASSE:
#
#   1. Der Vertrag wird aus der LAUFENDEN API neu aufgezeichnet und mit dem
#      eingecheckten Stand verglichen  →  Server-Drift wird rot.
#   2. Die generierten Client-Dateien werden neu erzeugt und verglichen
#      →  vergessenes Regenerieren wird rot.
#      (Die PWA leitet ihre Typen aus contract.gen.ts ab; eine Umbenennung im
#       Server wird dort zusaetzlich zum Compile-Fehler im Web-Build.)
#   3. Unbeobachtete Endpunkte werden ausgewiesen — dort schuetzt nichts.
#
# Beispieldateien (docs/contract/samples/) werden bewusst NICHT byteweise
# verglichen: ihr Inhalt haengt von der Uhrzeit ab (Zahl der Abfahrten im
# Zeitfenster). Ihre FORM steckt im Vertrag, und der wird verglichen.
#
# Nutzung: scripts/verify-contract.sh [api-url=http://127.0.0.1:5099]
# Exit: 0 = in Ordnung · 1 = Drift · 3 = uebersprungen (keine API erreichbar)
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="${1:-${API:-http://127.0.0.1:5099}}"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT

command -v node >/dev/null || { echo "⏭️  uebersprungen: node fehlt"; exit 3; }
curl -sf --max-time 5 "$API/health/ready" >/dev/null 2>&1 \
  || { echo "⏭️  uebersprungen: keine API unter $API (erst starten: dotnet run --project src/TransitGuard.Api)"; exit 3; }

FAIL=0
cd "$ROOT" || exit 1

# --- 1. Server-Drift ---------------------------------------------------------
mkdir -p "$TMP/contract"
if ! API="$API" node verifikation/contract_capture.mjs --out "$TMP/contract" >"$TMP/capture.log" 2>&1; then
  echo "  ✘ Aufzeichnung fehlgeschlagen:"; sed 's/^/      /' "$TMP/capture.log" | head -12; exit 1
fi
if diff -q docs/contract/api-contract.json "$TMP/contract/api-contract.json" >/dev/null; then
  echo "  ✔ Server-Vertrag unveraendert"
else
  echo "  ✘ SERVER-DRIFT: die laufende API weicht vom eingecheckten Vertrag ab"
  diff -u docs/contract/api-contract.json "$TMP/contract/api-contract.json" | head -40 | sed 's/^/      /'
  echo "      → Absicht? Dann: node verifikation/contract_capture.mjs && die Generatoren erneut laufen lassen."
  FAIL=1
fi

# --- 2. Frische der generierten Client-Dateien -------------------------------
pruefe_generat() { # pruefe_generat <name> <generator> <datei>
  node "$2" --out "$TMP/$(basename "$3")" >/dev/null 2>&1 || { echo "  ✘ $1: Generator fehlgeschlagen"; FAIL=1; return; }
  if diff -q "$3" "$TMP/$(basename "$3")" >/dev/null; then echo "  ✔ $1 ist aktuell"
  else echo "  ✘ $1 ist VERALTET ($3) — neu erzeugen: node $2"; FAIL=1; fi
}
pruefe_generat "TypeScript-Typen" verifikation/contract_gen_ts.mjs   web/src/contract.gen.ts
pruefe_generat "Dart-Vertragsdaten" verifikation/contract_gen_dart.mjs app/lib/contract.gen.dart

# --- 3. Lueckenbericht -------------------------------------------------------
UNBEOB=$(node -e "
  const v = require('./docs/contract/api-contract.json');
  const u = Object.entries(v.endpunkte).filter(([,e]) => e.unbeobachtet).map(([k]) => k);
  console.log(u.join(' '));
")
if [ -n "$UNBEOB" ]; then
  echo "  ℹ Unbeobachtete Endpunkte (dort schuetzt der Vertrag nichts): $UNBEOB"
fi

[ $FAIL = 0 ] && echo "Vertrag in Ordnung." || echo "VERTRAGSDRIFT festgestellt."
exit $FAIL
