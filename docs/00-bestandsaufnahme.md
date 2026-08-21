# TransitGuard — Bestandsaufnahme der Übergabe (v1.1 → v1.2-Pflicht)

**Datum:** 2026-08-21 · **Autor:** Technischer Lead (Neuübernahme) · **Status:** verbindliche Grundlage für alle Folgeartefakte
**Update 21.08. (2. Messrunde, nach Auftragserteilung):** J3/J6-Stadt-Gates **GRÜN**, Rechtslage recherchiert, F-15/F-16 entschieden → `14-j3-j6-messung.md`, `15-rechtsstand-2026-08.md`, `16-innovationen.md`, `17-dsfa-entwurf.md`, aktualisiertes `12-offene-fragen.md`. Stadt-Commitment Hamburg+Umland erteilt.
**Methode:** Unabhängige Nachmessung beider Feeds am 2026-08-21, 09:37–09:41 UTC (eigene Skripte, `gtfs-realtime-bindings`, Abruf protokolliert in `verifikation/`), plus Recherche der externen offenen Punkte J1/J4/J7/J8 und der gtfs.de-Anbieterdokumentation. Keine Zahl unten unbelegt übernommen.

---

## 1. Verifikation der Dossier-Messwerte (2026-08-20)

| Merkmal | Dossier (20.08., 17:22 UTC) | Eigene Messung (21.08., ~09:38 UTC, Mittag) | Befund |
|---|---|---|---|
| gtfs.de Größe | 33.421.155 B | 38,9 – 42,4 MB über 5 Abrufe (innerhalb von 3 Minuten!) | **Bestätigt + wichtige Erweiterung:** Feedgröße schwankt stark (Tageszeit ±20 %, minütlich ±5 %). Planungszahlen müssen Peak-Werte nutzen. |
| gtfs.de Entities | 122.745 (TU 55.314, Alert 67.431, VP 0) | 178.211 (TU 81.262, Alert 96.949, VP 0) | Struktur bestätigt; absolute Werte tageszeitabhängig. |
| VehiclePositions | 0 | 0 | **Bestätigt.** |
| TU `route_id` leer | ja | 81.262/81.262 = 100 % leer | **Bestätigt.** C.3.1-Regel bleibt Pflicht. |
| `vehicle`-Feld im TU | „fehlt" | Feld bei 6.819 TUs (8,4 %) **präsent, aber id/label leer** (`id='' label=''`) | **Widerspruch in der Formulierung, Bestätigung im Schluss** (keine nutzbare Fahrzeug-Referenz). Detail unten, A2. |
| TU mit Delay-Wert | 54.481 (98,5 %) | 79.372 (97,7 %) | **Bestätigt** (Größenordnung stabil). |
| — | nicht gemessen | Delay-Magnitude (n=200.000): **Median 0 s**, Mittel 63 s, p90 186 s, min −30.384 s, max +7.148 s | **Neu.** Drei Konsequenzen unten (N2, B3, B4). |
| — | nicht gemessen | STU je TU: gtfs.de **Median 3** (p90 25) vs. VBB Median 21 | **Neu.** gtfs.de liefert „Kurz-TUs" — Detail unten (N3). |
| VBB | 7.699 TUs, 0 Alerts, 40,3 % Delay | 8.166 TUs, 0 Alerts, 43,8 % Delay, `route_id` 100 % gesetzt, `vehicle` 0, STU-Median 21 | **Bestätigt.** |
| VBB `schedule_sha256` | 48f25664 | **unverändert 48f25664** | Bestätigt; kein Static-Wechsel zwischen den Messungen beobachtbar (erwartbar, kein Widerspruch). |
| VBB Content-Type | application/protobuf; schedule_sha256 | identisch | Bestätigt. gtfs.de: kein Content-Type, kein Cache-Control gesetzt — ebenfalls wie Dossier. |

