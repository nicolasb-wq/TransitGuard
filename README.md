# TransitGuard

ÖPNV-Companion (Fahrplan · Abfahrten · Störungen) mit Community-Kontrollhinweisen und Free/Pro-Stufen. Anonym, ohne Registrierung. **Dokumentation zuerst:** [`docs/00-bestandsaufnahme.md`](docs/00-bestandsaufnahme.md) → [`docs/02-architektur.md`](docs/02-architektur.md). Produkt- und Konfidenz-Marker (`Bestätigt/Glaube ich/Unsicher/Weiß ich nicht`) gelten im gesamten Repo.

## Schnellstart (lokale Entwicklung)

Voraussetzungen: .NET 8 SDK, Node 20+, Flutter (stable), Docker (nur für lokale Postgres), psql-Client.

```bash
# 1) Dev-Datenbank (Postgres 15 + PostGIS)
docker compose -f docker-compose.dev.yml up -d

# 2) Migrationen + Seed (Stadt 'hamburg' inaktiv, TTL-Profile, Noise-Regeln)
./scripts/migrate.sh local

# 3) Tests (Stand 21.08.2026: 50/50 gruen — TTL/Normalizer/Trust/Gate/Echtdaten-Fixtures)
dotnet test TransitGuard.sln -c Release

# 4) API starten (Lokal-Modus mit In-Memory-Stores; Prod-Modus via DATABASE__APP, Ticket M4-EF)
ASPNETCORE_URLS=http://localhost:5099 dotnet run --project src/TransitGuard.Api

# 5) Web (PWA — gebaut: Ticket-Gate, Fahrtensuche, Warnungen, Abo; Dev-Proxy auf :5099)
npm --prefix web ci && npm --prefix web run dev

# 6) App (Flutter — nach PWA)
cd app && flutter run

# Deploy-Gate (Bündelung; Produktivsystem braucht .NET 8 SDK)
./scripts/test.sh
```

## Umgebungsvariablen (`deploy/env.example` → `/opt/transitguard/shared/.env`, chmod 600)

| Variable | Zweck | Beispiel |
|---|---|---|
| `DATABASE__APP` | Npgsql-URL Rolle tg_app | `Host=localhost;Database=transitguard;Username=tg_app;Password=…` |
| `DATABASE__BILLING` | Rolle tg_billing (nur Billing-Endpunkte) | … |
| `DATABASE__INGEST` | Rolle tg_ingest (Hangfire) | … |
| `SIGNALR__TOKEN_SIGNING_KEY` | Signatur Hüll-Token (≥32 B) | random |
| `INGEST__FEED_URL` | GTFS-RT-Endpoint | `https://realtime.gtfs.de/realtime-free.pb` |
| `INGEST__POLL_INTERVAL_S` | Poll-Intervall (Default 60) | `60` |
| `OPS__ALERT_EMAIL` / `OPS__SMTP_*` | Alarmierung | … |
| `BILLING__APPLE_*` / `BILLING__GOOGLE_*` | Store-Server-API (M6) | … |
| `PUBLIC_URLS__API` / `__APP` | Origin-CORS, Caddy | `https://api.example.de` |

Nicht in Repo: echte Passwörter, Store-Secrets, Signierschlüssel.

## Datenquellen & Attribution
Fahrplan-/Echtzeitdaten: **gtfs.de** (CC BY-SA 4.0; DELFI/Verbund-Daten), Namensnennung in App/About + API-Header `X-Data-Attribution` (Rechtsteil `docs/11-recht.md` §1). Feed polle ich mit 1 req/60 s + kontaktierbarem User-Agent.

## Repo-Layout
Siehe [`docs/01-verzeichnisbaum.md`](docs/01-verzeichnisbaum.md). Kern: `src/` (Api/Core/Ingest/Data) · `app/` Flutter · `web/` PWA · `docs/` verbindliche Spezifikation · `scripts/` Betrieb · `verifikation/` Messbelege 2026-08-21.

## Rechtliches
Kein Rechtsdokument; Kontrollhinweis-Feature mit serverseitigem Kill-Switch (3-stufig, ohne App-Update — `docs/11-recht.md` §4). Anonymität: Standort bleibt auf dem Gerät; Server speichert nur Meldungsinhalte pseudonym (DSGVO-Einordnung: Rechtsteil §2).
