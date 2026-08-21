# TransitGuard — Rechtliche Härtung: Ticket-First-Gate, AGB, DSA, Monitoring (2026-08-21)

**Auftrag:** „App rechtlich stärker … mit Empfehlung unbedingt eine Fahrkarte zu kaufen bzw. Frage, ob man schon eine hat … keinen Grund zur Klage bieten." **Ehrlichkeit vorweg (Prüfergebnis):** Kein Design der Welt garantiert „keine Klage" — jedermann kann klagen. Erreichbar und hier umgesetzt: **die Klagegründe der derzeit diskutierten Rechtsprechungsliteratur systematisch ausräumen** (Beihilfe-Optik, Aufforderungscharakter, Gewerbebetriebseingriff-Optik, DSGVO-Kontrollprofile) + Prozessrisiko begrenzen (Rechtsform). Alle Maßnahmen sind intern geprüft (Konsistenz mit Rechtsstand-Dok + ADRs + Architektur); die juristische Bestätigung ist als Frage 8–10 ins Anwalts-Briefing eingetragen — **die Umsetzung beginnt trotzdem jetzt**, weil jede Maßnahme null negative Seite hat (Prüfkriterien unten).

## 1. H1 — Ticket-First-Gate (Kernstück, verpflichtende Ticket-Frage)

