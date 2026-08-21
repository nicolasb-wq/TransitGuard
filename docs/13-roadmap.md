# TransitGuard — Implementierungs-Roadmap (Tickets je ½–1 Arbeitssitzung, mit Akzeptanzkriterien)

**Legende:** [AC] = Akzeptanzkriterien, alle erfüllt = fertig. Reihenfolge ist Abhängigkeitskette, nicht zwingend Zeitplan. Schätzung: 1 Ticket ≈ ½ Tag erfahrener Einzelentwickler (*Unsicher*, ohne Kenntnis der konkreten Person).

## M0 — Fundament — **ERLEDIGT 21.08.** (T0.1–T0.4 ✓; Details 19-build-log)

*(Originalplan:)*
- **T0.1 Repo-Skeleton**: Solution + 4 Projekte (Api/Core/Ingest/Data) + tests + scripts + Verzeichnis gem. 01-verzeichnisbaum. [AC: `dotnet build` grün, Struktur reviewt, CLAUDE.md/README gelten]
- **T0.2 Migrationsrunner**: `scripts/migrate.sh` + `schema_migrations` + 0001–0004 gegen lokale DB. [AC: frische DB = alle Migrationen grün; zweite Ausführung no-op]
- **T0.3 Dev-Umgebung**: docker-compose.dev.yml (postgres+postgis), .env.example, Seed-Skript (Stadt hamburg inaktiv, ttl_config, noise rules). [AC: `make dev-up && make seed` läuft]
- **T0.4 Build-/Testskripte**: scripts/test.sh + deploy.sh-Gerüst (ohne Server). [AC: test.sh rot/grün-steuerbar via Testprojekte]

## M1 — API-Gerüst & Betrieb
- **T1.1 App-Bootstrap**: ASP.NET 8, Config-Bindung, Health live/ready (DB-Ping), Strukturierung Middleware-Pipeline. [AC: /health/ready 200 gegen Dev-DB]
- **T1.2 Device-Auth**: POST /v1/devices, Token-Hash-Storage, Middleware, Rate-Limit-Basis (in-mem). [AC: Token-Ausstellung + 401-Fälle; Test T-API]
- **T1.3 Ops-Schiene**: feature_flags-Leser, tg_billing-Zweitverbindung (DI), Hangfire-Dashboard hinter Caddy-Basic-Auth. [AC: Flag-Kill auf Test-Endpoint wirkt]

## M2 — Static-Daten (GTFS)
- **T2.1 Static-Downloader**: ZIP → Temp, SHA256, gtfs_builds staged. [AC: idempotent, Abbruch sauber]
- **T2.2 GTFS-CSV-Parser**: routes/stops/trips/stop_times → staged build_id-Zeilen. [AC: parsing der realen gtfs.de-ZIP; Zeit gemessen & dokumentiert]
- **T2.3 Stadt-Extrakt**: bbox-Stops → Trip-Closure → Teilmenge. [AC: Hamburg-Extrakt aus DE-Feed; Zahl dokumentiert (= J3-Teil B)]
- **T2.4 Atomarer Swap + Degradation**: active/retired in 1 Transaktion; Meldungs-Degradation C.3.2.3. [AC: Tests T-ING Swap; Meldung überlebt]
- **T2.5 Whitelist-Aufbau**: In-Mem-Dictionary + Postgres-Fallback. [AC: Kaltstart-Test]
- **T2.6 J6-Messjob**: match_rate über 24 h, einmalig + Dauermetrik. [AC: Bericht mit Tagesgang]

## M3 — Realtime-Ingest
- **T3.1 PollJob**: 60 s, If-None-Match, ETag-Fallback, Metrikzeile, Circuit Breaker. [AC: Tests T-ING]
- **T3.2 Entity-Streaming-Parse** (CodedInputStream) + Speicherbudget-Messung. [AC: 40-MB-Testfeed verarbeitet, Peak-RAM dokumentiert]
- **T3.3 Normalizer TU**: Whitelist-Filter, route_id-Lookup (C.3.1), Delay-Clamp, rt_trip_state-Delta. [AC: T-NORM 1–5 grün]
- **T3.4 Alert-Pipeline**: Noise-Regeln, Dedup-Key, Upsert, UI-Fenster. [AC: T-NORM 6–10; realer Feed → gtfs_alerts ~hunderte Zeilen]
- **T3.5 FeedHealth + G11-Gate**: Wochentagsmittel, freeze-Logik, control.freeze-Event. [AC: T-TTL-10]
- **T3.6 Alarmierung**: Schwellen (Ingest-Doc §8) → Ops-E-Mail. [AC: künstliche Unterschreitung löst aus]

