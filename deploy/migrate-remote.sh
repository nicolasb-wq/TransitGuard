#!/usr/bin/env bash
# Serverseitiger Migrations-Runner (von deploy.sh via ssh aufgerufen; QA-Pass-1-Fund B: referenziert, aber nie angeliefert)
# Liest die SQL-Dateien aus dem AKTUELLEN Release und wendet sie transaktional+idempotent an.
set -euo pipefail
ROOT=/opt/transitguard
REL=$(readlink -f "$ROOT/current")
DB="${TG_DB_URL:-host=localhost port=5432 dbname=transitguard user=postgres}"
psql "$DB" -v ON_ERROR_STOP=1 -c "CREATE TABLE IF NOT EXISTS schema_migrations(filename TEXT PRIMARY KEY, applied_at TIMESTAMPTZ DEFAULT now());"
for f in "$REL"/docs/sql/[0-9][0-9][0-9][0-9]_*.sql; do
  base=$(basename "$f")
  if psql "$DB" -tAc "SELECT 1 FROM schema_migrations WHERE filename='$base'" | grep -q 1; then echo "skip  $base"; continue; fi
  echo "apply $base"; psql "$DB" -v ON_ERROR_STOP=1 -f "$f"
  psql "$DB" -c "INSERT INTO schema_migrations(filename) VALUES ('$base');"
done
