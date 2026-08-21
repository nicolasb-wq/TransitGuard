# TransitGuard — Entscheidungsprotokoll (inkl. verworfener Optionen)

**Format:** Entscheidung → ADR-Verweis (Detailbegründung) → verworfene Alternativen mit Verwerfungsgrund. Status: 🟢 beschlossen (dieses Paket) · 🟡 empfohlen, wartet auf Product Owner · ⚪ verworfen.

## Architektur & Daten

| Entscheidung | Status | Verworfene Alternativen (Grund) |
|---|---|---|
| Trip-Zuordnungs-Anker statt Positions-Tracking | 🟢 ADR-0001 | Fahrzeug-Anker (0 nutzbare vehicle-Felder, 2× gemessen); Nutzer-GPS-Crowd-Position (DSGVO-Hebel, unnötig); VP-Zuschaltung im MVP (YAGNI — Pfad bleibt Schema-Flag) |
| **Stadt-Commitment Hamburg + Umland erteilt (21.08.2026)** — Messbasis: 3.740 gleichzeitige Trips, 94,5 % Delay-Deckung, beste Stadt der Stichprobe; Stadt-Extrakt mit Umland-BBox (53.30–53.85 N, 9.55–10.50 E) | 🟢 14-j3-j6 | Berlin (via gtfs.de nur 984 TUs — bräuchte VBB-Overlay trotz aktiver Datenlücke); München/Köln (schlechtere Deckung als HH, kein Standort-Vorteil) |
| **Launch-Reihenfolge: PWA zuerst → Google Play → iOS zuletzt/ggf. gar nicht** — nach Apple-Entfernung des Referenzfalls FreiFahren (08/2026) | 🟢 15-rechtsstand §3 | iOS-first (hohes Ablehnungs-/Entfernungsrisiko nachweislich real); Play-only (PWA ist Distribution-Unabhängigkeit + Cold-Start-Kanal A4) |
| **Berlin-Expansion nur mit VBB-Overlay** (gtfs.de deckt Berlin nicht: 984 TUs) | 🟢 14-j3-j6 §3 | Berlin über Basis-Feed (Datenlage unzureichend nachgewiesen) |
| **F-15 entschieden (delegiert 21.08.):** Bus-TTL 720/1800/480 s; Trip-Anker wie Schiene | 🟢 | Schienen-Werte überall (zu träge für Bus-Takte) |
| **F-16 entschieden (delegiert 21.08.):** 45-min-Cap nur Stations-Anker; Trip-Anker endet `trip_end + 120 s` | 🟢 | ausnahmsloser Cap (v1.0-Wortlaut — schnitt Langfahrten ab) |
| **Barrierefreiheits-Badges aus Ausstattungs-Alerts (A1)** aufgenommen | 🟢 16-innovationen | Daten wegwerfen (Noise-Pipeline verwertet sie jetzt zweistufig) |
| gtfs.de als Basis-Feed, Overlay nur bei nachgewiesenem Mehrwert | 🟢 ADR-0002 | VBB parallel (0 Alerts, 43,8 % Delay, aktive Datenlücke, 8,3 MB Overhead); je-Verbund-Feeds ab Tag 1 (Operationslast ohne Nutzen); Geofox (keine offene RT-Quelle, J4) |
| PostgreSQL deklarative Tages-Partitionen, kein TimescaleDB | 🟢 ADR-0003 | TimescaleDB (Stack-Konflikt A7: vom Übergabe-Prompt nicht gesetzt; zweite Extension im Betrieb); Einzel-Tabelle + DELETE (TTL-Cleanup-Skandale vorprogrammiert); MongoDB (Stack) |
| PostGIS, kein H3 im MVP | 🟢 ADR-0004 | H3-Pyramide (v0.1-Erbe; Analyse-Feature ohne MVP-Nutzer) |
| Stadt-Extrakt statt DE-weitem Dictionary im Speicher | 🟢 ADR-0008 | DE-weites trip-Lookup (~2 Mio. Fahrten — dreistelliger MB-RAM-Bereich lt. v1.1-Schätzung, unnötige Betriebslast); reines Postgres-Lookup pro Entity (Hot-Path zu langsam bei 81 k TUs/Poll) |
| Alert-Pipeline mit Noise-Filter + Dedup; kein UI-Prominenz-Redesign | 🟢 ADR-0011 | Alerts als Stand-Alone-Wert (durch Messung widerlegt: 519 distinct, >90 % Attribution/Ausstattung); Alert-Ingest streichen (Companion-Wert bleibt, Kosten marginal) |
| Interpolation Soll+Clamp-Delay statt TU-STU-only | 🟢 (02 §6) | reine STU-Auswertung (gtfs.de Median 3 STU — zu dünn) |

## Produkt & Monetarisierung

