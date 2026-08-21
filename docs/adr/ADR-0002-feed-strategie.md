# ADR-0002 — gtfs.de als Basis-Feed; regionale Overlays nur bei nachgewiesenem Mehrwert
**Kontext:** gtfs.de: DE-weit, 98,5/97,7 % Delay-Quote, CC BY-SA 4.0 (Anbieter bestätigt), regeneriert alle 10 s. VBB: 43,8 % Delay, 0 Alerts, aktive Datenlücke seit 04.06.2026 (Statusseite), CC BY 4.0.
**Optionen:** (a) nur gtfs.de, (b) gtfs.de + VBB, (c) je Stadt Best-Feed, (d) Geofox-Integration Hamburg.
**Entscheidung:** (a) für MVP; (c)/(d) als Overlay-Pfad ohne Architekturumbau (`cities.gtfs_rt_source` pro Stadt konfigurierbar; VBB z. B. per Config zuschaltbar inkl. 60-req/min-Budget und Atom-Feed-Abo).
**Konsequenzen:** Ein Poll-Job, 60 req/h Last beim Anbieter; Overlay-Validierung durch VBB-Befund empirisch gestützt; Overlay-Auswahl braucht je Stadt eine J4-artige Prüfung.
