#!/usr/bin/env bash
# Wendet docs/sql/NNNN_*.sql in Reihenfolge an; zeichnet Fortschritt in schema_migrations.
# Nutzung: ./scripts/migrate.sh local   (DB-Verbindung via $TG_DB_URL oder docker-compose.dev)
set -euo pipefail
# TG_DB_URL als libpq-Conninfo ODER postgres://-URL (Npgsql-Format wird abgewiesen — Sweep-Fix 21.08.)
DB="${TG_DB_URL:-host=localhost port=5432 dbname=transitguard user=tg_admin password=dev-only}"
case "$DB" in Host=*|*";"*) echo "FEHLER: TG_DB_URL im Npgsql-Format — bitte libpq-Conninfo (host=... dbname=...) oder postgres://-URL verwenden." >&2; exit 2;; esac
DIR="$(cd "$(dirname "$0")/../docs/sql" && pwd)"
psql "$DB" -v ON_ERROR_STOP=1 -c "CREATE TABLE IF NOT EXISTS schema_migrations(filename TEXT PRIMARY KEY, applied_at TIMESTAMPTZ DEFAULT now());"
for f in "$DIR"/[0-9][0-9][0-9][0-9]_*.sql; do
  base=$(basename "$f")
  if psql "$DB" -tAc "SELECT 1 FROM schema_migrations WHERE filename='$base'" | grep -q 1; then
    echo "skip  $base"; continue
  fi
  echo "apply $base"
  psql "$DB" -v ON_ERROR_STOP=1 -f "$f"
  psql "$DB" -c "INSERT INTO schema_migrations(filename) VALUES ('$base');"
done
echo "migrations complete."
