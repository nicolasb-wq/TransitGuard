# ADR-0007 — TTL-Engine als reine, tabellengetriebene Funktion
**Kontext:** TTL-Regeln aus v0.1/v1.1 waren interdependent unterdefiniert (B3); Streuende Implementierungen wären untestbar.
**Optionen:** (a) Regelkern als Pure Function + ttl_config-Tabelle, (b) Regeln im Endpoint-Code, (c) Client-TTL.
**Entscheidung:** (a) — Core-Projekt, tabellengetriebene Tests (T-TTL-1…12), Serverzeit autoritativ. Profile rail/bus × station/trip/at_stop + derived_terminus; Widerspruch-Parität; CANCELED⇒0; Freeze unter G11-Gate.
**Konsequenzen:** Wertänderungen = Config, kein Deploy; offene PO-Entscheidungen F-15/F-16 sind reine Config-Werte; jede neue Regel braucht zuerst einen Testfall.
