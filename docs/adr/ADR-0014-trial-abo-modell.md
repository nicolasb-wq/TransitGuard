# ADR-0014 — 14-Tage-Testphase, danach Abo 2,99 €/Monat (ersetzt Free-forever-Matrix)

**Kontext (Produktentscheidung Auftraggeber 21.08.2026):** „14 Tage free, danach Abo 2,99 €/Monat." Damit wird die alte Free/Pro-Zweiteilung (ADR-0006: Kontroll-Kern dauerhaft gratis) **ersetzt**: Nutzung ist zeitlich begrenzt gratis, danach paywall über alle Features.

**Optionen:** (a) Free-forever-Kern + Pro (ADR-0006, alt), (b) 14-Tage-Trial → Abo komplett (Entscheidung), (c) Trial → Abo nur für Komfort, Melden/Sehen gratis (Kompromiss).

**Entscheidung:** (b) — umgesetzt als `TrialPolicy` (Geräte-gebunden, kontolos: Trial-Start = Device-Ausstellung; Abo wie bisher anonymes Besitz-Token, jetzt 2,99 €/Monat). Server-Durchsetzung: `AccessGate` (Trial → Subscriber → Locked, 402 `trial_expired` auf allen Feature-Endpunkten inkl. Kontroll-Zugriff; Billing/Ticket-Info/Health bleiben offen — Compliance: Kaufweg nie blockiert). Ticket-First-Gate (H1) bleibt in ALLEN Stufen aktiv.

**Konsequenzen — bewusst und dokumentiert:**
1. **Compliance-Re-Flag (Pflicht-Hinweis, kein Widerspruch, aber anwaltlich mit F-1 zu bestätigen):** Mit dieser Monetarisierung ist die Kontroll-Info ab Tag 15 **bezahlt**. Genau das Szenario A aus 08-entitlement §4, das wir als rechtlich/optisch riskant bewertet haben (bezahlte Warnung; kommerzieller Anbieter ist attraktiverer Klagegegner als e.V.). Mitigate: Ticket-Gate + Kauf-CTA + neutrale Copy + Kill-Switch + Rechtsform-Empfehlung (H5). **Diese Entscheidung ist explizit vom Auftraggeber getroffen und umgesetzt; die Risikodokumentation bleibt in 15-rechtsstand §1/§6 verlinkt.**
2. **Netzwerkeffekt-Risiko (08 §5):** Melder nach Trial = nur noch Abonnenten. Kompromiss-Option (c) bleibt per Config erreichbar (`AccessGate` ist der einzige Umschalt-Punkt; keine Architektur-Änderung nötig) — Empfehlung: nach 3 Monaten echten Daten prüfen, ob Meldedichte leidet.
3. **Ökonomie neu (2,99 statt 3,49 €):** Basisszenario 8.000 MAU × 3 % = 240 Abos ⇒ 718 €/Monat brutto (Range 2–5 %: 478–1.196 €); Netto (70–85 %) 334–1.017 €. Infrastruktur (~21–62 €) bleibt im Pessimistik-Szenario gedeckt (182 € netto bei 2.000×2 %). Konversion könnte über Free-Trial **steigen** (voll erlebbarer Wert statt Free-Dauerbremsung) — *Unsicher*, A/B-fähig über Trial-Länge.
4. Trial-Missbrauch (Geräte-Reset = neue 14 Tage): akzeptiert (Reset erfordert Neueinrichtung + Token-Verlust); harter Anti-Reset-Schutz bräuchte Identität → widerspricht ADR-0005.
