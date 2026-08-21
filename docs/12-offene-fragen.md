# TransitGuard — Offene Fragen (aktualisiert 2026-08-21, nach 2. Messrunde + Rechtsrecherche)

**Großer Stand:** Alle empirischen Gates **GRÜN** (J3, J6 — Details `14-j3-j6-messung.md`); Rechtslage umfassend recherchiert (`15-rechtsstand-2026-08.md`); PO-Entscheidungen F-15/F-16 im Auftrag vom 21.08. delegiert entschieden. **Implementierung kann starten.** Restliste:

## Erledigt (dieser Turn)

| # | Frage | Ergebnis | Beleg |
|---|---|---|---|
| ~~J3~~ | HVV-Anteil gtfs.de-TUs | **GRÜN:** 3.740 gleichzeitige HH-Trips mittags (4,5 % DE), Umland 4.319; 94,5 % mit Delay; Agentur „Hamburger Verkehrsverbund" dominiert | 14-j3-j6-messung §2 |
| ~~J6~~ | Static↔RT-Konsistenz | **GRÜN:** 100,0 % Match gegen 6 Tage alten Static; Stop-Lookup 99,998 %; G10 → Niedrig | §1 |
| ~~J1~~ | If-None-Match/304 | VBB 304 ✓; gtfs.de nie (10-s-Regeneration) | Bestandsaufnahme N4 |
| ~~J2/J2b~~ | Alert-Qualität | >99 % Duplikat (519 distinct; Attribution/Ausstattung); Rest: 7-Tage-Beobachtung als Dauercheck im Betrieb (AlertSweep-Metrik) | N1 + Ingest §7 |
| ~~J4~~ | Eigener HVV-RT-Feed | Existiert nicht (HAFAS zu, Community deprecated); Geofox nur per Anfrage → F-4 Rest | Bestandsaufnahme N5 |
| ~~J5-Vorbereitung~~ | Ticket-API-Anfrage | E-Mail-Entwurf fertig: `vorlagen/anfrage-mobilitybox.md` | — |
| ~~J7~~ | VBB-Datenlücke | Aktiv bestätigt (seit 04.06.2026, keine Prognose); Berlin-Overlay-Pflicht quantifiziert (984 TUs via gtfs.de) | 14 §3 |
| ~~J8~~ | Hetzner-Traffic | Ingress frei, 20 TB Out inklusive | Bestandsaufnahme N7 |
| ~~F-1-Vorbereitung~~ | Anwalts-Briefing | Vorlage fertig: `vorlagen/briefing-rechtsanwalt.md` | — |
| ~~F-3~~ | HVV-Static-Lizenz | DL-DE BY 2.0, Namensnennung „Hamburger Verkehrsverbund GmbH" | 15-rechtsstand §5 |
| ~~F-7~~ | DSFA | Entwurf v0.1 fertig: `17-dsfa-entwurf.md` (vor Launch finalisieren) | — |
| ~~F-8~~ | §265a-Reform/BVG-Status | Reform am 16.04.2026 vom Bundestag abgelehnt; BVG: keine Klage bekannt, „prüft fortlaufend"; **Apple entfernt FreiFahren aus dem Store (08/2026)** | 15-rechtsstand §1+§3 |
| ~~F-12~~ | Store-Server-APIs | Beide existieren wie designed: „App Store Server Notifications v2" (JWS) + Google „Real-time Developer Notifications" (Pub/Sub) | Recherche 21.08. |
| ~~F-13 (Teil)~~ | Namenskonflikt „TransitGuard" | Quick-Check: keine direkten Kollisionen in Web/Play-Suche gefunden; DPMA/EUIPO-Formalprüfung bleibt | 21.08. Recherche |
| ~~F-15~~ | Bus-TTL-Werte | **Entschieden (delegiert):** 720/1800 s Station, 480 s at_stop, Trip wie Schiene | 03-Entscheidungen |
| ~~F-16~~ | 45-min-Cap Trip-Anker | **Entschieden (delegiert):** Cap nur für Stations-Anker; Trip-Anker endet natürlich (trip_end + 120 s) | 03-Entscheidungen |
| ~~F-17~~ | FreiFahren-Benchmark | 3.518/7 d ✓ sekundärbestätigt; >55.000 meldende Nutzer (höher als Dossier-40k) — Ökonomie-Parameter leicht konservativ | 15-rechtsstand §4 |

