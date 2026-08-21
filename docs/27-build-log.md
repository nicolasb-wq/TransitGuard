# 27 — Build-Log (9. Session, 2026-08-22): die unbelegten Lücken schließen

**Auftrag:** Genau die Punkte belegen, die am Ende der 8. Session als *unbelegt*
ausgewiesen waren — plus die Fehlerklasse hinter drei der vier Launch-Blocker
strukturell schließen.

**Methode (verbindlich):** Kein Fix gilt als fertig, bevor er funktional
bewiesen ist. Jede neue Prüfung braucht eine Gegenprobe am kaputten Zustand.
Test zuerst, dann Fix. Widerlegt die Prüfung mich, gewinnt die Prüfung.

**Ergebnis in einem Satz:** A–E sind belegt, mit Gegenproben; dabei kamen **fünf
weitere echte Fehler** ans Licht, zwei davon launch-blockierend — darunter einer,
der die API in Produktionskonfiguration beim **Start** abstürzen ließ.

---

## A — Vertragsdrift strukturell schließen

### A.1 Warum kein OpenAPI-Codegen (verworfen mit Messbeleg)

Naheliegend wäre: OpenAPI aus dem Server generieren, daraus TypeScript- und
Dart-Typen erzeugen, Drift wird Compile-Fehler. Ich habe das **gemessen**, statt
es zu vermuten: Swashbuckle temporär eingebaut, Spec erzeugt, ausgewertet.

*Bestätigt:* **18 von 18 Operationen ohne jedes Antwortschema.** Beispiel für
`POST /v1/journeys/search`:

```json
{ "200": { "description": "OK" } }
```

Grund: Alle Controller geben `IActionResult` zurück und formen die Antwort als
**anonymen Typ** (`return Ok(new { trip_ref = …, route_id = … })`). Daraus kann
Swashbuckle nichts ableiten — und ausgerechnet diese Endpunkte sind die
driftanfälligen. Codegen wäre erst nach Umbau aller 18 Operationen auf benannte
DTOs möglich; danach prüfte er, was ein *Attribut behauptet*, nicht was der
Server *sendet*. Probe sauber zurückgebaut.

### A.2 Gewählt: aufgezeichneter Vertrag als maßgebliches Artefakt

`verifikation/contract_capture.mjs` fährt die **laufende** API ab und leitet aus
den echten Antworten die Feldform ab → `docs/contract/api-contract.json`
(20 Endpunkte) plus normalisierte Beispielantworten unter `docs/contract/samples/`.

Drei Eigenschaften, die den Unterschied machen:

1. **Optionalität wird beobachtet, nicht geraten.** Mehrere Beobachtungen je
   Endpunkt: `distance_km` ist optional, weil die Namenssuche es nicht liefert;
   `route_id` auf einer Meldung ist optional, weil Stations-Anker es nicht haben.
2. **Pflicht-Header werden BELEGT.** Die Aufzeichnung lässt den Header weg und
   schreibt die Antwort mit: `Idempotency-Key` fehlt → `422 validation`;
   `X-Ticket-Confirmed` fehlt → `403 ticket_gate_blocked`.
3. **Lücken werden ausgewiesen.** Endpunkte, deren Antwort leer war
   (`cities.alerts`, `journeys.warnings`, `reports.event`), tragen
   `unbeobachtet: true`. Dort schützt der Vertrag **nichts** — sichtbar, statt
   still als „keine Felder" verbucht.

### A.3 Die Clients leiten ab statt zweitzuschreiben

- **PWA:** `web/src/contract.gen.ts` wird erzeugt; `web/src/api.ts` leitet
  `Stop`, `Me`, `ReportView`, `Connection`, `TransferConnection` daraus ab.
  Eine Umbenennung im Server wird damit zum **Compile-Fehler an der Lesestelle**.
- **Flutter:** Dart hat keine strukturelle Typprüfung. Deshalb trägt
  `app/lib/contract.gen.dart` Pflicht-Header und Feldlisten als Daten, und
  `app/test/contract_test.dart` fährt die **echten Beispielantworten durch die
  echten Parser** und prüft per MockClient, dass die App die Pflicht-Header
  sendet. Dafür wurde der HTTP-Client injizierbar gemacht.
