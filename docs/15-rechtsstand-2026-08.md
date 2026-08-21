# TransitGuard — Rechtsstand-Recherche (2026-08-21) — §265a, Kontroll-Warn-Apps, Stores

**Auftrag:** „informiere dich über den rechtlichen Aspekt (Verleitung zum Schwarzfahren usw.)". **Keine Rechtsberatung** — Recherche mit Quellen; die anwaltliche Prüfung (F-1, Briefing-Vorlage liegt bei) bleibt Pflicht-Gate vor Launch. Alle Quellen am Ende; Richtigkeit nicht garantiert, zur Prüfung markiert.

## 1. Strafrechtlicher Rahmen — Stand August 2026

**§265a StGB gilt unverändert.** Der Bundestag hat am **16.04.2026** zwei Gesetzentwürfe zur Entkriminalisierung (Grüne 21/2722, Linke 21/1757) auf Empfehlung des Rechtsausschusses (21/5378) **abgelehnt**. Damit bestätigt sich die Dossier-Angabe („Reform gescheitert April 2026") präzise; das BMJ (Justizministerin Hubig) prüft laut Berichterstattung weiter. ~97 % der §265a-Fälle sind Beförderungserschleichung (PKS 2024).

**Für eine Kontroll-Warn-App relevante Straftatbestände — durchgeprüft (nach beck-aktuell-Interview mit RA Prof. Sobota, 11./12.08.2026, und Solmecke-Einschätzungen in techbook/BILD/Computer Bild, 12./13.08.2026):**

| Tatbestand | Einschätzung der zitierten Juristen | Konsequenz für TransitGuard |
|---|---|---|
| §27 Beihilfe z. §265a | Erfordert **konkrete, vorsätzliche Haupttat**; eine App fördert keine bestimmte Tat, sondern informiert generisch → Beihilfe „schwer konstruierbar" | Geringes Risiko — **solange** die App nicht kontextualisiert („fahr schwarz, wir warnen") |
| §111 öffentliche Aufforderung | Fehlt der appellative Charakter; App bietet Information, keine Aufforderung | **Copy-Guidelines sind Pflicht**: keinerlei Aufforderungs-CTAs |
| §258 Strafvereitelung | Benötigt gesteigerten Vorsatz + bestimmte Tat | Nicht einschlägig |
| §23 Abs. 1c StVO (Blitzer-App-Verbot) | Fahrscheinkontrollen sind **keine** „Verkehrsüberwachungsmaßnahmen" → nicht übertragbar | Kein Bußgeld-Risiko wie bei Blitzer-Apps |
| Meinungsfreiheit (Art. 5 GG) | Weitergabe wahrer Informationen grundsätzlich geschützt (Solmecke) | Tragt unser Informationsmodell |

**Die eigentliche Gefahr ist ZIVILRECHTLICH, nicht strafrechtlich** (Sobota, beck-aktuell): **Eingriff in den eingerichteten und ausgeübten Gewerbebetrieb (§823 BGB)** kommt „durchaus in Betracht" — die App greife erkennbar in betriebliche Abläufe ein; die Meinungsfreiheits-Rechtfertigung sei begrenzt, wenn gezielt und fortgesetzt Rechtsgüter eines bestimmten Unternehmens in Anspruch genommen würden. Kein deutsches Urteil zu dieser App-Kategorie existiert (Stand 21.08.2026); RV gegen einen kommerziellen Betreiber wäre **erstinstanzliches Neuland mit erheblichem Prozesskostenrisiko auf beiden Seiten**.

