# TransitGuard — Architektur (Stand: Implementierungsreife v1.2-Entwurf)

**Bezug:** Bestandsaufnahme `00-bestandsaufnahme.md` (verbindliche Korrekturen). Gekennzeichnete Konfidenz: *Bestätigt* (gemessen/verifiziert) · *Glaube ich* (mittel) · *Unsicher* · *Weiß ich nicht*.

## 1. Systemüberblick

```
┌────────────────────────── Clients ───────────────────────────┐
│ Flutter iOS/Android · React-PWA (kein Meldungs-Cache! B8)     │
└───────────────┬───────────────────────────┬──────────────────┘
        HTTPS REST /v1 (JSON)        WSS SignalR /hubs/v1/realtime
                │                           │
        ┌───────▼───────────────────────────▼───────┐   ┌─────────────────────┐
        │  Caddy (TLS, H2, Reverse Proxy)           │   │ Hetzner VM (1 Node) │
        │  production.<domain>  +  api.<domain>     │   │ Debian, systemd     │
        └───────┬───────────────────────────┬───────┘   └─────────────────────┘
                │                           │
        ┌───────▼───────────────────────────▼───────┐
        │ ASP.NET Core 8 App (ein Prozess, MVP)     │
        │  • REST Controllers (tier-geformt, 04)    │
        │  • SignalR Hub (Tier-Gruppen, 06)         │
        │  • Ingest Hosted Service + Hangfire       │
        │  • Normalizer · TTL-Engine · Trust-Engine │
        │  • EntitlementMiddleware (2. DB-Role)     │
        └───────┬───────────────────┬───────────────┘
                │                   │
      ┌─────────▼───────┐   ┌───────▼─────────────────────────┐
      │ PostgreSQL 15+  │   │ Externe Quellen (nur Ingress):  │
      │ + PostGIS       │   │ gtfs.de GTFS-Static (ZIP)       │
      │ RLS nach 0003   │   │ gtfs.de GTFS-RT (60 s, If-None- │
      │ Rollen: tg_app, │   │   Match; 10-s-Regeneration)     │
      │ tg_billing,     │   │ [opt. später: VBB (inaktiv),    │
      │ tg_ingest,      │   │  Geofox-Overlay, FCM/APNs]      │
      │ tg_ops, readonly│   └─────────────────────────────────┘
      └─────────────────┘
```

**Ingress-Planungszahl (gemessen 21.08.): 33–48 MB je Poll je Tageszeit → ~48–69 GB/Tag eingehend bei 60 s; Hetzner Ingress frei (J8 ✓).**

**MVP-Topologie: ein Prozess, eine VM.** Ingest (Hangfire) und API teilen die ASP.NET-App; getrennte systemd-Units sind ein späterer, konfigurativer Split (Skalierungspfad in ADR-0012). *Glaube ich:* Eine 4-vCPU/8–16-GB-VM trägt 1.440 Feeds/Tag à ~40 MB + Parse + eine Launch-Stadt Clients problemlos; Lasttest in M8 verifiziert (keine Zahl behauptet, Messung geplant).

## 2. Module (Solution-Struktur siehe `01-verzeichnisbaum.md`)

| Modul | Verantwortung | Kernahtstellen |
|---|---|---|
| `TransitGuard.Api` | REST, SignalR, Middleware, Rate-Limit | DTO-Former je Tier |
| `TransitGuard.Core` | Domäne: Report, TtlEngine, TrustEngine, Normalizer, CityFilter, GeoUtils | pur, ohne Infra-Abhängigkeiten → testbar |
| `TransitGuard.Ingest` | Hangfire-Jobs: Poll, Static-Sync, Stadt-Extrakt, Alert-Pipeline, Sweeper, Retention | schreibt nur als tg_ingest |
| `TransitGuard.Data` | EF-Core-DbContexts (App: tg_app; Billing: tg_billing), Raw-SQL-Migrationsrunner | Schema liegt als SQL vor, EF mappt nur lesend/schreibend, kein EF-Migrations |

## 3. Datenfluss 1 — Ingest (Detail: `07-ingest.md`)

```
Hangfire 60s  →  GET gtfs.de (If-None-Match, Timeout 30 s, Circuit Breaker)
  → 304? fertig (nur VBB realistisch; gtfs.de regeneriert 10 s — N4)
  → 200: Protobuf Parse ENTITÄS-WEISE (Streaming, kein Halten des Objektbaums)
       TU:  trip_id+start_date → Stadt-Lookup (In-Mem-Whitelist; Fallback PK)
             → route_id-Auflösung (C.3.1; 100 % leer gemessen)
             → Delay-Clamp [−120,+7200] s (B2) → rt_trip_state (Delta vs. voriger Poll)
       Alert: Noise-Regeln (Attribution/Ausstattung, N1) → Dedup-Key
             sha256(header|desc[:150]) → Upsert gtfs_alerts (entity_ids++)
  → Delta-Push an SignalR-Gruppen (nur Änderungen), Metriken-Zeile ingest_metrics
```

**Speicherbudget Ingest:** Whitelist (Stadt-Extrakt, zehntausende Trips) + letzter Snapshot der Stadt-TUs + Alert-Dedup-Map — einstelliger MB- bis niedriger zweistelliger MB-Bereich (*Unsicher*, wird beim ersten Lastlauf gemessen; DE-weites Halten bewusst vermieden, ADR-0008).

## 4. Datenfluss 2 — Meldung (Report-Pfad)

