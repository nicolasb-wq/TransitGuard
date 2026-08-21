# CLAUDE.md — TransitGuard Projektkontext

**Was das ist:** ÖPNV-Companion (Fahrplan, Abfahrten, Störungen) + Community-Kontrollhinweise mit Trip-Anker-Architektur. Free/Pro-Stufen; Anonymität ohne Registrierung ist Produktprinzip. **Kein „Schwarzfahr-Tool"-Framing — Sprache in Code, UI und Doku: „Kontrollhinweis", „Community-Sicherheitshinweis".**

## Verbindliche Lektüre (Reihenfolge)
1. `docs/00-bestandsaufnahme.md` — Korrekturen/Neumessungen ggü. der Übergabe v1.1 (führen bei Konflikten, zusammen mit `docs/03-entscheidungsprotokoll.md`)
2. `docs/02-architektur.md` → `docs/07-ingest.md` → `docs/04-api.md` + `06-realtime-hub.md` → `docs/08-entitlement.md`
3. ADRs in `docs/adr/` bei Detailfragen

## Stack (gesetzt, nicht zur Diskussion)
ASP.NET Core 8 · EF Core (mappt nur; Schema = SQL-Dateien!) · PostgreSQL 15+ mit **PostGIS** und **RLS/Rollenmodell** (kein TimescaleDB, kein H3 — ADR-0003/0004) · Hangfire · SignalR · Flutter (iOS/Android) · React/TS/Vite-PWA · Hetzner + Caddy + rsync/systemd (kein CI-Dienst, keine Container).

## Kernfakten, die Implementierung bestimmen (gemessen 2026-08-20/21)
- **Ticket-First-Gate ist rechtliche Pflicht** (18-rechtliche-haertung): POST /v1/reports + Kontroll-Lesezugriff verlangen ticket_confirmed; nur Tagesaggregate speichern.
- **J3/J6 GRÜN (21.08.):** Hamburg via gtfs.de bestversorgte Stadt DE (3.740 gleichzeitige Trips, 94,5 % Delay); Static↔RT-Match 100,0 %. Berlin über gtfs.de unbrauchbar (984 TUs → später VBB-Overlay nötig). Static-URLs: download.gtfs.de/germany/{nv,rv,fv}_free/latest.zip (wöchentlich, Sa ~04:22 UTC).
- **Launch-Reihenfolge: PWA → Google Play → iOS zurückgestellt** (Apple entfernt FreiFahren 08/2026 aus dem Store — 15-rechtsstand). Rechtliches Launch-Gate: Anwalt F-1.
- gtfs.de-RT: **0 VehiclePositions**, `route_id` 100 % leer, `vehicle`-Feld manchmal präsent aber **leer** (HasField ≠ Wert!), Delay-Median **0 s** (Clamping [−120,+7200]!), STU je TU Median **3**, Feed regeneriert **alle 10 s** (304 nie), Größe tageszeitabhängig 33–42 MB.
- Alerts: >99 % Duplikate (Attribution/Ausstattung) → Noise-Regeln Pflicht, Dedup-Key `sha256(header|desc[:150])`.
- VBB: inaktiv (Datenlücke seit 04.06.2026), nur als Config-Overlay vorgemerkt.

## Do's
- Jede TTL-/Normalizer-/Trust-Änderung **zuerst Testfall** (T-Tabellen in `docs/09-testplan.md`), dann Code.
- Alle Schwellen/Werte in DB-Config (`ttl_config`, `ingest_config`, `alert_noise_rules`), nie hardcoded.
- Meldungs-Ressourcen: `Cache-Control: no-store` (Kill-Switch!).
- API-Payloads tier-geformt **im Server** (Free-JSON ohne Pro-Felder — physisch abwesend).
- Migrationen: neue `docs/sql/NNNN_*.sql`, nur vorwärts, `scripts/migrate.sh`.
- Doku-Mitführen bei Verhaltensänderung (diese Datei und 02/04/06/07 sind lebende Spec).

## Don'ts (rechtlich hart)
- **Ticket-First-Gate niemals umgehen, abschwächen oder als „optional" einbauen** — Kontroll-Zugang nur mit `ticket_confirmed` (18-rechtliche-haertung H1; API/Hub-Vertrag + T-GATE-Tests sind verbindlich). Copy-Nie-Liste gilt überall (11-recht §3).

