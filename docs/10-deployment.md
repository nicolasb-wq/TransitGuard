# TransitGuard — Deployment (rsync, Caddy, systemd, Backup, Rollback, Secrets)

**Constraints (vom Auftrag, hart):** rsync über SSH; keine GitHub Actions, keine Container-Registry, keine externen CI-Dienste; Build lokal oder auf Zielserver. Ziel: Debian 12 VM (Hetzner), 1 Node.

## 1. Verzeichnislayout auf dem Server

```
/opt/transitguard/
  releases/<YYYYMMDD-HHMM>/        # je Deploy: self-contained Build + Migrations
  current -> releases/<neueste>    # Symlink — Rollback = Symlink zurück
  shared/
    .env                (chmod 600, nicht im Repo)
    logs/               (journald primär; App-Dateilog nur für strukturierte Events)
    backups/            (pg_dump-Ausgabepuffer, versandt nach Storage Box)
/var/lib/postgresql/…   (DB-Daten — eigene Platte/Volume empfohlen)
/srv/pwa/               # React-Build (statisch, von Caddy serviert)
```

## 2. Build & Release (lokal, Beispiel Linux-Dev-Maschine)

```bash
# API (self-contained, keine Runtime-Abhängigkeit auf dem Server)
dotnet publish src/TransitGuard.Api -c Release -r linux-x64 --self-contained -o build/api
# PWA
npm --prefix web ci && npm --prefix web run build      # web/dist
# Übertragen (ein Ziel-Verzeichnis je Release; --delete nur innerhalb des Release!)
ssh deploy@server "mkdir -p /opt/transitguard/releases/$(date +%Y%m%d-%H%M)"
rsync -az --chmod=D755,F644 build/api/ deploy@server:/opt/transitguard/releases/$REL/
rsync -az web/dist/ deploy@server:/srv/pwa/            # atomar: erst nach Build-Tausch
```

`scripts/deploy.sh REL` auf dem Server: Migrations ausführen (§4) → Symlink umzeigen → `systemctl restart transitguard` → Health-Check (§7) → bei Misserfolg automatisch Symlink zurück (Rollback-Schutz).

## 3. systemd-Units

```ini
# /etc/systemd/system/transitguard.service
[Unit]
Description=TransitGuard API+Ingest
After=network-online.target postgresql.service
Wants=network-online.target

[Service]
User=transitguard
Group=transitguard
WorkingDirectory=/opt/transitguard/current
EnvironmentFile=/opt/transitguard/shared/.env
ExecStart=/opt/transitguard/current/TransitGuard.Api
Restart=always
RestartSec=5
LimitNOFILE=16384           # SignalR-Verbindungen
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/opt/transitguard/shared/logs

[Install]
WantedBy=multi-user.target
```

PostgreSQL: eigener `postgresql.service` (Debian-Paket), `local_prefer_unix_socket`; Ingest-Zeitzone UTC (`Environment=TZ=UTC` global via `/etc/systemd/system/transitguard.service.d/tz.conf`).

## 4. Datenbank-Migrationen

Reine SQL-Dateien (`docs/sql/NNNN_*.sql`), angewendet durch `scripts/migrate.sh` (psql, ON_ERROR_STOP, Schema-Migrations-Tabelle `schema_migrations(filename, applied_at)`). Regeln: nur vorwärts (keine Down-Skripte — Rollback = altes Release + alter DB-Stand aus Backup); jede Migration in einer Transaktion; Deploys mit Schemaänderung NUR nach frischem Backup.

## 5. Caddy

```caddy
api.example.de {
    encode zstd gzip
    reverse_proxy 127.0.0.1:5080 {
        header_up X-Forwarded-Proto {scheme}
    }
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
        X-Content-Type-Options nosniff
        Referrer-Policy no-referrer
        # keine CSP-Header hier — App-spezifisch siehe PWA-Build (nonce-basiert)
    }
    # SignalR-WebSockets: Caddy handled Upgrade automatisch
}
app.example.de {
    root * /srv/pwa
    try_files {path} /index.html
    file_server
    # Service-Worker-Build ohne Meldungsdaten (B8): API-Calls immer live
}
```

Stufe-3-Kill-Switch (Notfall ohne DB): `respond 503`-Zeile vor `reverse_proxy` einkommentieren + `systemctl reload caddy`.

## 6. Test- & Deploy-Gate (kein CI-Dienst — lokale Pipeline)

`scripts/test.sh` (dotnet test + npm test + lint) läuft vor JEDEM Deploy auf der Build-Maschine; Deployment verweigert bei Fehlschlag (deploy.sh prüft Test-Marker-Datei). Auf dem Server post-Deploy: `/health/ready` + Smoke (1 API-Runde via curl-Skript).

## 7. Monitoring & Alarmierung

- Health: `/health/ready` prüft DB-Ping + letzter erfolgreicher Poll < 5 min.
- Hangfire-Dashboard unter `/admin/hangfire` (Auth: tg_ops-Basicauth via Caddy `basic_auth` — Zwangs-Header, nur von Ops-IP/VPN).
- Externe Wachhunde: einfacher Cron auf Zweit-VM (oder bestehender Uptime-Checker des Betreibers) prüft /health/ready alle 60 s + Ingest-Metriken-Endpoint (tg_readonly) — E-Mail bei 2 Fehlern.
- Log-Aufbewahrung: journald 14 Tage (`SystemMaxUse=1G`); danach weg (DSGVO, Rechtsteil).

## 8. Backup & Restore

- Täglich 03:00 `pg_dump -Fc transitguard` → `/opt/transitguard/shared/backups/` → `rsync` zur Hetzner Storage Box (Ziel: 7 daily, 4 weekly, 6 monthly via `rsync --link-dest`-Rotation).
- Config-Backup: `.env`-Kopie im Passwortmanager (nicht auf dem Server abgelegt außerhalb 600), Caddyfile + Units im Repo (ohne Secrets).
- **Restore-Drill:** monatlich (Testplan T-DATA) Restore des jüngsten Dumps in Scratch-DB + App-Start dagegen; Dokumentation des Gemessenen (Dauer) im Runbook.

## 9. Rollback

1. App: `ln -sfn releases/<vorher> current && systemctl restart transitguard` (< 30 s).
2. DB: nur per Restore (deshalb Backup-vor-Deploy-Pflicht); kein Migrations-Rückbau.
3. Daten-Notfälle (Feed-Vergiftung o. ä.): ingest pausieren (`Hangfire delete job`), `rt_trip_state`/`gtfs_alerts` TRUNCATE als tg_ingest, Re-Sync — Reports (Nutzerdaten) unberührt.

## 10. Secrets-Verwaltung

`.env` (Beispiel in README; echte Werte nie committen): DB-URLs (tg_app/tg_billing/tg_ingest getrennt), SignalR-Hüll-Token-Signatur, Store-API-Credentials, Ops-E-Mail. Serverzugriff ausschließlich SSH-Key (kein Passwort-Login), `deploy`-User mit beschränkter Shell für rsync. Keine Secrets in Prozesstop-Dateien erlaubt (`ProtectSystem=strict`).
