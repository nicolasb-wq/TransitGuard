# ADR-0004 — PostGIS ohne H3 im MVP
**Kontext:** v0.1 führte Uber-H3 für Geo-Queries an; MVP braucht nur Stationsnähe + Bbox-Filter + Karte.
**Optionen:** (a) nur PostGIS (GIST), (b) PostGIS+H3 von Anfang an.
**Entscheidung:** (a). H3 wird erst mit Muster-Analytik (Post-Launch, Pro-Feature #7) relevant.
**Konsequenzen:** Eine Abhängigkeit weniger; spätere H3-Einführung nur im Analyse-Pfad, nicht im Hot-Path.
