#!/usr/bin/env bash
# TransitGuard Deploy (ADR-0012): lokaler Build → rsync → Migrationen → Symlink-Swap → Health → Auto-Rollback
# Nutzung:  ./scripts/deploy.sh <user@host> [release-name]
# Voraussetzungen Lokal: dotnet 8 SDK, rsync, ssh-Key. Server: siehe deploy/RUNBOOK-SERVER.md Phase 1-3.
set -euo pipefail

TARGET="${1:?Nutzung: deploy.sh user@host [release-name]}"
REL="${2:-$(date -u +%Y%m%d-%H%M%S)}"
ROOT=/opt/transitguard
APP=src/TransitGuard.Api
echo "== 1/6 Test-Gate (lokale Suite muss grün sein) =="
dotnet test TransitGuard.sln -c Release --nologo | grep -E "Passed!|Failed!" || { echo "Tests nicht auswertbar — Abbruch"; exit 1; }
dotnet test TransitGuard.sln -c Release --nologo 2>&1 | grep -q "Failed:     0" || { echo "FEHLER: Tests rot — kein Deploy."; exit 1; }

echo "== 2/6 Publish (self-contained linux-x64, Server braucht keine Runtime) =="
dotnet publish "$APP" -c Release -r linux-x64 --self-contained -o "build/api" --nologo -v q
mkdir -p build/api/docs/sql && cp docs/sql/*.sql build/api/docs/sql/

echo "== 3/6 rsync → $TARGET:$ROOT/releases/$REL =="
ssh "$TARGET" "mkdir -p $ROOT/releases/$REL $ROOT/shared"
rsync -az --chmod=D755,F644 build/api/ "$TARGET:$ROOT/releases/$REL/"

echo "== 4/6 Migrationen (Backup-Pflicht davor, ON_ERROR_STOP) =="
ssh "$TARGET" "cd $ROOT/releases/$REL/docs/sql && sudo -u postgres psql -d transitguard -v ON_ERROR_STOP=1 -f /dev/null 2>/dev/null; \
  bash $ROOT/shared/migrate-remote.sh" || { echo "Migrationen fehlgeschlagen — KEIN Swap, alter Stand bleibt."; exit 1; }

echo "== 5/6 Symlink-Swap + Restart + Health =="
PREV=$(ssh "$TARGET" "readlink $ROOT/current || true")
ssh "$TARGET" "ln -sfn $ROOT/releases/$REL $ROOT/releases/next && mv -Tf $ROOT/releases/next $ROOT/current && systemctl restart transitguard"
sleep 6
if ! ssh "$TARGET" "curl -fsS http://127.0.0.1:5080/health/ready" | tee /dev/stderr | grep -q '"status":"ready"'; then
  echo "!! Health fehlgeschlagen — AUTO-ROLLBACK auf ${PREV:-keinen vorherigen Stand}"
  [ -n "${PREV:-}" ] && ssh "$TARGET" "ln -sfn $PREV $ROOT/releases/next && mv -Tf $ROOT/releases/next $ROOT/current && systemctl restart transitguard"
  exit 1
fi
echo "== 6/6 Deploy OK: $REL (vorher: ${PREV:-initial}) =="