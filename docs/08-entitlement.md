# TransitGuard — Entitlement-Dokument (Free/Pro: Matrix, Durchsetzung, Anonymität, Compliance, Ökonomie)

**Auftrag:** Prompt §3 (Free/Pro-Ausarbeitung). **Grundsatz vorab:** Die Paywall ist ein Produktmittel, kein Selbstzweck — sie darf die Meldungsdichte (Daten-Flywheel) und das Compliance-Narrativ nicht beschädigen. Alle Zahlen: eigene Rechnung, Annahmen markiert.

---

## 1. Entitlement-Matrix (Empfehlung; je Zeile Begründung)

Legende: ✔ = enthalten, ✘ = nicht enthalten. **Trennlinie-Prinzip: „Sicherheits-Kerninformation jetzt" = Free; „Komfort, Tiefe, Historie, Bequemlichkeit" = Pro** (wie bei Blitzerwarn-Apps: Grundwarnung gratis, Komfort kostet).

| # | Feature | Free | Pro | Begründung |
|---|---|---|---|---|
| 1 | Aktive Kontrollmeldungen sehen (Karte, ganze Stadt, alle Städte) | ✔ | ✔ | Kern-Sicherheitsinfo; Netzwerkeffekt (jeder Betrachter = potenzieller Melder); „paid escape"-Optik vermeiden (→ §4) |
| 2 | Meldung erstellen, bestätigen, widersprechen | ✔ | ✔ | Datenlieferanten **nie** drosseln — Drosselung hier tötet das Flywheel direkt (→ §5.3); Anti-Spam-Rate-Limits gelten für alle |
| 3 | Basis-Details (Station, Linie, Richtung, Alter, Typ, Status, „Verbleib unbekannt"-Kennzeichnung) | ✔ | ✔ | Ohne Basis-Details wäre die Free-Meldung wertlos und die Karte tot |
| 4 | Detailtiefe: Kontrolleur-Anzahl, Vertrauensstatus des Melders | ✘ | ✔ | Analytik, keine Handlungsoption verloren (man weicht der Linie auch ohne Anzahl aus) |
| 5 | Bewegungs-Verlauf des Trip-Ankers + „erreicht Station X in ~Y min" (movement/ETA) | ✘ | ✔ | Prognose-Komfort; das Basis-Fakt („Kontrolle auf Linie X Richtung Y, vor Z min") bleibt Free — ohne Pro ist man informiert, nur weniger präzise |
| 6 | Push für Stammstrecken/Favoriten (Hintergrund-Warnung) | ✘ | ✔ | Klassische Pro-Achse der Kategorie; kostet Infra (FCM/APNs); Grundwert in-App-Echtzeit bleibt Free |
| 7 | KI-Prognose (Risiko-Score je Linie/Tag/Stunde, Muster) | ✘ | ✔ | Statistisches Produkt aus Historie; kein Echtzeit-Handlungsverlust; benötigt ohnehin Datenreife (Post-Launch) |
| 8 | „Letzte Kontrolle auf deiner Linie vor X min" (Basis-Rückblick) | ✔ | ✔ | Ein-Klick-Faktenlage gehört zur Kern-Transparenz |
| 9 | Historie & Statistiken (Heatmaps, eigene Meldungshistorie >7 Tage, Netz-Trends) | ✘ | ✔ | Retention-basiertes Produkt; DSGVO-freundlich (Free-Historie kurz = Datenminimierung) |
| 10 | Störungen/Hinweise anzeigen (gefilterte GTFS-Alerts) | ✔ | ✔ | Downgrade-Realität (Bestandsaufnahme A1): bundesweit nur ~hunderte echte Items — als Pro-Kaufargument trägt das nicht; Free-Feature zur Alltagstauglichkeit |
| 11 | Störungs-Push + erweiterte Filter (Region/Linie als Digest) | ✘ | ✔ | Komfort-Variante von #10 |
| 12 | Offline-Fahrplan: Heimatnetz (Basis) | ✔ | ✔ | Kern-Companion-Wert; Datenmenge beherrschbar (CC BY-SA-Anforderungen identisch) |
| 13 | Offline-Fahrplan: alle Städtenetze + tagesaktuelle Folgeversionen | ✘ | ✔ | Ressourcen-Achse (Speicher/Aktualisierungsfrequenz), kein Sicherheitsbezug |
| 14 | Ticket-Deep-Link | ✔ | ✔ | **Immer** Free — das Compliance-Schild muss für alle Nutzer gelten |
| 15 | Werbung | keine (MVP enthält gar keine) | — | Falls später Werbung: Werbefreiheit als Pro-Bonus; keine Personalisierung (Rechtsteil) |

