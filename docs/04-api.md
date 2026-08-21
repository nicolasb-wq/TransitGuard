# TransitGuard — REST-API-Spezifikation (OpenAPI-kompatibel; Maschinenfassung: `05-openapi.yaml`)

**Konventionen:** JSON, UTF-8, UTC ISO-8601, Version im Pfad `/v1`. Auth: `X-Device-Token` (opaque, bei `POST /v1/devices` ausgegeben; sha256-Hash serverseitig). Pro-Endpunkte zusätzlich `Authorization: Bearer et_…` (Entitlement-Token). **Cache-Regel:** alle Meldungs-/Kontrollressourcen → `Cache-Control: no-store` (Kill-Switch-Konsistenz, B8). Fehlermodell: `{ "error": { "code": "…", "message": "…", "details": {} } }`, Problem+RFC7807-kompatibel (`type`, `title`, `status` zusätzlich).

## 1. Endpunkte

### Infrastruktur
| Methode/Pfad | Zweck | Auth | Tier |
|---|---|---|---|
| `GET /health/live` `GET /health/ready` | Liveness/Readiness (DB, Feed-Alter) | keine | — |

### Städte & Fahrplan (Companion-Kern, immer Free)
| Methode/Pfad | Zweck | Anmerkung |
|---|---|---|
| `GET /v1/cities` | aktive Städte + Public-Flags (reports_enabled …) | Kill-Switch-Transport |
| `GET /v1/cities/{slug}/stops?lat&lon&radius=1000&limit` | Haltestellensuche (Geo) | PostGIS-Index |
| `GET /v1/stops/{stop_id}/departures?limit=20&trip_ref=true` | Abfahrten Soll+Ist; **enthält `trip_ref` (trip_id+start_date)** | Client wählt daraus die gemeldete Fahrt |
| `GET /v1/stops/{stop_id}` | Haltestellen-Details | |

### Meldungen (Kontroll-Feature)
| Methode/Pfad | Zweck | Regeln |
|---|---|---|
| `POST /v1/reports` | neue Meldung | Header `Idempotency-Key` Pflicht; Schema §2; Rate 5/10 min/Device; Stadt aktiv + `reports_enabled` |
| `GET /v1/reports/{id}` | Einzelabruf | Tier-geformt §3 |
| `GET /v1/cities/{slug}/reports?bbox&active=true&since_id&limit` | Karte/Liste | Tier-geformt; max. `limit=200` |
| `POST /v1/reports/{id}/events` | `{"type":"confirm"\|"contradict"}` | 1×/Device/Report; B6-Paritätsregeln |
| `GET /v1/cities/{slug}/events?since_id&limit` | Realtime-Resync (Kursor = `report_events.event_id`) | nur sichtbare Meldungen |

### Fahrplan-Kern (Session 2 — Detail in 05-openapi + 20-build-log)
| `GET /v1/cities/{slug}/stops/nearby?lat&lon&q&take` | Nearby per Standort ODER Namenssuche (q) — Autofill Start/Ziel |
| `GET /v1/stops/{stop_id}/departures?date&limit` | Abfahrten Soll+Ist (Delay aus RT), realtime-Flag |
| `POST /v1/journeys/search` `{from_stop_id|from_lat/from_lon, to_stop_id}` | Direktverbindungen + nächste Abfahrten **+ Kontroll-Warnungen** |
| `GET /v1/journeys/{trip_id}/warnings?start_date&from_stop_id` | Warnungen einer Fahrt (Anzeige ≥1 Haltestelle vorher; Ticket-Gate aktiv) |
| `GET /v1/devices/me` | Trust + `access` (trial/subscriber/locked), `trial_days_remaining`, Abo-Preis |
| **Neu seit ADR-0014:** alle Feature-Endpunkte antworten nach Testablauf mit **402 `trial_expired`** (AccessGate); Billing/Ticket/Health bleiben offen.

### Störungen/Hinweise
| `GET /v1/cities/{slug}/alerts?route_id&stop_id` | gefilterte, nicht-Rausch-Alerts | Felder: header, description, url, severity, route_refs, until |

### Gerät & Vertrauen
| `POST /v1/devices` | Device+Token ausstellen | Antwort zeigt Token **einmalig** |
| `GET /v1/devices/me` | trust_score, rank, eigene Statistik | |
| `DELETE /v1/devices/me` | **DSGVO-Selbstlöschung** | löscht devices, trust_scores; anonymisiert reporter_device_id in reports (setzt NULL, Meldungen bleiben als Crowd-Wissen erhalten — Aufbewahrung anonym) |