- **Gate:** `scripts/verify-contract.sh` vergleicht laufende API gegen den
  eingecheckten Vertrag und prüft die Frische beider Generate.

### A.4 Beweispflicht: künstliche Vertragsverletzung

**Verletzung 1 — `leg_a.route_id` → `leg_a.line` umbenannt.** Alle vier Ebenen
schlagen an:

| Ebene | Reaktion |
|---|---|
| `scripts/verify-contract.sh` | `SERVER-DRIFT`, zeigt die geänderte Zeile, Exit 1 |
| Server-Tests (T-TRANSFER) | 1 von 4 rot |
| **Web-Build** | `error TS2339: Property 'route_id' does not exist` — an der Lesestelle in `Fahren.tsx` |
| Dart-Vertragstest | rot |

**Verletzung 2 — `headsign` wird nullable.** Das Gate meldet die Typänderung.
Der Compiler nicht, und das ist richtig: der Client behandelte null bereits.

Beides zurückgebaut, danach alles wieder grün.

### A.5 Grenze, die der Vertrag NICHT abdeckt

Er kennt nur, was die Aufzeichnung gesehen hat. Ein Feld, das der Server als
nullable führt, in der Fixture aber nie null war, erscheint als nicht-nullable.
Der Vertrag fängt **Umbenennungen und Strukturbrüche** — nicht jede denkbare
Nullability. Steht so auch im Kopf von `web/src/api.ts`.

Die Echtzeitfelder (`delay_s`, `estimated_time`, `warnings[]`) sind im
Lokal-Modus gar nicht aufzeichenbar — es läuft kein Ingest. Statt eines
Ersatzbeweises setzen die Integrationstests den Zustand und nageln die Feldnamen
serverseitig fest (T-RTSHAPE).

---

## B — Postgres real hochgezogen

`scripts/pg-dev.sh` zieht PostgreSQL 16.10 + PostGIS 3.5 ohne Root und ohne
Container hoch, migriert und legt die Login-Rollen an. **Von Grund auf belegt:**
Datenverzeichnis gelöscht, neu aufgebaut — 5 Migrationen, 15 Tagespartitionen,
13 Policies, in 23 s.

11 Integrationstests gegen die echte Instanz:

| Test | prüft |
|---|---|
| T-PG-SCHEMA-1 | alle fünf Migrationen angewandt; zweiter Lauf meldet nur `skip` |
| T-PG-SCHEMA-2 | PostGIS **rechnet** (`ST_Contains` auf der Hamburg-Box) |
| T-PG-SCHEMA-3 | `reports` ist RANGE-partitioniert, ≥15 Tagespartitionen |
| T-PG-SCHEMA-4 | Partitionsanlage idempotent, weiterer Vorlauf legt genau die neuen Tage an |
| T-PG-SCHEMA-5 | Retention **löscht** eine 200 Tage alte Partition, junge bleiben |
| T-PG-SCHEMA-6 | eine Meldung liegt physisch in `reports_YYYYMMDD` (`tableoid`) |
| T-PG-RLS-1/2 | `tg_app` bekommt `permission denied` auf `entitlements`, `tg_billing` nicht |
| T-PG-RLS-3 | `feature_flags`: dieselbe Abfrage, je Rolle andere Zeilen (1 vs. 2) |
| T-PG-RLS-4 | deaktivierte Stadt ⇒ Meldungen für `tg_app` unsichtbar, **nicht gelöscht** (Kill-Switch-Stufe 3) |
| T-PG-RLS-5 | gesperrte Geräte verschwinden für die App-Rolle |

**Gegenprobe:** Mit `DISABLE ROW LEVEL SECURITY` auf drei Tabellen werden 3 von 5
RLS-Tests rot; mit einem `GRANT SELECT ON entitlements TO tg_app` plus Policy
zusätzlich der vierte. Zurückgebaut, danach wieder grün — **zweimal
hintereinander**, nachdem der Partitionstest sich selbst aufräumt.

**Ohne Datenbank:** `Skipped: 11, Passed: 0`. Eine fehlende Datenbank ist kein
Beweis für ein korrektes Schema.

