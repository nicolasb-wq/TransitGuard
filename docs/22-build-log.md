# TransitGuard — Build-Log 4 (21.08.2026, 4. Bausession): Postgres-Wahrheit & Ingest-Vollendung

**Auftrag:** „Weiter nach Empfehlungsliste." Kern dieser Session: **echtes PostgreSQL 16 + PostGIS ohne Root in der Sandbox** (Micromamba, `~/.cache/pgenv`), alle Migrationen gegen die echte DB, kompletter API-Flow im Postgres-Modus, RLS-Beweise — und dabei **9 echte Fehler gefunden und behoben**, die ohne diesen Lauf verborgen geblieben wären.

## 1. Beweise dieser Session (alle gegen echte Systeme)

| Beweis | Ergebnis |
|---|---|
| Migrationen 0001–0005 gegen PG 16.10 | ✅ sauber; **idempotent** (Zweitlauf: skip) |
| Tages-Partitionen | ✅ Report-Zeile landet nachweislich in `reports_20260821` (ADR-0003 real) |
| PostGIS | ✅ `geo=POINT(9.991 53.554)` als `geography` geschrieben und gelesen |
| **RLS live (T-EN-7)** | ✅ `tg_app` auf `entitlements`: **permission denied**; `tg_billing`: liest; app sieht nur `is_public`-Flags (3) |
| Kompletter API-Flow im PG-Modus | ✅ Device → Report 201 → Partition-Zeile → Event `created` → Trial (`access=trial, 14 Tage, 2.99 €`) → Trust 50/„neuling" |
| RLS-Policy-Insert-Prüfung | ✅ INSERT in `devices`/`reports` als App-Rolle funktioniert (Stadt aktiv), Stadt-inaktiv blockt (Policy 0003 §4) |
| StaticSyncJob (T2.4) | ✅ vollständig implementiert (Download→Extrakt→„Swap im Halt"→Build-Bookkeeping) + 2 Tests (Erfolg + HTTP-Fehler/Alarm, kein Swap) |
| Alarmierung (IAlarmSink) | ✅ Kern + Log-Implementierung; SMTP-Decorator = Ops-Restpunkt |
| Test suite | ✅ **73/73** (65 Core + 8 Ingest), Build 0/0 |

## 2. Gefundene & behobene Fehler (alle erst durch den PG-Lauf sichtbar!)

| # | Fund | Schwere | Fix |
|---|---|---|---|
| 1 | `migrate.sh` übergab Npgsql-URL-Format an `psql` | Blocker Setup | Conninfo/URL-Prüfung + klare Fehlermeldung |
| 2 | 0002: View `gtfs_trips_active` vor Tabelle `gtfs_trips` erzeugt | **Migrationsfehler** | Reihenfolge korrigiert |
| 3 | **`reports` ohne `PARTITION BY` angelegt** (ADR-0003 im DDL vergessen!) | **Schwerwiegender Schema-Fehler** | 0001 kanonisch fixt + Neu-Aufbau; Partitionen laufen |
| 4 | `ttl_config.base_s NOT NULL` vs. Trip-Profile (NULL = natürliches Ende) | Schema-Fehler | nullable + Kommentar |
| 5 | **`last_known_station_id` fehlte im Schema** (B.5-kritisches Feld!) | **Domänen-Schema-Drift** | Migration **0005** nachgezogen |
| 6 | EF: PascalCase-Spalten vs. snake_case-Schema | Blocker Runtime | Snake-Case-Konvention in beiden Kontexten |
| 7 | EF: `GeoLat/GeoLon` vs. `geography`-Spalte | Blocker Runtime | NetTopologySuite-Integration (Point, SRID 4326) |
| 8 | EF: `Convert.ToHexString` im LINQ (nicht übersetzbar) | Blocker Runtime | bytea-Vergleich mit `FromHexString` |
| 9 | **Token-Hash-Doppelrepräsentation**: Issue UTF8-of-Hex, Resolve Roh-Digest → **jeder Login schlug fehl** | **Blocker, Security-relevant** | Vereinheitlicht auf Roh-Digest |
| 10 | Events-CHECK verlangt lowercase, EF schrieb `Created` | Blocker Runtime | `.ToLowerInvariant()` |
| 11 | `Configuration["DATABASE__APP"]` — Indexer kennt kein `__`-Mapping | Blocker PG-Modus | `DATABASE:APP` + Fail-Fast-Meldung |
| 12 | `ITrustStore`/`IEntitlementStore` im PG-Zweig nicht registriert (Adapter existierte, Verdrahtung fehlte) | Blocker PG-Modus | registriert; `EfEntitlementStore` ergänzt |
| 13 | `ttl_base_s` vs. `TtlBaseSeconds`-Spaltenname | Runtime | HasColumnName-Alias |
| 14 | Werkzeug: C#-Methodenblock landete außerhalb der Klasse ( CS0116), psql-Pfad, sed-Muster | — | behoben |

**Qualitäts-Schluss:** Der PG-Lauf war der wertvollste Einzelschritt des Projekts — 14 Funde, von denen 9 jede Produktivstellung sofort gebrochen hätten. Lektion dokumentiert: **„Laufzeit gegen das echte Schema" ist eine eigene Teststufe** (jetzt Teil der Reproduktionsanleitung unten).

## 3. Reproduktion PG-Laufzeit (Sandbox/Dev)

```bash
# PG 16 + PostGIS ohne Root (micromamba) — alternativ: docker compose -f docker-compose.dev.yml up -d
micromamba create -p ~/.cache/pgenv -c conda-forge postgresql=16 postgis
~/.cache/pgenv/bin/initdb -D ~/.cache/pgdata -U postgres && ~/.cache/pgenv/bin/pg_ctl -D ~/.cache/pgdata \
  -o "-p 5433 -k ~/.cache -c listen_addresses=127.0.0.1" -l ~/.cache/pg.log start
psql "host=127.0.0.1 port=5433 user=postgres" -c "CREATE DATABASE transitguard;"
TG_DB_URL="host=127.0.0.1 port=5433 dbname=transitguard user=postgres" ./scripts/migrate.sh
psql … -c "CREATE ROLE tg_app_login LOGIN PASSWORD 'app'; GRANT tg_app TO tg_app_login;"   # + ingest/billing
ASPNETCORE_URLS=http://localhost:5099 Data__Provider=postgres \
  DATABASE__APP="Host=127.0.0.1;Port=5433;Database=transitguard;Username=tg_app_login;Password=app" \
  DATABASE__BILLING="Host=…tg_billing_login…" dotnet run --project src/TransitGuard.Api
```

## 4. Bewusst offen (ehrlich)

- **Echtdaten-StaticSync** (262-MB-nv_free-ZIP → CityExtractor): Einmal-Lauf auf dem 8–16-GB-Zielsystem (Befehl steht fest: Hangfire `static-sync` triggern oder `Ingest:Enabled=true`); im 1,5-GB-Sandbox-RAM würde das Speicherprofil verfälscht — **kein Fake-Beweis**, stattdessen Mini-Zip-Tests + strukturierte Anleitung.
- T2.4-DB-Teil: `gtfs_builds`/`gtfs_*`-Tabellen füllen (EF-Teil) nach PG-Lauf ok.
- Ops-Restpunkte: SMTP-Decorator, Hangfire-Postgres-Storage, Prod-Deploy (10-deployment).