**Kommerz verschärft das Bild:** Für den gemeinnützigen FreiFahren-e.V. sehen die zitierten Einschätzungen wettbewerbsrechtliche Ansprüche als kaum durchsetzbar; **ein kommerzieller Anbieter (wir, mit Pro-Abo) ist eine attraktivere Gegner-Figur** (UWG-Behörden-/Wettbewerbsfälle, §823, ggf. Unterlassungsklagen). Präzise deshalb gilt unsere Doppelstrategie: (1) Kontroll-Kern Free & Ticket-Integration für alle (ADR-0006), (2) Kill-Switch <24 h (ADR-0009), (3) neutrale Copy, (4) **Rechtsform-Prüfung** (F-18): ob ein e.V./gemeinnütziger Träger für den Melder-Kern mit kommerziellem Pro-Arm (oder Sponsor-Modell) die Prozessrisiken senkt — Frage an den Anwalt, keine Entscheidung hier.

**Ausland als Frühindikator:** Île-de-France Mobilités kündigte Jan. 2025 rechtliche Schritte gegen eine Pariser Kontroll-App (~130 k Nutzer) an und forderte Apple/Google zur Entfernung auf; gerichtlich ungeklärt. Zeigt den Eskalationspfad, der auch in DE denkbar ist.

## 2. DSGVO-Feinrisiko: Kontrolleur-Profile

Solmecke/Sobota benennen das relevante Datenschutzproblem: Werden aus Ort+Zeit+Linie **wiederkehrende Einsatzmuster einzelner Kontrolleur*innen** rekonstruierbar, kippt die Bewertung (Bewegungsprofile = personenbezogen). **Unsere Architektur ist bereits dagegen gebaut** (nur Station/Linie/Richtung/Zeit, keine Personenbeschreibung, keine Fotos, Aggregations-Schwellen im Prognose-Feature). Ergänzend festzuschreiben: Prognose/Heatmaps (Pro #7) nur **aggregiert über ≥ N Fahrten**, nie als „Team X fährt Strecke Y" Einzelauflösung; Löschung kontrollbezogener Rohdaten nach 90 Tagen (bestehende Retention). → In DSFA-Entwurf (17-dsfa-entwurf.md) aufgenommen.

## 3. Store-Risiko: Nicht mehr hypothetisch — der Referenzfall wird gerade entfernt

**Apple entfernt FreiFahren (Stand Mitte August 2026) aus dem App Store.** rbb (13.08.2026): Die iOS-App zeigt ein Banner, Apple könne die App „in den nächsten Tagen" entfernen, Nutzer sollen die Web-App nutzen; Trieloff: Grund unklar, Entfernung aber sicher. Apple verweist auf die Richtlinien (Ablehnung von Apps, die kriminelles Verhalten auffordern/„fördern"). Eine Klage der BVG ist bislang nicht bekannt; die BVG „prüft fortlaufend rechtliche Schritte".

**Konsequenzen für TransitGuard (in Roadmap + Store-Strategie umgesetzt):**
1. **Launch-Reihenfolge gedreht: PWA zuerst, Google Play zweitens, iOS bewusst zuletzt** (oder gar nicht zuerst). Begründung: Apple hat die removable Bereitschaft im exakt relevanten Kategoriefall demonstriert; Google hat im FreiFahren-Fall (noch) nicht gehandelt; die PWA ist store-unabhängig in DE gehostet. iOS-Einreichung erst nach 6 Monaten stabilen Companion-Betriebs mit Review-Dossier, das den Mehrspalten-Charakter (Fahrplan/Abfahrten/Barrierefreiheit/Störungen vs. Kontrollhinweis als Teilmenge) belegt.
2. Das Kontroll-Feature **muss** in der iOS-Fassung hinter dem gleichen Kill-Switch liegen (ist es — ADR-0009), damit ein Apple-Verlangen ohne Neubau umsetzbar ist.
3. Marketing-Nie-Liste (gilt überall): „schwarzfahren", „Kontrollen entgehen/umgehen", „ohne Ticket" — nur „Fahrplan-Companion", „Community-Hinweise", „Ticket hier kaufen".

## 4. Wahrheitsgehalt der FreiFahren-Benchmarks (F-17)