| Entscheidung | Status | Verworfene Alternativen (Grund) |
|---|---|---|
| Kontroll-Kern komplett Free; Pro = Push/Prognose/movement/History/Offline-Vielfalt | 🟢 ADR-0006 (Empfehlung an PO, konsequent umgesetzt in Matrix) | Kontrollinfo hinter Paywall (Compliance §4: Store-Risiko, Beihilfe-Optik, Flywheel-Bruch); Vorwarnzeit-Gating (Safety-Gating, Optik); Reichweiten-Gating (rechnerisch tot: 1 sichtbare Meldung in Wochen); Sichtkontingente (Feedback-Loop bricht); Melden limitieren (kategorisch nein) |
| Besitz-Token-Entitlement ohne Konto, ohne Geräte-Verkettung | 🟢 ADR-0005 | Nutzerkonten mit E-Mail (Anonymitäts-Bruch); Geräte-gebundene Käufe (App-Store-Policies + Restore-Chaos); ID-Verkettung für Trust-Transfer (DSGVO-Anreicherung, Missbrauchs-Angriffsfläche) |
| Trust nicht übertragbar bei Gerätewechsel (v1) | 🟢 ADR-0005 | Both-Online-Handshake jetzt bauen (v2-Option, Komplexität ohne echte Nachfrage-Daten) |
| Entitlement-Infrastruktur ab Tag 1, `pro_tier_enabled=false` bis Launch | 🟢 (08 §5.3) | Paywall später „dranbauen" (Umbau von DTO/Hub-Ketten später teurer); sofortige Aktivierung (Konversions-Optik vor Datenqualität) |
| Ticket-Deep-Link immer, für alle Tiers | 🟢 (E-Entscheidung v0.1, verschärft) | Paywall vor Ticket-Link (Compliance-Schild muss alle erreichen) |
| **Ticket-First-Gate** (H1): Kontroll-Feature nur nach Bestätigung eines gültigen Tickets; Kauf-Option in jeder Warn-Situation; keine Zugriffs-Option ohne Ticket | 🟢 18-rechtliche-haertung | Kein Gate (größtes Restrisiko: Gestaltungs-Beihilfe-Optik); hartes Server-Gate mit Verifikation (technisch unmöglich ohne Identität, DSGVO-Widerspruch) |
| **AGB-Zweckbestimmung + DSA-Notice-and-Action** (H2/H3) | 🟢 vorlagen/nutzungsbedingungen-entwurf | keine AGB (fehlender Beleg dokumentierten Willens); Konfrontationskurs mit Verbünden (Eskalationsrisiko Klage) |
| **Rechtsform-Empfehlung UG/GmbH + Versicherungen** (H5/F-18) | 🟡 (Empfehlung, mit F-1 final) | e.V. (kollidiert mit Pro-Einnahmen, verhindert Klagen nicht); Privatbetrieb (privates Vermögen im Risiko) |
| B2B-Datenverkauf | ⚪ | v0.1-brainstorm; ethisch/DSGVO verworfen (Rechtsteil §2) |
| Werbung im MVP | ⚪ | kein Bestand; falls später: nicht personalisiert, Pro = werbefrei (Matrix #15) |
| Umlauf-Verkettung (Rücktrip-Heuristik) | ⚪ | v1.1 B.5 — ohne Fahrzeug-ID Geratei; v2-Option nur mit echten Meldedaten belegt |

## Regeln & Betrieb

| Entscheidung | Status | Verworfene Alternativen |
|---|---|---|
| TTL-Engine = reine Funktion, tabellengetrieben, alle Werte Config | 🟢 ADR-0007 | Streuende Regeln im Code (untestbar); Client-seitige TTL (Manipulation) |
| 45-min-Cap: Empfehlung „nur Stations-Anker" | 🟡 5.2 | ausnahmsloser Cap (v1.0-Wortlaut) — Entscheidung PO (F-16) |
| Bus-TTL-Werte 720/1800/480 s | 🟡 5.1 (Rekonstruktion) | Schienen-Werte überall — Entscheidung PO (F-15) |
| G11-Health-Gate vor jeder Degradation | 🟢 (07 §5) | blinde Regelausführung (VBB-Szenario würde Karten leeren — B1) |
| Kill-Switch 3-stufig serverseitig | 🟢 ADR-0009 | App-Update-abhängig (unbrauchbar im Notfall); nur Stufe 3 (kein selektiver Rückzug) |
| Polling 60 s, ein Feed, ETag senden ohne Ersparnishoffnung | 🟢 ADR-0010 | 30 s (doppelter Traffic für keine UX-Wahrnehmung, Feed regeneriert 10 s); VBB-Mitpoll (kein Inhalt); 304 als Sparstrategie (widerlegt, N4) |
| RLS + Rollensplit als Defense-in-Depth, ehrlich begrenzt | 🟢 ADR-0013 | „RLS löst Sicherheit" (tut es im Ein-Mandanten-Anonymous-Setup nicht allein); gar kein RLS (Anforderung des gesteckten Stacks) |
| Deployment rsync + Symlink-Release + systemd | 🟢 ADR-0012 | Docker auf Server (Registry-Verbot; reinstall-Komplexität); Git-Pull-Deploy (kein CI, aber auch kein Rollback-Kanal); Blue/Green-VMs (Kosten) |
| Migrations nur vorwärts, SQL-Dateien, Backup-Pflicht vor Deploy | 🟢 (10 §4) | EF-Migrations (Schema-Drift-Risiko, weniger lesbar); Down-Migrations (Illusion von Reversibilität) |

## Verwandte Vorentscheidungen (unbeanstandet übernommen aus v1.1/Übergabe)

Anonymität ohne Registrierung · Hamburg-Empfehlung unter J3-Vorbehalt · keine Telegram/NLP-Ingestion im MVP · Flutter + React-PWA · Hetzner/Caddy/rsync-Stack · CANCELED ⇒ TTL 0 · keine spekulative Verkettung · Labels „Echtzeit-Prognose"/„Laut Fahrplan" („Live" entfällt).
