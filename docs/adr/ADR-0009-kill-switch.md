# ADR-0009 — Serverseitiger Kill-Switch, 3 Stufen, ohne App-Update
**Kontext:** BVG prüft rechtliche Schritte gegen FreiFahren; Store-Risiken; Auftrag fordert Abschaltbarkeit ohne Update.
**Optionen:** (a) Feature-Flag DB + Stadtebene + Proxy-Stufe, (b) nur Remote-Config im Client, (c) Domain-Shutdown.
**Entscheidung:** (a): Stufe 1 feature_flags.reports_enabled (API 404 + Hub-Control-Event), Stufe 2 cities.is_active (RLS!), Stufe 3 Caddy respond 503. Clients degradieren zum Fahrplan-Companion.
**Konsequenzen:** Reaktionszeit <5 min; Meldungsdaten dürfen nie im Client-Cache liegen (no-store, B8) — sonst gälte Altlast nach Abschaltung; E2E-Test T-E2E-3 beweist Verhalten.
