# TransitGuard — SignalR-Hub-Vertrag (Realtime)

**Hub:** `/hubs/v1/realtime` (WebSocket; Fallback SSE/LongPoll erlaubt). Auth: `access_token` (JWT-förmiges Hüll-Token, enthält device_id und/oder entitlement_id, 6 h gültig, rotation via REST) — SignalR-Standard `Authorization` bzw. Query bei WS-Handshake.

## 1. Gruppen & Mitgliedschaftsregeln (serverseitig erzwungen)

| Gruppe | Beitritt | Inhalt |
|---|---|---|
| `city.{slug}.reports.free` | Device-Token authentifiziert, Stadt aktiv, **Ticket-Gate-Claim `ticketConfirmed=true`** (18-rechtliche-haertung §1.2; sonst Hub-Fehler `ticket_gate_blocked` + `control.ticket_gate`) | Report-Ereignisse, **Free-Payload** |
| `city.{slug}.reports.pro` | nur mit gültigem Pro-Entitlement | gleiche Ereignisse, **Pro-Payload** (inspector_count, trust, movement/ETA) |
| `city.{slug}.alerts` | jeder | gefilterte Alerts (kein Rauschen) |
| `stop.{stop_id}` | jeder | `departure.tick` (Ist-Aktualisierung einzelner Abfahrten; optional, MVP: nur bei Delta) |

Beitritt via Hub-Method `join(group)` — Server prüft Stadt-Status (RLS-äquivalent im Hub: `cities.is_active`, `feature_flags.reports_enabled`) **vor** jedem `Groups.Add`; bei Kill-Switch wird `join` mit Hub-Fehler `feature_disabled` abgelehnt und ein `control.feature_disabled`-Event an bestehende Gruppen gesendet.

## 2. Events (Server→Client; JSON, `v` = Payload-Version)

Alle Events tragen `id` (monotone `report_events.event_id` je Stadt) → Resync-Kursor.

| Event | Free-Payload | Pro-Zusatzfelder |
|---|---|---|
| `report.created` / `report.updated` | ReportFree-DTO (04 §3) | inspector_count, reporter_trust, movement |
| `report.confirmed` | report_id, confirmations | — |
| `report.contradicted` | report_id, contradictions, grace_hinweis | — |
| `report.expired` / `report.cancelled` | report_id | — |
| `report.degraded` | report_id, neu abgeleitete Station, „Verbleib unbekannt", trust-Faktor-Hinweis | wie created |
| `alert.created` / `alert.updated` | Alert-DTO | — |
| `control.feature_disabled` | `{feature:"reports"}` — Kill-Switch-Bestätigung | — |
| `control.freeze` | `{reason:"feed_degraded"}` — G11-Gate aktiv, Meldungen eingrozen (B1) | — |
| `control.ticket_gate` | `{reason:"ticket_not_confirmed"}` — Kontroll-Feature nur nach Ticket-Bestätigung (H1) | — |

Client→Server: `join(group)`, `leave(group)`, `ping`.

## 3. Reconnect-Semantik

- Client: `withAutomaticReconnect([0,2,5,10,30]s)`, danach manueller Reconnect mit Exponential-Backoff bis 5 min; UI-Indikator „Verbindung getrennt".
- Nach Reconnect: Client sendet letzten `id`; Server antwortet mit `resync.required {since_id}` falls Lücke > Ring-Buffer (2.000 Events je Stadt, in-mem) — Client holt `GET /v1/cities/{slug}/events?since_id=…` und tritt danach Gruppen wieder bei.
- Hüll-Token-Ablauf (6 h): Client erneuert via REST vor Ablauf; bei 401 → Neuverhandlung. Keine Sessions-Server state; Gruppen sind rekonstruierbar (idempotentes `join` nach Reconnect im Client fest verankert).
- Doppel-Verbindungen: Cap je Entitlement-Token (`connections_cap=3`); Device-Token: 2 parallele Verbindungen; Überschreitung → älteste Verbindung wird serverseitig geschlossen (Metrik `token_kick`).

## 4. Lastannahmen & Skalierungspfad

MVP: eine Stadt, ein Knoten — *Glaube ich* weit unter SignalR-Grenzen (zehntausende gleichzeitige Verbindungen je Knoten sind in .NET üblich; ungetestet für dieses Setup). Skalierungspfad dokumentiert, nicht gebaut: Redis-Backplane (ADR-0012), dann Hub-Host-Trennung vom REST-Host. Delta-Push statt Voll-Snapshot hält das Event-Volumen klein (nur Änderungen; Bestätigungen 1 Event).

## 5. Missbrauchsgrenzen

`join`-Rate 10/min/Verbindung; unbekannte Gruppen → Fehler; fremde `stop.{id}` erlaubt (öffentliche Daten); Entitlement-Gruppenbeitritt mit ungültigem Token wird **geloggt und gezählt** (`entitlement_join_violations` Metrik) — Muster → Device-Block (ops).