### Billing/Entitlement (tg_billing-Verbindung)
| `POST /v1/billing/activate` | `{platform, receipt}` → `{access_token, restore_code, valid_until}` | Store-Server-Validierung; Receipt-Dedup |
| `POST /v1/billing/restore` | `{restore_code}` → neues Token | Brute-Force-Limit 5 Versuche/h/IP |
| `POST /v1/billing/validate` | `{token}` → `{tier, valid_until}` | Renewal-Check |
| `POST /v1/billing/refund-hook` | Store-Server-Notification → revoke | nur intern/IP-restricted |

## 2. `POST /v1/reports` — Request

```json
{
  "city_slug": "hamburg",
  "anchor_type": "trip",                  // station | trip
  "report_type": "in_vehicle",            // in_vehicle | on_platform | at_stop
  "vehicle_kind": "rail",                 // rail | bus
  "station": { "stop_id": "A123", "geo": {"lat":53.55,"lon":9.99} },   // immer (Einstieg/Ort)
  "trip": { "trip_id": "1757213", "start_date": "20260821", "route_id": "U1", "direction_id": 1, "headsign": "Ohlsdorf" },  // Pflicht bei anchor_type=trip
  "inspector_count": 2,
  "client": { "app_version": "1.0.0", "platform": "android", "ticket_confirmed": true }
}
```
**Rechtliches Pflichtfeld (18-rechtliche-haertung H1):** `client.ticket_confirmed == true` ist erforderlich — fehlt es/false: **422 `ticket_confirmation_required`**. Der Server prüft nur die Präsenz der Erklärung (Age-Gate-Muster) und speichert keine personenbezogene Aussage — nur Tagesaggregate `gate.confirmations`/`gate.blocks` als Rechts-Beweismittel-Pipeline.
Validierung (Ablehnung mit 422 + `details`): Station gehört zur Stadt (bbox/Netz), `trip.trip_id+start_date` ∈ aktiver Whitelist (Trip-Fenster ±10 min um Soll-Abfahrt), Radius-Plausibilität GPS↔Station ≤ 750 m (C3; überschreitbar mit `confirmed_by_user: true`), Counts 1–8.
Antwort 201: vollständiger Report-Datensatz (Tier-geformt) inkl. `expires_at` und `ttl_profile`.

## 3. Tier-Formung (serverseitig, DTO-Former — gleiche Logik im Hub)

| Feld | Free | Pro | Begründung Zeile (Kurz) |
|---|---|---|---|
| id, city, anchor_type, station_name, route_id, headsign, age, expires_at, report_type | ✔ | ✔ | Kern-Sicherheitsinfo (ADR-0006) |
| status, origin | ✔ | ✔ | Ehrlichkeit („Verbleib unbekannt") |
| trip_id, trip_start_date | ✔ | ✔ | Meldungsbezug — ohne ihn kein Bestätigen von Fahrten |
| inspector_count | ✘ | ✔ | Detailtiefe |
| reporter_trust, reporter_rank | ✘ | ✔ | Analytik |
| movement (interpolierter Verlauf, „erreicht Station X in ~4 min") | ✘ | ✔ | Prognose-Komfort; Basis-Fakt (Linie+Richtung+Alter) bleibt Free |
| confirm/contradict-Rechte | ✔ | ✔ | Datenlieferanten nie drosseln |

**Kontrakt-Tests (Testplan T-EN-1) stellen sicher, dass Free-Antworten diese Felder physisch nicht enthalten** (Serialisierung, nicht null — Feld fehlt).

## 4. Idempotenz, Rate-Limits, Fehlertypen

- `Idempotency-Key`: Server merkt (key → response) 24 h in `idempotency_keys` (TTL-Tabelle); Replay liefert gleiche Antwort ohne Neuanlage.
- Rate-Limits (pro Device-Token, 429 + `Retry-After`): Meldungen 5/10 min, Events 20/10 min, Departures 120/min, Billing-Restore 5/h.
- Codes: `device_blocked` (403), `feature_disabled` (404 — Kill-Switch), `city_inactive` (404), `entitlement_required` (402), `rate_limited` (429), `trip_not_resolvable` (422), `station_not_in_city` (422), `ticket_confirmation_required` (422 — Ticket-Gate), `ticket_gate_blocked` (403 — Kontroll-Lesezugriff ohne Bestätigung), `validation` (422), `internal` (500).
- **Ticket-First-Gate auch auf Lesezugriff:** `GET /v1/cities/{slug}/reports` und alle kontrollbezogenen Hub-Joins verlangen `X-Ticket-Confirmed: true` bzw. Join-Claim — sonst 403 `ticket_gate_blocked` (Details 18-rechtliche-haertung §1.2).

## 5. Versionierung & Kompatibilität

Additiv innerhalb `/v1`; Breaking → `/v2` parallel (≥6 Monate). Clients senden `X-App-Version`; Server kann Mindestversion erzwingen (`426 upgrade_required`) — Notfall-Kanal ohne Store-Update.
