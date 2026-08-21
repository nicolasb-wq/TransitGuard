# TransitGuard — Server-Runbook & Erst-Deploy-Checkliste (konkret, ausführbar)

**Zweck:** Zielsystem (Hetzner, Debian 12) von Null auf „Echtdaten-Ingest läuft" bringen. Jede Phase ist abhakbar und mit echten Befehlen + erwarteten Ausgaben versehen. Server-Mindestgröße: **CPX31 (4 vCPU/8 GB)** — Static-Sync hält DE-weites stop_times im Stream + HH-Schedules im RAM (~50–150 MB; Messung in Phase 5 verifizieren).

## Phase 0 — Voraussetzungen
- [ ] Hetzner-Projekt, Debian 12, CPX31, SSH-Key hinterlegt · Domain mit DNS-A/AAAA auf Server-IP (api.example.de, app.example.de)
- [ ] Lokal: dotnet 8 SDK, rsync; Repo-Stand: Tests grün (`dotnet test`)

## Phase 1 — Basis-Anhärtung (~15 min)
```bash
adduser --disabled-password deploy && mkdir /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
# Lokalen PublicKey eintragen:  echo "<ssh-ed25519 …>" > /home/deploy/.ssh/authorized_keys ; chown -R deploy:deploy /home/deploy/.ssh
sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/;s/^#\?PermitRootLogin.*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config && systemctl restart ssh
apt update && apt -y full-upgrade && apt -y install unattended-upgrades caddy postgresql-common curl
# PGDG + PostgreSQL 16 + PostGIS:
echo "deb http://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" > /etc/apt/sources.list.d/pgdg.list
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /etc/apt/trusted.gpg.d/pgdg.gpg
apt update && apt -y install postgresql-16 postgresql-16-postgis-3 postgresql-client-16 fail2ban
```
- [ ] `pg_lsclusters` → 16/main online · `systemctl is-active caddy fail2ban` → active

## Phase 2 — Datenbank + Rollen + Schema (~10 min)
```bash
sudo -u postgres psql -c "CREATE DATABASE transitguard;"
# STARKE Passwörter erzeugen (openssl rand -base64 24) und in /opt/transitguard/shared/.env MERKEN:
sudo -u postgres psql <<'SQL'
CREATE ROLE tg_app_login     LOGIN PASSWORD '<APP_PW>';
CREATE ROLE tg_ingest_login  LOGIN PASSWORD '<INGEST_PW>';
CREATE ROLE tg_billing_login LOGIN PASSWORD '<BILLING_PW>';
GRANT tg_app TO tg_app_login; GRANT tg_ingest TO tg_ingest_login; GRANT tg_billing TO tg_billing_login;
SQL
sudo -u postgres -i pg_is_in_recovery -q  # irrelevant einzeln; direkt:
sudo -u postgres psql -d transitguard -f - < /dev/null  # Platzhalter — Migrationskanon folgt:
# Migrationen (aus dem Repo — bei Erst-Deploy vorab per scp): 
bash scripts/migrate.sh   # lokal mit TG_DB_URL="host=<server-ip> port=5432 dbname=transitguard user=postgres" — oder auf dem Server ausführen
```
**Akzeptanz:** `psql -d transitguard -tAc "SELECT count(*) FROM pg_tables WHERE tablename LIKE 'reports_2026%'"` → 15+ · `SELECT count(*) FROM pg_policies` → 13 · RLS-Schnelltest:
`psql "dbname=transitguard user=tg_app_login password=<APP_PW>" -c "SELECT count(*) FROM entitlements"` → **permission denied** ✅

## Phase 3 — App-Deploy (~10 min)
```bash
# Server: mkdir -p /opt/transitguard/{releases,shared} && chown -R deploy:deploy /opt/transitguard
# /opt/transitguard/shared/.env (chmod 600, Vorlage deploy/env.example):
#   DATABASE:APP / DATABASE:BILLING (Npgsql-Strings mit tg_app_login/tg_billing_login!)
#   Data__Provider=postgres · Ingest__Enabled=false (erst Phase 5!) · OPS__ALERT_EMAIL=…
systemctl edit --force --full transitguard   # Unit aus deploy/transitguard.service einfügen
systemctl daemon-reload && systemctl enable transitguard
# LOKAL: ./scripts/deploy.sh deploy@<server-ip>   (baut, testet, migriert, swapt, health-prüft, rollt bei Fehler zurück)
```
- [ ] `systemctl status transitguard` → active · `journalctl -u transitguard -n 20` zeigt „Stadt-Extrakt geladen" nur im Mini-Fix-Modus — Prod lädt Phase 5.
- [ ] Caddy (`/etc/caddy/Caddyfile` aus deploy/Caddyfile, Domains anpassen) → `systemctl reload caddy` → `curl -s https://api.<domain>/health/live` = `{"status":"live"}`

