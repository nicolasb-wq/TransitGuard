-- ============================================================================
-- TransitGuard Migration 0002 — GTFS-Static-Cache (Stadt-Extrakt), Builds,
-- Staging (atomarer Swap, C.3.2), Alerts inkl. Noise-Regeln und Dedup
-- Konzept: DE-weiter Feed wird NICHT vollständig vorgehalten. Pro aktive Stadt
-- wird ein "Stadt-Extrakt" (Bounding-Box + Trip-Closure) gezogen; nur dieser
-- liegt in gtfs_* und bildet das In-Memory-Lookup (C.3.1, ADR-0008).
-- ============================================================================

BEGIN;

CREATE TABLE gtfs_builds (
  build_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  city_id      TEXT NOT NULL REFERENCES cities(city_id),
  source_url   TEXT NOT NULL,
  fetched_at   TIMESTAMPTZ NOT NULL,
  source_sha256 TEXT,                          --_HASH der heruntergeladenen ZIP
  schedule_sha256 TEXT,                        -- vom RT-Feed deklarierter Wert (VBB-Muster), falls verfügbar
  routes_count INTEGER, trips_count INTEGER, stops_count INTEGER, stop_times_count INTEGER,
  status       TEXT NOT NULL DEFAULT 'staged' CHECK (status IN ('staged','active','retired')),
  activated_at TIMESTAMPTZ,
  retired_at   TIMESTAMPTZ
);
CREATE INDEX gtfs_builds_city_active_idx ON gtfs_builds (city_id, status);

CREATE TABLE gtfs_routes (
  build_id        BIGINT NOT NULL REFERENCES gtfs_builds(build_id) ON DELETE CASCADE,
  route_id        TEXT NOT NULL,
  agency_id       TEXT,
  route_short_name TEXT,
  route_long_name TEXT,
  route_type      SMALLINT NOT NULL,           -- 0 Tram,1 U-Bahn,2 Eisenbahn,3 Bus,4 Fähre,5 Seilbahn,6 Luftseilbahn,7 Standseilbahn
  route_color     TEXT,
  PRIMARY KEY (build_id, route_id)
);

CREATE TABLE gtfs_stops (
  build_id      BIGINT NOT NULL REFERENCES gtfs_builds(build_id) ON DELETE CASCADE,
  stop_id       TEXT NOT NULL,
  stop_name     TEXT NOT NULL,
  parent_station TEXT,
  location_type SMALLINT,
  geo           GEOGRAPHY(POINT, 4326) NOT NULL,
  PRIMARY KEY (build_id, stop_id)
);
CREATE INDEX gtfs_stops_geo_idx ON gtfs_stops USING GIST (geo);

CREATE TABLE gtfs_trips (
  build_id     BIGINT NOT NULL REFERENCES gtfs_builds(build_id) ON DELETE CASCADE,
  trip_id      TEXT NOT NULL,
  route_id     TEXT NOT NULL,
  service_id   TEXT NOT NULL,
  trip_headsign TEXT,
  direction_id SMALLINT,
  -- Denormalisierte Anker-Daten für Interpolation + B.5 (Trip-Ende):
  first_stop_id TEXT,
  last_stop_id  TEXT,
  start_time_s  INTEGER,                      -- Sekunden ab Mitternacht (GTFS-Mehrtageszeiten >86400 zulässig)
  end_time_s    INTEGER,
  PRIMARY KEY (build_id, trip_id)
);
CREATE INDEX gtfs_trips_lookup_idx ON gtfs_trips (build_id, trip_id);          -- Kaltstart-Fallback C.3.1 Ebene 2
CREATE INDEX gtfs_trips_route_idx  ON gtfs_trips (build_id, route_id);

-- Aktive Sichten (der Ingest liest nur aktive Builds; Swap per Status-Wechsel in
-- EINER Transaktion statt Rename — semantisch äquivalent zu C.3.2, aber simpler):
CREATE VIEW gtfs_trips_active AS
  SELECT t.* FROM gtfs_trips t JOIN gtfs_builds b ON b.build_id = t.build_id WHERE b.status = 'active';

CREATE TABLE gtfs_stop_times (
  build_id     BIGINT NOT NULL REFERENCES gtfs_builds(build_id) ON DELETE CASCADE,
  trip_id      TEXT NOT NULL,
  stop_sequence INTEGER NOT NULL,
  stop_id      TEXT NOT NULL,
  arrival_s    INTEGER,                       -- Sekunden ab Mitternacht (Soll)
  departure_s  INTEGER,
  timepoint    BOOLEAN,
  PRIMARY KEY (build_id, trip_id, stop_sequence)
);
CREATE INDEX gtfs_stop_times_stop_idx ON gtfs_stop_times (build_id, stop_id);

