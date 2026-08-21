#!/usr/bin/env bash
# ============================================================================
# pg-dev.sh — lokale PostgreSQL-16-Instanz mit PostGIS ohne Root und ohne
# Container hochziehen, migrieren und für die Integrationstests bereitstellen.
#
# Warum das ein Skript verdient: Bis 22.08.2026 standen RLS und Partitionierung
# auf Belegen einer früheren Session, weil das Hochziehen jedes Mal Handarbeit
# war. Ein Beweis, den man nicht wiederholen kann, ist ein schwacher Beweis.
#
# Nutzung:
#   scripts/pg-dev.sh up      Instanz starten (installiert bei Bedarf), migrieren
#   scripts/pg-dev.sh down    Instanz stoppen
#   scripts/pg-dev.sh status  Zustand + Verbindungszeichenfolge ausgeben
#   scripts/pg-dev.sh env     nur die TG_TEST_DB-Zeile ausgeben (für eval)
#
# Exit: 0 = in Ordnung · 1 = Fehler · 3 = Voraussetzungen fehlen (übersprungen)
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PGENV="${PGENV:-/home/user/.cache/pgenv}"
PGDATA="${PGDATA:-/home/user/.cache/pgdata}"
PGPORT="${PGPORT:-5433}"
PGUSER_OS="${PGUSER_OS:-pgrun}"          # Postgres verweigert den Start als root
DBNAME="${DBNAME:-transitguard}"
CONN="Host=127.0.0.1;Port=$PGPORT;Database=$DBNAME;Username=postgres"

hat() { command -v "$1" >/dev/null 2>&1; }
pg() { "$PGENV/bin/$1" "${@:2}"; }
laeuft() { "$PGENV/bin/pg_isready" -h 127.0.0.1 -p "$PGPORT" -q 2>/dev/null; }

installiere() {
  [ -x "$PGENV/bin/postgres" ] && return 0
  hat curl || { echo "⏭️  uebersprungen: curl fehlt"; return 3; }
  echo "  … PostgreSQL 16 + PostGIS werden geholt (einmalig, ~300 MB)"
  local mm=/tmp/micromamba-bin/micromamba
  if [ ! -x "$mm" ]; then
    mkdir -p /tmp/micromamba-bin
    curl -sL --retry 3 https://micro.mamba.pm/api/micromamba/linux-64/latest \
      | tar -xj -C /tmp/micromamba-bin --strip-components=1 bin/micromamba || return 3
  fi
  MAMBA_ROOT_PREFIX=/home/user/.cache/mamba "$mm" create -y -p "$PGENV" \
    -c conda-forge postgresql=16 postgis >/dev/null 2>&1 || return 1
}

hochfahren() {
  installiere || return $?
  if [ ! -f "$PGDATA/PG_VERSION" ]; then
    id "$PGUSER_OS" >/dev/null 2>&1 || useradd -m -s /bin/bash "$PGUSER_OS" || return 1
    mkdir -p "$PGDATA"; chown -R "$PGUSER_OS":"$PGUSER_OS" "$PGDATA"; chmod -R a+rX "$PGENV"
    runuser -u "$PGUSER_OS" -- env PATH="$PGENV/bin:/usr/bin:/bin" \
      "$PGENV/bin/initdb" -D "$PGDATA" -U postgres -A trust --encoding=UTF8 --locale=C >/dev/null 2>&1 || return 1
    printf "port = %s\nlisten_addresses = '127.0.0.1'\n" "$PGPORT" >> "$PGDATA/postgresql.conf"
  fi
  if ! laeuft; then
    # Das Logfile MUSS dem Postgres-Systemnutzer gehoeren: liegt es unter /tmp und
    # gehoert root, scheitert pg_ctl stumm und meldet trotzdem Erfolg.
    # pg_ctl -w wartet selbst auf Bereitschaft — eine eigene Warteschleife ohne
    # Taktung lief hier zu schnell durch und meldete faelschlich "startete nicht".
    runuser -u "$PGUSER_OS" -- env PATH="$PGENV/bin:/usr/bin:/bin" \
      "$PGENV/bin/pg_ctl" -D "$PGDATA" -l "$PGDATA/postmaster.log" -w -t 60 start >/dev/null 2>&1
  fi
  laeuft || { echo "  ✘ Instanz startete nicht:"; tail -8 "$PGDATA/postmaster.log" 2>/dev/null; return 1; }

  pg psql -h 127.0.0.1 -p "$PGPORT" -U postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname='$DBNAME'" | grep -q 1 \
    || pg psql -h 127.0.0.1 -p "$PGPORT" -U postgres -qc "CREATE DATABASE $DBNAME" >/dev/null

  PATH="$PGENV/bin:$PATH" TG_DB_URL="host=127.0.0.1 port=$PGPORT dbname=$DBNAME user=postgres" \
    "$ROOT/scripts/migrate.sh" >/tmp/pg-dev-migrate.log 2>&1 \
    || { echo "  ✘ Migrationen fehlgeschlagen:"; tail -12 /tmp/pg-dev-migrate.log; return 1; }

  # Login-Rollen für den Postgres-Modus der API (die tg_*-Rollen sind NOLOGIN).
  pg psql -h 127.0.0.1 -p "$PGPORT" -U postgres -d "$DBNAME" -qtAc "
    DO \$\$ BEGIN
      IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='tg_app_login') THEN
        CREATE ROLE tg_app_login LOGIN PASSWORD 'dev-only'; GRANT tg_app TO tg_app_login;
      END IF;
      IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='tg_billing_login') THEN
        CREATE ROLE tg_billing_login LOGIN PASSWORD 'dev-only'; GRANT tg_billing TO tg_billing_login;
      END IF;
    END \$\$;" >/dev/null
  echo "  ✔ PostgreSQL $(pg psql -h 127.0.0.1 -p "$PGPORT" -U postgres -tAc 'SHOW server_version') "\
"mit PostGIS $(pg psql -h 127.0.0.1 -p "$PGPORT" -U postgres -d "$DBNAME" -tAc 'SELECT postgis_lib_version()') auf Port $PGPORT"
}

case "${1:-up}" in
  up)     hochfahren; rc=$?; [ $rc = 0 ] && echo "  TG_TEST_DB=\"$CONN\"" ; exit $rc ;;
  down)   if laeuft; then
            # pg_ctl muss als der besitzende Systemnutzer laufen; als root scheitert
            # es stumm. Danach wird geprueft, ob der Port wirklich frei ist —
            # ein "gestoppt", das nicht stimmt, ist schlimmer als kein Stopp.
            runuser -u "$PGUSER_OS" -- env PATH="$PGENV/bin:/usr/bin:/bin" \
              "$PGENV/bin/pg_ctl" -D "$PGDATA" -m fast -w -t 60 stop >/dev/null 2>&1
            if laeuft; then echo "  ✘ läuft weiterhin auf Port $PGPORT"; exit 1; fi
            echo "  gestoppt"
          else echo "  lief nicht"; fi ;;
  status) if laeuft; then echo "  läuft — TG_TEST_DB=\"$CONN\""; else echo "  läuft nicht"; exit 1; fi ;;
  env)    laeuft && echo "$CONN" || exit 1 ;;
  *)      echo "Nutzung: $0 {up|down|status|env}"; exit 2 ;;
esac
