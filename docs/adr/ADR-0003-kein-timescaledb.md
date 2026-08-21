# ADR-0003 — Keine TimescaleDB: deklarative Tages-Partitionen
**Kontext:** v0.1 nannte TimescaleDB; das Übergabe-Prompt (maßgeblich) setzt nur PostgreSQL+RLS+PostGIS. Bestandsaufnahme A7.
**Optionen:** (a) deklarative Partitionierung + Retention-Funktionen, (b) TimescaleDB-Extension, (c) unpartitionierte Tabelle mit DELETE-Cleanup.
**Entscheidung:** (a) — reports tag-partitioniert; `tg_ensure_report_partitions`/`tg_drop_old_report_partitions` (0004); TTL-Sweeper aktualisiert Zeilen, Retention = Partition-Drop (O(1)).
**Konsequenzen:** Kein zweites Betriebsystem im System; PK muss created_at enthalten;Queries über Partitionsgrenzen planen (Partitions-Pruning); Wechsel zu TimescaleDB später möglich, da Schema-kompatibel.
