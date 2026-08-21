# TransitGuard — Rechts- & Compliance-Dokument

**Kein Rechtsdokument im Sinne einer Rechtsberatung.** Alle Aussagen mit Konfidenz-Marker; verbindliche Klärung nur durch anwaltliche Prüfung (Aufwand in `12-offene-fragen.md`). Stand 2026-08-21, eigene Quellenrecherche eingearbeitet.

## 1. Lizenzen der Datenquellen & Attribution

| Quelle | Lizenz | Beleg / Status | Pflichten für uns |
|---|---|---|---|
| gtfs.de GTFS-Static (nv/rv/fv free) | **CC BY 4.0** (Feed-Seiten verlinken creativecommons.org/licenses/by/4.0) | *Bestätigt*: gtfs.de/de/feeds/de_{nv,rv,fv} (21.08. gesichtet) | Namensnennung (gtfs.de/DELFI/Verbünde). **Abweichung zum RT-Stream (CC BY-SA 4.0)** — getrennt attributieren & deklarieren |
| gtfs.de GTFS-RT (Free) | **CC BY-SA 4.0** | *Bestätigt*: Anbieterseite gtfs.de/de/realtime (21.08. + 14-j3-j6 §4) | Namensnennung; ShareAlike-Klärung für API-Weitergabe (F-2, mit Anwalt) |
| DELFI (über gtfs.de, NeTEx-Ursprung) | Namensnennung läuft über gtfs.de-Kette; DELFI-eigene Lizenz *Weiß ich nicht* (F-2) | indirekt | mit F-1 klären |
| VBB GTFS-RT | **CC BY 4.0**, 60 Req/min, „ohne Gewähr" | *Bestätigt*: production.gtfsrt.vbb.de (21.08.) | nur Namensnennung (kein SA); im MVP nicht konsumiert; Berlin-Overlay-Pflicht dokumentiert |
| HVV GTFS-Static (Transparenzportal HH) | **Datenlizenz Deutschland Namensnennung 2.0**; Namensnennung „Hamburger Verkehrsverbund GmbH" | *Bestätigt*: GovData-Metadaten (21.08.) | Backup-Quelle für HH-Static; Attribution dann auf HVV umstellen |
| Geofox-API (falls Overlay) | unbekannt | nicht geprüft (F-4) | nur relevant wenn J4-Follow-up |