**Arithmetik des Dossiers (Abschnitt I) selbst nachgerechnet:** 33.421.155 + 8.296.039 = 41.717.194 B ✓; ×2.880 = 120,15 GB/Tag ✓; ×1.440 = 60,07 ✓; gtfs.de-only 60 s = 48,13 GB/Tag ✓; Entities/Tag 375.678.720 ✓ (130.444 × 2.880). Alle Rechnungen korrekt. **Ergänzung:** Mittagsmessung ergibt ~58–61 GB/Tag (gtfs.de-only, 60 s) — korrigierter Planungsbereich: **~48–61 GB/Tag eingehend**.

## 2. Neue eigene Befunde (nicht im Dossier)

**N1 — Alert-Rohzahl ist zu >99 % Duplikat, kein Störungsinhalt (J2 vorab beantwortet).**
96.400–96.949 Alert-Entities → nur **519 distinct Inhalte** (Dedup-Schlüssel `sha256(header + "|" + description[:150])`). Die größten Gruppen:

| Gruppe | Entities | Inhalt |
|---|---|---|
| leerer Header, desc „Echtzeitdaten aufbereitet von GTFS.de, bereitgestellt von DELFI" | 64.246 | **Attributierungs-Marker** (Lizenz-Namensnennung, pro Trip repliziert) |
| „… bereitgestellt von Verkehrsverbund Bremen" | 12.900 | Attributierung |
| „… Rhein-*" (3 Gruppen) + „Nahverkehrsgesellschaft …" + „VAG Nürnberg" … | ~9.300 | Attributierung je Verbund |
| „Rollstuhlgeeignet" / „Niederflur" / „NIEDERFLUR" | 4.328 | **Fahrzeug-Ausstattungsnotizen**, pro Trip |
| Bordrestaurant, WLAN verfügbar, Klimaanlage, nur 2. Kl., … | mehrere hundert | Ausstattung |
| Echte Störungen/Baustellen (Stuttgart S-Bahn-Sperrung, Karlsruhe AVG, Haltestellenverlegungen …) | Rest: niedriger dreistellig distinct | realer, überwiegend **langlaufender** Inhalt |

Masse klassifiziert mit `cause=UNKNOWN_CAUSE`, `effect=UNKNOWN_EFFECT`, `severity=INFO`. Kein `active_period.end` in irgendeinem Alert (Dauer-Filterung über active_period unmöglich). Stichprobe von 12 Volltexten: überwiegend Baustellen-/Umleitungshinweise mit Wochen-Dauern.
**Konsequenz:** Die v1.1-Hoffnung (Abschnitt 4/F: „könnte gleichwertiger Stand-Alone-Wert werden") ist in dieser Form **widerlegt**. Nach Filterung bleibt bundesweit ein niedriger dreistelliger Bestand an echten Hinweisen, für Hamburg in meiner Stichprobe **praktisch null** (kein „HVV"-Attributierungs-Marker sichtbar; Hamburg-Keywords ~0 Treffer). Störungs-Feature bleibt Nebenfeature mit Filter-Pipeline; J2 ist im Wesentlichen erledigt (Rest: einmalige Dauerbeobachtung über 7 Tage zur Bestätigung der Verteilung).

**N2 — „98,5 % verspätungskorrigiert" ist schief interpretiert.** 97,7 % der TUs tragen Delay-Daten, aber der **Median-Delay ist 0 s**. Richtig ist: Der Fallback-Pfad hat fast immer *frische Ist-Daten* — meist mit der Aussage „punktuell/pünktlich". Die v1.1-Formulierung „fast immer verspätungskorrigiert" suggeriert große Korrekturen. Kein Architektur-Bruch, aber das Nutzer-Label „Echtzeit-Prognose" muss ehrlich bleiben: Prognosegüte hängt an Median-0-Daten.

