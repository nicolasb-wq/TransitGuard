# TransitGuard — Build-Log 2 (21.08.2026, 2. Bausession): Produkt-Erweiterung + Tiefen-Sweep

**Auftrag:** Trial/Abo 2,99 €, Fahrtensuche Start→Ziel mit Standort-Autofill, Kontroll-Warnung ≥1 Haltestelle vorher, „extrem starke Verbesserungen", Weiterbau, extrem gründliche Fehlersuche in der gesamten Struktur.

## 1. Neue Produktfunktionen (alle kompiliert + getestet + live verifiziert)

| Funktion | Umsetzung | Verifikation |
|---|---|---|
| **14 Tage Trial → 2,99 €/Monat** (ADR-0014) | `TrialPolicy` + `AccessGate` (Trial/Subscriber/Locked, 402 `trial_expired`); Preis als Konstante; `/v1/devices/me` liefert `access`, `trial_days_remaining`, Preis | TrialPolicyTests (4) + Live: Trial aktiv, Lock-402-Pfad implementiert |
| **Fahrtensuche Start→Ziel** | `JourneyService.FindDirectConnections` (Direktverbindungen über Trip-Schedules, Richtungskorrektur, Abfahrten gruppiert je Linie); CityExtractor liefert jetzt Stops + Fahrpläne | 5 Journey-Tests + Live: HHA1→HHA4 = R_U1, Auto-Start per lat/lon → HHA1 |
| **Standort-Autofill** | `GET /v1/cities/{slug}/stops/nearby` mit lat/lon (Haversine) **und** Namenssuche `?q=` fürs Ziel; PWA-Button 📍 Standort | Live: Jungfernstieg-Koordinaten → sortierte Nearby-Liste mit km |
| **Kontroll-Warnung ≥1 Haltestelle vorher** | `WarningsForJourney`: aktive Meldungen auf gleicher Fahrt/Linie+Richtung, betroffener Halt nach Einstieg, **ETA in Sekunden ab jetzt**; API `POST /v1/journeys/search` reichert Abfahrten mit Warnungen an + `GET /v1/journeys/{trip}/warnings` | Live: Meldung auf T_HH_3 @HHA2 → Warnung „Achtung Kontrolle – halte deine Fahrkarte bereit", HHA2, ETA ✓; Gegenrichtung/nach Einstieg korrekt gefiltert (Tests) |
| **PWA (Launch-Kanal 1)** | `web/`: Vite+React+TS (strict), Ticket-Gate-Dialog (24 h), Trial-Banner, Standort/Ziel-Suche, Linien+Abfahrten+Warnungs-Banner, Kontroll-Meldung mit einem Tap, Abo-Screen (Receipt-Sandbox + Restore), Ticket-Kauf-Screen (hvv) | `tsc -b && vite build` ✓ (49 KB gzip), API-Endpunkte alle live gegen Probe geliefert |
| **Abfahrtsliste mit Ist/Soll** | `DeparturesFrom` (Vergangenheits-Filter 60 s, Delay-Merge aus RT-State, realtime-Flag) | 3 Tests + Live |

## 2. Fehler-Sweep (Tiefe + Breite) — 12 Befunde, alle behoben