**Offener Punkt der Übergabe geschlossen:** Die API läuft im
`Data:Provider=postgres`-Modus. Acceptance 5/5 dagegen; eine über die API
erzeugte Meldung liegt in `reports_20260821`, ist für `tg_app` über RLS sichtbar
und über die API zurücklesbar — mit Klarnamen „Jungfernstieg".

*Dabei zwei Fehler im eigenen Werkzeug:* `pg-dev.sh` meldete „gestoppt", ohne zu
stoppen (`pg_ctl` als root scheitert stumm), und das Postmaster-Logfile unter
`/tmp` gehörte root, weshalb `pg_ctl` den Start verweigerte, ohne es zu sagen.
Beides behoben; `down` prüft jetzt nach, ob der Port wirklich frei ist.

---

## C — Umstiegsverbindungen

Die Fixture kannte vier Haltestellen auf **einer** Linie — jedes Paar war direkt
verbunden, `transfer_connections` war in jedem Testlauf leer. Genau dort saß
Launch-Blocker 1.

`verifikation/build_static_mini.py` erzeugt die Fixture jetzt reproduzierbar:
zusätzlich HHA5 (Farmsen) und Linie U2, beide im 20-Minuten-Takt über den ganzen
Betriebstag (der Router verlangt eine Abfahrt im Fenster [jetzt−30 s, jetzt+90 min];
mit festen Einzelzeiten wäre der Test von der Uhrzeit abhängig gewesen).
**HHA1 → HHA5 hat keine Direktfahrt** und erzwingt einen Umstieg in HHA3.

Durchgetestet auf allen drei Ebenen, jeweils gegen **beide** Liniennummern:

- **Server** (T-TRANSFER, 4 Tests): `leg_a.route_id = R_U1`, `leg_b.route_id = R_U2`,
  kein PascalCase im Vertrag, Verkettung zeitlich plausibel, Puffer ≥ 120 s,
  und die Gegenprobe, dass eine Direktstrecke keinen Umstieg auslöst.
- **PWA** (Rauchtest im Browser): die gerenderte Umstiegs-Karte trägt
  `["R_U1","R_U2"]` — eine **leere** Plakette wäre genau Blocker 1 und lässt den
  Test fallen.
- **Flutter**: `Api.journey` verwarf `transfer_connections` **stillschweigend** —
  die App zeigte „keine Verbindung", wo der Server einen Umstieg anbot. Jetzt
  DTOs, eine Umstiegszeile in der Oberfläche und ein Widget-Test gegen die echte
  aufgezeichnete Antwort.

Die Kennzahlen der Ingest-Tests wurden nachgezogen (5 Halte, 147 Fahrten).

---

## D — Service Worker, eigener geprüfter Durchgang

**Strategie: Allowlist, nicht Sperrliste.** Gecacht wird ausschließlich, was
ausdrücklich erlaubt ist — App-Hülle, `/assets/*`, Manifest, Icons. `/v1/*`,
`/hubs/*` und `/health/*` werden gar nicht angefasst. Eine vergessene Ausnahme
kann damit **konstruktionsbedingt** keine Meldung in den Cache spülen. Der
Cache-Name leitet sich aus den Asset-Hashes ab; ein neuer Build räumt auf.

`verifikation/sw_offline.mjs` prüft das als Verhalten:

| Prüfung | Ergebnis |
|---|---|
| Worker wird aktiv | ✔ |
| Nach echter Meldung: Cache vollständig aufgezählt | 8 Einträge, **keine** /v1-, /hubs-, /health-Antwort |
| App-Hülle offline | lädt, Deep-Link inklusive |
| `/v1/cities` offline | **schlägt fehl** (`Failed to fetch`) statt alte Daten zu liefern |

**Gegenprobe:** Mit `/v1/` in der Allowlist landen fünf Einträge im Cache,
darunter `/v1/cities/hamburg/reports` — also wörtlich die Meldungsdaten, die die
Projektregel verbietet. Die Prüfung nennt sie und geht auf Exit 1.

---

## E — Flutter-Laufzeit

**Erst geprüft, was überhaupt geht:**

