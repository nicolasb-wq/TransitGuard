# TransitGuard — Ingest-Dokument (Jobs, Idempotenz, Fehler, Metriken, Alarme)

**Bezug:** v1.1 Abschnitt I + Bestandsaufnahme N1–N8, B1–B3. Zahlen mit Konfidenz-Marker.

## 1. Job-Inventar (Hangfire, Queue `ingest`; UTC)

| Job | Schedule | Idempotenz | Retry | Bemerkung |
|---|---|---|---|---|
| `PollRealtimeJob` | `*/60 * * * * *` (Cron min-basiert: jede Minute) | ja (Delta gegen Snapshot; NT-Monotonie) | 3× exp. Backoff 10/30/90 s | 1 Feed (gtfs.de), `If-None-Match`-Senden, 30-s-Timeout |
| `StaticSyncJob` | 2×/Woche (Di/Sa 03:30) + on-demand | ja (build_id-Staging) | 2× / alarmiere | ZIP-Download, validieren, Stadt-Extrakt, Swap |
| `CityExtractJob` | nach StaticSync | ja (trägegt build_id) | 3× | Whitelist (In-Mem) neu aufbauen |
| `AlertSweepJob` | 5 min | Upsert nach `alert_key` | 3× | Sichtbarkeit-Fenster 6 h ohne Sichtung ⇒ UI-Ende |
| `TtlSweepJob` | 15 s | Zustandsübergänge idempotent | 3× | Expiry, B.5-Derivation, CANCELED, Swap-Degradation |
| `TrustRecalcJob` | 1 h | deterministisch aus Events | 2× | Score/Rang, Missbrauchsanomalien |
| `PartitionMaintJob` | täglich 04:00 | ja | 2× | `tg_ensure_report_partitions(14)`, `tg_drop_old_report_partitions(90)` |
| `FeedHealthJob` | 5 min | ja | 1× | G11-Gate-Bewertung, Alerting |
| `MatchRateWatchJob` | nach jedem Poll (inline) | — | — | G10-Detektor, schreibt `ingest_metrics.match_rate` |

## 2. PollRealtimeJob — Ablauf (sequenziell, Budget < 5 s Parse *Unsicher*, wird gemessen)

1. `GET` mit `If-None-Match: <etag>`, `User-Agent: TransitGuard/1.0 (+kontakt@domain)` (Anbieter hat keine Policy; höflich sein).
2. `304` → nur Metrikzeile, Ende. *Bestätigt:* realistisch nur bei langsamen Feeds (VBB: 304 2× gemessen; gtfs.de regeneriert alle 10 s → nie).
3. `200` → **Entity-Streaming-Parse** (`CodedInputStream`, Entities einzeln verarbeiten/freigeben; kein vollständiger Objektbaum — Feed 33–48 MB je Tageszeit, Peak-mittags ~48 MB / ~180 k Entities gemessen).
4. TU-Pfad: trip_id+start_date → Stadt-Whitelist (In-Mem; Postgres-PK-Fallback bei Kaltstart). Hit → route_id übernehmen (VBB-Muster) **oder** Lookup `gtfs_trips` (gtfs.de: 100 % leer — Bestätigt). Miss → verwerfen + `route_misses++`. Delay clampen `[-120, +7200] s`, Verletzungen zählen (`delay_clamped`, B2). Delta vs. `rt_trip_state` → nur Änderungen weiterreichen (Hub + DB).
5. Alert-Pfad: Noise-Regeln (Tabelle `alert_noise_rules`, gemessene Muster) → `is_noise`-Alerts nur als Zähler führen (Lizenz-Namensnennung bleibt unangetastet gespeichert? **Nein** — Attribution wird als Metadatum im Attribution-Service geführt, nicht pro Trip). Dedup `sha256(header|desc[:150])` → `gtfs_alerts`-Upsert, `entity_ids++`, `last_seen_at=now`.
6. Metrikzeile schreiben; `FeedHealthJob`-Daten aktualisieren.

**Fehlverhalten:** HTTP 4xx/5xx, Timeout, Protobuf-Parsefehler, Feed-Alter > 5 min ⇒ Zähler `consecutive_failures`; ≥3 ⇒ Circuit Breaker (Poll pausiert 10 min, Alarm). **Feed-Vergiftung** (plötzlich riesige Entities/Payloads > 200 MB) ⇒ Abbruch + Alarm (Testplan T-IN-5).

## 3. StaticSyncJob — atomarer Build-Swap (C.3.2)

1. ZIP laden (gtfs.de deutschlandweit; SHA256 notieren), entpacken in Temp (Platzbedarf DE-weit *Unsicher*, vermutlich niedriger einstelliger GB-Bereich — auf VM prüfen).
2. `gtfs_builds`-Zeile `staged` anlegen; Stadt-Extrakt einlesen: alle Stops in `cities.bbox` → alle Trips, die diese Stops berühren → deren Routes + StopTimes (Closure über `gtfs_stop_times`). Nur diese Teilmenge landet in `gtfs_*` (build_id-gebunden).
3. Validierung: Zeilenzahlen > 0, Stichproben-Join RT-trip_id ∈ Static (Vorab-`match_rate`-Messung = **J6-Aufgabe**, einmalig vor Stadt-Commitment).
4. Swap in EINER Transaktion: neuer Build `active`, alter `retired`. Kein In-Place-Update.
5. `CityExtractJob`: Whitelist (In-Mem-Dictionary `trip_id → (route_id, direction_id, last_stop_id, end_time_s)`) neu aufbauen; Kaltstart-Fallback bleibt Postgres-PK.
6. **Degradation-Regel (C.3.2 Nr. 3):** aktive Trip-Anker-Meldungen gegen neuen Build prüfen; trip_id verschwunden ⇒ Meldung → Stations-Anker (letzte interpolierte Station, Rest-TTL). Keine Meldung stirbt.