Sekundärquellen bestätigen: 3.518 Kontrollmeldungen in 7 Tagen (netztrends 17.08.2026, Titelzahl) ✓; **„mehr als 55.000 Nutzer haben Kontrollen gemeldet"** (itopnews 01.08.2026, höher als die 40-k-Regelnutzer-Zahl des Dossiers — beide Größenordnungen konsistent). Die Ökonomie-Rechnung (0,0126 Meldungen/Nutzer/Tag) bleibt damit belastbar; Bereich in 08-entitlement.md vermerkt. FreiFahren plant laut Berichten Android + weitere Städte („technisch vorbereitet") — Zeitfenster für uns bleibt begrenzt.

## 5. Quellen (gesichtet 21.08.2026)

- Bundestag 16.04.2026, Ablehnung der Entkriminalisierung: bundestag.de/dokumente/textarchiv/2026/kw16-de-schwarzfahren-1165328 (+ Drucksachen 21/1757, 21/2722, 21/5378 auf dserver.bundestag.de)
- beck-aktuell (11./12.08.2026): „Achtung, Kontrolle!" — RA Prof. Sobota zu Beihilfe/§111/§258/StVO/§823: beck-aktuell.de/…/bahn-kontrolle-schwarzfahren-app-berlin-rechtswidrig-2026-08-11
- techbook (13.08.2026): Solmecke-Einschätzung + DSGVO-Profil-Risiko; BILD (13.08.2026): zusätzlich Verein-vs-kommerziell-Argument; Computer Bild (12.08.2026): gleichlautend
- rbb24 (13.08.2026): App-Store-Entfernung FreiFahren, BVG-Position, Strafanzeige-Praxis (3. Wiederholungsfall; 3.400 Anzeigen 2025): rbb24.de/panorama/beitrag/2026/08/berlin-bvg-ohne-fahrschein-app-frei-fahren-diskussion.html
- netz-trends (17.08.2026): 3.518/7d, BVG 20–25 Mio € Ausfall, Paris-Fall (IDFM, Jan. 2025, 130 k Nutzer)
- itopnews (01.08.2026): >55.000 meldende Nutzer, Städte-Pläne FreiFahren
- gtfs.de/de/realtime (CC BY-SA 4.0 RT) + gtfs.de/de/feeds/de_{nv,rv,fv} („Creative Commons 4.0" → Link CC BY 4.0)
- GovData/Transparenzportal HH: HVV-GTFS unter „Datenlizenz Deutschland Namensnennung 2.0" (HVV GmbH)

## 6. Härtungspaket (nach Auftragserteilung 21.08.2026 — Design: `18-rechtliche-haertung.md`)

Umgesetzt gegen die in §1–§2 identifizierten Risiken: **Ticket-First-Gate** (Kontroll-Zugang nur nach Bestätigung eines gültigen Tickets; Kauf-Empfehlung in jeder Warn-Situation — die Umkehr des Erleichterungs-Narrativs), **AGB-Zweckbestimmung** mit ausdrücklichem Nutzungs-Verbot der Schwarzfahr-Vorbereitung, **DSA-Notice-and-Action-Kanal** (Gewerbebetriebseingriff wird über kooperative Einzelfall-Entfernung entkräftet statt durch Konfrontation), **Selbstverpflichtungs-Charta**, **Rechtsform-Empfehlung UG/GmbH** (F-18), **Legislative-Monitoring** mit 7-Tage-Kill-Switch-Trigger. Legislative Ergänzung: Das Blitzer-App-Totalverbot wurde am **26.03.2026 vom Bundestag abgelehnt** (Bundesrat-Initiative gescheitert, Frankreich-Modell nicht übernommen); für Fahrkartenkontroll-Warn-Apps existiert kein Gesetzentwurf (Stand 21.08.2026). Bewertung jeder Maßnahme gegen §1-Kriterien: 18-rechtliche-haertung §1.4 — Ergebnis **eingebaut**, Anwaltsbestätigung (Fragen 8–10) offen.
