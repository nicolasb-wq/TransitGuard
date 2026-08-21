# TransitGuard — Innovations-Backlog (gesichtet, bewertet, geordnet)

**Auftrag:** „suche aktiv nach innovativen Verbesserungen." Kriterien: Mehrwert für Nutzer ODER Risiko-Minderung ODER Dichte-Flywheel; Aufwand klein genug für Einzelentwickler; keine Fork-Entscheidungen gegen ADRs. Bewertung: **Aufstieg** (in Roadmap aufgenommen) / Kandidat (nach Launch prüfen) / verworfen (mit Grund).

## A — Aufgestiegen in die Roadmap (kostenlos aus vorhandenen Daten)

### A1 ♿ Barrierefreiheits-Badges aus dem „Alert-Rauschen" (Flagship-Idee)
**Beobachtung aus der Messung (N1):** Zehntausende Alerts sind Fahrzeug-Ausstattungsnotizen („Rollstuhlgeeignet", „Niederflur", „Fahrzeuggebundene Einstiegshilfe vorhanden", „Bordrestaurant" …), **pro Trip** mitgeteilt — wir filtern sie als Noise raus. Genau diese Daten sind für Rollstuhlnutzer:innen und Menschen mit Gehhilfe hochwertig: Sie sagen pro **konkreter Abfahrt**, welches Fahrzeug wahrscheinlich barrierefrei ist.
**Feature:** Abfahrtsliste mit Filter „nur barrierefreie Fahrten" + Badge an der Departure (🦽 vermutlich / gesichert). Datenbasis: die Noise-Pipeline zweigt die Ausstattungs-Flags ab (Trip → Badge), kein neuer Ingest.
**Warum Aufstieg:** (a) Kosten ~1 Ticket, (b) echtes Unique-Selling-Feature, das **kein** deutscher Fahrplan-App-Dienst aus GTFS-RT zieht (*Glaube ich*, kein Produkt mit per-Trip-Rollstuhl-Badge bekannt), (c) NICHT nur Image — es verschiebt die Produkt-Story endgültig vom „Kontrollen-Thema" hin zum Companion (Compliance-Mehrwert!), (d) positives Presse-Narrativ zum Launch („App macht ÖPNV barrierefrei transparent"). **Vorbehalt:** Aussagekraft je Trip prüfen (Badge = „lt. Meldung des Betreibers für diese Fahrt"); Fehlalarme kommunizieren.
**Roadmap:** Ticket T8.3a; Metrik: wie viele HH-Abfahrten tragen Badges (aus Messung: tausende Trips bundesweit mit Flags).

### A2 Attribution-Automatisierung
Die Attributierungs-Alerts („… bereitgestellt von DELFI/Verbund X") liefern uns **in-band die korrekte Namensnennungsliste je Datenquelle**. Der Ingest baut daraus automatisch den Attribution-Text (App/About + API-Header). Rechtlich sauber, wartungsfrei, exakt. → T3.4 erweitert.

### A3 „Ticket-Erinnerung" statt Warnung (Compliance-Köder umdrehen)
Bei jeder aktiven Kontroll-Meldung: dezenter, nicht-moralischer Hinweis „Ticket prüfen → [Deutschlandticket/Verbund-Deep-Link]". Macht das Schutzschild (Rechtsteil §3) zum sichtbaren Produktbestandteil und ist ehrlich nützlich. Copy streng neutral. → T5.3/T5.4 (Client).

### A4 Reisemodus-Cold-Start über PWA-Links
Statt Store-Hürde: Stadtführungs-Links („hamburg.transitguard.app") direkt teilbar (WhatsApp/Telegram-Sticker in lokalen Communities). PWA braucht keinen Store → Distribution unabhängig vom Apple-Risiko (Rechtsstand §3). Onboarding <30 s: Stadt erkennen → Abfahrten sofort, Melden-Zettel sichtbar. → T5.4 erweitert.

### A5 📣 Stammfahrten-Alarm („Deine U1 fällt heute aus") — Pro-Kandidat mit Launch-Prio
Unsere Messbasis (94,5 % Delay-Deckung Hamburg) macht Folgetrips prognostizierbar: Nutzer markiert eine übliche Fahrt (Station+Linie+Zeitfenster); der Server prüft bei jedem Poll Ausfall/CANCELED/≥5-min-Verspätung und pusht (mit Push M8) bzw. zeigt Badge. **Rechtlich neutral** (pure Fahrplan-Qualität), starker Alltagsnutzen, klarer Pro-Wert neben Push #6. Aufwand 3–4 d. → Roadmap M8-Kandidat (nach T8.2 Push).
### A6 ⚖️ Gate- & Entfernungs-Transparenz
Die Ticket-Gate-Aggregate (H1) und Notice-and-Action-Entfernungen (H3) werden — nur als Tagesaggregate — öffentlich sichtbar (Charta H6 + Statistik B2). Verbindet Rechtssicherheit mit Community-Vertrauen. → mit T7.4a.

## B — Kandidaten (nach Launch, mit Prüfschwelle)

| # | Idee | Wert | Aufwand | Risiko/Prüfung |
|---|---|---|---|---|
| B1 | **Umlauf-Heuristik v2** (Rücktrip „gleiche Linie, Gegenrichtung, Abfahrt Endstation +2–8 min" mit Unsicherheits-Label) | Schließt B.4-Lücke #1 | 3–5 d | Nur bauen, wenn reale Meldedaten zeigen, wie oft Teams sitzen bleiben (§5-Entscheidung von v1.1 bleibt: nicht raten). Auslöser: ≥100 Trip-Ende-Derivationen mit Folgemeldung <10 min |
| B2 | **Kontroll-Statistik-Portal (öffentlich, anonym)**: „X aktive Meldungen, Ø-Bestätigungsquote" — Transparenz schlägt Vertrauen + Presse-Anlaufstelle | Trust + PR | 2 d | Keine operativen Rückschlüsse auf Teams (DSGVO-Kontrollleur-Profil-Gefahr, Rechtsstand §2): nur Aggregate über ≥5 Meldungen |
| B3 | **Datenqualitäts-Rückmeldung an gtfs.de/DELFI** (Match-Rate-, Delay-Ausreißer-Berichte als Service) | Goodwill, Possibly priority treatment | 1–2 d | Keine Nutzerdaten; nur Ingest-Metriken. Kein B2B-Datenverkauf (bleibt verworfen) |
| B4 | **Widgets/Watch-Complication** „nächste U1 in 3 min + Störungs-Ampel" | Alltagstauglichkeit, Sichtbarkeit | 3–5 d | Erst nach Kernstabilität; PWA-Limitierung beachten (iOS-Widget braucht native App) |
| B5 | **Push-Verfeinerung**: Stammstrecken-Geofences (Stationsnähe aktiviert Abos) statt Dauer-Push | Pro-Wert #6 schärfer | 3 d | Batterie/DSGVO (Standort auf Gerät — Konzept hält) |
| B6 | **Fahrgast-„Bedienung vorbei"-One-Tap** (Gegenmeldung mit Haltestellen-Kontext statt Freitext) | TTL-Genauigkeit steigt | 1 d | Missbrauch already über Paritäts-Regeln gedeckt |
| B7 | **Deutschlandticket-Kauf-Deep-Links pro Verbund** (statt nur Verbund-Apps) | Ticket-CTA-Abrundung | 1–2 d | Nur Deep-Links, keine Verkauf-Integration (Variante A bleibt); Konditionen J5 |
| B8 | **Nachtbus-Sicherheitsmodus** („mitfahrer-Info", Helligkeit, Notruf-Verknüpfung) | Neue Nutzergruppe | 5–8 d | Scope-Gefahr; nur wenn Community es fordert; NIEMALS Kontroll-Daten mit Personenbeschreibung vermischen |

## C — Prüft und verworfen (Begründung)

- **Telegram-NLP-Bot** à la FreiFahren: Fehlerquote 17,3 % (deren eigenes Problem) + Moderationslast + keine Anonymitäts-Kontrolle → verworfen für v1; neutrale Lese-Gruppen-Integration wäre B-Kandidat, falls Community es beweist.
- **Foto-Meldungen (DSGVO)**: bleibt verworfen (v0.1-Risiko R2, Rechtsstand §2).
- **B2B-Datenverkauf an Verkehrsplaner**: bleibt verworfen (ethisch/DSGVO; auch B3 grenzt sich sauber ab — nur Betriebs-Metriken, keine Verhaltensdaten).
- **Eigene Position über Nutzer-GPS aggregieren** („Crowd-VP"): bleibt verworfen (ADR-0001; Anonymitätsprinzip; GPS unterirdisch eh tot).
- **„Score-Jagd"-Gamification über Ränge hinaus**: Anreiz-Fehlsteuerung (Meldungen ohne Sichtung) → nur passive Boni (bestehendes Trust-Design).
- **KI-„Kontrolleur-Bewegungsvorhersage" auf Einzel-Team-Ebene**: DSGVO-Kipp-Punkt (Rechtsstand §2) — Prognose nur aggregiert (Matrix #7, Schwelle ≥N).
