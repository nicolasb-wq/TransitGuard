-- ============================================================================
-- TransitGuard Migration 0003 — GRANTs + Row Level Security
-- Ehrliche Einordnung (ADR-0013): TransitGuard ist ein Ein-Mandanten-System mit
-- anonymen Clients; RLS ersetzt hier keine Application-Layer-Autorisierung,
-- sondern wirkt als Defense-in-Depth gegen Fehler im API-Prozess und gegen
-- seitliche Privilegien-Eskalation (z.B. kompromittierte Readonly-Credentials).
-- ============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- 1) GRANTs (Sperrprinzip; tg_app bekommt KEINEN Zugriff auf Billing/Ops)
-- ---------------------------------------------------------------------------
GRANT USAGE ON SCHEMA public TO tg_app, tg_billing, tg_ingest, tg_ops, tg_readonly;

-- Kern
GRANT SELECT, INSERT, UPDATE ON cities TO tg_app, tg_ingest;
GRANT SELECT ON cities TO tg_readonly, tg_ops;

GRANT SELECT, INSERT, UPDATE ON feature_flags TO tg_ops;
GRANT SELECT ON feature_flags TO tg_app, tg_readonly;

GRANT SELECT, INSERT, UPDATE, DELETE ON devices, trust_scores TO tg_app;  -- DELETE nur für DSGVO-Selbstlöschung
GRANT SELECT ON devices, trust_scores TO tg_readonly;

GRANT SELECT, INSERT, UPDATE ON reports TO tg_app, tg_ingest;             -- DELETE läuft über Retention-Job (tg_ingest)
GRANT SELECT ON reports TO tg_readonly;
GRANT SELECT, INSERT ON report_events TO tg_app, tg_ingest;

GRANT ALL ON entitlements TO tg_billing;                                  -- bewusst NUR Billing
GRANT SELECT ON ingest_metrics TO tg_ingest, tg_ops, tg_readonly;
GRANT INSERT ON ingest_metrics TO tg_ingest;

-- GTFS
GRANT SELECT ON gtfs_routes, gtfs_stops, gtfs_trips, gtfs_stop_times, gtfs_builds TO tg_app, tg_ingest, tg_readonly;
GRANT SELECT, INSERT, UPDATE, DELETE ON gtfs_trips, gtfs_routes, gtfs_stops, gtfs_stop_times, gtfs_builds TO tg_ingest;
GRANT SELECT, INSERT, UPDATE, DELETE ON rt_trip_state, gtfs_alerts, alert_noise_rules TO tg_ingest;
GRANT SELECT ON rt_trip_state, gtfs_alerts, alert_noise_rules TO tg_app, tg_readonly;
GRANT SELECT, UPDATE ON alert_noise_rules TO tg_ops;

-- Sequenzen für Identity-Spalten
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO tg_app, tg_ingest, tg_billing;

-- ---------------------------------------------------------------------------
-- 2) RLS: entitlements — härteste Grenze. Nur tg_billing sieht Zeilen.
-- ---------------------------------------------------------------------------
ALTER TABLE entitlements ENABLE ROW LEVEL SECURITY;
ALTER TABLE entitlements FORCE ROW LEVEL SECURITY;   -- gilt auch für Tabellen-Owner

CREATE POLICY entitlement_billing_all ON entitlements
  FOR ALL TO tg_billing USING (true) WITH CHECK (true);
-- tg_app hat ohnehin keinen GRANT => doppelt abgeriegelt (Grant-Fehler + RLS).

-- ---------------------------------------------------------------------------
-- 3) RLS: feature_flags — App darf nur öffentliche Schalter sehen (Kill-Switch-
--    Metadaten, ops-Interne bleiben unsichtbar), Ops sieht alles.
-- ---------------------------------------------------------------------------
ALTER TABLE feature_flags ENABLE ROW LEVEL SECURITY;

CREATE POLICY flags_app_read_public ON feature_flags
  FOR SELECT TO tg_app USING (is_public = true);
CREATE POLICY flags_ops_all ON feature_flags
  FOR ALL TO tg_ops USING (true) WITH CHECK (true);
CREATE POLICY flags_readonly_public ON feature_flags
  FOR SELECT TO tg_readonly USING (is_public = true);

-- ---------------------------------------------------------------------------
-- 4) RLS: reports — App sieht/ändert nur Meldungen aktiver Städte.
--    Wirkt als Kill-Switch-Stufe 3: deaktivierte Stadt => Daten unsichtbar,
--    selbst wenn ein Endpoint fehlerhaft keinen Flag-Check macht.
-- ---------------------------------------------------------------------------
ALTER TABLE reports ENABLE ROW LEVEL SECURITY;

CREATE POLICY reports_app_city_active ON reports
  FOR SELECT TO tg_app
  USING (EXISTS (SELECT 1 FROM cities c WHERE c.city_id = reports.city_id AND c.is_active));
CREATE POLICY reports_app_insert ON reports
  FOR INSERT TO tg_app WITH CHECK (
    EXISTS (SELECT 1 FROM cities c WHERE c.city_id = reports.city_id AND c.is_active)
    AND (SELECT enabled FROM feature_flags f WHERE f.key = 'reports_enabled' AND f.is_public));
CREATE POLICY reports_app_update ON reports
  FOR UPDATE TO tg_app
  USING (EXISTS (SELECT 1 FROM cities c WHERE c.city_id = reports.city_id AND c.is_active));
CREATE POLICY reports_ingest_all ON reports
  FOR ALL TO tg_ingest USING (true) WITH CHECK (true);   -- TTL-Sweeper, Degradation, Retention
CREATE POLICY reports_readonly_active ON reports
  FOR SELECT TO tg_readonly
  USING (EXISTS (SELECT 1 FROM cities c WHERE c.city_id = reports.city_id AND c.is_active));

ALTER TABLE report_events ENABLE ROW LEVEL SECURITY;
CREATE POLICY report_events_app_read ON report_events
  FOR SELECT TO tg_app
  USING (EXISTS (SELECT 1 FROM cities c WHERE c.city_id = report_events.city_id AND c.is_active));
CREATE POLICY report_events_app_insert ON report_events
  FOR INSERT TO tg_app WITH CHECK (
    EXISTS (SELECT 1 FROM cities c WHERE c.city_id = report_events.city_id AND c.is_active));
CREATE POLICY report_events_ingest_all ON report_events
  FOR ALL TO tg_ingest USING (true) WITH CHECK (true);

-- ---------------------------------------------------------------------------
-- 5) RLS: devices/trust_scores — App arbeitet nur auf nicht gesperrten Geräten
-- ---------------------------------------------------------------------------
ALTER TABLE devices ENABLE ROW LEVEL SECURITY;
CREATE POLICY devices_app ON devices
  FOR ALL TO tg_app USING (is_blocked = false) WITH CHECK (is_blocked = false);
-- gesperrte Geräte: nur tg_ops (via Grant nicht vorhanden => Superuser/Betreiber)

-- ---------------------------------------------------------------------------
-- 6) Hinweis Migrationen/Sequences: ALTER TABLE … ENABLE ROW LEVEL SECURITY
--    betrifft nicht den Owner-Migrationuser (Superuser umgeht RLS per Definition).
-- ---------------------------------------------------------------------------

COMMIT;
