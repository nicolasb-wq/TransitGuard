# TransitGuard — Build-Log 3 (21.08.2026, 3. Bausession): Empfehlungsliste abgearbeitet

**Auftrag:** „Arbeite entsprechend deiner Empfehlungen weiter und baue die App weiter." Abgearbeitet in Prioritätsfolge der eigenen Empfehlung (20-build-log §3). Alles kompiliert, getestet und — wo möglich — **live bewiesen**.

## 1. Umsetzungen der Empfehlungen

### ① Hangfire-Registrierung — ✅ umgesetzt UND bewiesen
`JobRegistration` (Api/Jobs): RecurringJobs **ttl-sweep** (15 s), **rt-poll** (60 s, nur `Ingest:Enabled`), **partition-maint** (täglich 04:00), **static-sync** (Di/Sa 03:00); Dashboard `/admin/hangfire` (Prod: hinter Caddy-Basic-Auth). MemoryStorage lokal, Prod-Empfehlung Postgres-Storage (M6). `FeedHealthProvider` verkoppelt Poll→Sweep (G11-Gate läuft mit).
**Beweis (Live-Smoke mit `TTL__DEVOVERRIDESECONDS=3`):** Meldung → `expires_at = +3 s` → nach 25 s aktive Liste **leer**, Event-Log `['created','expired']` → der 15-s-Job hat tatsächlich gesweept. Startup-Warnt laut, falls der Dev-Override gesetzt ist.

### ② SignalR-Live in der PWA — ✅ umgesetzt UND end-to-end bewiesen
PWA verbindet beim Start (nur mit Ticket-Bestätigung), tritt `city.hamburg.reports.free` mit Gate-Claim bei, Live-Panel zeigt neue Kontrollhinweise in Echtzeit; Reconnect-Strategie [0,2,5,10,30] s wie docs/06 §3.
**Beweis:** `verifikation/signalr_e2e.mjs` (Node-Client): WebSocket ✅, Join ✅, **Negativprobe** (Pro-Gruppe ohne Gate abgelehnt ✅), REST-Meldung → Live-Event `report.created` empfangen ✅ → **PASS**.

### ③ M4-EF-Verdrahtung — ✅ umgesetzt (compile-geprüft; Laufzeit braucht PG-Host)
`Data/EfRepositories.cs` + `EfServiceCollectionExtensions`: ReportRepository, EventLog, FeatureFlags, TrustStore (+Api-Adapter), DeviceAuth (Hash-Storage), DeviceRegistry (Trial-Start = `devices.created_at` — keine Zusatztabelle), TripStateStore (`rt_trip_state`), rt-Row-Mapping im AppDbContext. Program.cs-Schalter: `Data:Provider=postgres` + `DATABASE__APP/BILLING` → EF-Stores; sonst In-Memory. **Kein Postgres in der Sandbox** — Laufzeit-Verifikation auf Zielsystem (`docker compose -f docker-compose.dev.yml up -d && ./scripts/migrate.sh local`), ehrlich als Restpunkt notiert.

### ④ Umstiegsverbindungen v2 — ✅ umgesetzt + 4 Tests
`TransferRouter` (Core): 1 Umstieg, Abfahrtsfenster 90 min, Umstiegpuffer 120 s, Bein-Deckel (45 min, Top 400 Abfahrten), beste Gesamtankunft, Gruppierung je Linienkombination. API: `journeys/search` liefert jetzt `transfer_connections` (nur wenn < 3 Direkte — Rausch-Vermeidung); PWA rendert „🔁 Mit 1 Umstieg" mit Gesamtdauer/Wartezeit.

### ⑤ Weiterer Fehlersweep — 7 Befunde (Nr. 1–3 produktrelevant)

| # | Fund | Fix |
|---|---|---|
| 1 | **Start-Crash:** `TtlSweepService`/`PollRealtimeJob` nie registriert → jede API-Instanz starb sofort (alle „HTTP 000" des Smoke waren das Symptom) | Registrierung als Scoped (Hangfire-Scope je Ausführung) |
| 2 | **Selbstverschuldeter DI-Verlust:** mein Zeilen-Deletionsschritt hatte auch die Neu-Registrierungen im else-Zweig des Provider-Schalters gelöscht → `IReportRepository` unlösbar | else-Zweig repariert |
| 3 | **`TtlEngine` zweimal gebaut:** Controller nutzte `new TtlEngine()` statt der Options-tragenden DI-Instanz → Dev-Override/TTL-Config hätte in Produktion NIE gegriffen | Controller auf DI-Instanz umgestellt |
| 4 | EF-Modus: Trust-Mutationen an gelösten Objekten wären **verloren** gegangen (In-Memory-Referenzsemantik schlich sich in die API-Verträge) | `ITrustStore.Save()` eingeführt, Controller speichert explizit; In-Memory = No-Op |
| 5 | `IDeviceRegistry` lag in der Api → Zirkelbezug Data→Api | Interface nach Core/Abstractions verschoben |
| 6 | Warnungs-Serialisierung in `journeys/search` war indirekt & fragil (Rückles-Schleife über c.NextDepartures) | sauberer `DepartureDto`-Former; departures-Endpunkt bekam Warnings-Feld gleich mit |
| 7 | Werkzeugkasten: `pkill -f` erschoss die eigene Shell (Match auf Kommandozeile); `eval` ohne `export` verschluckte Env-Overrides; doppelte `useState`-Importe in App.tsx | PID-Datei-Runner (`/tmp/run_api.sh`), `export`, Bereinigung |

## 2. Stand

- **71/71 Tests grün** (65 Core inkl. 4 neuer TransferRouter-Tests + 6 Ingest), Build 0 Fehler/0 Warnungen.
- **PWA** baut (63,7 KB gzip, +SignalR) mit Live-Feed und Umstiegs-Anzeige.
- **Live bewiesen in dieser Session:** Realtime-Kette (WebSocket→Gate→Event), Hangfire-Sweep-Kette (Cron→Engine→Store→Event), Gate-Negativproben, Trial-Warnung.
- **Neue Echtdaten-Nutzung:** nichts erfunden — jeder Beweis lief gegen die laufende API.

## 3. Restliste (nächste Sessions, Priorität)

1. **PG-Laufzeit:** EF-Stores + Migrationen gegen echtes Postgres (docker-compose.dev), inkl. RLS-Rollen-Test (T-EN-7) und Partition-Job.
2. **StaticSyncJob vollständig** (T2.4: Download→Staging→atomarer Swap→Whitelist/Schedules/Stores befüllen — CityExtractor als Bibliothek steht fertig).
3. Flutter-App (Play-Kanal) — nach PWA-Stabilisierung; Payment-Anbindung F-12/J5 (extern).
4. Ops: Alarmierung (Ingest §8) an E-Mail-SMTP; `Ingest:Enabled`-Konfiguration für Prod-Deploy.
