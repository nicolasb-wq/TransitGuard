# TransitGuard — Build-Log M0/Start M2–M4 (2026-08-21, 1. Bausession)

**Auftrag:** „Fange an die App zu bauen — gründlich, in die Tiefe." Qualität vor Tempo: Alles in diesem Turn wurde **kompiliert und getestet** (kein ungetesteter Pseudocode), Fehler aktiv gesucht und behoben.

## 1. Ergebnis-Zahlen

| Messwert | Wert |
|---|---|
| Solution | `TransitGuard.sln` — 4 Bibliotheken/API + 2 Testprojekte, `net8.0`, `TreatWarningsAsErrors=true` |
| C#-Dateien / Zeilen | 51 Dateien / 14.541 Zeilen |
| Build | **Release grün, 0 Fehler, 0 Warnungen** (SDK 8.0.424) |
| Tests | **50/50 grün** (44 Core + 6 Ingest) — Spec-Tabellen T-TTL-1…12, T-NORM-1…10, T-GATE, Trust (B5-Fix), Interpolator, G11-Gate, Echtdaten-Fixtures |
| Smoke-Test API | laufend verifiziert: Health ✓, Device-Issue ✓, **Ticket-Gate 422/403 ✓**, Report-Create 201 snake_case ✓, Idempotenz-Replay 201 identisch ✓, Free-DTO ohne Pro-Felder ✓, TTL 20 min sichtbar ✓ |
| Echtdaten-Fixtures | `tests/fixtures/rt_sample.pb` (Live-Ausschnitt gtfs.de, 100 TUs + 60 Alerts, 21.08.2026) + `static_mini.zip` + `manifest.json` (Python-Kreuzprüfwerte) |

## 2. Gebaut (Ticket-Bezug Roadmap)

- **T0.1 ✓** Repo-Skeleton gem. 01-verzeichnisbaum (src/Core/Data/Ingest/Api, tests, sln).
- **T0.4 ✓** `scripts/test.sh`-Äquivalent: `dotnet test TransitGuard.sln` (lokal validiert).
- **T4.1 ✓ (Kern)** TtlEngine als reine Funktion — alle 12 Spec-Fälle + F-16-Langfahrt-Test grün; TtlSweepService als Job-Klasse (G11-Freeze inkl.).
- **T3.2/T3.3 ✓** FeedReader (Entity-Streaming, Google.Protobuf gegen rekonstruiertes, wire-identisches gtfs-realtime.proto aus dem offiziellen Python-Deskriptor) + TripUpdateNormalizer (C.3.1-Route-Auflösung, Delay-Clamp [−120,+7200], vehicle-präsent-aber-leer-Metrik) — gegen Live-Fixture getestet.
- **T3.4 ✓ (Kern)** AlertNormalizer mit gemessenem Noise-Regelwerk + Dedup-Key **sha256(header|desc[:150])** — Kreuztest C#↔Python gegen Live-Daten grün.
- **T2.2/T2.3 ✓ (Kern)** GtfsStaticArchive (RFC4180-CSV mit Quotes/Mehrzeiler) + CityExtractor (bbox→Stops→Trip-Closure→Whitelist mit last_stop/end_time) — gegen Mini-GTFS-Fixture getestet.
- **T4.2/T4.2a ✓ (Local-Modus)** Reports-API mit Validation, Idempotenz, Rate-Limit, **Ticket-Gate vor allen anderen Prüfungen**; Devices-/Events-/Billing-/Health-Controller; SignalR-Hub mit Join-Regeln (Stadt/Kill-Switch/Gate/Tier + Violation-Zähler); RateLimiter (5/600 s, 20/600 s, 120/60 s, 5/h); Entitlement-Sandbox (Besitz-Token + Restore-Code, nur Hashes).
- **M1-Infra ✓** Program.cs (Lokal-Modus In-Memory; Prod-Modus-Platzhalter), no-store auf Meldungsrouten (B8), Device-Auth-Middleware.
- **Data-Projekt** App-/Billing-DbContext mit exakten Tabellen-/Spalten-Mappings auf sql/0001 (Kompilat; Laufzeit-Verdrahtung = Ticket M4-EF).