## 4. G10 — trip_id-Drift (Risiko bleibt real)

Anbieter-Behauptung „RT passt zu allen statischen Feeds" ist inzwischen **empirisch bestätigt** (J6, 21.08.2026: 100,0 % Match gegen 6 Tage alten Static, 14-j3-j6-messung §1). trip_ids sind reine Numerics ohne Route-Präfix. Detektor bleibt: `match_rate` pro Poll, Beobachtungsmoment = wöchentlicher Static-Refresh (Sa 04:22 UTC). Schwellen (0004): < 0,90 warnen, < 0,70 kritisch ⇒ automatischer Re-Sync + Ops-Alarm. Anzeige im Ops-Dashboard (Hangfire-Metrikpanel).

## 5. G11 — Feed-Degradation (VBB-Fall generalisiert)

`FeedHealthJob` vergleicht je Poll: Entity-Anzahl vs. gleitendes Wochentags-Mittel (7 Tage), Feed-Alter, HTTP-Status. **Krank** = Alter > 300 s ∨ Entities < 50 % des Mittels ∨ 3 konsekutive Fehler.
**Gesundheits-Gate (neu, B1):** Trip-Anker-Degradation (Trip-Ende/Verschwund/CANCELED-Massenereignis) wird nur ausgeführt, wenn der Feed als gesund gilt. Sonst: Meldungen einfrieren (`metadata.frozen=timestamp`), Banner „Echtzeitdaten gestört — Meldungen eingefroren", UI-Fallback „Laut Fahrplan". Damit kann ein gtfs.de-Zwischenfall (à la VBB-Lücke seit 2026-06-04, *Bestätigt* lt. Statusseite) nicht die Karte leeren.

## 6. Idempotenz & At-Least-Once

Polls sind zustandslos-idempotent (Delta-Berechnung), Jobs deduplizieren über natürliche Schlüssel (`alert_key`, `(city_id,trip_id,start_date)`), Meldungs-Erstellung über Client-Idempotency-Key (API-Doc §4). Hangfire `DisableConcurrentExecution` pro Job-Klasse; Doppel-Dispatch harmlos durch Zustandsmaschinen (Reports: Übergänge nur vorwärts).

## 7. Alert-Pipeline — gemessene Realität (N1)

Roh (21.08.2026, Mittag): ~96,5 k Alert-Entities = 519 distinct Inhalte; 64.246× DELFI-Attribution, 12.900× Bremen-Attribution, ~8,9 k weitere Verbund-Attributionen, 4,3 k Ausstattungsnotizen „Niederflur/Rollstuhlgeeignet", Rest echte (meist langlaufende) Bau-/Störungshinweise; `cause/effect` überwiegend UNKNOWN.
Pipeline: Noise-Klassifikation (DB-Tabelle, hot-fixbar) → Dedup → Stadt-Zuordnung über informed_entity.trip_id (Whitelist) → UI-Fenster `last_seen_at + 6 h`. Erwartung HBV-Anteil Hamburg: **nahe null** (*Indiz*: kein HVV-Attributionsmarker; TU-seitig misst J3). Konsequenz für Produkt: Störungen sind Komfort-Nebenfeature, kein Stand-Alone-Wert (Änderung ggü. v1.1 §4 — Begründung in Bestandsaufnahme A1).

## 8. Health-Metriken & Alarme (Kanäle: Ops-E-Mail + Hangfire-Dashboard; FCM-Admin später)

| Metrik | Quelle | Warn | Kritisch |
|---|---|---|---|
| `feed_age_s` | Poll | > 300 | > 900 |
| `entities` vs. Wochentagsmittel | FeedHealth | < 70 % | < 50 % (Gate!) |
| `match_rate` | Poll | < 0,90 | < 0,70 → Re-Sync |
| `route_misses` (absolut je Poll) | Poll | > 5 % der Stadt-TUs | > 20 % |
| `parse_ms` | Poll | > 2× 7-Tage-p95 | > 5× p95 |
| `consecutive_failures` | Poll | 3 | 6 |
| `delay_clamped`-Anteil | Poll | > 2 % | > 10 % (Datenqualität) |
| `ingest_lag_s` (Poll-Dauer-Überschreitung) | Job | > 60 | > 180 |

## 9. Verhalten bei eigenem Ausfall

App-Neustart: Whitelist aus `gtfs_builds(active)` rekonstruierbar, `rt_trip_state` ist DB-Spiegel ⇒ kein Zustandsverlust über Neustart; erste Polls nach Kaltstart nutzen Postgres-Fallback (C.3.1 Ebene 2). DB-Ausfall: Poll pausiert (Circuit), Clients → „Laut Fahrplan".