## Weiter offen (nicht launch-blockierend, außer markiert)

| # | Frage | Nächster Schritt | Aufwand | Blockiert |
|---|---|---|---|---|
| **F-1** | **Anwaltliche Prüfung** (Straf-/Zivil-/Form/Store) | Briefing abschicken, 2–3 Angebote | 1–2 d + Wartezeit + Kosten | **Launch (hartes Gate)** |
| F-2 | ShareAlike-Umsetzung API (gtfs.de RT, CC BY-SA 4.0) | mit F-1 klären; bis dahin API-Lizenzdeklaration konservativ | in F-1 | Public-API-Öffnung |
| F-4 | Geofox getVehicleMap-Zugang | Anfrage an HVV (nur falls Overlay nötig wird) | 1 h + Wartezeit | nichts |
| F-5 | Hetzner-AVV unterschreiben | Doc-Routine | 15 min | Launch (DSGVO) |
| F-6 | Receipt-Hash-Aufbewahrung (Steuer) | Steuerberater | 30 min | Billing-Detail |
| F-9 | Impressum §5 DDG-Feinheiten | in F-1 | — | Impressum |
| F-10 | Store-Metadaten (nur noch Play + PWA für Launch) | Play-Console + PWA-Checkliste | 2 h | Play-Einreichung |
| F-11 | Konversions-Benchmark verifizieren | später eigene Daten | 2 h | Preis-Feintuning |
| F-14 | v1.0-H1–H7-Inhalte rekonstruieren | Rückfrage Auftraggeber | 15 min | nichts |
| **F-18 (neu)** | **Rechtsform** (e.V./gGmbH/GmbH) — Risiko-Dämpfung vs. Pro-Einnahmen | mit F-1 | in F-1 | Betreiber-Setup |
| F-19 (neu) | iOS-Launch-Zeitpunkt nach Apple/FreiFahren-Präzedenz | nach 6 Monaten Companion-Betrieb evaluieren | Entscheidung | nur iOS-Kanal |

## Status-Update 21.08.2026, 3. Turn (Härtungsauftrag)

| # | Früher | Jetzt |
|---|---|---|
| **F-2** | ShareAlike offen | **Empfehlung liegt bei** (15-rechtsstand §6-Recherche): CC BY-SA 4.0 Abschnitt 3(a)+4 — API-Ausgaben mit Transit-Datenanteil erhalten Attribution + Lizenzhinweis + Änderungsangabe; unsere API-Bedingungen deklarieren diese Anteile als CC BY-SA 4.0. Finale anwaltliche Formulierung mit F-1. |
| **F-4** | Geofox offen | Anfrage-Vorlage erstellt: `vorlagen/anfrage-hvv-geofox.md` (fragt zugleich offizielle Ticket-Deep-Links ab — H1 braucht sie) |
| **F-6** | Receipt-Hash-Dauer offen | **Empfehlung (*Glaube ich*, Steuerberater bestätigen):** Buchungsbelege ~8–10 Jahre (§147 AO/§257 HGB); wir lagern die Abrechnung auf die Store-Berichte aus (Apple/Google liefern Monatsreports) und behalten nur Hashes bis Widerruf + steuerlich benötigte Aggregatreports. Keine Nutzerdaten davon betroffen. |
| **F-9** | Impressum offen | Entwurf erstellt: `vorlagen/impressum-entwurf.md` (§5-DDG-Struktur; mit F-1 finalisieren) |
| **F-18** | Rechtsform offen | **Empfehlung:** UG (haftungsbeschränkt), später GmbH + IT-/Medienhaftpflicht & D&O — privates Vermögen aus dem Titelrisiko; e.V. passt nicht zu Pro-Einnahmen. Mit F-1 Frage 4 abstimmen. |
| **NEU H1–H6** | — | Rechtliches Härtungspaket spezifiziert & eingebaut: `18-rechtliche-haertung.md` (Ticket-First-Gate in API/Hub/Tests/Roadmap; AGB-, Impressum-, Charta-, DSA-, Monitoring-Bausteine). Anwaltsfragen 8–10 ergänzt. |
| **F-1** | Launch-Gate | **Einzig verbleibendes hartes Gate.** Briefing ist vollständig (10 Fragen + 5 Vorlagen/Entwürfe). Absendung = Aufgabe des Betreibers. |
