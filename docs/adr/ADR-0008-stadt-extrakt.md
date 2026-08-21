# ADR-0008 — Stadt-Extrakt + In-Mem-Whitelist statt DE-weitem Static-Bestand
**Kontext:** gtfs.de Static ist DE-weit (~2 Mio. Fahrten lt. Anbieter); MVP braucht eine Stadt.
**Optionen:** (a) alles laden + filtern zur Laufzeit, (b) Stadt-Extrakt beim Sync (bbox + Trip-Closure) + Whitelist-RAM + PK-Fallback, (c) nur Postgres-Lookups.
**Entscheidung:** (b) (entspricht C.3.1/V1.1, präzisiert). Builds mit build_id + active/retired = atomarer Swap (C.3.2).
**Konsequenzen:** RAM-Budget klein & planbar; Kaltstart aus DB; Extrakt-Erzeugung ist Teil des StaticSync-Jobs (Aufwand einmal je Sync); J3 misst zugleich die Extraktgröße (HVV-Eignung).