| Weg | Ergebnis |
|---|---|
| Android-Emulator | **nicht möglich**: kein `/dev/kvm`, 0 CPU-Virtualisierungsflags |
| Linux-Desktop | **nicht sinnvoll**: `gtk+-3.0` fehlt und `geolocator` hat keine Linux-Implementierung |
| Flutter-Web | **möglich** — beide Plugins haben Web-Implementierungen |

`flutter drive` mit `integration_test` hängt in diesem Container endlos.
**Ursache gemessen, nicht vermutet:** Flutter-Web lädt CanvasKit von
`www.gstatic.com`; der Container-Proxy blockt das mit `ERR_CONNECTION_RESET`,
die App bootet nie — und der Treiber wartet auf eine Verbindung, die eine nie
gestartete App nicht aufbaut. Im Release-Build löst `--no-web-resources-cdn` das,
im Debug-Build (den `drive` nutzt) greift der Schalter nicht.

Deshalb wird der **Release-Build** getrieben (`verifikation/flutter_web_smoke.mjs`),
ausgeliefert unter demselben Origin wie die API — der Aufbau, den
`deploy/Caddyfile` für Produktion beschreibt. Flutter rendert auf Canvas;
bedient wird über Flutters Barrierefreiheits-Baum, der echte DOM-Knoten liefert.

Belegt: App bootet **ohne Seitenfehler**, holt Geräte-Token (`201 POST /v1/devices`)
und Zugangsstand (`200 GET /v1/devices/me`) über echtes HTTP, der Warnen-Tab ist
ohne Ticket verschlossen und nach der Bestätigung offen, die Standortsuche findet
eine Haltestelle, und eine Meldung geht mit `201 POST /v1/reports` durch.

**Gegenprobe — erst falsch angesetzt.** Mein erster Eingriff (`_ticketOk = true`)
wurde von `initState` sofort überschrieben; das Gate war gar nicht kaputt und die
Prüfung blieb grün. Dieser „Nachweis" war wertlos. Richtig angesetzt am
Entscheidungspunkt selbst meldet die Prüfung
„Warnen-Tab ist ohne Ticket-Bestätigung NICHT verschlossen" und Exit 1.

Dafür nötig, bewusst eng gehalten: ein CORS-Schalter, der im
Auslieferungszustand **aus** ist (`Cors:AllowedOrigins`; ohne Konfiguration wird
keine Middleware registriert). Produktion bleibt Same-Origin. T-CORS hält beides
fest — auch, dass keine Wildcard entsteht.

---

## Fünf weitere echte Fehler, nebenbei gefunden

**F-1 — `/v1/billing/restore` war dauerhaft kaputt (launch-blockierend).**
Der Pfad steht in der Auth-Middleware auf der „offen"-Liste; dort wurde
`Items["DeviceId"]` **nie** aufgelöst. Der Controller verlangt sie aber und fiel
deshalb IMMER in den 429-Zweig: **jeder** Wiederherstellungsversuch endete in
`rate_limited`, auf jedem Gerät, immer. Ohne Konten (ADR-0005) ist der
Restore-Code der einzige Weg auf ein neues Gerät — ein zahlender Nutzer wäre nach
einem Gerätewechsel dauerhaft ausgesperrt gewesen. „Offen" heißt: kein Token
**nötig** — nicht: Token ignorieren. Vier Tests (T-BILL-RESTORE), darunter der,
dass ein fehlendes Gerät `401` ergibt und nicht `429`.

**F-2 — Die API stürzte mit eingeschaltetem Ingest beim START ab (launch-blockierend).**
`IIngestMetricsSink` hatte im ganzen Repo **keine Implementierung und keine
Registrierung**; `PollRealtimeJob` verlangt sie im Konstruktor, und
`JobRegistration` löst ihn bei `Ingest:Enabled=true` beim Start auf. Gemessen:

```
Unhandled exception. System.InvalidOperationException:
Unable to resolve service for type 'TransitGuard.Core.Abstractions.IIngestMetricsSink'
```

Im Lokal-Modus ist Ingest aus — deshalb acht Bausessions unentdeckt. Der
Erst-Deploy wäre in Runbook-Phase 5 gestorben. Behoben: Log-Senke im
Memory-Modus, `ingest_metrics`-Senke im Postgres-Modus. T-DI prüft jeden
Hangfire-Job über den **echten** Hangfire-Aktivator.

