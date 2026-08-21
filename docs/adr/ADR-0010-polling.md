# ADR-0010 — Polling 60 s, ein Feed; ETag ohne Ersparnis-Erwartung
**Kontext:** Feed regeneriert alle 10 s (Anbieterdokumentation; eigene ETag-Beobachtung konsistent). J1-Messung: VBB 304 ✓, gtfs.de nie (immer neue Generation).
**Optionen:** (a) 60 s, (b) 30 s, (c) 120 s, (d) ETag-basiertes Conditionals-Polling als Sparstrategie.
**Entscheidung:** (a); If-None-Match senden (kostenlos, greift bei langsamen Feeds/Overlays), aber kein Traffic-Sparen einplanen.
**Konsequenzen:** ~48–61 GB/Tag Ingress (Tagesgang!) — bei Hetzner ingress frei (J8 ✓); Datenalter im Mittel ~40 s (60 s Poll + 10 s Generation) — für Verspätungsprognosen ausreichend (v1.1-Begründung). 30 s nur falls UX-Messung (Post-Launch) es fordert.
