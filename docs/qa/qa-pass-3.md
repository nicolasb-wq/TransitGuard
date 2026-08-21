# QA-Pass 3 — 2026-08-21T23:07:22Z

| Gate | Ergebnis | prüft |
|---|---|---|
| Backend-Build | ✅ grün | 0 Fehler, 0 Warnungen |
| Backend-Tests | ✅ grün | Core, Ingest, API-Kette |
| Postgres-Tests | ✅ grün | Migrationen, RLS, Partitionen gegen echtes PG |
| Flutter-Analyze | ✅ grün | statische Analyse |
| Flutter-Tests | ✅ grün | DTO-Verträge, Gate, Copy-Nie-Liste |
| Web-Build | ✅ grün | tsc gegen den generierten Vertrag |
| Acceptance | ✅ grün | Vertragskette über HTTP |
| Vertrag | ✅ grün | Server-Drift + Frische der Generate |
| Caddy-Auslieferung | ✅ grün | Same-Origin, no-store, Deep-Links |
| PWA-Rauchtest | ✅ grün | echter Browser, hell+dunkel, Umstiege |
| Service Worker | ✅ grün | keine Meldungsdaten im Cache, offline |
| Flutter-Laufzeit | ✅ grün | echte App im Browser gegen echte API |

Vollständige Gate-Ausgaben: `docs/qa/logs/<gate>.log` (wird je Pass überschrieben).
„Übersprungen" heißt: Werkzeug oder Dienst fehlt — das ist KEIN Beweis für Korrektheit.
