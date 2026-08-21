-- ============================================================================
-- TransitGuard Migration 0004 — Partitionen + Retention + Seed
-- reports ist deklarativ TAG-partitioniert (ADR-0003: kein TimescaleDB).
-- Hangfire-Jobs rufen tg_ensure_report_partitions()/tg_drop_old_report_partitions()
-- täglich auf. DSGVO-Retention: 90 Tage (Rechtsteil-Tabelle).
-- ============================================================================

BEGIN;

-- Partitionen im Voraus anlegen (14 Tage); wird vom Job verlängert.
CREATE OR REPLACE FUNCTION tg_ensure_report_partitions(p_days INT DEFAULT 14)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
  d DATE := date_trunc('day', now())::date;
  i INT;
  part TEXT;
BEGIN
  FOR i IN 0 .. p_days LOOP
    part := format('reports_%s', to_char(d + i, 'YYYYMMDD'));
    IF NOT EXISTS (SELECT 1 FROM pg_class WHERE relname = part) THEN
      EXECUTE format(
        'CREATE TABLE %I PARTITION OF reports FOR VALUES FROM (%L) TO (%L)',
        part, d + i, d + i + 1);
    END IF;
  END LOOP;
END $$;

-- Retention: Partitionen älter als p_keep_days fallen lassen (DETACH + DROP,
-- O(1) statt DELETE-Scans; DSGVO-Löschkonzept reports: 90 Tage).
CREATE OR REPLACE FUNCTION tg_drop_old_report_partitions(p_keep_days INT DEFAULT 90)
RETURNS INT LANGUAGE plpgsql AS $$
DECLARE
  part TEXT; cutoff DATE := date_trunc('day', now())::date - p_keep_days;
  dropped INT := 0;
BEGIN
  FOR part IN
    SELECT c.relname FROM pg_class c
    JOIN pg_inherits i ON i.inhrelid = c.oid
    WHERE c.relname ~ '^reports_[0-9]{8}$'
      AND to_date(substring(c.relname from '[0-9]{8}$'), 'YYYYMMDD') < cutoff
  LOOP
    EXECUTE format('DROP TABLE %I', part);  -- Partitionen sind nie Primary, kein DETACH nötig
    dropped := dropped + 1;
  END LOOP;
  RETURN dropped;
END $$;

-- report_events: einfache Retention per DELETE (kleiner, BRIN-optimiert)
CREATE INDEX IF NOT EXISTS report_events_created_brin ON report_events USING BRIN (created_at);

CREATE OR REPLACE FUNCTION tg_drop_old_report_events(p_keep_days INT DEFAULT 90)
RETURNS void LANGUAGE sql AS
  $$ DELETE FROM report_events WHERE created_at < now() - make_interval(days => p_keep_days) $$;

-- ingest_metrics: 180 Tage (Operational); alert-Rohdaten werden gar nicht persistiert.
CREATE INDEX IF NOT EXISTS ingest_metrics_ts_brin ON ingest_metrics USING BRIN (ts);

-- ---------------------------------------------------------------------------
-- Seed: Launch-Stadt Hamburg (inaktiv bis J3-Gate bestanden). Bounding-Box
-- grob HVV-Kernraum (Unsicher: exakte Box vor Launch aus HVV-Static ableiten).
-- ---------------------------------------------------------------------------
-- BBox = Umland-Box (Auftrag 21.08.2026: "Hamburg und Umgebung"); Messgrundlage: 14-j3-j6-messung.md
-- (4.319 gleichzeitige TUs im Umland-BBox; Kern-BBox 9.73-10.32/53.40-53.75 bleibt als Filterkriterium erhalten)
INSERT INTO cities (city_id, slug, display_name, is_active, gtfs_static_source, bbox)
VALUES ('hamburg', 'hamburg', 'Hamburg & Umland', FALSE, 'gtfs.de/de',
        ST_GeomFromText('POLYGON((9.55 53.30, 10.50 53.30, 10.50 53.85, 9.55 53.85, 9.55 53.30))', 4326))
ON CONFLICT (city_id) DO NOTHING;

-- TSL-Profile (Server-Config, siehe Architektur-Doc §5; Bus-Werte = Rekonstruktions-
-- vorschlag aus Bestandsaufnahme §5.1 — endgültige Entscheidung Product Owner).
CREATE TABLE ttl_config (
  profile        TEXT PRIMARY KEY,           -- 'rail_station','rail_trip','bus_station','bus_trip','derived_terminus','at_stop'
  base_s         INTEGER,                    -- NULL = natürliches Ende (Trip-Anker: trip_end + Buffer, F-16)
  confirm_bonus_s INTEGER NOT NULL DEFAULT 300,
  confirm_max_s  INTEGER NOT NULL DEFAULT 900,
  contradiction_grace_s INTEGER NOT NULL DEFAULT 120,
  cap_s          INTEGER,                    -- NULL = natürliches Ende (Trip-Ende), siehe Bestandsaufnahme 5.2
  trip_end_buffer_s INTEGER NOT NULL DEFAULT 120
);
INSERT INTO ttl_config (profile, base_s, cap_s) VALUES
  ('rail_station', 1200, 2700),
  ('rail_trip',    NULL, NULL),              -- endet trip_end + buffer; cap-Entscheidung offen (5.2)
  ('bus_station',   720, 1800),
  ('bus_trip',     NULL, NULL),
  ('derived_terminus', 600, 600),
  ('at_stop',       480, 1800);

-- Interpolations-/Normalisierungs-Config (B2/B3, Ingest-Doc)
CREATE TABLE ingest_config (
  key TEXT PRIMARY KEY,
  value JSONB NOT NULL,
  note TEXT
);
INSERT INTO ingest_config (key, value, note) VALUES
  ('poll_interval_s', '60', 'v1.1 Abschnitt I; Anbieter regeneriert alle 10 s (Bestandsaufnahme N4)'),
  ('delay_clamp_s', '[-120, 7200]', 'gemessene Ausreißer −30.384 s .. +7.148 s (B2)'),
  ('feed_health_min_entities_ratio', '0.5', 'G11-Gate: Degradation nur bei gesundem Feed (B1)'),
  ('feed_health_max_age_s', '300', 'G11-Gate Feed-Alter'),
  ('match_rate_warn', '0.90', 'G10-Warnschwelle'),
  ('match_rate_critical', '0.70', 'G10-kritisch -> Re-Sync + Alarm');

SELECT tg_ensure_report_partitions(14);

COMMIT;