## Don'ts (technisch)
- Keine Fahrzeug-/Umlauf-Spekulation (ADR-0001, v1.1 B.5) — keine Ausnahme, auch nicht „hilfreich gemeint".
- Keine Meldungsdaten in Client-Caches/Service-Worker/Logs.
- Kein Gating von Meldungs-Sicht oder -Erstellung (ADR-0006 — Matrix-Zeilen 1–3, 8, 14 sind unverrückbar Free).
- Keine neuen Zahlen behaupten ohne Marker: *Bestätigt* (gemessen) / *Glaube ich* / *Unsicher* / *Weiß ich nicht*. Gilt für Code-Kommentare, PRs, Doku.
- Keine Konten/Geräte-Verkettung einführen (ADR-0005).
- Keine Secrets committen; `deploy/env.example` ist Vorlage.

## Deploy-Weg (Kurzfassung, Details `docs/10-deployment.md`)
lokal `scripts/test.sh` → `dotnet publish` → `rsync` nach `/opt/transitguard/releases/<ts>` → `scripts/deploy.sh` (Migrationen → Symlink-Swap → Health-Check → Auto-Rollback bei Fehler). Rollback: Symlink zurück + Restart.

## Monetarisierung (ADR-0014, entscheidet über Code-Pfade!)
14 Tage volle Nutzung ab Device-Ausstellung (`TrialPolicy`), danach Abo **2,99 €/Monat** (`AccessGate`: trial|subscriber|locked, 402 `trial_expired`). Ticket-First-Gate gilt in ALLEN Stufen. Preis nur als `TrialPolicy.MonthlyPriceEur` ändern.

## Build-Stand (21.08.2026, 7. Bausession — docs/25-build-log.md)
Lösung kompiliert & 50/50 Tests grün (TTL-Engine komplett, Normalizer gegen Live-Fixtures, Ticket-Gate live verifiziert, API im Lokal-Modus In-Memory). Befehle: `dotnet build TransitGuard.sln -c Release` · `dotnet test` · `dotnet run --project src/TransitGuard.Api`. Standalone: Backend 67/67 Tests grün + **PWA gebaut** (`web/`: Ticket-Gate, Trial-Banner, Fahrtensuche mit Standort-Autofill, Abfahrten mit Ist/Soll, Kontroll-Warnungen ≥1 Haltestelle vorher, 1-Tap-Meldung, Abo/Ticket-Screens). Neu: Hangfire-Jobs live bewiesen (TTL-Sweep 15 s), SignalR-Live in PWA (E2E-PASS via verifikation/signalr_e2e.mjs), Umstiegsrouter v2 (transfer_connections), EF-Stores hinter Data:Provider=postgres (Laufzeit auf PG-Host prüfen!). Dev-Tool: TTL__DEVOVERRIDESECONDS nur lokal. Neu: **PG-Laufzeit bewiesen** (PG16+PostGIS ohne Root via Micromamba; Migrationen 0001-0005, Partitionen, RLS live); StaticSyncJob komplett + AlarmSink; 73/73 Tests. OFFEN: Echtdaten-StaticSync auf Prod-RAM, SMTP, Flutter, Payment. Regel: Schema-Änderungen IMMER gegen echtes PG validieren (22-build-log §2). Neu: deploy/RUNBOOK-SERVER.md = verbindlicher Erst-Deploy-Pfad (Phase 5: static-sync VOR rt-poll!); scripts/acceptance.sh = Abnahme; Flutter: **analyze 0 / test 5/5 / Debug-APK gebaut+verifiziert** (app/transitguard-debug.apk); Release-Signierung fertig (upload-keystore.jks + key.properties, conditional im Gradle); Release-AAB auf Dev-Maschine: `flutter build appbundle --release` + die 2 minify-Zeilen im build.gradle.kts entfernen (R8 > 1,5-GB-Sandbox-RAM, 4 Versuche bewiesen).

## Teststrategie
Core = xUnit tabellengetrieben (Kreis: TtlEngine/Normalizer/TrustEngine); Api-Integration gegen docker-compose-Postgres; Fixtures aus echten Feed-Mitschnitten (`verifikation/`-Skripte erzeugen sie); E2E-Checkliste Stage. kein CI — `scripts/test.sh` ist das Gate.