-- Rollende Ist-Anker je aktiver Fahrt (In-Memory-State, gespiegelt für Kaltstart
-- und Debugging; NICHT Historie — Historie lebt in ingest_metrics)
CREATE TABLE rt_trip_state (
  city_id         TEXT NOT NULL,
  trip_id         TEXT NOT NULL,
  trip_start_date TEXT NOT NULL,
  route_id        TEXT NOT NULL,              -- aufgelöst via C.3.1
  last_stop_id    TEXT,
  last_delay_s    INTEGER,                    -- geclampter letzter bekannter Delay
  delay_source    SMALLINT NOT NULL DEFAULT 0,-- 0=soll,1=ist
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  feed_seen_at    TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (city_id, trip_id, trip_start_date)
);

-- ---------------------------------------------------------------------------
-- Alerts: normalisiert + dedupliziert (N1: Rohzahl ist >99 % Duplikat)
-- Noise-Regeln zuerst (FK-Referenz), gemessen 2026-08-21
-- (verifikation/verify_alerts_output.txt); Konfiguration, kein Code.
-- ---------------------------------------------------------------------------
CREATE TABLE alert_noise_rules (
  rule_id    TEXT PRIMARY KEY,
  kind       TEXT NOT NULL CHECK (kind IN ('attribution','amenity','other')),
  match_type TEXT NOT NULL CHECK (match_type IN ('desc_prefix','header_equals','header_contains')),
  pattern    TEXT NOT NULL,
  enabled    BOOLEAN NOT NULL DEFAULT TRUE,
  note       TEXT
);

INSERT INTO alert_noise_rules (rule_id, kind, match_type, pattern, note) VALUES
  ('attr_gtfsde',  'attribution','desc_prefix',  'Echtzeitdaten aufbereitet von GTFS.de', 'Lizenz-Namensnennung als Alert (64.246x am 21.08.)'),
  ('amen_niederflur','amenity','header_equals','Niederflur',NULL),
  ('amen_niederflur2','amenity','header_equals','NIEDERFLUR',NULL),
  ('amen_rollstuhl','amenity','header_equals','Rollstuhlgeeignet',NULL),
  ('amen_bordrest','amenity','header_equals','Bordrestaurant',NULL),
  ('amen_wlan',    'amenity','header_equals','WLAN verfügbar',NULL),
  ('amen_klima',   'amenity','header_equals','Klimaanlage',NULL),
  ('amen_2kl',     'amenity','header_equals','nur 2. Kl.',NULL),
  ('amen_behind',  'amenity','header_equals','Behindertengerecht',NULL),
  ('amen_behind2', 'amenity','header_equals','Behindertengerechtes Fahrzeug',NULL),
  ('amen_fahrrad', 'amenity','header_equals','Fahrradmitnahme begrenzt möglich',NULL),
  ('amen_fahrrad2','amenity','header_equals','Fahrradmitnahme reservierungspflichtig',NULL),
  ('amen_checkin', 'amenity','header_equals','Komfort Check-in verfügbar - wenn möglich bitte einchecken',NULL),
  ('amen_einstieg','amenity','header_equals','Fahrzeuggebundene Einstiegshilfe vorhanden',NULL);
-- 'attr_gtfsde' deckt alle "… bereitgestellt von <Verbund>"-Varianten ab (Präfixregel).

CREATE TABLE gtfs_alerts (
  alert_key      TEXT PRIMARY KEY,            -- sha256(header|desc[:150]|city) — identischer Schlüssel wie im Ingest (N1)
  city_id        TEXT NOT NULL,
  build_id       BIGINT,                      -- NULL: alert-bezogen, nicht build-gebunden
  entity_ids     INTEGER NOT NULL DEFAULT 1,  -- wie viele Roh-Entities darunterliegen (Duplikat-Zähler)
  header_text    TEXT NOT NULL,
  description_text TEXT NOT NULL DEFAULT '',
  cause          SMALLINT NOT NULL DEFAULT 1,
  effect         SMALLINT NOT NULL DEFAULT 8,
  severity       SMALLINT NOT NULL DEFAULT 2,
  url            TEXT,
  informed_trip_ids TEXT[] NOT NULL DEFAULT '{}',
  informed_stop_ids  TEXT[] NOT NULL DEFAULT '{}',
  is_noise       BOOLEAN NOT NULL DEFAULT FALSE,   -- Attribution/Ausstattung -> nie in UI
  noise_rule     TEXT REFERENCES alert_noise_rules(rule_id),
  first_seen_at  TIMESTAMPTZ NOT NULL,
  last_seen_at   TIMESTAMPTZ NOT NULL
);
CREATE INDEX gtfs_alerts_city_idx ON gtfs_alerts (city_id, last_seen_at DESC) WHERE is_noise = FALSE;
CREATE INDEX gtfs_alerts_noise_idx ON gtfs_alerts (city_id) WHERE is_noise = TRUE;

COMMIT;