## M4 — Domäne Meldungen
- **T4.1 TTL-Engine** — ✅ komplett 21.08. (13 Tests grün inkl. F-16-Langfahrt); ttl_config-DB-Anbindung mit M4-EF
- **T4.2 Reports-API** — ✅ Lokal-Modus 21.08. (201/422/403/Idempotenz live verifiziert); EF-Modus offen
- **T4.2a Ticket-Gate (API)** — ✅ Logik grün + Smoke 422/403; Tagesaggregate `gate.*` mit M4-EF
- **T4.3 Report-Events + Trust-Gate**: confirm/contradict inkl. Paritätsregeln; visible-Schwelle. [AC: T-TRUST-Fälle]
- **T4.4 TtlSweep + B.5**: Expiry, Trip-Ende via Interpolation, derived_terminus, CANCELED, Swap-Degradation, Freeze. [AC: T-TTL 7–10]
- **T4.5 Trust-Engine**: Scoring, Ränge (fixt B5), Anomalie-Dämpfung. [AC: Rang-Übergänge + Spam-Fall]
- **T4.6 Interpolator**: Soll+Delay → Fortschritt/ETA; Label-Regel. [AC: Beispielreisen aus Fixture; Median-0-Fall „laut Fahrplan"-Label]

## M5 — Realtime & Clients
- **T5.1 SignalR-Hub + Gruppen + Tier-Split**: join-Regeln, Payload-Former. [AC: T-EN-2, T-HUB-Divergenz]
- **T5.2 Ring-Buffer + Resync**: events?since_id + resync.required. [AC: T-HUB Reconnect]
- **T5.3 Flutter-App Kern**: Stationswahl (GPS-Pre-Fill), Abfahrten, Melden-Flow (1-Tap + Trip aus Liste). [AC: E2E-Szenario 1 auf Stage]
- **T5.3b/T5.4b Ticket-First-Gate (Client)**: 24-h-Gate-Dialog (Ja / Kaufen / Später), Kauf-Fenster (hvv-Web-URLs + Store-Link hvv switch), keine Zugriffs-Option für Nutzer ohne Ticket, Gate-Header an allen Kontroll-Zugriffen. [AC: T-GATE E2E; Copy-Review gegen Nie-Liste 11-recht §3]
- **T5.4 PWA**: Karte/Liste, Melden, Install-Prompt, kein Meldungs-Cache. [AC: E2E 1 + no-store-Verifizierung]
- **T5.5 Pro-Client-Ansichten** (hinter `pro_tier_enabled=false` ausgebaut): movement, Trust, Historie. [AC: T-EN-1]

## M6 — Entitlement
- **T6.1 Entitlement-Service + DB-Zugriff tg_billing**: activate/restore/validate inkl. Hashes. [AC: T-EN-3/4/7]
- **T6.2 Store-Validierung**: Apple/Google-Sandbox (F-12-Recherche vorher!). [AC: Sandbox-Kauf → Pro]
- **T6.3 Verbindungscaps + Widerruf**: SignalR-Kicks, Refund-Hook. [AC: T-EN-5/6]
- **T6.4 Kill-Switch-Integration**: Stufe 1+2 über alle Endpunkte + Hub. [AC: T-E2E-3]

## M7 — Launch-Härtung (Gates!)
- **T7.1 J3-Gate-Entscheidung**: Hamburg aktivieren ODER Alternativstadt analysieren (cities.is_active). [AC: schriftliche Entscheidung mit Messdaten]
- **T7.2 Lasttest Ingest**: 24 h Dauerlauf, Peak-RAM/CPU dokumentiert, Metriken alarmfrei. [AC: Bericht]
- **T7.3 Backup-Drill + Rollback-Übung**. [AC: Restore in <1 h lauffähig]
- **T7.4 Rechts-Gate**: F-1/F-5/F-7 abgeschlossen, Impressum live, Attributionen sichtbar. [AC: Checkliste Rechtsteil abgehakt]
- **T7.4a Härtung live**: AGB (vorlagen/nutzungsbedingungen-entwurf finalisiert), Impressum, Selbstverpflichtungs-Charta (H6), Notice-and-Action-Postfach monitored, Gate-Aggregate im Ops-Dashboard. [AC: Anwalts-Freigabe der Texte; Charta öffentlich]
- **T7.5 Store-Einreichung** (iOS+Android): Dossier review-notes. [AC: genehmigt oder Eskalationspfad dokumentiert]
- **T7.6 Betatest**: 20–50 Nutzer HH, 2 Wochen, Trust-/TTL-Metriken beobachten. [AC: Fehlerberichte abgearbeitet; Meldungsdichte ≥ Modellannahme?]

## M8 — Post-Launch (Pro-Aktivierung)
- **T8.1 pro_tier_enabled=true + Preis-Live-Schaltung**. [T-EN-Suite grün, Store-Produkte live]
- **T8.2 Push-Infra** (FCM/APNs, Pro #6): Token-Registrierung nur Pro; DSGVO-Folgen umgesetzt. [AC: Test-Push auf Stammstrecke]
- **T8.3 Historie/Statistik (Pro #9)**. — **T8.4 Konversions-/Dichte-Report**: Ökonomie-Modell mit echten Zahlen rückrechnen.

**Kritischer Pfad:** M0 → M2 → M3 → M4 → M5 → M7. M6 kann bis T7 parallel/parallelisiert laufen (Billing-Store-Validierung T6.2 braucht F-12). **Gesamtaufwand grob** (*Unsicher*, kein Commitment): 34 Tickets ≈ 17–25 Arbeitstage Kern + Betatest-/Gate-Wartezeiten.

## Deltas 21.08.2026, Session 7 (Release-Signierung + RAM-Grenze — 25-build-log)

- Upload-Keystore + conditional Signierkonfiguration (kompiliert), DSL-Fix java.util; Release-Bau 4× OOM → Grenze >1,5 GB dokumentiert, Dev-Maschinen-Befehl final. analyze 0 / test 5/5 frisch.

## Deltas 21.08.2026, Session 6 (Flutter real — 24-build-log)

- **Flutter-Kanal kompiliert:** SDK in Sandbox, analyze 0, tests 5/5, **Debug-APK gebaut & apksigner/badging-verifiziert**; 6 Fehler gefixt (u. a. warnings-Parsing im Vertrags-Client). Release-AAB → Dev-Maschine (R8-RAM-Grenze ehrlich dokumentiert).

## Deltas 21.08.2026, Session 5 (Deploy+Flutter — 23-build-log)

- **T7.x-Deploy konkretisiert:** RUNBOOK-SERVER.md (6 Phasen, Echtdaten-Erstimport mit Plausibilitätsfenstern aus J3), deploy.sh/backup.sh/restore.sh/acceptance.sh (Acceptance 5/5 live ✔, Auto-Rollback eingebaut).
- **Flutter (Play-Kanal) gestartet:** app/ komplett (Gate, Fahrtensuche, Warnungs-Banner, 1-Tap-Meldung, Trial) + PLAY-CHECKLISTE.md; Kompilier-Gate auf Dev-Maschine.
- Sweep: Gate-Reihenfolge-Verstoß gefixt (Idempotenz lief vor Ticket-Gate), uuidgen-Portabilität.

## Deltas 21.08.2026, Session 4 (PG-Wahrheit — 22-build-log)

- **M4-EF ✅ LAUFZEIT-BEWISEN:** PG 16.10+PostGIS (Micromamba, rootless), Migrationen 0001–0005 (drei Schema-Bugs gefunden+fixt: PARTITION BY gefehlt, View-Reihenfolge, last_known_station_id → 0005), kompletter Flow im PG-Modus, **RLS live** (T-EN-7), Tages-Partition + PostGIS-Geo bewiesen.
- **T2.4 StaticSyncJob ✅ komplett** (Download→Extrakt→Swap→Bookkeeping, 2 Tests) + `IAlarmSink`.
- 14 Fehler gefunden/behoben (9 davon launch-blockierend) — Lektion: echte-Schema-Laufzeit ist eigene Teststufe.

## Deltas 21.08.2026, Session 3 (Empfehlungsliste — 21-build-log)

- **TtlSweep/Poll als Hangfire-RecurringJobs registriert + live bewiesen** (TTL-Override-Smoke: created→expired in <25 s); Dashboard /admin/hangfire.
- **T5.4 PWA + SignalR-Live-Feed** (E2E-Beweis signalr_e2e.mjs: WebSocket, Gate-Join, Negativprobe, Live-Event) + **Umstiegsverbindungen v2** (TransferRouter + 4 Tests + UI).
- **M4-EF-Stores implementiert** (Data:Provider=postgres; Laufzeitprüfung auf PG-Host offen).
- Sweep: 7 weitere Befunde behoben, darunter Start-Crash (fehlende Job-Registrierung) und Doppel-TtlEngine (Config hätte nie gegriffen).

## Deltas 21.08.2026, Session 2 (Produkt-Erweiterung — 20-build-log)

- **T5.4 PWA — ✅ Kern gebaut** (Ticket-Gate-Dialog, Trial-Banner, Standort-Autofill + Zielsuche, Direktlinien + Abfahrten mit Ist/Soll, **Kontroll-Warnung ≥1 Haltestelle vorher**, 1-Tap-Meldung, Abo-/Ticket-Screens; `vite build` ✓). Offen darin: SignalR-Live-Anbindung, Offline-Cache Fahrplan (nur Fahrplan! B8).
- **NEU ADR-0014 umgesetzt:** 14-Tage-Trial → 2,99 €/Monat (AccessGate + 402 trial_expired); `/v1/devices/me` liefert Trial-Stand.
- **NEU T5.4c — Umstiegsverbindungen (v2):** v1 liefert bewusst nur Direktverbindungen (echter Router = eigener Milestone); Hinweis im UI vorhanden.
- **NEU T4.2b — Meldungs-/Warn-Kette live verifiziert:** Fenster-Validierung (422 für beendete Fahrten), Warnungs-ETA ab jetzt.
- Fehler-Sweep 21.08.: 12 Befunde behoben (20-build-log §2) — darunter produktkritisch #1 (tot geborene Trip-Anker).

## Deltas 21.08.2026 (nach 2. Messrunde + Rechtsrecherche — 14-j3-j6-messung / 15-rechtsstand)

- **T7.1 ist erledigt:** J3/J6 GRÜN → `cities.is_active=true` für Hamburg+Umland mit T2.3 (Stadt-Extrakt mit **Umland-BBox** 53.30–53.85 N / 9.55–10.50 E) freigegeben. Anwalt F-1 bleibt einziges Launch-Gate.
- **Launch-Reihenfolge gedreht:** PWA zuerst (T5.4 priorisiert), Google Play zweitens, **iOS zurückgestellt** (F-19) — Apple entfernt Mitte 08/2026 den Referenzfall FreiFahren. T7.5 = „Play-Einreichung + PWA-Launch-Check".
- **NEU T8.3a — Barrierefreiheits-Badges** (16-innovationen A1): Ausstattungs-Flags aus der Alert-Noise-Pipeline → Departure-Badges (🦽, „lt. Betreiber-Meldung") + Filter „nur barrierefreie Fahrten". AC: Badge-Anteil HH gemessen & dokumentiert; Filter in PWA.
- **NEU T3.4b — Attribution-Automatisierung** (A2): Attributierungs-Alerts → `attribution_services`-Tabelle → App/About + API-Header automatisch generiert. AC: Verbund-Liste im Feed ändert Attribution ohne Deploy.
- **T5.3/T5.4 + Ticket-Reminder** (A3): dezenter „Ticket prüfen"-CTA mit Deep-Link bei jeder aktiven Meldung (Copy-Guidelines 11-recht §3).
- **Dauermetriken:** T3.1 loggt `match_rate` ab Tag 1 (empirische Basis: 100,0 %); Beobachtungsmoment ist der wöchentliche Static-Refresh (Sa 04:22 UTC).