**F-3 — Der rt-poll-Zeitplan war ungültig.** `"*/60 * * * * *"` — das Sekundenfeld
erlaubt nur 1–59, Hangfire warf beim Registrieren. Auch nach F-2 wäre die API
also nicht gestartet. „Alle 60 Sekunden" schreibt man als Sekunde 0 jeder Minute.
Zeitpläne sind jetzt Konstanten (`JobSchedules`) und werden geprüft — nicht nur
auf Parsebarkeit, sondern auf den gemeinten Abstand (T-CRON).

**F-4 — Flutter warf bei Entfernung 0.** `distance_km` kommt als JSON-Integer,
wenn der Wert ganzzahlig ist (System.Text.Json schreibt `0` für `0.0`); Dart
warf `type 'int' is not a subtype of type 'double?'` — genau dann, wenn man
**direkt an der Haltestelle steht**. Gefunden durch die echten Vertragsbeispiele.
Alle Zahlenfelder lesen jetzt über `num`.

**F-5 — `/v1/journeys/{tripId}/warnings` liegt hinter dem Ticket-Gate.** Bei der
Aufzeichnung aufgefallen; stand in keiner Doku als Gate-Endpunkt. Jetzt im
Vertrag mit Negativprobe belegt.

*Nebenbefund, kein Fehler:* `/v1/stops/{id}/departures` berechnet **nie**
Kontroll-Warnungen — die entstehen ausschließlich in der Fahrtensuche. Als
Vertragsfakt festgehalten (T-RTSHAPE-0), damit niemand eine Oberfläche auf die
Abfahrtstafel baut und sich wundert.

---

## QA-Loop: erneut geprüft, erneut Defekte gefunden

Vor dem Ausbau wurde der Loop wieder auf eigene Fehler geprüft. Zwei gefunden:

**Q-1 — `start_api` übernahm eine fremde Instanz.** Lief bereits eine API auf dem
Port, antwortete `/health/ready` — und der Loop prüfte fröhlich gegen ein
womöglich veraltetes Binary. **Falsches Grün.** Jetzt: belegter Port ⇒ Abbruch
mit Begründung.

**Q-2 — Übersprungene Postgres-Tests galten als grün.** `dotnet test` liefert 0,
wenn alle Tests übersprungen wurden. Die Postgres-Tests haben jetzt ein eigenes
Gate, das rot wird, wenn **kein einziger** wirklich lief.

**Q-3 — zwei Loops gleichzeitig, still falsche Ergebnisse.** Beim ersten
Fünfer-Lauf meldete Pass 2 fünf rote Gates. Ursache war *nicht* das Produkt:
ein abgebrochener Vorlauf hatte einen verwaisten Loop-Prozess hinterlassen
(PPID 1), der mit dem neuen um Ports, API und `docs/qa/` stritt. Bevor ich das
gemessen hatte, hatte ich zwei falsche Erklärungen — beide durch ein
Fünf-Zeilen-Experiment widerlegt (der EXIT-Trap läuft *nicht* in
Kommandosubstitutionen; es gab keinen OOM-Kill). Der Beleg kam erst aus
`ps`: zwei `qa-loop.sh`-Prozessbäume gleichzeitig.

Der Loop hat jetzt eine **Einzelinstanz-Sperre** (`flock`): ein zweiter Lauf
bricht mit Exit 2 und Begründung ab, statt still Ergebnisse zu verfälschen.
Gegenprobe erbracht (Sperre gehalten ⇒ Exit 2; Sperre frei ⇒ Lauf startet).
Ein Ergebnis, das von einem unbemerkten Nachbarn abhängt, ist kein Ergebnis.

Der Loop fährt jetzt **zwölf** Gates: Build · Backend-Tests · Postgres-Tests ·
Flutter-Analyze · Flutter-Tests · Web-Build · Acceptance · Vertrag ·
Caddy-Auslieferung · PWA-Rauchtest · Service Worker · Flutter-Laufzeit.
Die vier Zustände bleiben, „übersprungen" ist weiterhin kein Beweis.
