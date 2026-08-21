# TransitGuard — Repo-Verzeichnisbaum (Zweck je Ordner)

```
transitguard/
├── CLAUDE.md                        # Projektkontext für Assistenzsysteme & neue Devs (Lieferumfang 1)
├── README.md                        # Setup, lokale Entwicklung, Env-Variablen (Lieferumfang 2)
├── TransitGuard.sln                  # 6 Projekte; Build grün + 50 Tests (19-build-log)
├── src/
│   ├── TransitGuard.Api/            # ASP.NET Core: Controller, Middleware (Auth/Tier/Rate), SignalR-Hub,
│   │                                #   Program-Bootstrap, Billing-Endpunkte (Zweitverbindung tg_billing)
│   ├── TransitGuard.Core/           # Domäne PUR (keine Infra-Referenzen): Report, TtlEngine, TrustEngine,
│   │                                #   Normalizer, CityFilter, Interpolator, Entitlement-Modell
│   ├── TransitGuard.Ingest/         # Hangfire-Jobs: PollRealtime, StaticSync, CityExtract, AlertSweep,
│   │                                #   TtlSweep, TrustRecalc, PartitionMaint, FeedHealth (docs/07)
│   └── TransitGuard.Data/           # EF-Core-DbContexts (App/Billing), Entitäts-Mappings auf SQL-Schema,
│                                    #   Idempotency-Store, Raw-SQL-Zugriffe (Partition-Funktionen)
├── tests/
│   ├── TransitGuard.Core.Tests/     # TTL/Normalizer/Trust tabellengetrieben (T-TTL, T-NORM, T-TRUST)
│   ├── TransitGuard.Api.Tests/      # Integrationstests mit Test-Postgres (T-API, T-EN, T-HUB)
│   └── fixtures/                    # ECHTDATEN: rt_sample.pb (gtfs.de-Ausschnitt 21.08.), static_mini.zip, manifest.json (Python-Kreuzwerte)
├── app/                             # Flutter (iOS + Android): Melden-Flow, Karte, Abfahrten, Pro-Ansichten
├── web/                             # React/TS/Vite-PWA: Karte, Melden; Service-Worker OHNE Meldungs-Cache (B8)
├── docs/                            # DIESER Dokumentationskorpus
│   ├── 00-bestandsaufnahme.md       # Widersprüche/Fehler/Lücken der Übergabe + eigene Messungen (führend)
│   ├── 01-verzeichnisbaum.md        # diese Datei
│   ├── 02-architektur.md            # System, Datenflüsse, TTL-Engine, Kill-Switch
│   ├── 03-entscheidungsprotokoll.md # alle Entscheidungen + verworfene Optionen
│   ├── 04-api.md / 05-openapi.yaml  # REST-Spezifikation (Mensch/Maschine)
│   ├── 06-realtime-hub.md           # SignalR-Vertrag: Events, Gruppen, Reconnect
│   ├── 07-ingest.md                 # Jobs, Metriken, Alarme, G10/G11
│   ├── 08-entitlement.md            # Free/Pro-Matrix, Durchsetzung, Anonymität, Compliance, Ökonomie
│   ├── 09-testplan.md · 10-deployment.md · 11-recht.md · 12-offene-fragen.md · 13-roadmap.md
│   ├── 14-j3-j6-messung.md        # 2. Messrunde: Hamburg-Gate GRÜN, Match-Rate 100 %, Städtevergleich
│   ├── 15-rechtsstand-2026-08.md  # Rechtsrecherche §265a/Stores/BVG-FreiFahren mit Quellen
│   ├── 16-innovationen.md         # bewertetes Innovations-Backlog (u.a. Barrierefreiheits-Badges)
│   ├── 17-dsfa-entwurf.md         # Datenschutz-Folgenabschätzung (Art. 35) v0.1
│   ├── 18-rechtliche-haertung.md  # Ticket-First-Gate H1-H6, DSA, Monitoring, Prüfbefunde
│   └── vorlagen/                  # briefing-rechtsanwalt.md · nutzungsbedingungen-entwurf.md · impressum-entwurf.md · anfrage-mobilitybox.md · anfrage-hvv-geofox.md
│   ├── adr/                         # ADR-0001…0013 (Kontext/Optionen/Entscheidung/Konsequenzen)
│   └── sql/                         # 0001–0004: Kern, GTFS, RLS, Partitionen/Seed — migrate.sh-Kanon
├── scripts/
│   ├── migrate.sh                   # psql-Migrationsrunner (schema_migrations)
│   ├── test.sh                      # lokales Test-Gate vor Deploy (kein CI-Dienst, Constraint)
│   ├── deploy.sh                    # rsync-Orchestrierung + Symlink-Swap + Health-Check + Rollback-Schutz
│   ├── backup.sh · restore.sh       # pg_dump → Storage Box Rotation / Restore-Drill
│   └── j3_probe.py                  # J3/J6-Messskript (basiert auf verifikation/)
├── deploy/                          # serverseitige Konfigurationen (Vorlagen, ohne Secrets)
│   ├── Caddyfile
│   ├── transitguard.service         # systemd-Unit
│   └── env.example
└── verifikation/                    # Unabhängige Messskripte + Rohausgaben 2026-08-21 (Belege Bestandsaufnahme)
    ├── verify_feeds.py / verify_deep.py / verify_alerts.py   (1. Messrunde)
    ├── j3j6.py / citycmp.py                                  (2. Messrunde, Static-Join)
    └── *_output.txt · alert_headers_distinct.txt
```

**Konventionen:** keine Binärarten im Repo; `docs/` ist verbindlich (bei Code-Änderung Doku-Mitführen — CLAUDE.md-Regel); Tests gehören zu jedem Ticket (Roadmap AC); Secrets ausschließlich `deploy/env.example`-Platzhalter.
