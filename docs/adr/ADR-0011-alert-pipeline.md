# ADR-0011 — Alerts: Pipeline mit Noise-Filter, kein UI-Prominenz
**Kontext:** Messung 2026-08-21: ~96,5 k Alert-Entities = 519 distinct; >90 % Attributierungs-/Ausstattungs-Duplikate; echte Störungen niedriger dreistellig, langlaufend; HVV-Bezug ~null.
**Optionen:** (a) voller Alert-Ingest in UI (Rauschen flutet), (b) Pipeline + Regeln + Dedup, dezente UI (Free-Feature), (c) kein Alert-Ingest.
**Entscheidung:** (b). Noise-Regeln DB-Tabelle (hot-fixbar), Dedup-Key sha256(header|desc[:150]), UI-Fenster last_seen+6 h. v1.1-Hoffnung „Stand-Alone-Wert" damit zurückgenommen (Bestandsaufnahme A1).
**Konsequenzen:** Geringe Mehrkosten (gleiche Pipeline); Störungen sind Daily-Driver-Nebenwert, kein Verkaufsargument; Attribution-Strings fließen als Metadatum in den Attribution-Service (Rechtsteil §1).
