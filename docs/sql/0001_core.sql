-- ============================================================================
-- TransitGuard Migration 0001 — Kern-Schema (Städte, Geräte, Meldungen, Trust,
-- Entitlements, Kill-Switch, Ingest-Metriken)
-- Voraussetzung: PostgreSQL 15+, PostGIS, pgcrypto. Ausführung als Superuser/Owner.
-- Alle Zeitstempel UTC (timestamptz). Alle Beziehungen soft (keine harten FKs auf
-- Meldungstabellen, damit Geräte-Löschung ohne Cascade-Keys läuft).
-- ============================================================================

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS postgis;

-- ---------------------------------------------------------------------------
-- Datenbank-Rollen (Login erfolgt ausschließlich über die App; Passwörter via
-- Secrets, nie im Repo). Vgl. 0003 für GRANTs/RLS.
-- ---------------------------------------------------------------------------
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tg_app') THEN
    CREATE ROLE tg_app NOLOGIN;          -- API-Prozess (Standardverbindung)
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tg_billing') THEN
    CREATE ROLE tg_billing NOLOGIN;      -- zweite, schmale Verbindung nur für Billing-Endpunkte
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tg_ingest') THEN
    CREATE ROLE tg_ingest NOLOGIN;       -- Ingest-/Sync-Jobs (Hangfire)
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tg_ops') THEN
    CREATE ROLE tg_ops NOLOGIN;          -- Kill-Switch, Betriebs-Metriken
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tg_readonly') THEN
    CREATE ROLE tg_readonly NOLOGIN;     -- Dashboards, Debugging
  END IF;
END $$;