**Ausdrücklich verworfene Trennlinien (Kandidaten aus dem Auftrag):**
- **Vorwarnzeit** (Pro sieht früher): verworfen — Safety-Gating mit-optisch maximal schädlich („Bezahle, sonst wirst du später gewarnt") und justiziabel schlechter Standpunkt. 
- **Reichweite** (Free nur eigene Station): verworfen — rechnerisch tot: §5.2 zeigt, dass eigene-Station-Sicht bei realistischer Dichte ~1 sichtbare Meldung in Wochen bedeutet → App wirkt tot → Churn → weniger Meldungen → Tod der Datenbasis.
- **Meldungen pro Tag** (Sichtkontingent): verworfen — gleicher Mechanismus; zusätzlich bleibt bei Kontingent-Aufbrauch der Melderblindheit überlassen, ob er die Bestätigung seiner Meldung sieht → Feedback-Loop bricht.
- Meldung **erstellen** limitieren: kategorisch ausgeschlossen (siehe #2).

## 2. Serverseitige Durchsetzung (Paywall darf nicht client-seitig sein)

**Bedrohungsmodell:** Free-Client + Proxy soll Pro-Daten nicht bekommen. Folge: Reduktion findet im **Server** statt, Serialisierung inklusive.

1. **REST:** Tier-Former wählt DTO-Typ (`ReportFree`/`ReportPro`) **vor** Serialisierung; Free-JSON enthält Pro-Felder physisch nicht (nicht `null` — abwesend; Kontrakt-Test T-EN-1). Enforcet in einer Middleware-Kette: Device-Auth → Entitlement-Auflösung → Feature-Flags → Rate-Limit.
2. **SignalR:** Getrennte Gruppen `…reports.free`/`…reports.pro` mit unterschiedlichen Payloads; Gruppenbeitritt nur nach Token-Prüfung im Hub (06 §1). Ein Free-Client kann die Pro-Gruppe nicht betreten (Fehler + Metrik `entitlement_join_violations`).
3. **Push:** Pro-Feature → Registrierung von Push-Tokens nur mit aktivem Pro-Entitlement; nightly Reconciliation entzieht Registrierungen abgelaufener Entitlements.
4. **DB-Ebene:** `entitlements` nur über Rolle `tg_billing` erreicht (GRANT + FORCE RLS, 0003 §2); der API-Hauptprozess (tg_app) kann Entitlements nicht einmal lesen — eine SQL-Injection in einem Content-Endpoint exfiltriert keine Abo-Daten.
5. **Operations-Daten:** Kill-Switch-Schalter (feature_flags) für tg_app nur `is_public`-Zeilen lesbar (RLS, 0003 §3) — Ops-Interna bleiben unsichtbar.
6. **Metriken der Durchsetzung:** `entitlement_join_violations`, `tier_dto_mismatches` (soll 0), `billing_restore_failures` — im Ops-Dashboard alarmiert.

**Bewusst akzeptiert:** Serverseitige Durchsetzung verhindert Pro-Diebstahl nicht zu 100 % (Token-Sharing, s. §3), aber jedes Leck ist serverseitig sichtbar und widerrufbar. Client-Obfuskation wäre Security by Obscurity — nicht gebaut.

## 3. Anonymität contra Abo — Lösungsdesign (ADR-0005)

**Konflikt:** Produkt verspricht nutzung ohne Registrierung; ein Abo braucht eine wiedererkennbare Berechtigung. Auflösung: **Besitz-Modell statt Identitäts-Modell.** Berechtigung ist der Besitz eines geheimen Tokens, nicht ein Konto.

```
Kauf (App Store/Play)  ──▶  POST /v1/billing/activate {platform, receipt}
                              Server validiert Receipt gegen Store-Server-API
                              (App Store Server API / Google Play Developer API —
                               Details vor Implementierung verifizieren, offene Frage F-12)
                              ├─ Receipt bereits benutzt? → 409 receipt_already_used
                              └─ ok ⇒ Entitlement anlegen:
                                   access_token  = "et_" + 32 Byte Zufall (Base64url)
                                   restore_code  = XXXX-XXXX-XXXX-XXXX (16 Zufallszeichen)
                                   Server speichert NUR sha256-Hashes beider Werte
Antwort (einmalig!) enthält Token + Restore-Code → Client speichert im Secure Storage
(Keychain/Keystore) + Nutzer wird zur Notiz des Restore-Codes aufgefordert.
```

| Frage | Antwort / Regel |
|---|---|
| **Gerätewechsel** | Restore-Code eingeben → neues access_token (altes bleibt gültig bis Ablauf, widerrufbar). Kein Server-Profil, das Geräte verkettet. |
| **Verlust beider (Token + Code)** | Abo-Potenzial verloren bis zur nächsten Store-Verlängerung — dann wieder über `activate` (Receipt-Dedup erlaubt erneute Ausstellung **für dieselbe laufende Subscription**; Store-Server-Validierung entscheidet). Ehrlich kommunizieren: „Code notieren." |
| **Token-Sharing** | Cap 3 gleichzeitige SignalR-Verbindungen + Verbindungs-Metrik; Renewal-Validierung bei jeder `validate`; REST-Rate-Limits greifen je Token. Restrisiko: N<=3 Freunde teilen 3,49 €/Monat — akzeptiert (Kosten der Härte wären Identitätsbindung). |
| **Trust-Score bei Wechsel** | Bleibt an altem Gerät (device_id), **nicht übertragbar** in v1. Begründung: Übertragung erzeugt eine Gerät↔Gerät-Kante = Identitäts-Anreicherung, genau was das Konzept vermeidet; außerdem wäre Trust-Transfer ein Missbrauchs-Vektor (gek Accounts mit Trust verkaufen). v2-Option: geräteseitiger Both-Online-Handshake (alter Client signiert Übergabe), standardmäßig aus. |
| **Refund/Kündigung** | Store-Server-Notifications (Webhook) oder Validierung bei Nutzung → Status `expired`; Tier fällt auf Free; Meldungsdaten bleiben (anonym, Crowd-Eigentum). |
| **DSGVO-Einordnung** | Entitlement-Token ist pseudonym (Online-Kennung, ErwG 26). Keine Verknüpfung mit device_id in der DB; Receipt-Hash ist der gesetzlich erforderliche Abrechnungsbeleg (Interessenabwägung; Speicherdauer nach steuerlichen Vorgaben, Rechtsteil). |

## 4. Compliance-Analyse der Paywall (ergebnisoffen geprüft; Empfehlung widerspricht bewusst dem naheliegenden Wunsch)

### Szenario A: Kontrollmeldung als Pro-Kern („Paywall trägt das Kontroll-Feature")
- **Store-Review:** Risiko steigt spürbar. Ein Produkt, dessen Bezahl-Kern die Kontrollwarnung ist, wird als „paid facilitation" gelesen — Apple/Google-Leitlinien kennen keine explizite „Fare-Evasion-Regel", aber Ablehnungen nach Ermessen (Apple 5.1/„illegal or reckless behavior"-Nachbarschaft, Play „illegale Dienste") sind dann Realität (*Einschätzung, nicht Rechtsauskunft*). Review-Notes müssten das Geschäftsmodell erklären — schlecht verteidigbar.
- **Beihilfe-Narrativ:** Die anwaltliche Einschätzung („zulässig, solange nicht aktiv zur Schwarzfahrt aufgerufen") kippt in der öffentlichen Wahrnehmung, sobald Warnung × Geld entsteht: „Kommerzielles Produkt verkauft Kontrollvermeidung" ist der Fall, den Verkehrsunternehmen juristisch suchen. Die BVG prüft bereits Schritte gegen das *gemeinnützige* FreiFahren (Dossier 2.6) — ein kommerzieller Akteur wäre die bessere Klageziel-Geschichte.
- **Öffentliche Wahrnehmung:** Presse-Schlagzeilen wären vorprogrammiert („Bezahl-App warnt vor Kontrollen"). Das positioning „Companion mit Community-Feature" würde vom eigenen Preismodell widerlegt.
- **Netzwerkeffekt:** Melder sind Free-Nutzer; Paywall vor dem Kern trennt Seher und Melder → §5.2-Rechnung gilt doppelt.

### Szenario B (Empfehlung): Kontroll-Kern Free, Komfort/Tiefe/Historie Pro
- Store-Review: Geschäftsmodell erklärbar ohne das Wort Kontrolle („Pro = Push, Prognosen, Statistiken, Offline-Komfort" — identische Achsen wie etablierte Verkehrapps).
- Beihilfe-Argument unverändert zur Free-Only-Lage; Ticket-Kaufsandbox bleibt für 100 % der Nutzer.
- Öffentlichkeit: „Community-App, finanziert Komfort-Features" — angreifbar bleibt das Kontroll-Feature selbst (Risiko G-legal, Kill-Switch vorhanden), aber die Monetarisierung fügt kein neues Angriffsziel hinzu.
- Flywheel unangetastet: alle melden, alle sehen — Pro verkauft Bequemlichkeit obendrauf.

### Empfehlung (auch gegen den ersten Impuls des Auftrags)
**Szenario B.** Die Kontrollmeldung bleibt vollständig Free (Sehen UND Melden). Pro trägt sich aus Push (#6), Prognose (#7), movement/ETA (#5), Historie (#9), Offline-Vielfalt (#13) — lauter Achsen, die etablierte Kategorien (Blitzer-, Verkehr-, Wetter-Apps) ebenfalls monetarisieren, ohne Sicherheits-Kerninformation zu Gate-n. Sollte sich nach 6 Monaten zeigen, dass Pro-Konversion strukturell <1 % bleibt, ist eine **Option C** denkbar (Pro-Exklusivität nur für #7+#9, niemals #1–#3, #8, #14) — dokumentiert als Revisionspunkt, nicht als Plan.

## 5. Freemium-Ökonomie (gerechnet, nicht behauptet)

### 5.1 Kalkulationsbasis
Benchmark FreiFahren: 3.518 Meldungen/7 Tage **sekundärbestätigt** (netz-trends 17.08.2026); >55.000 meldende Nutzer (itopnews 01.08.2026) vs. ~40 k Regel-Nutzer (Dossier) ⇒ **0,0126 Meldungen/Nutzer/Tag** (konservativ, F-9/17). Alle Folgerechnungen skalieren linear mit dieser Größe — ihr Wahrheitsgehalt ist der größte Hebel der Rechnung.

### 5.2 Dichte-Rechnung Hamburg (warum Drosselung tödlich ist)
Annahmen (alle *Unsicher*, bewusst konservativ-basis): Hamburg Launch +6 Monate: 8.000 MAU; aktiver Zeitraum 18 h/Tag; relevante Linien-Richtungen Schiene ~20, Bus ~280 (HVV-Größenordnung *Glaube ich*, nicht belegt — F-10).

- Erwartete Meldungen: 8.000 × 0,0126 ≈ **101/Tag ≈ 5,6/h** (über 18 h).
- Pro Schienen-Linie+Richtung: 5,6 ÷ 20 ≈ **0,28 Meldungen/h** → im Mittel alle ~3,5 h eine Meldung „pro Linie". 
- Eigene-Station-Sicht (verworfene Trennlinie „Reichweite"): ~10.000 Haltestellen im Netz (*Glaube ich*) → Wahrscheinlichkeit, dass eine zufällige Meldung „an meiner Station" (±500 m, 1–3 Haltestellen) liegt ≈ 1–3/10.000 ⇒ erwartete sichtbare Meldungen **0,01–0,03/Tag ≈ 1 Treffer alle 1–3 Monate**. Die Free-App wäre faktisch leer → Churn → Meldungen sinken → noch leerer (Todesspirale). Einzige Gegenmaßnahme wäre Stadt-weite Sicht — genau Zeile #1 der Matrix.
- Selbst im Optimistik-Szenario (25.000 MAU ≈ 315 Meldungen/Tag) sieht die Einzel-Station im Schnitt nur **1 Treffer alle 10–30 Tage**. Die Karte (Bündelung) ist kein Luxus-Feature, sie ist die einzige Darstellungsform, in der die Datenmenge überhaupt Nutzen erzeugt.

### 5.3 Konversions- und Erlösszenarien (Preis 3,49 €/Monat, Annahme *Unsicher* — A/B-fähig)
Free-to-Paid-Konversion Consumer-Utilities: 2–5 % (*Glaube ich*, branchenüblich publiziert, nicht belegt — F-11). Store-Gebühr 15–30 %.

| Szenario (Monat 6) | MAU | Pro (3 % Basis; Range 2–5 %) | Brutto/Monat | Netto (70–85 %) |
|---|---|---|---|---|
| Pessimistisch | 2.000 | 60 (40–100) | 209 € (140–349) | 98–297 € |
| Basis | 8.000 | 240 (160–400) | 838 € (558–1.396) | 390–1.187 € |
| Optimistisch | 25.000 | 750 (500–1.250) | 2.618 € (1.745–4.363) | 1.221–3.708 € |

Betriebskosten (Jahr 1, Hetzner-Listenpreise *Glaube ich*, F-8): VM ~15–56 € + Storage Box ~5 € + Domain ~1 € ≈ **21–62 €/Monat**. Ergebnis: Break-even der Infrastruktur schon im Pessimistik-Szenario möglich; ein Einkommen entsteht frühestens im Optimistik-Szenario — **die Paywall refinanziert das Produkt, sie finanziert kein Leben.** Daraus folgt zwingend die Sequenzierung: Entitlement-Infrastruktur ab Tag 1 (sonst späterer Umbau), Pro-Schalter **aus** bis Launch+Push (feature_flags `pro_tier_enabled=false`, so ausgeliefert).

### 5.4 Flywheel-Schutz als Ökonomie-Regel
Jede künftige Paywall-Idee wird gegen drei Tests geprüft (in dieser Reihenfolge): (1) verkleinert sie die Menge aktiver Melder oder Seher? → dann nein. (2) ist sie in der Presse-Schlagzeile „App verkauft X" verteidigbar, wobei X ≠ Kontrollvermeidung sein muss? (3) ist sie serverseitig sauber durchsetzbar (§2)? Nur wer alle drei besteht, kommt in die Matrix.
