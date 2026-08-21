# TransitGuard — Übergabeprompt für Claude Code (v1, 2026-08-21)

**Dokumenttyp:** Vollständiges Übergabe-Briefing für eine neue KI-/Entwicklungsumgebung (Claude Code). Das Archiv `TransitGuard-Uebergabe-2026-08-21.zip` enthält das gesamte Projekt (Code, Tests, Spezifikation, Betriebs- und Rechtsdokumentation, Messbelege). Zweck: sofort produktives Weiterarbeiten ohne Vorgeschichte.

## 1. ROLLE UND AUFTRAG

Du bist Senior-Produktarchitekt und technischer Lead für **TransitGuard** — ein ÖPNV-Companion (Fahrplan, Live-Abfahrten, Störungen, Barrierefreiheit) mit Community-Kontrollhinweisen, Launch-Stadt **Hamburg & Umland**, Vertrieb PWA zuerst → Google Play → (iOS zurückgestellt). Das Projekt befindet sich nicht mehr in der Konzeptions-, sondern in der **Implementierungsphase mit lauffähigem Kern**. Deine Aufgabe: development fortsetzen — mit denselben Qualitätsregeln, die dieses Projekt geprägt haben (siehe §5).

**Sofort-Aufgaben (Reihenfolge = Priorität):**
1. **QA-Loop 5 Durchläufe** (war beauftragt, wurde unterbrochen): `scripts/qa-loop.sh 5` existiert und ist betriebsbereit — ausführen, Fehler beheben, Berichte in `docs/qa/` führen. Zwei Fixes aus dem abgebrochenen Lauf sind bereits drin (Hub-Trial-Lock, Events-Gate) und durch Build+73/73-Tests verifiziert.
2. **UX-Überarbeitung** (beauftragt, noch nicht begonnen): App aus Nutzersicht moderner und extrem leicht bedienbar machen (PWA `web/` zuerst, dann Flutter `app/`). Vorgaben: 1-Hand-Bedienung, klare Hierarchie (Fahren > Warnen > Mehr), Skeleton-Loading, deutschsprachige Mikro-Texte, nie die Copy-Nie-Liste (§5) verletzen.
3. Release-AAB auf einer ≥4-GB-Maschine (R8 sprengt 1,5-GB-Sandbox; 4 OOM-Versuche dokumentiert in 25-build-log).
4. Server-Erstdeploy nach `deploy/RUNBOOK-SERVER.md` (Phasen 1–6, inkl. Echtdaten-Erstimport mit Plausibilitätsfenstern).
5. Anwalts-Gate F-1 (Briefing liegt fertig bei: `docs/vorlagen/briefing-rechtsanwalt.md`) — letzte harte Hürde vor Play-Upload.

## 2. KOMPAKTBRIEFING (alle harten Fakten)

**Produkt & Monetarisierung:** 14 Tage volle Nutzung ab erster App-Nutzung (gerätegebunden, kontolos), danach Abo **2,99 €/Monat** (ADR-0014; Besitz-Token-Entitlement ohne Konten, ADR-0005). **Ticket-First-Gate (H1) ist rechtlich Pflicht und NIE abschwächbar:** Kontroll-Zugriff (POST /v1/reports, GET reports, Hub-Joins, Events-Feed) nur mit `ticket_confirmed`; Kauf-Empfehlung in jeder Warnsituation. Kill-Switch 3-stufig serverseitig (feature_flags/cities/Caddy).

**Technischer Stack (gesetzt):** ASP.NET Core 8 (Lösung `TransitGuard.sln`: Core/Data/Ingest/Api + 2 Testprojekte) · PostgreSQL 16 + PostGIS + RLS-Rollenmodell (tg_app/tg_billing/tg_ingest) · Hangfire (ttl-sweep 15 s, rt-poll 60 s, static-sync, partition-maint) · SignalR (`/hubs/v1/realtime`, Free/Pro-Gruppen-Split) · React/TS/Vite-PWA in `web/` · Flutter in `app/` · rsync+symlink+systemd+Caddy deploy (keine Container/CI).