### 1.1 Verhalten (Client)
```
Erster Start / alle 24 h Nutzung des Kontroll-Features / jeder Gerätewechsel:
  „Bist du gerade mit gültigem Ticket unterwegs?"
  (Deutschlandticket, hvv-Ticket, Jobticket, Einzelticket …)

  [ ✓ Ja, ich habe ein gültiges Ticket ]  → Kontroll-Karte wird sichtbar
  [ Ticket kaufen → ]                      → Kauf-Fenster (siehe 1.3), KEINE
                                            Kontroll-Karte in dieser Session;
                                            Companion-Wert (Abfahrten, Störungen,
                                            Barrierefreiheit) bleibt frei nutzbar
  [ Später ]                               → wie „Ticket kaufen", ohne Kauf-Fenster
  Es gibt KEINE Option „Ich fahre ohne Ticket" mit Zugriff. (Bewusst: Wer ohne
  Ticket unterwegs ist, erhält keinen Kontroll-Zugriff — exakt das Gegenteil
  einer Schwarzfahrer-Unterstützung.)
```
Copy-Regeln: neutral, nicht moralisierend („Kontrollhinweise sind für Fahrgäste mit gültigem Ticket gedacht — danke fürs Fairplay"). Keine Erklärung „wie man Kontrollen entgeht" irgendwo.

### 1.2 Server-Durchsetzung (weich, dokumentiert — bewusst wie ein Age-Gate)
- `POST /v1/reports`: Pflichtfeld `client.ticket_confirmed == true`, sonst **422 `ticket_confirmation_required`** (04-api ergänzt).
- Kontroll-Lesezugriff (GET reports, Hub-Join `reports.*`): Header/Feld `ticketConfirmed=true` erforderlich, sonst 403 `ticket_gate_blocked` (Hub-Event `control.ticket_gate`).
- Server speichert **keine** Pro-User-Aussage — nur anonyme Zähler `gate.confirmations` / `gate.blocks` (Tagesaggregat) als **Beweismittel-Pipeline**: „87 % der aktiven Nutzer bestätigen täglich ein Ticket" ist genau das Gesamtbild, das Beihilfe-/Gewerbebetriebs-Argumente entkräftet (Verwendung nur als Aggregat, §2 DSFA-konform).
- Umgehbarkeit (Client-Decompilation) ist einkalkuliert: Wie bei Age-Gates zählt der **dokumentierte Wille + die Standard-Hürde**, nicht die Unüberwindbarkeit. Das ist bewertbar und verschlechtert niemanden.

### 1.3 Kauf-Verbindung (reale, verifizierte Ziele — keine erfundenen URL-Schemata)
- **Deutschlandticket/Verbund-Tickets Hamburg: hvv switch App** (iOS/Android; offizieller Kauf-Kanal lt. hvv.de; Zahlung PayPal/Kreditkarte/SEPA) — Verlinkung über **offizielle Web-URLs** (hvv.de/deutschlandticket, Onlineshop) + Store-Link der App. *Weiß ich nicht, ob hvv switch ein öffentliches Deep-Link-Schema hat → keins behaupten, keins erfinden; per J5-/F-4-Anfrage beim HVV nachfragen (vorlage liegt).*
- Zusätzlich: allgemeiner Hinweis „Deutschlandticket – 63 €/Monat (hvv), gültig bundesweit" (*PreisStand hvv.de 08/2026, zur Prüfung markiert*).
- `cities.ticket_deeplink` (SQL 0001) wird mit der Web-URL bevüllt — konfigurierbar je Stadt.

### 1.4 Prüfbefund („besteht die Lösung?")
| Kriterium (aus Rechtsstand-Dok) | Deckt H1 das? |
|---|---|
| „Nicht aktiv auffordern, ohne Fahrschein zu fahren" (Solmecke) | ✓ übererfüllt: aktive Gegen-Aufforderung (Kauf) + Zugang nur mit Ticket-Bekenntnis |
| „Kein appellativer Charakter zur Straftat" (§111, Sobota) | ✓ Appell zielt auf Ticket-Besitz |
| „Nicht jede Ausgestaltung ist unproblematisch" (presse.online: Gestaltung entscheidet) | ✓ Gestaltung dokumentiert auf Produkt-Ebene |
| Gewerbebetriebseingriff (Lutzi/Sobota): „App, die Umgehung der Zahlungspflicht erleichtert" | ✗ nicht vollständig verhinderbar — aber stärkste verfügbare Entkräftung: Zahlung wird in jeder Warn-Situation mit angeboten; Zielgruppe sind Ticketbesitzer (s. Zähler 1.2). Restrisiko = Kill-Switch + Rechtsform |
| DSGVO-Kontrollprofile (Solmecke) | ✓ unberührt (Gate speichert keine Personendaten) |
**Ergebnis: eingebaut.** Externe Bestätigung: Anwaltsfrage 8 (Briefing ergänzt).

## 2. H2 — Nutzungsbedingungen (AGB-Entwurf liegt bei: `vorlagen/nutzungsbedingungen-entwurf.md`)
Kernklauseln (anwaltlich zu finalisieren): Zweckbestimmung „Companion für Fahrgäste **mit gültigem Fahrschein**"; **Untersagung jeder Nutzung zur Vorbereitung/Erleichterung einer Beförderung ohne gültigen Fahrschein (§265a StGB)**; Falschmeldungs-/Automatisierungsverbot; Hinweis „Meldungen sind unverifizierte Community-Angaben, keine Garantie"; sofortiges Abschaltrecht (Kill-Switch-Grundlage); Lizenzen/Attribution; anwendbares Recht. **Wirkung:** Die vertragliche Zweckbestimmung ist der Beleg, dass die Plattform *nicht* auf Erschleichung zielt — sie rahmt Missbrauch als Verstoß gegen uns, nicht als Geschäftsmodell.

## 3. H3 — DSA-Hosting-Struktur (eine der stärksten, oft übersehene Waffen)
TransitGuard hostet nutzergenerierte öffentliche Inhalte (Meldungen) → voraussichtlich „Online-Plattform" im DSA-Sinn (*Glaube ich*, Auslegung mit Anwalt prüfen — Frage 9). Konkret eingebaut:
- **Notice-and-Action-Kanal** (`legal@…`, Impressum + App-Footer, <48 h Reaktionsziel): Verkehrsunternehmen können rechtswidrige Inhalte/Missbrauch melden — genau der Mechanismus, der DSA §3-Hosting-Haftungsfreistellung erhält und gleichzeitig dem BVG-Szenario „Unternehmen will Meldungen weg haben" einen prozessoravoidierenden Kanal gibt: Wir können einzelne Meldungen entfernen, bevor aus Verärgerung eine Klage wird.
- Missbrauchsmeldung endet NICHT in Präzedenz-Löschung ganzer Features — Eskalationsstufen: Einzelmeldung löschen → Stadt deaktivieren (RLS-Stufe 2) → globaler Kill-Switch.
- Transparenz der Entfernungen (Zähler, keine Nutzerdaten) als öffentliche Statistik (B2).
*Vorsicht, nicht überziehen:* DSA-Details (Meldepflichten, Kontaktstellen) anwaltlich bestätigen lassen.

## 4. H4 — Legislative-Frühwarnung (Monitoring, nicht Panik)
Blitzer-App-Totalverbot wurde **26.03.2026 vom Bundestag abgelehnt** (Bundesrat-Initiative gescheitert; Frankreich-Modell nicht übernommen) — der Gesetzgeber hat sich also gerade *gegen* Ausweitung von Warn-App-Verboten entschieden. Für Fahrkartenkontroll-Apps existiert kein Gesetzentwurf (*Stand 21.08.2026, keine gefunden*). Frankreich (IDFM ging gegen Pariser App vor, Jan. 2025) zeigt den Eskalationspfad außerhalb DE. **Maßnahmen:** monatlicher 15-Minuten-Check (F-8-Routine ergänzt): „Kontroll-App Verbot/Gesetzentwurf" + „BVG Schritte FreiFahren". Auslöser-Regel im Runbook: Bei erstem seriösen Verbotsentwurf → Anwalt + kill-Switch-Entscheidung binnen 7 Tagen (nicht erst bei Inkrafttreten).

## 5. H5 — Prozessrisiko begrenzen: Rechtsform-Empfehlung (F-18, mit Anwalt finalisieren)
Eine Klage lässt sich nicht verhindern — aber **wogegen**: Empfehlung **UG (haftungsbeschränkt), später GmbH** als Betreiberin (+ IT-/Medienhaftpflicht & D&O): privates Vermögen des Gründers bleibt außerhalb des Titelrisikos; Pro-Abbreviatur passt zur kommerziellen Form; ein e.V. verhindert Klagen nicht und kollidiert mit Pro-Einnahmen (steuerlich/gemeinnützig heikel). Alternative für später: gGmbH-Trägerin für den Community-Kern + GmbH für Pro — **erst nach Anwalt, jetzt nicht bauen.** (*Glaube ich*-Empfehlung, Frage 4 im Briefing.)

## 6. H6 — Öffentliche Selbstverpflichtung („TransitGuard-Prinzipien", 8 Punkte)
Launch-Begleiter, im About + auf der Website: (1) Ticket-First — Kontrollinfos nur mit Ticket-Bestätigung. (2) Jede Warnung trägt den Ticket-Kauf-Link. (3) Keine Personenbeschreibungen, keine Fotos, keine Kontroll-Routen-Prognosen auf Einzelteam-Ebene. (4) Meldungen verfallen automatisch (TTL). (5) Kill-Switch <24 h auf Verlangen. (6) Anonymität: keine Konten, Standort bleibt auf dem Gerät. (7) Volle Daten-Attribution (gtfs.de/DELFI/Verbünde). (8) Notice-and-Action-Kanal für Verkehrsunternehmen. **Wirkung:** prägt die öffentliche Wahrnehmung („das Gegenteil von FreiFahren") und ist vor Gericht der Beleg gelebter Sorgfalt.

## 7. Einbau-Stellen (bereits durchgeführt)
- `docs/04-api.md` §2/§4: `ticket_confirmed`-Pflicht + Fehlercodes → **done**
- `docs/06-realtime-hub.md` §1: Hub-Join-Gate → **done**
- `docs/09-testplan.md`: T-GATE-Fälle → **done**
- `docs/13-roadmap.md`: Tickets T4.2a (API-Gate), T5.3b/T5.4b (Client-Gate), T7.4a (AGB/Impressum/Charta live) → **done**
- `docs/15-rechtsstand-2026-08.md` §6 Härtungsmaßnahmen-Referenz → **done**
- `vorlagen/nutzungsbedingungen-entwurf.md`, `vorlagen/impressum-entwurf.md`, `vorlagen/anfrage-hvv-geofox.md` → **done**
- Anwalts-Briefing: Fragen 8 (Gate-Bewertung), 9 (DSA-Einordnung), 10 (Verbotsrisiko-Trigger) ergänzt → **done**