-- ---------------------------------------------------------------------------
-- Städte (Launch-Konfiguration)
-- ---------------------------------------------------------------------------
CREATE TABLE cities (
  city_id        TEXT PRIMARY KEY,                   -- z.B. 'hamburg'
  slug           TEXT NOT NULL UNIQUE,               -- URL-Pfad
  display_name   TEXT NOT NULL,
  is_active      BOOLEAN NOT NULL DEFAULT FALSE,     -- Kill-Switch auf Stadtebene (Stufe 2)
  launched_at    TIMESTAMPTZ,
  gtfs_static_source TEXT NOT NULL,                  -- URL/Region-Schlüssel des Static-Feeds
  gtfs_rt_source     TEXT NOT NULL DEFAULT 'https://realtime.gtfs.de/realtime-free.pb',
  rt_contains_vp     BOOLEAN NOT NULL DEFAULT FALSE, -- Prio-1-Pfad (im MVP immer false, siehe v1.1 §1)
  rt_schedule_sha256 TEXT,                           -- VBB-Muster; NULL wenn Anbieter nichts liefert
  ticket_deeplink    TEXT,                           -- Deep-Link-Vorlage Verbund-App, z.B. 'hvvswitch://'
  bbox           GEOMETRY(POLYGON, 4326) NOT NULL,   -- Stadt-Filter (Ingest + API)
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- Kill-Switch / Feature-Flags (Stufe 1: global). App liest nur is_public-Zeilen.
-- ---------------------------------------------------------------------------
CREATE TABLE feature_flags (
  key         TEXT PRIMARY KEY,            -- z.B. 'reports_enabled', 'alerts_ui_enabled'
  enabled     BOOLEAN NOT NULL DEFAULT TRUE,
  is_public   BOOLEAN NOT NULL DEFAULT TRUE,   -- für tg_app lesbar?
  note        TEXT,
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_by  TEXT
);

INSERT INTO feature_flags (key, enabled, is_public, note) VALUES
  ('reports_enabled',   TRUE, TRUE, 'Globaler Kill-Switch Kontroll-Feature (rechts-/store-seitig)'),
  ('alerts_enabled',    TRUE, TRUE, 'Störungs-/Hinweis-Feature'),
  ('pro_tier_enabled',  FALSE, TRUE, 'Bezahlte Stufe aktiv? (Default aus bis Launch)'),
  ('push_enabled',      FALSE, FALSE,'Push-Versand (FCM/APNs) — Post-MVP');

-- ---------------------------------------------------------------------------
-- Geräte (anonym): zufällige UUID, vom Client bei Erstkontakt geholter Token.
-- Kein Hardware-Identifier, keine IP-Speicherung in dieser Tabelle.
-- ---------------------------------------------------------------------------
CREATE TABLE devices (
  device_id     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  token_hash    BYTEA NOT NULL UNIQUE,           -- sha256(Client-Token); Token selbst nur beim Issue sichtbar
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at  TIMESTAMPTZ,
  is_blocked    BOOLEAN NOT NULL DEFAULT FALSE   -- Anti-Abuse-Sperre (ops)
);

CREATE TABLE trust_scores (
  device_id        UUID PRIMARY KEY REFERENCES devices(device_id) ON DELETE CASCADE,
  score            NUMERIC(6,2) NOT NULL DEFAULT 50.0 CHECK (score >= 0 AND score <= 200),
  total_reports    INTEGER NOT NULL DEFAULT 0,
  confirmed_count  INTEGER NOT NULL DEFAULT 0,
  false_count      INTEGER NOT NULL DEFAULT 0,
  contradicted_count INTEGER NOT NULL DEFAULT 0,
  rank             SMALLINT NOT NULL DEFAULT 1,  -- 1 Neuling (0-59), 2 Scout (60-99), 3 Wächter (100-149), 4 Legende (150+); fixt B5 aus Bestandsaufnahme
  last_state_change TIMESTAMPTZ
);

-- ---------------------------------------------------------------------------
-- Meldungen. Partitioniert nach created_at (Tag). anchor_type ist die zentrale
-- Neuerung gegenüber v0.1: Trip-Anker (B.4) + abgeleitete Endstations-Meldung (B.5).
-- Partiitions-Verwaltung: 0004.
-- ---------------------------------------------------------------------------
CREATE TABLE reports (
  id              UUID NOT NULL DEFAULT gen_random_uuid(),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  city_id         TEXT NOT NULL REFERENCES cities(city_id),
  anchor_type     SMALLINT NOT NULL CHECK (anchor_type IN (1,2,3)),   -- 1=station, 2=trip, 3=derived_terminus
  -- Stations-Anker
  station_id      TEXT,
  station_name    TEXT,                                              -- Denormalisiert für UI-Historie
  -- Trip-Anker
  trip_id         TEXT,
  trip_start_date TEXT,                                              -- GTFS start_date YYYYMMDD
  route_id        TEXT,
  direction_id    SMALLINT CHECK (direction_id IN (0,1)),
  headsign        TEXT,
  -- Gemeinsam
  report_type     SMALLINT NOT NULL CHECK (report_type IN (1,2,3)),  -- 1=in_vehicle, 2=on_platform, 3=at_stop
  vehicle_kind    SMALLINT NOT NULL DEFAULT 1 CHECK (vehicle_kind IN (1,2)), -- 1=schiene, 2=bus/tram (TTL-Profile)
  inspector_count SMALLINT NOT NULL DEFAULT 1 CHECK (inspector_count BETWEEN 1 AND 8),
  reporter_device_id UUID NOT NULL,                                  -- ohne FK: Löschbarkeit (B8)
  reporter_trust  NUMERIC(6,2) NOT NULL,
  status          TEXT NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active','expired','cancelled','degraded')),
  expires_at      TIMESTAMPTZ NOT NULL,
  ttl_base_s      INTEGER NOT NULL,
  confirmations   INTEGER NOT NULL DEFAULT 0,
  contradictions  INTEGER NOT NULL DEFAULT 0,
  parent_report_id UUID,                                             -- derived_terminus -> Original
  origin          TEXT NOT NULL DEFAULT 'user' CHECK (origin IN ('user','derived')),
  visible         BOOLEAN NOT NULL DEFAULT TRUE,                     -- FALSE = unter Vertrauens-Schwelle, wartet auf Bestätigung
  geo             GEOGRAPHY(POINT, 4326),
  metadata        JSONB NOT NULL DEFAULT '{}'::jsonb,                -- Quelle: client_type, app_version …
  PRIMARY KEY (id, created_at),
  CHECK (anchor_type = 1 AND station_id IS NOT NULL
      OR anchor_type = 2 AND trip_id IS NOT NULL AND trip_start_date IS NOT NULL
      OR anchor_type = 3)                                             -- derived: Station + parent gefüllt per Job
) PARTITION BY RANGE (created_at);   -- ADR-0003: deklarative Tages-Partitionen (Retention = Partition-Drop)

CREATE INDEX reports_active_idx   ON reports (city_id, created_at) WHERE status = 'active';
CREATE INDEX reports_route_active_idx ON reports (city_id, route_id, created_at) WHERE status = 'active' AND anchor_type = 2;
CREATE INDEX reports_trip_idx     ON reports (city_id, trip_id, trip_start_date);
CREATE INDEX reports_station_idx  ON reports (city_id, station_id);
CREATE INDEX reports_device_idx   ON reports (reporter_device_id, created_at);  -- DSGVO-Löschung
CREATE INDEX reports_geo_idx      ON reports USING GIST (geo);

-- Ereignis-Stream (Audit + Realtime-Resync-Kursor; monotone id pro Stadt)
CREATE TABLE report_events (
  event_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  city_id         TEXT NOT NULL,
  report_id       UUID NOT NULL,
  event_type      TEXT NOT NULL CHECK (event_type IN
                    ('created','confirmed','contradicted','ttl_extended',
                     'degraded','expired','cancelled')),
  actor_device_id UUID,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  payload         JSONB NOT NULL DEFAULT '{}'::jsonb                   -- tier-reduziert wird erst im API-Layer erzeugt
);
CREATE INDEX report_events_city_id_created_idx ON report_events (city_id, event_id);
CREATE INDEX report_events_report_idx ON report_events (report_id);

-- ---------------------------------------------------------------------------
-- Entitlements (Free/Pro). Besitz-Modell: Token = Berechtigung, keine Geräte-
-- Verkettung (ADR-0005). Nur tg_billing darf lesen/schreiben.
-- ---------------------------------------------------------------------------
CREATE TABLE entitlements (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tier               TEXT NOT NULL DEFAULT 'free' CHECK (tier IN ('free','pro')),
  status             TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active','expired','revoked','pending')),
  store              TEXT NOT NULL DEFAULT 'none' CHECK (store IN ('none','apple','google','manual')),
  store_ref_hash     BYTEA,                     -- sha256(Receipt-/Transaktions-ID); Dedup gegen Replay
  access_token_hash  BYTEA NOT NULL UNIQUE,     -- sha256(et_…-Token)
  restore_code_hash  BYTEA NOT NULL UNIQUE,     -- sha256(Wiederherstellungscode)
  connections_cap    SMALLINT NOT NULL DEFAULT 3,  -- SignalR-Verbindungen je Token (Sharing-Dämpf)
  valid_until        TIMESTAMPTZ,               -- NULL = unbefristet (Free, manuell)
  purchased_at       TIMESTAMPTZ,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
  revoked_at         TIMESTAMPTZ,
  UNIQUE NULLS NOT DISTINCT (store, store_ref_hash)  -- PG15-Syntax; jede Receipt genau ein Entitlement
);

-- ---------------------------------------------------------------------------
-- Ingest-Metriken (Basis für G10/G11-Alarme; Hangfire schreibt je Poll)
-- ---------------------------------------------------------------------------
CREATE TABLE ingest_metrics (
  id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ts            TIMESTAMPTZ NOT NULL DEFAULT now(),
  feed          TEXT NOT NULL,                -- 'gtfs.de' | 'vbb' | …
  http_status   INTEGER,
  bytes         BIGINT,
  feed_age_s    INTEGER,
  entities      INTEGER,
  tu_count      INTEGER,
  alert_count   INTEGER,
  vp_count      INTEGER,
  city_trips_matched INTEGER,                 -- TUs nach Stadt-Filter
  match_rate    NUMERIC(5,4),                 -- Anteil auflösbarer trip_ids (G10-Detektor)
  route_misses  INTEGER,                      -- C.3.1 Miss-Counter
  delay_clamped INTEGER,                      -- B2
  parse_ms      INTEGER,
  etag_hit      BOOLEAN,
  error         TEXT
);
CREATE INDEX ingest_metrics_ts_idx ON ingest_metrics (ts DESC);

COMMIT;
