# TransitGuard — Messbericht J3/J6 + Städtevergleich (2026-08-21, 2. Messrunde)

**Auftrag:** Alle empirischen Gates vor Stadt-Commitment Hamburg klären (Auftrag 21.08.2026). **Methode:** Eigene Messung: GTFS-Static (gtfs.de free: `nv_free` 262 MB ZIP/1,6 Mio. Trips, `rv_free`, `fv_free`; Stand 15.08.2026) + 2 frische RT-Snapshots (10:03/10:04 UTC = 12:03/12:04 MESZ). Skripte & Rohausgaben: `verifikation/j3j6.py`, `verifikation/citycmp.py`.

## 1. J6 — Static↔RT-Konsistenz: **GRÜN (100 %)**

| Messung | Wert |
|---|---|
| RT-TripUpdates | 82.568 |
| davon im 6 Tage alten Static auflösbar | **82.568 = 100,0 %** |
| Verteilung | nv (Nahverkehr) 76.210 · rv (Regional) 6.192 · fv (Fern) 166 |
| Stop-Lookup (RT-Stop-IDs → Static-Stops) | 875.527 ok / **15 Miss** (99,998 %) |

**Interpretation:** trip_ids sind zwischen Static-Generationen stabil (hier: 6 Tage überbrückt). Die Anbieter-Behauptung „RT passt zu den Static-Feeds" ist empirisch bestätigt. **G10 wird von „Mittel" auf „Niedrig (Restrisiko Tageswechsel/Ferienwechsel)" herabgestuft** — die Match-Rate bleibt als Dauer-Metrik mit Alarm-Schwellen (0,90/0,70) im Ingest, genau für den Fall, dass sich die Pipeline einmal ändert. *Vorbehalt:* Einzelmessung; der kritische Moment ist der Static-Refresh (Sa 04:22 UTC wöchentlich) — dort die Match-Rate beobachten (Roadmap T2.6 misst automatisch).

## 2. J3 — Hamburg-Anteil: **GRÜN, beste Stadt der Stichprobe**

| Messung (12:04 MESZ, Normalbetrieb) | Wert |
|---|---|
| TUs mit ≥1 Stop im Kern-BBox (53.40–53.75 N, 9.73–10.32 E) | **3.740 (4,5 % aller DE-TUs)** |
| TUs inkl. Umland-BBox (53.30–53.85, 9.55–10.50) | 4.319 (5,2 %) |
| Distinct Hamburger Trips | 3.740 |
| davon mit Echtzeit-Delay | **3.533 = 94,5 %** |
| Agenturen (distinct Trips, Kern) | Hamburger Verkehrsverbund 3.479 · S-Bahn Hamburg 88 · DB Fernverkehr 42 · DB Regio Nord 36 · Nordbahn 32 · metronom 29 · **AKN 29** · DB Regio Nordost 4 |

**Interpretation:** „Hamburg und Umgebung" ist über den einen gtfs.de-Feed voll versorgt: ~3.700 gleichzeitige Fahrten mittags (U-Bahn, Bus, Tram(?), S-Bahn, AKN, Regional als Durchfahrer), 94,5 % mit Verspätungsdaten. Für die Meldungsdichte heißt das: Jede Fahrt der Region ist als Trip-Anker adressierbar. **Stadt-Commitment Hamburg: erteilt.**

## 3. Städtevergleich — und der strategische Berlin-Befund

Gleiche Methode, gleicher Snapshot (TU-Zahl je Stadt-BBox; Delay-% hier nur für Hamburg belastbar, siehe Skript-Anmerkung):

| Stadt | gleichzeitige TUs via gtfs.de |
|---|---|
| **Hamburg (Kern)** | **3.735** |
| München | 2.716 |
| Köln | 2.188 |
| Frankfurt | 2.178 |
| Dresden | 1.031 |
| **Berlin (Stadt)** | **984** |

**Befund Berlin:** gtfs.de liefert für Berlin erstaunlich wenig (BVG-Realtime fließt offenbar primär durch die VBB-Eigenpipeline — das erklärt die Existenz und Größe des VBB-Feeds). Zwei Konsequenzen: (a) die v1.1-Entscheidung „VBB nicht pollen" bleibt für Hamburg korrekt, (b) **eine spätere Berlin-Expansion braucht zwingend den VBB-Feed als Overlay** (per `cities.gtfs_rt_source`-Config zuschaltbar, genau dafür gebaut) — trotz der dortigen Datenlücke (aktiv, seit 04.06.2026). Die Overlay-Architektur ist damit nicht nur theoretisch validiert, sondern für Berlin nachweislich Pflicht.

## 4. Konsequenzen für Konfiguration & Betrieb

1. **Stadt-Extrakt (T2.3) nutzt die Umland-BBox** („Hamburg und Umgebung"): 53.30–53.85 N, 9.55–10.50 E — SQL-Seed entsprechend angepasst (0004). Erwartete Whitelist-Größe: einige zehntausend Trips (aus 4.319 gleichzeitigen + Tagesverlauf; exakte Zahl liefert der erste Sync).
2. **Traffic-Korrektur (Abschnitt I):** Peak-Feed jetzt mit 47,9 MB gemessen (10:05 UTC) → **worst case ~67–69 GB/Tag Ingress** bei 60 s (statt 48 GB Abend-Niveau). Hetzner-Ingress frei, 20 TB Outbound inklusive (J8) — unkritisch, Planungszahl im Ingest-Doc aktualisiert.
3. **Static-Quellen konkret:** `download.gtfs.de/germany/{nv,rv,fv}_free/latest.zip` (nv 262 MB ZIP, wöchentlich Sa ~04:22 UTC). Nur nv+rv werden für HH gebraucht (fv 0,4 MB irrelevant klein, kann mitgeladen werden). **Lizenz-Präzisierung:** Static-Seiten verlinken CC **BY 4.0**, die Realtime-Seite nennt CC **BY-SA 4.0** — Lizenzkette im Rechtsteil aktualisiert, Attribution unchanged.
4. **Backup-Quelle Static HH (F-3 geklärt):** HVV-Rohdaten via Transparenzportal Hamburg unter **„Datenlizenz Deutschland Namensnennung 2.0"** (Attribution: „Hamburger Verkehrsverbund GmbH") — als Notfall-Alternative, falls gtfs.de ausfällt; Namensnennung dann anzupassen.
5. **citycmp.py-Hinweis:** Delay-Prozent je Stadt war im ersten Lauf fehlerhaft hochgezählt (Zähler im Stop-Loop); Skript korrigiert archiviert; gültiger Delay-Wert für Hamburg stammt aus j3j6.py (94,5 %).
