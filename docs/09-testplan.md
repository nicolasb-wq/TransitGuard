# TransitGuard — Testplan (Module, Negativfälle, Missbrauchsszenarien)

**Strategie:** Domänenkern (`TransitGuard.Core`: TtlEngine, Normalizer, TrustEngine, CityFilter) als reine Funktionen tabellengetrieben xUnit; Integrationsklassen gegen Test-Postgres (docker-compose.dev) mit Fixtures aus **echten, aufgezeichneten Feed-Auszügen** (verifikation/*.py liefern Erzeuger); E2E-Smoke auf Stage-VM. Kein CI-Dienst (Constraint) — `scripts/test.sh` läuft lokal/auf Zielserver pre-Deploy (Deployment-Doc §6). Coverage-Ziel Domänenkern: ≥ 85 % Zeilen (*Zielmarke, kein Dogma*).

## T-TTL — TTL-Engine (höchste Kritikalität; jede Zeile = 1 Testfall-Eingabe)

| ID | Fall | Erwartung |
|---|---|---|
| T-TTL-1 | Stations-Anker Schiene, keine Events | expires = created+1200 s |
| T-TTL-2 | 4 Bestätigungen | +1200 s (4×300), nicht über +900-Deckel… (Deckel: max +900 gesamt) → created+1200+900 |
| T-TTL-3 | Widerspruch mit Parität (Actor-Trust ≥ Reporter) | expires = now+120 s |
| T-TTL-4 | Widerspruch ohne Parität, 1× | unverändert; contradictions=1, Review-Flag |
| T-TTL-5 | 2 Widersprüche ohne Parität | expires = now+120 s (B6-Regel) |
| T-TTL-6 | Cap: Station-Anker Bus mit 6 Bestätigungen | ≤ cap 1800 s |
| T-TTL-7 | Trip-Anker erreicht trip_end+120 s (Interpolation) | Original beendet + derived_terminus (600 s, trust×0,7, Kennzeichnung) |
| T-TTL-8 | RT meldet CANCELED | expires = now; KEIN derived |
| T-TTL-9 | trip_id verschwindet aus Static-Swap | Degradation auf Station, Rest-TTL erhalten |
| T-TTL-10 | **G11-Gate: Feed ungesund + trip_end erreicht** | KEINE Degradation; freeze; Banner-Datenpunkt (B1) |
| T-TTL-11 | at_stop/derived Profile | 480/600 s |
| T-TTL-12 | Uhr-Sprung (now−10 min vs. Ereignis) | deterministisch (Serverzeit autoritativ; Events mit Zukunftszeit → ignorieren+Metrik) |

## T-NORM — Normalisierung

| ID | Fall | Erwartung |
|---|---|---|
| T-NORM-1 | TU ohne route_id (gtfs.de, 100 % der Realität) | Lookup liefert route_id aus Whitelist |
| T-NORM-2 | Lookup-Miss | Entity verworfen; route_misses++ |
| T-NORM-3 | TU mit route_id (VBB-Muster) | direkt übernommen |
| T-NORM-4 | Delay −30.384 s / +7.148 s (gemessen!) | clamp auf [−120,+7200]; delay_clamped++ |
| T-NORM-5 | `vehicle`-Feld präsent aber leer (gemessen 8,4 %) | Feld „leer" ≠ „fehlt": Metrik `vehicle_field_empty`++, keine Fahrzeug-Anker-Erzeugung |
| T-NORM-6 | Alert: „Echtzeitdaten aufbereitet von GTFS.de …" | is_noise, nie UI |
| T-NORM-7 | Alert „Niederflur"/„Bordrestaurant"/… (Regeltabelle) | is_noise |
| T-NORM-8 | 92.460 identische Empty-Header-Alerts | 1 dedup-Zeile, entity_ids-Zähler korrekt |
| T-NORM-9 | Echter Bau-Alert (Stuttgart-Muster aus Messung) | sichtbar, Stadt-Zuordnung via informed_entity.trip_id |
| T-NORM-10 | Alert ohne auflösbaren Trip + ohne Stop | Stadt „unbekannt" → verworfen (keine Fehlanzeige) |

## T-GATE — Ticket-First-Gate (rechtliche Pflicht, 18-rechtliche-haertung)

| ID | Fall | Erwartung |
|---|---|---|
| T-GATE-1 | POST /v1/reports ohne `client.ticket_confirmed` | 422 `ticket_confirmation_required`, kein Report, Tagesaggregat `gate.blocks` +1 |
| T-GATE-2 | GET reports ohne `X-Ticket-Confirmed: true` | 403 `ticket_gate_blocked`, keine Report-Daten im Body |
| T-GATE-3 | Hub-Join reports.* ohne Gate-Claim | Hub-Fehler + `control.ticket_gate`, keine Gruppen-Mitgliedschaft |
| T-GATE-4 | Bestätigung gesetzt | Normaler Ablauf; `gate.confirmations` +1 (nur Tagesaggregat, kein Nutzereintrag) |
| T-GATE-5 | Gate-Metrik-Datenminimalität | Keine ticketbezogenen Pro-Device-Daten in API/DB (DSGVO-Kontrakt) |
| T-GATE-6 | Bypass per Header-Fälschung (weiches Gate) | Dokumentiert + gezählt; kein harter Block (Age-Gate-Muster); E2E prüft Standard-Pfad |

## T-ING — Ingest-Jobs

ETag-Handling (200 neu / 304 / weak ETag Wechsel), Circuit-Breaker nach 3 Fehlern, Protobuf-Müll (Random-Bytes) → sauberer Fehler, überdimensionierter Feed (>200 MB) → Abbruch (Feed-Vergiftung), Delta-Berechnung (nur Änderungen → Event-Zähler), Static-Swap-Transaktion (Rollback bei Validierungsfehler), Whitelist-Kaltstart aus DB, Match-Rate-Schwellen triggern Re-Sync-Job, G11-Health-Bewertung (Wochentagsmittel-Fenster).

## T-API — REST

Idempotency-Key-Replay (gleiche ID, gleiche Antwort, kein Duplikat), Rate-Limits (429+Retry-After), Validation 422-Fälle (fremde Station, Trip außerhalb Fenster, inspector_count=9), Kill-Switch: reports_enabled=false → 404 feature_disabled auf POST+GET, city inactive → RLS macht Zeilen unsichtbar (nicht 403 — Existenz nicht leaken), Device-Selbstlöschung: devices/trust weg, reporter_device_id=NULL, Events behalten, `Cache-Control: no-store` auf allen Meldungs-Endpunkten (B8).

## T-EN — Entitlement-Durchsetzung (Paywall-Vertrag)

| ID | Fall | Erwartung |
|---|---|---|
| T-EN-1 | Free-GET /v1/reports | JSON enthält **physisch nicht** inspector_count/reporter_trust/movement (String-Assert auf Roh-JSON) |
| T-EN-2 | Free-Client join pro-Gruppe | Hub-Fehler + Metrik |
| T-EN-3 | Receipt-Replay | 409 receipt_already_used |
| T-EN-4 | Restore-Brute-Force 6. Versuche | 403 + Sperre (5/h) |
| T-EN-5 | 4. gleichzeitige Verbindung mit gleichem Token | älteste gekickt (token_kick) |
| T-EN-6 | Abo abgelaufen (validate) | Tier→free; Pro-Gruppen-Membership beendet |
| T-EN-7 | tg_app-SELECT auf entitlements | Permission denied (RLS+GRANT) |
| T-EN-8 | Kill-Switch unter Pro-Nutzern | Hub control.feature_disabled; REST 404 — gleiche Behandlung wie Free |

## T-TRUST — Vertrauen & Anti-Missbrauch

Spam-Burst 6 Meldungen/10 min → 429 + score−20; Troll-Widerspruch ohne Parität → kein TTL-Effekt; Anomalie: Gerät meldet ausschließlich an Stationen mit bekannten Anker-Teams → Score-Dämpfung; Trust<20 → Meldung unsichtbar bis 1. Bestätigung; GPS-Spoof (60 km Sprung in 30 s) → Plausibilitäts-Ablehnung; Rang-Übergänge korrekt (fixt v0.1-Bug B5).

## T-HUB — Realtime

Resync nach Lücke > Ring-Buffer; Reconnect tritt Gruppen wieder bei; doppelte Event-IDs nie ausgeliefert (Monotonie); Free/Pro-Payload-Divergenz für dasselbe Event; Freeze-Event bei G11-Gate.

## T-DATA — Daten & Betrieb

Partition-Erzeugung voraus (+14 d), Retention-Drop 90 d, Backup-Restore-Drill (monatlich, bereitgestellter Dump startet gegen Test-DB), Migration Up/Down auf Kopie, device-Löschung über alle Partitionen.

## T-E2E — Szenarien auf Stage (manuell, Checkliste)

1. Melden → Karte sieht Meldung → Bestätigung verlängert → Ablauf verschwindet. 2. Trip-Meldung aus Abfahrtsliste → interpolierte Mitwanderung (nur Pro-Client sichtbar movement) → Trip-Ende → „Verbleib unbekannt"-Meldung 10 min. 3. Kill-Switch umschalten → beide Clients sehen Fahrplan weiter, Kontroll-UI weg, ohne Update. 4. Feed-Stop simulieren (URL auf Dummy) → nach 15 min freeze-Banner, Karte bleibt. 5. Kauf-Sandbox: activate → Pro-Sicht → refund → Free-Rückfall.
