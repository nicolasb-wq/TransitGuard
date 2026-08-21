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

## Build-Stand (22.08.2026, 9. Bausession — docs/27-build-log.md)
**QA-Loop 5/5 grün** (`scripts/qa-loop.sh 5`, Exit 0) über **zwölf** Gates: Build · Backend-Tests · **Postgres-Tests** · Flutter-Analyze · Flutter-Tests · Web-Build · Acceptance · **Vertrag** · **Caddy-Auslieferung** · **PWA-Rauchtest** · **Service Worker** · **Flutter-Laufzeit**.
Vier Zustände bleiben: grün / durch Fixer behoben / rot / **übersprungen** — eine fehlende Toolchain oder Datenbank ist nie ein Beweis.

## Vertragsdrift (NEU — zuerst lesen, bevor ein Feld geändert wird)
Drei der vier Launch-Blocker waren Server/Client-Uneinigkeit über einen Feldvertrag. Die Fehlerklasse ist jetzt geschlossen:
- **Maßgeblich ist `docs/contract/api-contract.json`** — aus der LAUFENDEN API aufgezeichnet (`verifikation/contract_capture.mjs`), nicht aus Attributen abgeleitet. OpenAPI-Codegen wurde mit Messbeleg verworfen: Swashbuckle liefert für 18/18 Operationen kein Antwortschema (anonyme Typen).
- **PWA leitet ihre Typen ab** (`web/src/contract.gen.ts` → `web/src/api.ts`): eine Server-Umbenennung wird zum **Compile-Fehler**. Flutter nutzt `app/lib/contract.gen.dart` + Tests gegen die echten Beispielantworten.
- **Nach jeder API-Änderung:** `node verifikation/contract_capture.mjs && node verifikation/contract_gen_ts.mjs && node verifikation/contract_gen_dart.mjs`, dann `scripts/verify-contract.sh`.
- **Grenze:** Der Vertrag kennt nur Beobachtetes. Nullability, die die Fixture nie zeigt, fängt er nicht. Unbeobachtete Endpunkte sind als `unbeobachtet: true` ausgewiesen.

## Werkzeuge (alle mit Gegenprobe belegt)
`scripts/pg-dev.sh up` (PG16+PostGIS ohne Root, migriert, 23 s von Grund auf) · `scripts/verify-contract.sh` · `scripts/verify-caddy.sh` · `verifikation/pwa_smoke.mjs` · `verifikation/sw_offline.mjs` · `verifikation/flutter_web_smoke.mjs` · `verifikation/build_static_mini.py` (Fixture reproduzierbar) · `scripts/flutter-e2e.sh` (für Maschinen mit Gerät).

**Neu in Session 9 — fünf echte Fehler, zwei launch-blockierend:**
- `/v1/billing/restore` war **dauerhaft kaputt**: Pfad auf der „offen"-Liste der Auth-Middleware ⇒ `DeviceId` nie aufgelöst ⇒ jeder Versuch `rate_limited`. Ohne Konten wäre ein zahlender Nutzer nach Gerätewechsel ausgesperrt gewesen (T-BILL-RESTORE).
- **API stürzte mit `Ingest:Enabled=true` beim START ab**: `IIngestMetricsSink` ohne Implementierung/Registrierung (T-DI). Zusätzlich war der rt-poll-Cron `*/60 * * * * *` ungültig (Sekundenfeld 1–59) — Zeitpläne sind jetzt Konstanten mit Test (T-CRON).
- Flutter warf `int is not a subtype of double?` bei Entfernung **0** (direkt an der Haltestelle) — alle Zahlenfelder lesen über `num`.
- `/v1/journeys/{tripId}/warnings` liegt hinter dem Ticket-Gate (stand in keiner Doku).
- Flutter verwarf `transfer_connections` stillschweigend — Umstiege wurden nie angezeigt.

**Belegt statt geglaubt:** PG16+PostGIS-Laufzeit inkl. RLS (Rollentrennung, Kill-Switch-Stufe 3), Partitionsanlage/-rotation und Retention · API im `Data:Provider=postgres`-Modus mit Acceptance 5/5 · Umstiege auf allen drei Ebenen mit **beiden** Liniennummern · Service Worker cacht nachweislich keine Meldungsdaten · Flutter-App läuft im echten Browser gegen die echte API.

**Fixture:** `tests/fixtures/static_mini.zip` wird von `verifikation/build_static_mini.py` erzeugt — 5 Halte, 147 Fahrten, HHA1→HHA5 erzwingt einen Umstieg. Wer sie ändert, zieht die Kennzahlen in den Ingest-Tests nach.

**OFFEN:** Gerätetest der Flutter-App (Android/R8 weiterhin **unbelegt** — kein KVM im Container) · Server-Erstdeploy · Echtdaten-StaticSync auf Prod-RAM · Anwalt F-1/F-18 · eigener Upload-Key + Play App Signing · SMTP · Hangfire-Postgres-Storage · T2.4-DB-Teil.

**Regel bleibt:** Schema-Änderungen IMMER gegen echtes PG validieren (`scripts/pg-dev.sh up`). Jeder Bugfix bekommt zuerst einen Testfall. Jede neue Prüfung braucht eine Gegenprobe am kaputten Zustand.

## Teststrategie
Core = xUnit tabellengetrieben (Kreis: TtlEngine/Normalizer/TrustEngine); Api-Integration gegen docker-compose-Postgres; Fixtures aus echten Feed-Mitschnitten (`verifikation/`-Skripte erzeugen sie); E2E-Checkliste Stage. kein CI — `scripts/test.sh` ist das Gate.
