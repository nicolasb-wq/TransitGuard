-- 0006_hangfire.sql — Hangfire-Auftragsspeicher in Postgres statt im Prozessspeicher.
--
-- Warum: MemoryStorage verliert bei jedem Neustart alle Auftraege und Sperren. Ein
-- Deploy (Symlink-Swap + Restart, docs/10 §5) mitten in einem StaticSync haette den
-- Auftrag stumm verschluckt. Mit Postgres-Storage ueberlebt der Zeitplan den Neustart,
-- und die verteilte Sperre verhindert Ueberlappung (docs/07 §1).
--
-- Rollenmodell (0001_core.sql): tg_ingest ist ausdruecklich die Rolle der Ingest-/Sync-
-- Jobs. Hangfire bekommt ein EIGENES Schema, damit seine Tabellen nicht im public-Schema
-- neben den fachlichen Tabellen liegen und tg_app sie nicht sieht.
--
-- Hangfire legt seine Tabellen selbst an (PrepareSchemaIfNecessary). Diese Migration
-- schafft nur das Schema und die Rechte — die DDL bleibt bei Hangfire, damit ein
-- Versionswechsel der Bibliothek nicht an einer handgepflegten Kopie scheitert.

CREATE SCHEMA IF NOT EXISTS hangfire;

-- tg_ingest darf im Schema alles (inkl. CREATE fuer die Selbstanlage der Tabellen).
GRANT USAGE, CREATE ON SCHEMA hangfire TO tg_ingest;
ALTER SCHEMA hangfire OWNER TO tg_ingest;

-- Betrieb darf mitlesen (Dashboard/Debug), aber nichts aendern.
GRANT USAGE ON SCHEMA hangfire TO tg_ops, tg_readonly;
ALTER DEFAULT PRIVILEGES FOR ROLE tg_ingest IN SCHEMA hangfire
  GRANT SELECT ON TABLES TO tg_ops, tg_readonly;

-- tg_app bekommt bewusst NICHTS: die API-Anfragen brauchen den Auftragsspeicher nicht.
-- (Gegenprobe dazu: T-HANGFIRE-RLS in tests/TransitGuard.Api.Tests/PostgresHangfireTests.cs)