## Phase 4 — Ops-Grundausstattung (~15 min)
- [ ] Cron Backups: `crontab -e` → `0 3 * * * /opt/transitguard/current/../scripts/backup.sh` (besser: Skript nach /opt/transitguard/shared/ kopieren) — Restore-Drill einmalig ausführen (`restore.sh`) und Dauer notieren.
- [ ] Uptime-Wächte (Zweit-VM o.ä.): `GET /health/ready` alle 60 s, Alarm bei 2×.
- [ ] Hangfire-Dashboard via Caddy-Basic-Auth freischalten (10-deployment §7) — prüfen: recurring **ttl-sweep**, **partition-maint**, **static-sync** sichtbar (rt-poll erst nach Phase 5).

## Phase 5 — ERST-IMPORT ECHTDATEN (Herzstück, ~30–60 min)
**Reihenfolge ist Pflicht: erst Static, dann RT — sonst matcht kein TU (C.3.1).**
1. [ ] `journalctl -u transitguard -f` in zweitem Terminal mitlaufen lassen.
2. [ ] Hangfire-Dashboard → Jobs → **static-sync** → „Trigger now". Erwartet (Messwerte 21.08., gtfs.de nv_free 262 MB):
   - Download ~250–300 MB (1–5 min) · Log: `StaticSync hamburg: ~2xx MB ZIP → {Stops} Stops, {Trips} Trips, Whitelist {N}`
   - **Plausibilitätsfenster** (J3/14-Messung): Stops in Umland-Box ~5.000–20.000 · Trips ~20.000–80.000 · Whitelist = Trips.
   - **Abbruch-Kriterium:** `Whitelist 0` → Alarm `static-sync: Extrakt leer` → NICHT weiter; Runbook §Fehlerbilder.
   - RAM notieren: `systemctl status transitguard | grep Memory` — Soll < 1,5 GB Peak.
3. [ ] RT-Poll scharf schalten: in `.env` `Ingest__Enabled=true` + `systemctl restart transitguard`.
4. [ ] Nach 2 min prüfen (DB, als postgres):
```sql
SELECT ts, http_status, bytes, entities, tu_count, city_trips_matched, match_rate, route_misses, feed_age_s, error
FROM ingest_metrics ORDER BY id DESC LIMIT 5;
```
   - Erwartet (werktags 6–23 Uhr): entities 60.000–180.000 · **match_rate ≥ 0,98** · city_trips_matched: mittags 4.000–6.000 (HH-Umland; Nacht < 500 normal) · feed_age_s < 60 · error NULL.
   - **match_rate < 0,7** → G10-Alarmpfad: Static erneut triggern; bleibt niedrig → gtfs.de-Pipeline inkonsistent → rt-poll stoppen (`Ingest__Enabled=false`), Karten laufen auf „Laut Fahrplan".
5. [ ] Live-Kette: Meldung über API/PWA anlegen → Zeile in `reports` (Partition des Tages) + `report_events` → nach TTL Ablauf + ≤15 s Status `expired`.
6. [ ] Wochentagsmittel-Check nach 24 h: `FeedHealth` darf nicht dauerhaft „unhealthy" stehen (sonst G11-Gate friert Degradationen — Doku 07 §5).

## Phase 6 — Abnahme & Live-Gang
- [ ] Akzeptanz-API (Skript `scripts/acceptance.sh <basis-url>`): health/device/gate/trial/report/warnung — alles grün.
- [ ] PWA deployen (`npm --prefix web ci && npm --prefix web run build` → rsync `web/dist/ → /srv/pwa/`).
      **Ohne `VITE_API_URL` bauen** — die PWA spricht dann relative Pfade an und Caddy
      reicht `/v1/*`, `/health/*`, `/hubs/*` an die API weiter (Same-Origin, kein CORS).
      Gegenprobe nach dem Deploy: `curl -s https://app.example.de/v1/cities | head -c 40`
      muss JSON liefern, **nicht** `<!doctype html>` — sonst fehlt der `@api`-Block im Caddyfile.
- [ ] Rechtliche Gates T7.4 (AGB/Impressum/Charta live, Anwalt F-1) **vor** Play-Store-Einreichung (Flutter-Kanal separat).
- [ ] Rollback-Generalprobe: `ln -sfn …releases/<vorher> current && systemctl restart transitguard` < 30 s — einmal live üben.

## Fehlerbilder (Erste Hilfe)
| Symptom | Ursache | Gegenmaßnahme |
|---|---|---|
| StaticSync: „Extrakt leer" | ZIP-URL/Format geändert, bbox falsch | `curl -sI https://download.gtfs.de/germany/nv_free/latest.zip` prüfen; Log CityExtractor |
| match_rate ~0 | Static fehlt/alt | static-sync erneut; Whitelist-Zahl im Log prüfen |
| city_trips_matched 0 mittags | RT-Feed leer/degradiert | entities im Metric-Verlauf; gtfs.de-Statusseite; G11 greift automatisch |
| API 402 überall | Trial-Rolle falsch gemappt | `.env` DATABASE:APP-Zugang + `devices.created_at` prüfen |
| Meldung „stirbt" nie | ttl-sweep hängt | Dashboard → Jobs → ttl-sweep LastError; journalctl |
