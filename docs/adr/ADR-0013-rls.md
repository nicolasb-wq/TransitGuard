# ADR-0013 — Rollen + RLS als Defense-in-Depth (ehrliche Einordnung)
**Kontext:** Stack fordert „PostgreSQL mit RLS"; Realität: Ein-Mandant, anonyme Clients, eine API-Instanz.
**Optionen:** (a) RLS überall behaupten als Sicherheitslösung, (b) gezielter Rollensplit (tg_app/tg_billing/tg_ingest/tg_ops) + RLS auf den Tabellen, wo Grenzen reale Wirkung haben (entitlements, feature_flags, reports, devices), (c) kein RLS.
**Entscheidung:** (b). Application-Layer-Auth bleibt Leading; RLS fängt Grant-Fehler und kompromittierte Nebenrollen ab.
**Konsequenzen:** entitlements für tg_app physikalisch unsichtbar (Test T-EN-7); Kill-Switch-Stufe 2 wirkt auf DB-Ebene mit; RLS-Nebenkosten (Policy-Evaluation) im Rahmen — Lasttest T7.2 prüft.
