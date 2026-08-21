# ADR-0001 — Trip-Anker statt Fahrzeug-/Positions-Anker
**Kontext:** Beide verfügbaren Feeds (gtfs.de, VBB) liefern 0 VehiclePositions; `vehicle`-Feld bei gtfs.de in 8,4 % der TUs vorhanden, aber id/label leer (Messung 2026-08-20 + 2026-08-21, doppelt). GPS versagt unterirdisch.
**Optionen:** (a) Trip-Anker + Fahrplan-Interpolation, (b) Fahrzeug-Anker über vehicle.id, (c) Nutzer-GPS-Crowdpositionen, (d) VP-Zuschaltung sobald verfügbar.
**Entscheidung:** (a). VP-Pfad bleibt Schema-Flag (`cities.rt_contains_vp`), wird nicht implementiert. Nutzer-GPS verlässt das Gerät nicht.
**Konsequenzen:** Umlauf-Bruch an Endstationen (B.4-Tabelle) = akzeptierte Lücke, B.5-Derivation mildert; „Live"-Label entfällt; Ingest muss `HasField` vs. leeres Feld unterscheiden (Metrik, Test T-NORM-5). Widerruf nur bei VP-Lieferung eines Regional-Feeds mit Beweiswert.