**N3 — gtfs.de-TUs sind „Kurz-TUs" (Median 3 StopTimeUpdates, VBB: 21).** Bestätigt indirekt die Architektur (Voll-Interpolation zwingend über Static + Delay-Anker), widerlegt aber jede Hoffnung, aus TU-STUs allein ganze Fahrtenverläufe zu lesen. Auch: Die Dossier-Beobachtung „28 STU" (VBB-Beispiel) darf nicht auf gtfs.de verallgemeinert werden.

**N4 — J1 (If-None-Match/304): VBB ja, gtfs.de praktisch nie.** VBB: 2× hintereinander **304 mit 0 Byte** (sofort und +5 s). gtfs.de: bei jedem Replay **200 mit neuem ETag** — und die Anbieterseite dokumentiert: *„Er wird alle 10 Sekunden aktualisiert."* Bei 60-s-Polling ist 304 für gtfs.de mathematisch ausgeschlossen (Feed ist immer ≥1 Generation weiter). Header weiter senden (kostenlos), aber keine Ersparnis einplanen. Meine ETags wechselten sogar innerhalb von 20 s — inklusive Wechsel strong↔weak (`"268b93e-…"` → `W/"286b155-…"`): **ETag nicht als Inhalts-Hash missbrauchen.**

**N5 — J4 (eigener HVV-Feed): Es gibt keinen offiziellen HVV-GTFS-RT-Feed.** Der lange genutzte Community-Weg lief über die HVV-HAFAS-API, diese ist **abgeschaltet**; das Community-Projekt (`hamburg-gtfs-rt-server`) ist als deprecated markiert. Rest-Option wäre die HVV-eigene Geofox-API (Stichwort `getVehicleMap`) — Konditionen/Lizenz für Dritte **ungeklärt** (nicht geprüft, keine Quelle gefunden, die freien Zugang bestätigt). plan A bleibt gtfs.de; Overlay über Geofox wäre ein J4-Follow-up mit Anfrage-Aufwand.

**N6 — J7 (VBB-Datenlücke): live bestätigt.** Die Statusseite des Feeds meldet weiterhin: Datenlücke seit **2026-06-04 16:00**, keine Behebungsprognose. Zusätzlich dokumentiert die Seite: Lizenz **CC BY 4.0** (nicht BY-SA), Rate-Limit **60 Requests/min**, ETag+CORS vorhanden, Atom-Feed für Störungsnachrichten existiert (abonnieren, wenn Berlin je relevant wird).

**N7 — J8 (Hetzner): eingehender Traffic wird nicht abgerechnet.** Hetzner-Quellen (Doku-Zitat + Produktseiten 2025/26): nur ausgehender/interner Traffic zählt; Cloud-Server inkludieren 20 TB Outbound/Monat. Bei ~48–61 GB/Tag Ingress (und wenigen GB Egress im MVP) ist Traffic **endgültig kein Blocker**. Vor Vertragsabschluss aktuelle Konditionen prüfen (Quellen: siehe §6).

**N8 — gtfs.de-Anbieterdokumentation (neu gesichtet).** Lizenz des RT-Streams: **CC BY-SA 4.0** — die v0.1-Behauptung stimmt. Die Feeds werden **aus dem öffentlichen DELFI-NeTEx-Datensatz generiert**; RT „passt zu allen angebotenen statischen Fahrplandatensätzen" (Anbieter-Behauptung — genau das prüft J6 empirisch). Veröffentlichung „ohne Gewähr auf Korrektheit, ständige Verfügbarkeit, Vollständigkeit". Keine dokumentierte Rate-Limit-Policy für den Free-Stream (1 Request/60 s liegt trotzdem um Größenordnungen unter jeder bekannten Schwelle — Glaube ich; Verhaltenskodex: User-Agent mit Kontaktmöglichkeit senden).

## 3. Widersprüche, Fehler, Lücken im Briefing

### Kategorie A — entscheidungsrelevant