**Attributions-Umsetzung (konkret):**
1. App: „Fahrplan- & Echtzeitdaten: gtfs.de / DELFI e.V. / beteiligte Verbünde, CC BY-SA 4.0" — Einstellungen + About, verlinkt auf Lizenztext.
2. API: HTTP-Header `X-Data-Attribution` auf allen Fahrplan-/Echtzeit-Endpoints; gleicher Text in OpenAPI-Beschreibung.
3. PWA-Quellcode-Kommentar + Impressum.
4. Der Feed transportiert Namensnennungen zusätzlich **in-band** als Attribution-Alerts (gemessen: „Echtzeitdaten aufbereitet von GTFS.de, bereitgestellt von <Verbund>", N1) — wir führen die Verbund-Liste als Metadatum (`attribution_services`-Tabelle möglich, aus Noise-Pipeline gespeist) statt 64k Duplikat-Alerts anzuzeigen.

## 2. Datenschutz (DSGVO)

**Rollen:** Betreiber = Verantwortlicher; keine Auftragsverarbeiter außer Hoster (Hetzner, AVV nötig — F-5 prüfen/abschließen).

**Datenminimierung als Produktprinzip:** keine Konten, keine E-Mail, Standort bleibt auf dem Gerät (nur Meldungsinhalt verlässt das Gerät). ABER ehrlich: `device_id` + Token + Verhaltensdaten (Meldungen, Bestätigungen) = **pseudonyme personenbezogene Daten** (Erwägungsgrund 26: Online-Kennungen). Die v0.1-Formulierung „keine personenbezogenen Daten speichern" ist **zu absolut** und wird ersetzt durch: „pseudonym, minimiert, kurzlebig".

| Datenkategorie | Speicherdauer | Löschweg |
|---|---|---|
| Meldungen (reports) | 90 Tage (Partition-Drop) | automatisch; DSGVO-Art.-17-Anfrage überflüssig macht Zusammenfassung: Meldungen anonym nach Device-Löschung |
| device, trust_scores | bis Selbstlöschung + 90 Tage Inaktivität | `DELETE /v1/devices/me`; Job löscht inaktive |
| Entitlements | Token-Hashes bis Widerruf/Ablauf; Receipt-Hash steuerrechtlich (Unsicher: Dauer, F-6) | revoke; Refund-Hook |
| IP in Logs | 14 Tage (journald) | Rotation |
| Push-Tokens (Post-MVP) | bis Deaktivierung | App-Deinstallation → Provider-Bounce |
| Ingest-Metriken | 180 Tage, ohne Personenbezug | Rotation |

**Betroffenenrechte ohne Konto:** Auskunft/Löschung selbstbedient über Device-Token („Meine Daten" → JSON-Export; „Gerät löschen"). Für Lost-Device-Fälle: Impressum-Kontakt manuell (Nachweis über …… — ehrlich: ohne Konto ist Verifikation schwach; dokumentierte Prozedur + Einzelfallentscheidung).
**Keine** Verhaltensprofile für Werbung; keine Weitergabe an Dritte (B2B-Datenverkauf verworfen — Entscheidungsprotokoll).
**Datenschutz-Folgenabschätzung:** empfohlen (systematische Verhaltensaufzeichnung); vor Launch erstellen (F-7).

## 3. Kontroll-Feature: rechtliche Einordnung (Stand der Diskussion)

> **Update 21.08.2026 — Vollständige Recherchielage mit Quellen: `15-rechtsstand-2026-08.md`.** Kernpunkte: §265a-Reform am 16.04.2026 vom Bundestag abgelehnt (Norm gilt unverändert); Beihilfe/§111/§258 nach zitierten Juristen-Einschätzungen fernliegend, Blitzer-App-Verbot (§23 Abs. 1c StVO) nicht übertragbar; **Hauptrisiko zivilrechtlich** (§823 BGB Eingriff in Gewerbebetrieb — kommerzieller Betreiber angreifbarer als e.V.); DSGVO-Risiko Kontrolleur-Profile durch unsere Architektur adressiert; **Apple entfernt FreiFahren (08/2026) aus dem App Store → Launch-Reihenfolge PWA → Play → iOS** (umgesetzt in Roadmap + Entscheidungsprotokoll). FreiFahren-Benchmark-Zahlen (3.518/7 d, >55 k meldende Nutzer) sekundärbestätigt.

- Ausgangslage (Dossier 2.6): nach medial zitierter anwaltlicher Einschätzung sind Kontrollmeld-Apps derzeit zulässig, solange nicht aktiv zur Schwarzfahrt aufgerufen wird. §265a-Reform gescheitert (April 2026, *nun präzise verifiziert: Ablehnung 16.04.2026, Bundestag 21/5378*). **Keine Rechtsberatung.**
- Unsere Verstärkungen (vollständiges Härtungspaket seit 21.08.2026: **`18-rechtliche-haertung.md`**): **Ticket-First-Gate** (H1: Kontroll-Zugang nur mit Ticket-Bestätigung + Kauf-Empfehlung bei jeder Warnung), **AGB-Zweckbestimmung** (H2), **DSA-Notice-and-Action-Kanal** (H3), Legislative-Monitoring mit 7-Tage-Trigger (H4), Rechtsform-Empfehlung UG/GmbH (H5), öffentliche Selbstverpflichtung (H6); dazu Companion-Framing + Ticket-Deep-Links für ALLE Nutzer + Kill-Switch (3 Stufen) + keine Umlauf-Spekulation.
- Beihilfe-Risiko steigt mit Kommerzialisierung des Kerns → Paywall-Empfehlung Szenario B (Entitlement-Doc §4) ist auch Rechtstrategie.
- Copy-Guidelines (produktiv): niemals „Schwarzfahren", „ohne Ticket fahren", „Kontrollen entgehen"; immer „Sicherheits-/Community-Hinweis", „kaufe dein Ticket" (CTA bei Warnung). Gilt für Store-Texte UND In-App-Copy.
- BVG-Rechtslage gegen FreiFahren beobachten (RSS/Presse); eigenes Notfall-Runbook: Kill-Switch + anwaltlicher Erstkontakt (Rechtsteil Anhang A, 1 Seite, im Repo `docs/runbook-legal.md` bei Launch zu erstellen).

## 4. Kill-Switch (verbindliche Spezifikation)

| Stufe | Mechanismus | Wirkung | wer darf |
|---|---|---|---|
| 1 | `feature_flags.reports_enabled=false` (tg_ops) | API 404 `feature_disabled`; Hub control-Event; Neueingaben global gestoppt | Ops |
| 2 | `cities.is_active=false` | Stadt verschwindet (RLS); Ingest für Stadt pausiert | Ops |
| 3 | Caddy `respond 503` auf api-Subdomain | Notfall ohne DB-Kontakt | Server-Admin |
Test T-E2E-3 beweist: keine App-Update nötig; Clients laufen als Fahrplan-Companion weiter. **Public-Commitment:** Reaktionszeit auf Behördenkontakt ≤ 24 h (Impressum-Postfach monitored). **Ergänzt (H3):** Eskalationsstufen für Unternehmensbeschwerden — Einzelmeldung entfernen → Stadt deaktivieren → globaler Kill-Switch (kanalisiert Konflikte, bevor aus Verärgerung eine Klage wird).

## 5. Impressum & formale Pflichten (DE)

- Impressum (§5 DDG — *Glaube ich*: TMG wurde 2024 in DDG überführt; anwaltlich gegenprüfen, F-9) mit Verantwortlichem, Postfach-Kontakt; gilt für App + PWA + API.
- Stores: Anbieterkennung, Altersfreigabe 4+/12+ (Apple/Google unterscheidlich — Store-Texte je F-10), Privacy-Nutrition-Labels wahrheitsgemäß („Data Not Linked to You" — nur wenn wirklich zutreffend, prüfen!).
- Domain: WHOIS-Datenschutz default; Betreiber-Identität trotzdem im Impressum (Pflicht).

## 6. Store-Strategie (Reihenfolge aktualisiert 21.08.2026 nach Apple/FreiFahren-Präzedenz — Details `15-rechtsstand-2026-08.md` §3)

1. **Launch-Kanal 1: PWA** (store-unabhängig, DE-gehostet, funktionsgleich inkl. Kontroll-Karte) — Distribution-Risiko null, Cold-Start über teilbare Links (Innovation A4).
2. **Launch-Kanal 2: Google Play** — Einreichung als „ÖPNV-Companion: Fahrplan, Abfahrten, Störungen, Barrierefreiheit"; Kontroll-Feature in Beschreibung als eines unter mehreren, nie Titel/Icon/Screenshots-dominant.
3. **Launch-Kanal 3 (zurückgestellt, F-19): Apple App Store** — erst nach ~6 Monaten stabilen Companion-Betriebs; Review-Notes-Dossier mit belegtem Mehrspalten-Charakter (Fahrplan-Nutzungszahlen, Barrierefreiheits-Feature A1, Ticket-Kauf-Stats). Apple hat den Referenzfall FreiFahren Mitte 2026 entfernt — Risiko als real bewertet, nicht hypothetisch.
3. Ablehnungs-Eskalationspfad: Appeal mit Dossier; notfalls Feature-Flags regionale Reduktion (z. B. reports_enabled=false je Stadt → Store-Rerun ohne Feature).
4. PWA bewusst funktionsgleich (auch Kontroll-Karte): Vertriebs-Unabhängigkeit als Risiko-Hebel — dokumentiert, damit kein Store das Produkt erpressen kann. PWA hosten in DE (Hetzner ✓).

## 7. Offene Rechts-Risiken (Kurzliste; Details 12-offene-fragen.md)

F-1 anwaltliche Gesamtprüfung (**Launch-Gate**, Briefing liegt vor) · F-2 ShareAlike-Umsetzung API (mit F-1) · F-5 Hosting-AVV · F-7 DSFA finalisieren (Entwurf liegt) · F-9 Impressums-/DDG-Feinheiten (mit F-1) · F-18 Rechtsform (mit F-1) · F-11 Store-Label-Wahrheit · F-13 Produktname-Markenrecherche („TransitGuard" Arbeitstitel; Quick-Check 21.08. ohne Kollision, DPMA/EUIPO offen).