```
Client (1-Tap): Stationswahl (GPS<750 m Pre-Fill) → optional Fahrt aus Abfahrtsliste
  → POST /v1/reports  (Idempotency-Key; Device-Token)
  → Validation: Stadt aktiv? Kill-Switch reports_enabled? Rate-Limit (5/10 min)?
    Plausibilität: Station im Stadt-Netz? Trip im Whitelist-Fenster?
  → Trust-Gate: reporter_trust < 20 ⇒ visible=false bis 1. Bestätigung (B6)
  → TTL-Engine setzt expires_at (Profil aus ttl_config)
  → report_events('created') → SignalR city.{slug}.reports.free|.pro (Payload je Tier)
  → Hangfire Sweeper (15 s): expires_at erreicht ⇒ 'expired' + Hub-Event
       Trip-Anker: Interpolator erreicht trip_end+buffer ⇒ B.5:
         Original 'degraded/abgelaufen' + derived_terminus-Meldung (TTL 600 s,
         trust 0.7×, UI „Verbleib unbekannt") — NUR wenn Feed gesund (G11-Gate, B1)
       CANCELED im RT ⇒ sofort TTL 0 (v1.1 B.5)
  → Bestätigung/Widerspruch: POST /v1/reports/{id}/events → TTL-Anpassung nur
     unter Vertrauens-Parität (B6), sonst Review-Flag ohne TTL-Wirkung
```

## 5. TTL-Engine (deterministischer Regelkern; alle Werte `ttl_config`)

Eingaben: Report (anchor_type, vehicle_kind, report_type), trust, Ereignis-Strom, trip_state (Interpolation). Ausgabe: `expires_at`-Neuberechnung als **reine Funktion** — gleiche Eingaben, gleiches Ergebnis; tabellengetrieben testbar (Testplan M4).

```
Station-Anker (Schiene): base 1200 s · Bestätigung +300 s (max +900) · Cap 2700 s
Station-Anker (Bus/Tram): base 720 s · Cap 1800 s          [Rekonstruktion D, §5.1]
at_stop:                base 480 s · Cap 1800 s
Trip-Anker:             endet trip_end + 120 s (Interpolation); Bestätigungen
                        verlängern NICHT (natürliches Ende), Widerspruch wie unten
derived_terminus:       600 s fix, keine Verlängerung
Widerspruch (Parität erfüllt): expires_at = now + 120 s (Grace)
CANCELED (RT):          expires_at = now
Static-Swap trip_id weg: Degradation → Stations-Anker, Rest-TTL (C.3.2)
Feed ungesund (G11-Gate): KEINE Degradation, Status einfrieren (B1) ← neue Regel
```

**Offene Entscheidung 5.2 (Bestandsaufnahme):** ob der 45-min-Cap (v1.0-Wortlaut „IMMER") auch Trip-Anker schneidet. Empfehlung: nein (natürliches Ende); Umstellung = 1 Config-Wert.

## 6. Interpolation („Echtzeit-Prognose")

Positions-/Fortschrittsmodell einer gemeldeten Fahrt: `gtfs_stop_times` (Soll) + letzter bekannter Delay (rt_trip_state; Median 0 s — N2) → linear zwischen Zeitpunkten; Zeit > Soll+Delay ⇒ Halteposition. TU-STUs (gtfs.de Median 3, N3) dienen als Anker-Korrektur, niemals als alleinige Quelle. Labels: „Echtzeit-Prognose" (mind. 1 Ist-Anker < 5 min alt) vs. „Laut Fahrplan" — Label „Live" existiert nicht (0 VP, bestätigt).

## 7. Realtime-Pfad (Detail `06-realtime-hub.md`)

SignalR-Gruppen je Stadt: `city.{slug}.reports.free` / `.reports.pro` / `city.{slug}.alerts`. Tier-Membership wird beim Gruppenbeitritt serverseitig aus dem Entitlement-Token geprüft; Free-Gruppen empfangen reduzierte Payloads (Feldfilterung im Server, nie im Client — Paywall-Grundsatz). Resync über REST `/v1/cities/{slug}/events?since_id=` mit Ring-Buffer (in-mem, Spiegel `report_events`).

## 8. Entitlement-Pfad (Detail `08-entitlement.md`)

Store-Kauf → Receipt → `POST /v1/billing/activate` (tg_billing-Verbindung) → Validierung Store-API → Entitlement (Besitz-Token + Restore-Code, kein Geräte-Account). Middleware hebt Tier in den RequestContext; DTO-Former & Hub wählen danach Payload. Kern-Kontrollinfo bleibt Free (ADR-0006 — Compliance + Netzwerkeffekt).

## 9. Kill-Switch (3 Stufen, alle ohne App-Update)

1. `feature_flags.reports_enabled = false` (global) → API 404/`feature_disabled`, Hub sendet Kontroll-Event, keine Neueingaben.
2. `cities.is_active = false` (einzelne Stadt) → RLS-Policy (0003 §4) macht Daten unsichtbar.
3. Caddy: `respond 503` auf api-Subdomain (Notfall, ohne DB-Kontakt).
Clients zeigen danach den Fahrplan-Companion weiter (Der Wert der App bleibt ohne Kontroll-Feature bestehen — Cold-Start-Absicherung P1).

## 10. bewusst NICHT gebaut (YAGNI, mit ADR)

VehiclePositions-Pfad (0 VP, doppelt bestätigt) · Umlauf-Verkettung (v1.1 B.5) · H3 · TimescaleDB · Redis-Backplane (Single Node) · Multi-Region · eigene NLP/Telegram-Ingestion (FreiFahren-Merkmal, kein MVP) · B2B-Datenverkauf (ethisch/DSGVO verworfen, v0.1-Monetarisierungstabelle).
