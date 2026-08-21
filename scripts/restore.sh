#!/usr/bin/env bash
# Restore-Drill (10-deployment §8: monatlich üben!): neuestes (oder genanntes) Backup in ZIEL-DB laden.
# Nutzung auf dem SERVER: restore.sh [ziel-db=transitguard_restore] [dump=tg-…dump.gpg|neueste]
set -euo pipefail
DB="${1:-transitguard_restore}"
SRC="${2:-}"
ROOT=/opt/transitguard
D="$ROOT/shared/backups"
DUMP="${SRC:-$(ls -1t "$D"/tg-*.dump.gpg | head -1)}"
echo "Restore $DUMP → DB $DB (Create/Drop)"
sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname='$DB'" | grep -q 1 && sudo -u postgres dropdb "$DB"
sudo -u postgres createdb "$DB"
gpg --decrypt "$DUMP" | sudo -u postgres pg_restore -d "$DB" --no-owner --clean --if-exists
echo "Sanity:"; sudo -u postgres psql -d "$DB" -tAc "SELECT 'Tabellen: '||count(*) FROM pg_tables WHERE schemaname='public'"
echo "ERINNERN: App NICHT gegen Restore-DB starten lassen, außer drill=on (Config DATABASE:APP zeigen)."