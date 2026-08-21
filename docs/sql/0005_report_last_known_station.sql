-- ============================================================================
-- TransitGuard Migration 0005 — reports.last_known_station_id
-- Fund 21.08. (Session 4, PG-Lauf): Domänenfeld für B.5 (Trip-Ende-Derivation:
-- „Kontrollteam zuletzt in …") war im Kanon-Schema von 0001 nie angelegt.
-- ============================================================================

BEGIN;
ALTER TABLE reports ADD COLUMN IF NOT EXISTS last_known_station_id TEXT;
COMMENT ON COLUMN reports.last_known_station_id IS 'Zuletzt interpolierter Halt eines Trip-Ankers (B.5-Derivation, C.3.2-Degradation)';
COMMIT;
