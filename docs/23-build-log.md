# TransitGuard — Build-Log 5 (21.08.2026, 5. Bausession): Zielsystem-Vorbereitung + Flutter-Start

**Auftrag:** Echtdaten-Ingest auf Zielsystem vorbereiten (Deploy-Doc + Runbook als konkrete Checkliste) und Flutter-App starten.

## 1. Deploy & Runbook — konkret und teilweise PROBEgelaufen
- **`deploy/RUNBOOK-SERVER.md`**: 6 Phasen von Null auf „Echtdaten-Ingest läuft", jede mit echten Befehlen + erwarteten Ausgaben: Server-Basis (SSH-Härtung, PGDG/PostgreSQL 16 + PostGIS via apt), DB/Rollen/RLS-Schnelltest, App-Deploy, Ops-Grundausstattung, **Phase 5 Erst-Import** (static-sync VOR rt-poll — Pflichtreihenfolge; Plausibilitätsfenster aus den J3-Messwerten: Stops 5–20k, Trips 20–80k, match_rate ≥ 0,98, city_trips mittags 4–6k; Abbruchkriterium „Whitelist 0"), Abnahme, Rollback-Generalprobe, Fehlerbilder-Erste-Hilfe.
- **Skripte produktionsreif ergänzt:** `scripts/deploy.sh` (Test-Gate → self-contained Publish → rsync → Migrationen → Symlink-Swap → Health → AUTO-ROLLBACK), `backup.sh` (pg_dump + GPG + Rotation + Storage-Box), `restore.sh` (Drill), `acceptance.sh` (5-Punkte-Abnahme).
- **Probedurchlauf in Sandbox:** alle Skripte `bash -n` sauber; `acceptance.sh` gegen laufende API → **5/5 ✔** (nach 2 Fixes, s. §3).

## 2. Flutter-App (Play-Kanal) — gestartet
- `app/` vollständiges Flutter-Projekt: pubspec (http, geolocator, shared_preferences — keine Konten, Standort nur on-device), `lib/api.dart` (Vertrags-Client snake_case, Trial/402-Behandlung, Ticket-Gate-Header), `lib/main.dart` (Home: Standort-Autofill, Ziel-Suche, Linien + Abfahrten mit Ist/Soll, **Warnungs-Banner „Achtung Kontrolle – halte deine Fahrkarte bereit"**, 1-Tap-Meldung mit Gate-Erzwingung, Trial-Anzeige, Attribution-Footer), `test/api_test.dart` (Vertrags-DTO-Tests + ADR-0014-Konstanten), Android-Manifest (INTERNET + Fine/Coarse Location; bewusst KEIN Hintergrund-Standort), `deploy/PLAY-CHECKLISTE.md` (Store-Reihenfolge inkl. Rechts-Gate vor Einreichung, Data-Safety, Review-Notes-Verweis).
- **Ehrlich:** kein Flutter/Dart-SDK in der Sandbox (`~700 MB–1 GB`, tmpfs voll) → **kein Kompilier-Beweis hier**; `flutter analyze`/`flutter test`/`build apk` sind erste Aktion auf der Dev-Maschine (Play-Checkliste verankert das als Gate). Logik ist 1:1 die live bewiesene PWA-Kette.

## 3. Fehler-Sweep (2 Funde, beide vom Probelauf)
| # | Fund | Fix |
|---|---|---|
| 1 | `uuidgen` fehlt auf Minimal-Systemen → acceptance-Check brach ab | `/proc/sys/kernel/random/uuid` (portabler) |
| 2 | **Vertragsverstoß Gate-Reihenfolge:** Idempotency-Key-Prüfung lief VOR dem Ticket-Gate (docs/18 §1.2: „vor allen anderen Prüfungen") | Controller-Reihenfolge gedreht; acceptance bestätigt gate-422 jetzt auch ohne Idempotency-Key |

## 4. Stand
Backend 73/73 Tests grün (nach Gate-Fix erneut verifiziert), Build 0/0 · Acceptance 5/5 live ✔ · Ops-Paket (Runbook + 4 Skripte + Play-Checkliste) komplett.
**Nächste Schritte:** (a) Runbook auf dem echten Hetzner-Ziel durchexerzieren (Phase 1–6) inkl. Echtdaten-Erstimport; (b) Flutter auf Dev-Maschine kompilieren + Play-Track; (c) danach: SMTP-Alarmierung, Hangfire-Postgres-Storage, T2.4-DB-Teil.