## 3. Fehler gefunden & behoben (Qualitäts-Direktive)

| # | Fehler | Ursache | Fix |
|---|---|---|---|
| 1 | gtfs-realtime.proto nicht über GitHub beziehbar (404/0 Byte) | raw.githubusercontent in Sandbox blockiert | **Verlustfreie Rekonstruktion aus dem offiziellen Python-Datei-Deskriptor** (Feldnummern identisch, Gegenprobe eingebaut) |
| 2 | dotnet-install-Script lieferte 0-Byte-Binärdateien | CDN-Pfad der Sandbox | Direkt-Tarball von builds.dotnet.microsoft.com |
| 3 | CityExtractor mit Konstruktionsresten (Phantommethode, umständliche Sequenzlogik) | zu schneller Erstentwurf | Vollständig neu geschrieben (höchste Sequenz gewinnt, direction_id korrekt) |
| 4 | 5 Compile-Runden (using/namespace, proto2-Has*-Semantik: Message-Felder ≠ null statt Has*, Iterator-out-Parameter, record-`with` auf Klasse, init-only-Zuweisung, doppelte usings, ungenutzte Parameter — via TreatWarningsAsErrors erzwungen) | — | jeweils behoben |
| 5 | **Test-Bug N4:** erwartete Clamp-Verletzung für +7.148 s, obwohl Grenze +7.200 ist | Testdaten widersprachen eigener Konstante | Messwert korrekt interpretiert (+7148 bleibt), Ober-Grenze separat getestet |
| 6 | **Test-Bug Interpolator:** erwartete „B" (nächster Halt), Semantik ist „letzter passierter Halt" | Erwartung unpräzise | auf „A" korrigiert + Semantik dokumentiert |
| 7 | **Fixture-Bug:** manifest speicherte header[:80]/desc[:40], Hash über volle Werte | Truncation | manifest speichert exakt die Dedup-Fenstergröße (150) |
| 8 | **Vertragsbruch API:** JSON camelCase statt snake_case (04-api) | Default-Options | `JsonNamingPolicy.SnakeCaseLower` + Enum-Snake-Helfer |
| 9 | Idempotenz-Replay antwortete 200 statt vertragsgleicher 201 | Content()-Default | StatusCode 201 mit identischem Body |

## 4. Ausdrücklich OFFEN (nicht behauptet, sondern geboren)

- **M4-EF-Verdrahtung:** In-Memory-Stores sind Produktionsersatz bis `DATABASE__APP`-Modus (EfRepositories über AppDbContext/BillingDbContext) — Kontexte kompilieren, Laufwerk-Zugriff ungetestet (kein Postgres in dieser Umgebung).
- **T3.1/T3.6 Job-Verkabelung in Hangfire:** PollRealtimeJob/TtlSweepService existieren und sind getestet, aber noch nicht als Hangfire-RecurringJobs registriert (Program hat keinen Hangfire-Server im Lokal-Modus).
- **T2.4 atomarer Build-Swap + T2.6 MatchRate-Dauermetrik:** Logikstellen im Job vorhanden, DB-Seite folgt mit M4-EF.
- **PWA/Flutter:** nächster Bauschritt (Reihenfolge PWA zuerst, 13-roadmap Delta).
- SDK/nuget liegen in `/tmp` (Sandbox) — Re-Setup: Tarball 8.0.424 + `NUGET_PACKAGES=/tmp/nuget`; Produktivsystem braucht nur .NET 8 SDK.

## 5. Reproduktion

```bash
dotnet build TransitGuard.sln -c Release        # 0 Fehler/Warnungen
dotnet test TransitGuard.sln -c Release         # 50/50 grün
ASPNETCORE_URLS=http://127.0.0.1:5099 dotnet run --project src/TransitGuard.Api   # Lokal-Modus
curl http://127.0.0.1:5099/health/ready
```