| # | Befund | Schwere | Behandlung |
|---|---|---|---|
| **A1** | **Alert-Nutzwert-These (v1.1 §4, §6, G1') ist in der Rohform widerlegt** (N1). „67.431 Alerts" sind faktisch ~519 Inhalte, davon >90 % Attributierung + Ausstattung. | Hoch | Downgrade: Alerts = gefiltertes Nebenfeature; UI-Prominenz-Entscheidung fällt nicht mehr, J2 ist erledigt. Pipeline mit Noise-Regeln (gemessene Muster, siehe Ingest-Doc §7). |
| **A2** | **„vehicle-Feld fehlt" ist faktisch unpräzise** (N1-Messung: 8,4 % präsent, aber inhaltsleer). Für den Ingest ist der Unterschied `HasField` vs. `leerer Wert` technisch relevant (Schema-Evolution beobachtbar halten). | Mittel | B.4 bleibt in Kraft (keine nutzbare Referenz). Ingest trennt „Feld abwesend" / „Feld leer" in Metriken. |
| **A3** | **Feed-Regeneration alle 10 s (Anbieter)** vs. v1.1-Annahme „~30 s". Ändert nichts an der 60-s-Entscheidung, entwertet aber ETag-Ersparnis final und erklärt N4. | Niedrig | Abschnitt I korrigieren: 304-Erwartung = 0 für gtfs.de. |
| **A4** | **HVV-Anteil ungemessen (J3) — Alert-Seite liefert ein schwaches Indiz *gegen* starke HVV-Präsenz** (keine HVV-Attributierungsgruppe sichtbar). TU-Seite weiterhin offen. | Hoch für Stadt-Commitment | J3 bleibt **Pflicht-Gate vor Hamburg-Commitment** (nur noch TU-Messung nötig, ~1 h). |
| **A5** | **VBB-Lücke aktiv + Lizenz CC BY 4.0 + 60 Req/min** (N6) — Berlin-Option weiter geschwächt; Lizenzangaben im Rechtsteil ergänzt. | Mittel | Berlin bleibt Reserve; bei Reaktivierung: Atom-Feed abonnieren. |
| **A6** | **Kein offizieller HVV-RT-Feed** (N5) — die v1.1-Hoffnung „eine Stadt, ein Feed, keine Sonderintegration" trägt nur über gtfs.de/DELFI. | Mittel | Overlay-Architektur bleibt; Geofox-Anfrage als optionaler Follow-up (nicht MVP-blockierend). |
| **A7** | **Stack-Konflikt:** v0.1 nennt TimescaleDB + H3; das Übergabe-Prompt (maßgeblich, „gesetzt") nennt PostgreSQL+RLS+PostGIS ohne beides. | Mittel | Aufgelöst per ADR-0003 (deklarative Tages-Partitionen statt TimescaleDB) und ADR-0004 (kein H3 im MVP). |
| **A8** | **Verlorene v1.0-Abschnitte D/E/H:** B.5 referenziert „Bus-Werte aus Abschnitt D" — D existiert im Dossier **nicht**. Ebenso E (Ticketing-Entscheidung) und H1–H7 nur aus dem Kompaktbriefing rekonstruierbar. Übergabedefekt. | Mittel | Bus-TTL-Werte hier neu vorgeschlagen (§5.1), als Entscheidung markiert; H-Verlust dokumentiert (12-offene-fragen.md). |

### Kategorie B — Spezifikationslücken (in diesem Paket geschlossen)

| # | Lücke | Lösung hier |
|---|---|---|
| B1 | **G11-Gate fehlt:** Bei gtfs.de-Ausfall/-Degradation verschwinden alle trip_ids → v1.1-Regel „trip_id weg + Soll-Ende überschritten → wie Trip-Ende" würde **alle aktiven Trip-Anker massenhaft degradieren** — ein Feed-Zwischenfall würde die Karte leeren. | Neue Regel (Ingest-Doc §6.4): Degradation nur bei nachweislich gesundem Feed (Entity-Zahl ≥ 50 % des gleitenden Wochentags-Mittels, Feed-Alter < 5 min); sonst Meldungen einfrieren + Banner „Echtzeitdaten gestört". |
| B2 | Delay-Ausreißer (−30.384 s gemessen) würden Interpolationen ins Absurde verschieben. | Clamping-Konfiguration `delay_clamp_s = [-120, +7200]`, verletzte Werte → Metrik `delay_clamped_count`. |
| B3 | Interaktion der TTL-Regeln untereinander (Restfahrzeit vs. 45-min-Cap vs. Bestätigungs-Boni) war nie vollständig definiert. | TTL-Engine als deterministischer Regelkern mit Vorlagen, siehe Architektur §5 + Testplan; **offene Empfehlung:** 45-min-Cap gilt für Stations-Anker, Trip-Anker endet natürlich am Trip-Ende (v1.2-Entscheidungsvorschlag, siehe §5.2). |
| B4 | `reports`-Schema aus v0.1 kennt keine Trip-Anker (anchor_type, trip_id, start_date, parent_report_id, Degradation) — B.4/B.5-Semantik ist im Datenmodell **nicht** abgebildet. | Komplette Neufassung der Migrationen (sql/0001–0004). |
| B5 | Trust-Ränge in v0.1 defekt: Start 50 Punkte ⇒ sofort „Scout" (26–75); „Neuling" (0–25) ist unerreichbar. | Grenzen korrigiert (Neuling 0–49 bei Start 50 → Neuling bis 49? siehe Entitlement/Trust-Sektion: Start 50 = Neuling 0–59, Scout 60–99, Wächter 100–149, Legende 150+). |
| B6 | Gegen-Meldungs-Missbrauch (Troll/Betrieb widerspricht echte Meldungen weg) war nur unvollständig geschützt. | Regeln: Widerspruch wirkt nur bei Vertrauens-Parität (Actor-Trust ≥ Reporter-Trust) oder ≥2 Widersprüchen; sonst nur Review-Flag. |
| B7 | Push-Infrastruktur (FCM/APNs) fehlt im gesteckten Stack, ist aber Pro-Kernfeature (Stammstrecken-Push). | Roadmap: Push **nach** Launch (M10), Pro-Launch erst mit Push; DSGVO-Folgen im Rechtsteil. |
| B8 | Kill-Switch vs. Offline-Caching: Meldungsdaten dürfen niemals in Service-Worker-Cache landen. | API-Regel: Cache-Control: no-store auf allen Meldungs-Ressourcen; PWA cacht nur Fahrplan. |

### Kategorie C — redaktionell / klein

- C1: „Entities/Tag"-Spalte in v1.1 §I ist processiertes Rauschen ( dieselben Entities wiederholen sich je Poll) — als Durchsatz-Metrik lesbar, nicht als Datenmenge. Umbenennen.
- C2: v0.1 S3 „~50 % Markt fehlt" (Android): In Deutschland liegt der Android-Anteil erfahrungsgemäß **über** 50 % (Glaube ich; Zahl nicht belegt) — Schwäche also eher größer als beschrieben.
- C3: Zwei verschiedene Plausibilitäts-Radien in v0.1 (eigener Pre-Fill <500 m; FreiFahren-Check 1,5 km) — Parameter vereinheitlichen (Config: `report_station_match_radius_m=750`).
- C4: Dossier „Feed-Alter 26 s bei beiden" — war vermutlich derselbe Regenerationsrhythmus; jetzt erklärt (10-s-Aktualisierung, Alter = Abrufphase im Zyklus + Build-Zeit).

## 4. Was aus v1.1 unangetastet weitergilt

Trip-Zuordnungs-Architektur (B), Normalisierungsschicht (C.3) inkl. route_id-Lookup und Static-Swap, Polling-Entscheidung (nur gtfs.de, 60 s, Filter nach Parse), Hamburg-Empfehlung (unter J3-Vorbehalt), Anonymitäts-Grundsätze, Ticket-Deep-Link-Variante A, Kill-Switch-Pflicht, keine spekulative Umlauf-Verkettung, TTL-Grundwerte (20/10/45/2), Risikoregister G2–G9. Die Messdaten-Kernzahlen des Dossiers waren **in jeder geprüften Dimension korrekt** — Kritikpunkte betreffen Interpretation (N1/N2), Vollständigkeit (B1–B8) und verlorene Doku (A8).

## 5. Vorschläge für v1.2-Entscheidungen (beide Pfade dargelegt, Empfehlung markiert)

### 5.1 Bus-TTL-Werte (Rekonstruktion von Abschnitt D — Original verloren)

Vorschlag: Stations-Anker Bus **12 min** Basis, Cap **30 min**, `at_stop` **8 min**; Tram wie Bus; Trip-Anker wie Schiene (endet am Trip-Ende). Begründung: kürzere Verweilzeiten, dichtere Takte. **Alternativpfad:** Schiene-Werte (20/45) überall — einfacher, aber für Busse zu träge. Empfehlung: gestaffelte Werte (Config-getrieben, in `ttl_config` Tabelle).

### 5.2 45-Minuten-Cap für Trip-Anker

v1.0 sagte „Maximum 45 Minuten — IMMER verfallen". Ein Hamburger S-Bahn-Ritt kann 60+ min dauern; ein harter Cap würde Trip-Anker mitten in der Fahrt löschen, obwohl die Information („Team in dieser Fahrt") strukturell gültig bleibt und B.5 ohnehin hart am Trip-Ende beendet.
**Pfad 1 (Empfehlung):** Cap 45 min gilt für Stations-Anker und abgeleitete Meldungen; Trip-Anker enden zu `trip_end + 120 s` (natürliches Ende), Bestätigungen verlängern nur Stations-Anker.
**Pfad 2:** Cap 45 min ausnahmslos (v1.0-Wortlaut). Einfacher zu kommunizieren, verliert aber bei Langfahrten die korrekte Aussage.
Entscheidung erforderlich vom Product Owner; Umsetzung ist in beiden Fällen nur Config.

## 6. Quellen der eigenen Recherche (zur Prüfung markiert, Richtigkeit nicht garantiert)

- gtfs.de Realtime-Seite (Lizenz CC BY-SA 4.0, 10-s-Aktualisierung, DELFI-NeTEx-Herkunft, „ohne Gewähr"): https://gtfs.de/de/realtime/
- Mobility-Database-Eintrag DELFI Realtime (Producer-URL = realtime-free.pb): https://mobilitydatabase.org/feeds/gtfs_rt/mdb-3101
- VBB-Feed-Statusseite (Datenlücke seit 2026-06-04 16:00, CC BY 4.0, 60 req/min, Atom-Feed): https://production.gtfsrt.vbb.de/
- Hetzner-Traffic (nur Outgoing wird gezählt; 20 TB inklusive): Hetzner-Doku + Produktseiten, zitiert nach [Reddit-Thread mit Hetzner-Stellungnahme](https://www.reddit.com/r/hetzner/comments/1i27ii6/) und Übersichten ([1vps.com](https://1vps.com/review-hetzner/), [hostings.info](https://hostings.info/hosting/schools/hetzner-vps)) — **vor Vertragsabschluss original prüfen.**
- HVV: [hamburg-gtfs-rt-server (deprecated, HAFAS zu)](https://github.com/derhuerst/hamburg-gtfs-rt-server), [ehemaliger inoffizieller Feed](https://v0.hamburg-gtfs-rt.transport.rest/), HVV-GTFS-Static über [Transparenzportal Hamburg](https://www.transit.land/feeds/f-u1-hamburgerverkehrsverbundhvv~hamburgerverkehrsverbundhvv~ham) (Transitland-Mirror).
- Eigene Messskripte + Rohausgaben: `verifikation/` (verify_feeds.py, verify_deep.py, verify_alerts.py, Outputs, alert_headers_distinct.txt mit allen 427 Header-Texten).
