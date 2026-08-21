# Vorlage: Anwalts-Briefing (F-1) — Stand 2026-08-21, vor Ausfüllung prüfen

**An:** [Fachanwalt für IT-/Medienrecht] · **Von:** [Betreiber TransitGuard] · **Ziel:** schriftliche Einschätzung vor App-Launch (Budgetrahmen nennen).

## 1. Was wir betreiben (eine Seite, dem Anwalt geben)
ÖPNV-Companion-App („TransitGuard", Arbeitstitel): Fahrplan, Live-Abfahrten (GTFS-Realtime), Störungshinweise, Barrierefreiheits-Informationen, Ticket-Deep-Links. **Ein** Community-Feature: Nutzer können „Kontrolle gesehen" melden (Station+Linie+Richtung+Zeit, keine Personenbeschreibung, keine Fotos); Meldungen verfallen automatisch (TTL) und werden serverseitig per Kill-Switch abschaltbar (<24 h). Free-App; optionale Pro-Stufe (3,49 €/Monat) für Komfort (Push, Statistiken) — **die Kontroll-Information selbst ist gratis**. Anonym: keine Konten, Standort bleibt auf dem Gerät. Geplantes Launch-Gebiet: Hamburg. Vertrieb: zuerst Web-App (PWA) + Google Play; Apple App Store später.

## 2. Konkrete Fragen (numetriert antworten bitte)
1. Strafrecht: Beihilfe/Anstiftung zu §265a durch Bereitstellung/O nutzung? (Vergleichsfall FreiFahren; beck-aktuell/Sobota u. Solmecke-Einschätzungen 08/2026 liegen bei.) Risiko durch **kommerziellen** Betreiber + Pro-Abo?
2. Zivilrecht: Unterlassungs-/Schadensersatzrisiken aus §823 BGB (Eingriff Gewerbebetrieb) / UWG durch HVV, Hochbahn, DB? Konkrete Vermeidungs-Checkliste für uns?
3. Datenenschutz: Einschätzung Kontrolleur-Re-Identifikation (unser DSFA-Entwurf 17-dsfa); genügt 90-Tage-Rotation + Aggregation als Minderung? Impressum §5 DDG prüfen.
4. Form: Empfehlen Sie e.V./gGmbH/GmbH für dieses Modell (Risikodämpfung vs. kommerzielle Pro-Einnahmen)? (F-18)
5. Stores: Einschätzung Apple-Richtlinien nach FreiFahren-Entfernung 08/2026; Formulierungsvorschläge für Review-Notes?
6. Lizenzen: CC BY-SA 4.0 (gtfs.de-RT) — wie ShareAlike bei Weitergabe abgeleiteter Daten über unsere API korrekt umsetzen? (F-2) DL-DE BY 2.0 (HVV-Static als Backup) korrekt attributed?
7. §265a-Reform: Monitoring-Empfehlung; welche Auslöser würden die Rechtslage ändern (Reform, Urteil, Verbandsklage)?
8. **Ticket-First-Gate** (18-rechtliche-haertung §1): Stärkt das Gate (Zugang nur nach Bestätigung eines gültigen Tickets + Kauf-Empfehlung in jeder Warnsituation; weiche Durchsetzung wie ein Age-Gate) die Position messbar? Risiken der Konstruktion?
9. **DSA:** Sind wir Online-Plattform/Hosting-Dienst i.S.d. DSA? Ist der Notice-and-Action-Kanal so ausgestaltet, dass Haftungsprivilegien erhalten bleiben? Weitere DSA-Pflichten für Mikro-Anbieter?
10. **Verbotsrisiko:** Nach dem Scheitern des Blitzer-App-Totalverbots (26.03.2026) — wie bewerten Sie die Wahrscheinlichkeit eines Gesetzes gegen Fahrkartenkontroll-Warn-Apps? Welche Trigger gehört in unser 7-Tage-Kill-Switch-Runbook?

## 3. Beilagen
00-bestandsaufnahme.md (Abschnitt Recht) · 15-rechtsstand-2026-08.md (Quellen) · 08-entitlement.md §4 (Paywall-Compliance) · 17-dsfa-entwurf.md · API-Spezifikation (04) · Kill-Switch-Spezifikation (11-recht §4).

## 4. Zwei-Linien-Erwartung
Wir erwarten eine Einschätzung entlang (a) „Weiterbetrieb mit Auflagen" — welche Auflagen genau? oder (b) „Feature nicht vertretbar" — dann Kill-Switch-Plan. Beides ist umsetzbar; wir brauchen Klarheit vor Store-Einreichung, nicht danach.