| # | Fund (Ort) | Art | Fix |
|---|---|---|---|
| 1 | **Trip-Anker für beendete Fahrten wurden „tot geboren"** (ExpiresAt in Vergangenheit; Fenster-Validierung war nur dokumentiert) | **Logikfehler, produktkritisch** | API lehnt ab: 422 `trip_not_resolvable` wenn `trip_end+120 s ≤ now` — Live bewiesen (T_HH_1 → 422, T_HH_3 → 201) |
| 2 | Hub lehnte `city.{slug}.alerts`-Gruppen ab (Parser verlangte 4 Segmente; docs/06 definieren 3) | **Vertragsfehler** | `HubGroupRule` als getestete Kernlogik (7 Fälle); Hub nutzt sie |
| 3 | Hub-Regel akzeptierte leere Slugs (`city..alerts`) | Validierungslücke | `IsNullOrEmpty`-Prüfe + Test |
| 4 | `UserEtaSeconds` war Sekunden-des-Tages statt Sekunden-ab-jetzt | **Semantikfehler** | Umgestellt auf `(Ankunft − now)`; Test fixiert 600 s |
| 5 | `ReportEvent.EventId` wurde nie ins Objekt geschrieben (nur zurückgegeben) | Inkonsistenz | Append setzt ID; Property settable |
| 6 | `DevicesController.Delete` enthielt No-op-Zeile (`r.Status = r.Status`) | Code-Geruch | entfernt; Anonymisierung der Persistenzschicht dokumentiert (T-API/M4) |
| 7 | HttpClient ohne Timeout (Default 100 s; Spec: 30 s, docs/07 §2) | Spec-Abweichung | `c.Timeout = 30 s` bei Registrierung |
| 8 | `AddSignalR` doppelt registriert | Konfig-Fehler | bereinigt |
| 9 | Api-Projekt kopierte Fixtures nicht → Laufzeit ohne Stadt-Daten (leere Nearby-/Suchergebnisse im Smoke) | Build-Fehler | csproj-Copy `fixtures/`; Smoke-Prozess-Regel: **Api nach Fixture-Änderungen explizit neu bauen** (dotnet test baut nicht die ganze Solution) |
| 10 | Unbenutzte Felder/Parameter (`_cityScope`, `dispatcher` im PollJob, `access`-Namenskollision) | Warnhygiene | entfernt/umbenannt |
| 11 | PWA: `platform: web` wurde vom Billing abgelehnt | Schnittstellen-Mismatch | Backend akzeptiert `web` (PWA-Direktabo; Zahlungsdienstleister = M8) |
| 12 | Test-Suite eigene Fehler: 4 falsche Erwartungen (T3-Endhalt-in-A als Abfahrt übersehen ×2, widersprüchliche Headsign-Zeilen, ETA-Einheit) | Testfehler | korrigiert — die Implementierung hatte jeweils recht, außer wo nicht (vgl. #2–#4) |

**Breiten-Checks zusätzlich:** `TreatWarningsAsErrors` überall scharf (0 Warnungen); OpenAPI/grep-Konsistenz der Fehlercodes; statischer Blick auf Rennen in RateLimiter/Stores (in-memory MVP: dokumentiert, Single-Thread-Annahme pro Request-Pipeline unproblematisch für Dev-Modus; Prod = EF/PG mit Row-Locks, M4).

## 3. Stand nach dieser Session

- **Backend:** 67/67 Tests grün (61 Core + 6 Ingest), Build 0 Fehler/Warnungen; API lokal komplett erfahrbar (Trial, Gate, Suche, Warnungen, Meldungen, Abos, Alerts-Endpoint).
- **PWA:** `web/` vollständig gebaut (`npm run build` ✓). Start gegen lokale API: `ASPNETCORE_URLS=http://localhost:5099 dotnet run --project src/TransitGuard.Api` + `npm --prefix web run dev` (Dev-Proxy /v1 → 5099).
- **Offen (nächste Sessions):** M4-EF-Verdrahtung (PG-Modus), Hangfire-Registrierung, Umstiegsverbindungen (v2 — bewusst: echter Router), SignalR-Live-Updates in der PWA, Flutter, TtlSweep-Takt, echte Payment-Anbindung (F-12/J5).

## 4. Qualitätsschleife (bewusst durchgeführt)

Jede Änderung lief durch: Schreiben → Build → Test → **Live-Smoke** → Befund-Analyse (Code-Schuld vs. Test-Schuld sauber getrennt, siehe #12) → Fix → erneuter Voll-Lauf. Der wichtigste Fang (#1) stammt aus dem Live-Smoke, nicht aus dem Compiler — genau der Grund, warum jeder Strang live geprüft wurde.
