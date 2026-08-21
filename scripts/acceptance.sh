#!/usr/bin/env bash
# Abnahme-API-Check (Runbook Phase 6): prüft die Vertragskette in 60 Sekunden.
# Nutzung: scripts/acceptance.sh https://api.example.de
set -euo pipefail
B="${1:?Basis-URL fehlt}"; PASS=0; FAIL=0
chk() { local name="$1" want="$2" got="$3"; if echo "$got" | grep -q "$want"; then echo "  ✔ $name"; PASS=$((PASS+1)); else echo "  ✘ $name — erwarte '$want', bekam: $(echo "$got" | head -c 120)"; FAIL=$((FAIL+1)); fi; }

T=$(curl -s -X POST "$B/v1/devices" | python3 -c "import json,sys;print(json.load(sys.stdin)['device_token'])")
chk health   '"status":"ready"' "$(curl -s "$B/health/ready")"
chk trial    '"access":"trial"' "$(curl -s "$B/v1/devices/me" -H "X-Device-Token: $T")"
chk gate-422 'ticket_confirmation_required' "$(curl -s -X POST "$B/v1/reports" -H "Content-Type: application/json" -H "X-Device-Token: $T" -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" -d '{"city_slug":"hamburg","anchor_type":"station","report_type":"on_platform","vehicle_kind":"rail","station":{"stop_id":"X"},"client":{"ticket_confirmed":false}}')"
chk gate-403 'ticket_gate_blocked' "$(curl -s "$B/v1/cities/hamburg/reports" -H "X-Device-Token: $T")"
chk nearby   'stop_id' "$(curl -s "$B/v1/cities/hamburg/stops/nearby?lat=53.554&lon=9.991&take=2" -H "X-Device-Token: $T")"
echo "—— $PASS ✔ / $FAIL ✘"; [ "$FAIL" = 0 ]