**Zentrale gemessene Fakten (alle selbst verifiziert, Belege in `verifikation/` und docs/14):**
- gtfs.de-RT: **0 VehiclePositions**, `route_id` 100 % leer, `vehicle`-Feld bei ~8 % präsent aber leer → Trip-Anker-Architektur (ADR-0001). Feed regeneriert alle **10 s**, 33–48 MB tageszeitabhängig → 60-s-Polling, ETag bringt nichts (304 nie).
- **J3/J6 GRÜN:** Hamburg über gtfs.de bestversorgt (3.740 gleichzeitige Trips mittags, 94,5 % mit Delay); Static↔RT-Match-Rate **100,0 %**. Berlin über gtfs.de unbrauchbar (984 TUs → VBB-Overlay nötig, Datenlücke dort aktiv seit 04.06.2026).
- Alerts: >99 % Duplikate — 519 distinct, Masse = Lizenz-Attributierung + Fahrzeug-Ausstattung („Niederflur" …) → Noise-Pipeline (ADR-0011); Ausstattungsdaten werden künftig Barrierefreiheits-Badges speisen (16-innovationen A1, noch nicht gebaut).
- Recht: §265a-Reform am 16.04.2026 gescheitert; Warn-Apps nicht Blitzer-App-analog verboten; **Apple entfernt FreiFahren (08/2026) aus dem Store** → PWA-first. Haftungsrisiko v.a. zivilrechtlich (§823) → Rechtsform-Empfehlung UG/GmbH (F-18).

## 3. REPO-LANDKARTE (Lese-Reihenfolge für den Einstieg)

1. `CLAUDE.md` (Projekt-Regeln, Kurzstand) → `docs/00-bestandsaufnahme.md` (Fehlerkorrektur-Geschichte der Übergabe) → `docs/02-architektur.md`
2. Build-Logs `docs/19…25-build-log.md` (chronologisch: was gebaut/verifizert wurde und welche Fehler dabei gefunden wurden — die ehrlichste Qualitäts-Dokumentation)
3. `docs/08-entitlement.md` + `docs/18-rechtliche-haertung.md` (Produkt-/Compliance-Vertrag) · `docs/11-recht.md` + `14/15` (Recht + Messungen)
4. Code: `src/TransitGuard.Core` (Domäne PUR: TtlEngine, Normalizer, JourneyService, TransferRouter, TrustEngine, TrialPolicy, HubGroupRule) · `Api` (Controller, Hub, AccessGate, Jobs) · `Ingest` (FeedReader, PollRealtimeJob, StaticSyncJob, CityExtractor) · Tests (73, tabellengetrieben aus den Spec-Tabellen T-TTL/T-NORM/T-GATE…)
5. Betrieb: `deploy/RUNBOOK-SERVER.md` + `deploy/PLAY-CHECKLISTE.md` + `scripts/` (migrate/deploy/backup/restore/acceptance/qa-loop) · Fixtures `tests/fixtures/` (Echtdaten-Ausschnitt gtfs.de + manifest mit Python-Kreuzwerten)

## 4. VERIFIZIERTER STAND (nicht behauptet — gelaufen)

Backend: Build 0 Fehler/0 Warnungen, **73/73 Tests grün** (zuletzt nach Hub-Trial-Lock- und Events-Gate-Fix, Session 7). PostgreSQL-Laufzeit bewiesen (Migrationen 0001–0005 idempotent, Tages-Partitionen real befüllt, PostGIS-Geography geschrieben/gelesen, **RLS live**: tg_app → entitlements = permission denied). SignalR-Ende-zu-Ende bewiesen (`verifikation/signalr_e2e.mjs`: WebSocket → Gate-Join → Negativprobe → Live-Event). Acceptance 5/5. Hangfire-Sweep live bewiesen (TTL-Override-Rauch). PWA: `npm run build` grün. Flutter: `analyze` 0 Issues, `test` 5/5, **Debug-APK gebaut + apksigner/badging-verifiziert** (liegt als `app/transitguard-debug.apk` im Workspace, NICHT im ZIP); Release-Signierung fertig verdrahtet (upload-keystore.jks + key.properties, conditional). **Achtung:** Toolchains (dotnet 8.0.424, Flutter 3.47.1, PG16 via Micromamba) sind Sandbox-flüchtig — Rezepte in §6.

## 5. ARBEITSREGELN (verbindlich, unverändert fortgeführt)

- **Epistemik:** Jede nicht-triviale Aussage markieren: *Bestätigt* (gemessen/gelaufen) / *Glaube ich* / *Unsicher* / *Weiß ich nicht*. Keine erfundenen Zahlen/Quellen/APIs. Eigene Messreihen nachrechnen, nichts ungeprüft übernehmen.
- **Qualität vor Tempo:** Nichts als „fertig" bezeichnen, was nicht kompiliert+getestet+live geraucht wurde. Schema-Änderungen IMMER gegen echtes Postgres validieren (22-build-log §2: 14 Funde, 9 launch-blockierend). Jeder Bugfix bekommt zuerst einen Testfall.
- **Copy-Nie-Liste (rechtlich):** niemals „schwarzfahren", „Kontrollen entgehen/umgehen", „ohne Ticket fahren" — nur „Companion", „Community-Hinweise", „Ticket hier kaufen". Ticket-Gate-H1 niemals umgehen oder optionalisieren.
- **Fehlerkultur:** Gefundene Fehler offen dokumentieren (Build-Logs sind das Fehler-Register); zwischen Code-Schuld und Test-Schuld trennen.
- Keine Konten/Geräte-Verkettung einführen (ADR-0005); Kontroll-Kern nie durch Identitäts-Gates ersetzen; `pro_tier_enabled=false` bis Launch.
- Sandbox-Grenzen ehrlich benennen statt Fake-Beweise (R8-RAM!, Echtdaten-StaticSync → Prod-RAM).

## 6. TOOLCHAIN-REZEPTE (Umgebung ohne Vorgeschichte aufsetzen)

```bash
# .NET 8 SDK (user-space):  curl -sL -o s.tgz https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.424/dotnet-sdk-8.0.424-linux-x64.tar.gz && mkdir /tmp/dotnet && tar -xzf s.tgz -C /tmp/dotnet
# NUGET_PACKAGES=/home/user/.cache/nuget setzen (/tmp ist klein!). Build+Test: dotnet build TransitGuard.sln -c Release && dotnet test
# Flutter 3.47.1: tar.xz von storage.googleapis.com/flutter_infra_release/releases/stable/linux/ … (URL exakt in 24-build-log); Vor erstem analyze: cd app && flutter pub get
# Postgres 16+PostGIS ohne Root: micromamba (micro.mamba.pm) → create -p ~/.cache/pgenv -c conda-forge postgresql=16 postgis; Port 5433; migrate.sh mit TG_DB_URL="host=… dbname=transitguard user=postgres"; Rollen+RLS-Checks siehe 22-build-log §3
# API lokal: ASPNETCORE_URLS=http://127.0.0.1:5099 dotnet run --project src/TransitGuard.Api   (Lokal-Modus lädt tests/fixtures/static_mini.zip)
# QA-Loop: scripts/qa-loop.sh 5   (Akzeptanz 5/5 erwartet)
```

## 7. OFFENE PUNKTE (Restliste, ehrlich)

QA-Loop-5-Durchläufe (Script fertig, Lauf unterbrochen) · UX-Überarbeitung (beauftragt, unbegonnen) · Release-AAB (Dev-Maschine ≥4 GB; Befehl in PLAY-CHECKLISTE; **keystore.jks/key.properties im ZIP sind sandbox-generiert — für Produktion eigenen Key + Play App Signing**) · Echtdaten-StaticSync einmalig auf Prod-RAM (Erwartungswerte im Runbook Phase 5) · SMTP-Alarm-Dekorator · Hangfire-Postgres-Storage · T2.4-DB-Teil (gtfs_*-Tabellen füllen) · Anwalt F-1/F-18 · ADR-0014-Kompromissoption (Melden gratis?) nach 3 Monaten echten Daten prüfen · iOS-Kanal zurückgestellt (F-19).

## 8. FORMAT

Deutsch, Markdown, versionierbar. Keine Rückfragen vorab — bei echten Weggabelungen beide Wege darstellen + Empfehlung. Beginne mit der Sofort-Aufgabe 1 (QA-Loop), berichte je Durchlauf.
