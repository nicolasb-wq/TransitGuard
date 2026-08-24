# 28 — Bausession 10 (24.08.2026): Echtdaten-Ingest, produktionsnahe Laufzeit, Vertragslücken

**Auftrag:** die Punkte schließen, die ich in Session 9 selbst als unbelegt ausgewiesen hatte.
**Verschärfte Methodenregel dieser Session:**
> Eine Gegenprobe ist erst gültig, wenn belegt ist, dass der eingebaute Defekt tatsächlich
> aktiv war. Nicht nur, dass die Prüfung rot wurde — sondern dass sie aus dem richtigen Grund
> rot wurde.

Diese Regel hat sich sofort bewährt: die **erste** Gegenprobe der Session war ungültig
(Abschnitt F.1).

---

## A — Echtdaten-Ingest gegen `https://realtime.gtfs.de/realtime-free.pb`

Werkzeug: `verifikation/IngestProbe/` (neu). Bewusst gegen den **Produktionscode**
(`FeedFetcher`, `FeedReader`, `TripUpdateNormalizer`, `CityExtractor`, `PollRealtimeJob`),
nicht gegen eine Nachbildung — eine Nachbildung findet Fehler des Produktionscodes gerade nicht.
Rohausgaben: `docs/messungen/`.

### A.0 Ausgangswerte nachgeprüft (Vorgabe war vom 20.08., gemessen am 24.08. 08:36 UTC)

| Größe | Vorgabe (20.08.) | Gemessen (24.08.) | |
|---|---|---|---|
| Bytes | 33.421.155 | **44.076.902** | +32 % |
| Entities | 122.745 | **174.321** | +42 % |
| TripUpdates | 55.314 | **79.723** | |
| Alerts | 67.431 | **94.598** | |
| VehiclePositions | 0 | **0** | bestätigt |
| TUs mit Verspätung | 98,5 % | **97,7 %** | |
| `route_id` im TU | leer | **0,0 % gesetzt** | bestätigt |
| `vehicle`-Feld | „fehlt" | **8,0 % vorhanden, davon 100 % leere id** | **Vorgabe war falsch** |
| ETag | vorhanden | **vorhanden** | bestätigt |
| Cache-Control | nicht gesetzt | **nicht gesetzt** | bestätigt |
| Feed-Alter bei Abruf | 26 s | **25 s** | bestätigt |

*Bestätigt:* Die Feed-Größe schwankt stark und **wächst**. Innerhalb von 90 s gemessen:
37,9 / 38,3 / 40,3 / 38,7 MB; über den Vormittag 36,4 – 46,2 MB. `CLAUDE.md` nannte 33–42 MB —
das ist überholt.

*Bestätigt:* `vehicle` ist **nicht abwesend**, sondern in 8,0 % der TUs präsent und dann
immer leer. Das entspricht der älteren Notiz in `CLAUDE.md` („HasField ≠ Wert!"), nicht der
Vorgabe dieser Session. Für die Trip-Anker-Architektur ändert das nichts (ADR-0001), aber wer
`HasVehicle` als Signal benutzt, liegt falsch.

*Bestätigt:* Verspätungen liegen weit außerhalb des Clamp-Bereichs: min **−20.882 s**,
max **10.800 s**, 871 Werte außerhalb `[−120, +7200]`. Median 0 s, p95 240 s.
Das Clamping ist keine Vorsichtsmaßnahme, es greift real.

*Bestätigt:* Alerts: 94.598 Einträge, **513 verschiedene** Dedup-Keys ⇒ 99,5 % Duplikate.

### A.1 Ein vollständiger Zyklus von Ende zu Ende

`scripts/verify-ingest-e2e.sh` (neu) fährt die API mit **allen** Produktionsschaltern hoch
(`Data:Provider=postgres` · `Jobs:Storage=postgres` · `Ingest:Enabled=true`) und wartet auf
einen echten, von **Hangfire** ausgelösten Poll.

Ergebnis (mit vorher über das Hangfire-Dashboard ausgelöstem StaticSync):

```
status=200 bytes=39.836.079 entities=177.635 tu=80.876 alerts=96.759 vp=0
stadt_tus=3.604 parse_ms=24.618 feed_alter=36s
API-Prozess: Spitzen-RSS 1.512 MB
```

Damit sind belegt: Download · Parse · Stadt-Filter · Delta · **Persistenz** (`ingest_metrics`)
· API bleibt während des Polls bedienbar (`/v1/cities` und `/v1/devices/me` mit echtem
Geräte-Token, 200).

**Zwei Stufen der Kette existieren nicht** — das ist der ehrliche Teil der Antwort:

- **`rt_trip_state` wird nie geschrieben.** Die Tabelle steht in `0002_gtfs.sql`, ist in EF
  gemappt (`Contexts.cs:133`) — aber `ITripStateStore` ist auch im Postgres-Modus
  `InMemoryTripStateStore` (`Program.cs:42`). Der Echtzeitzustand überlebt keinen Neustart.
  Praktische Folge ist gering (der nächste Poll füllt ihn binnen 60 s), die Doku
  behauptet aber etwas anderes.
- **`departure.tick` wird nie gesendet.** `docs/06` Zeile 12 führt es als
  „optional, MVP: nur bei Delta". Der Poll-Job berechnet die Deltas (`changed`), ruft aber
  `IRealtimeDispatcher` nie auf. SignalR-Push gibt es nur für Meldungsereignisse
  (Controller + TTL-Sweep). Der Anknüpfungspunkt liegt bereit; implementiert ist es nicht.

Ich habe beides **nicht** implementiert. Beides sind neue Vertragsflächen (Payload-Form,
Tier-Formung, Hub-Gruppen je Halt) und hätten Aufgabe B in derselben Session wieder
aufgerissen. Der Befund ist mehr wert als eine schnelle Implementierung.

### A.2 Speicher und CPU — gemessen, nicht geschätzt

Messmethode: `VmHWM` aus `/proc/self/status`, **je Phase auf den aktuellen RSS zurückgesetzt**
(`/proc/self/clear_refs`). Ohne diesen Reset wäre jede Phasenmessung nur der Spitzenwert
aller vorherigen Phasen.

| Vorgang | Spitzen-RSS | Allokationen | CPU |
|---|---|---|---|
| Download 44 MB | 144 MB | 85 MB | 1,0 s |
| Parse + Zählung aller 174 k Entities | **610 MB** | 470 MB | 4,0 s |
| Ein Poll-Zyklus (Whitelist gefüllt) | **~640 MB** | — | 1,1–1,7 s |
| StaticSync über MemoryStream (251 MB ZIP) | **687 MB** | 80,8 GB | 91 s |
| StaticSync über Zwischendatei (**nach Fix**) | **437 MB** | 80,3 GB | 96 s |
| API-Prozess unter Hangfire, nur Poll | **710 MB** | — | — |
| API-Prozess, StaticSync **und** Poll | **1.512 MB** | — | — |

**Der Dokumentationskommentar von `FeedReader` ist falsch.** Er nennt sich
„Entity-Streaming-Reader … kein Zurückhalten des Objektbaums". Tatsächlich ruft er
`FeedMessage.Parser.ParseFrom(body)` und materialisiert den **kompletten** Baum: 610 MB
Spitze für 44 MB Eingabe, Faktor ~14. Das Iterieren danach ist Streaming, das Parsen nicht.
Ich habe den Kommentar korrigiert statt den Reader umzubauen — ein echter Streaming-Parser
über `CodedInputStream` wäre ein eigener, riskanter Umbau.

**80,3 GB Allokationen im CityExtractor** stammen aus `GtfsCsv.ParseLine` (ein `List<string>`
+ `string[]` je Zeile, über ~100 Mio. stop_times-Zeilen in zwei Durchgängen). Der Spitzen-RSS
bleibt trotzdem bei 437 MB — der GC räumt mit. Kosten sind 90 s CPU, zweimal die Woche.
*Glaube ich:* tolerierbar. *Unsicher:* wie stark das die API in diesen 90 s ausbremst; auf
einer 2-vCPU-Maschine ist ein Kern belegt.

#### Verdikt zur VM

`docs/10` legt nur „Debian 12 VM (Hetzner), 1 Node" fest, keine Größe.

*Bestätigt (gemessen):* Der API-Prozess braucht im ungünstigsten Fall — StaticSync und
RT-Poll im selben Prozessleben — **1,5 GB**. Im Normalbetrieb (nur Poll) **0,7 GB**.
Dazu kommt PostgreSQL auf derselben Maschine.

*Bestätigt:* **2 GB RAM reichen nicht.** Der API-Prozess allein überschreitet sie am
StaticSync-Tag.

*Glaube ich:* **4 GB sind knapp** (1,5 GB API + Postgres mit shared_buffers/Cache + System),
**8 GB sind die sichere Wahl**. Ich nenne bewusst GB statt Produktnamen — die Zuordnung zu
Hetzner-Typen habe ich nicht nachgeschlagen und würde sie sonst behaupten.

*Bestätigt:* Zwei Maßnahmen senken die Spitze:
1. `Hangfire.WorkerCount = 1` (neu gesetzt) — StaticSync und Poll können sich nicht mehr
   überlappen; ohne das wären es 437 + 640 = ~1,1 GB **gleichzeitig**.
2. Der Zwischendatei-Fix im StaticSync spart 250 MB.

### A.3 `route_id`-Auflösung (C.3.1) — mit wirklich gefülltem Dictionary

Whitelist aus dem echten Static-Bestand (nv + rv + fv), **88.320 Fahrten**.

```
Lookups          79.723
Treffer           4.024  (5,0 %)   ⇒ Stadt-Fahrten
Fehlschläge      75.699  (95,0 %)  ⇒ andere Verbünde, erwartbar
Treffer OHNE route_id: 0           ⇒ kein Datenfehler (T-NORM-2 schlägt nicht an)
Gesamtdauer      3,04 ms  ⇒  38 ns je Lookup
```

*Bestätigt:* Die Auflösung ist **kein** Kostenfaktor — 3 ms von ~4.500 ms Zyklusdauer (0,07 %).
Der Aufwand steckt im Protobuf-Parse, nicht im Lookup.

*Bestätigt, aber irreführend benannt:* `IngestMetricRow.MatchRate` meldet konstant 100 %.
Sie misst `(TripUpdatesSeen − RouteMisses) / TripUpdatesSeen`, also die **Auflösbarkeit der
route_id unter den gesehenen TUs** — nicht die Whitelist-Trefferquote (die liegt bei 5,0 %).
Beide Zahlen sind sinnvoll, der Name lädt zum Fehllesen ein.

### A.4 Zwölf Zyklen im 60-s-Takt

```
RSS Zyklus 1 → 12:        637,5 MB → 641,6 MB   Zuwachs 4,0 MB (0,6 %)
Zyklusdauer min/median/max: 3.637 / 4.488 / 9.335 ms
Match-Rate min/max:        100,00 % / 100,00 %
Stadt-TUs min/max:         3.961 / 4.024
Prozess-Peak-RSS:          657,1 MB
```

*Bestätigt:* **Kein Speicherleck.** 0,6 % über zwölf Zyklen liegt im Rauschen der
GC-Heapverwaltung; die Zwischenwerte schwanken in beide Richtungen (622–719 MB).
*Bestätigt:* **Kein Takt-Drift.** Der langsamste Zyklus (9,3 s) war der erste (kalter Start);
danach 3,6–5,5 s bei 60 s Takt — Auslastung unter 10 %.
*Bestätigt:* Deltas fallen von 4.024 (erster Lauf, alles neu) auf 45–723 im eingeschwungenen
Zustand. Der Delta-Filter arbeitet.
*Beobachtet:* `TripState` wächst langsam (4.024 → 4.255 über 12 Minuten) — Fahrten sammeln
sich über den Tag. In Produktion räumt der TTL-Sweep; **in dieser Messung lief er nicht**,
also ist die Langzeitgrenze des Speichers hier **nicht** belegt.

### A.5 `If-None-Match` (Spec-Prüftask J1)

```
1. Abruf ohne ETag : 200, 37.871.511 B, ETag "241df97-659c6e6e1931d"
2. Abruf mit ETag  : 304 Not Modified, Body 0 Bytes
3. Abruf mit FALSCHEM ETag (Gegenprobe): 200, 37.871.511 B
```

*Bestätigt:* Der Server **unterstützt** `If-None-Match` korrekt. Die Gegenprobe mit einem
ungültigen ETag ist entscheidend — ohne sie könnte der 304 auch von einem Server kommen,
der immer 304 sagt.

*Bestätigt:* Regenerationstakt über 90 s gemessen: Inhaltswechsel nach 28,0 / 29,8 / 27,9 s,
Mittel **28,8 s**, dazwischen 37 × 304. **`CLAUDE.md` sagte „alle 10 s" — das ist widerlegt.**
Bei 60-s-Poll ist ein 304 dennoch praktisch ausgeschlossen (Takt < Poll-Intervall).

*Und dabei gefunden:* Der `If-None-Match`-Zweig lief in Produktion **nie**. `PollRealtimeJob`
ist `Scoped`, Hangfire öffnet je Ausführung einen eigenen Scope, der ETag lag in einem
Instanzfeld — beim nächsten Lauf immer `null`. Behoben über `FeedEtagStore` (Singleton),
abgesichert durch T-ETAG-1/-2/-3.

### A.6 Fehlerpfade

Sieben Fälle gegen einen lokalen Testserver. **Zwei waren kaputt:**

| Fall | vorher | jetzt |
|---|---|---|
| Server nicht erreichbar | ok | ok |
| HTTP 500 | ok | ok |
| Antwort abgeschnitten | ok | ok |
| **Body leer (200, 0 Bytes)** | **`NullReferenceException`** | Success=false, Metrikzeile |
| **kaputtes Protobuf** | **`InvalidProtocolBufferException`** | Success=false, Metrikzeile |
| 304 Not Modified | ok | ok |
| gültiger Feed danach | ok | ok |
| Metrikzeilen | **5 von 7** | **7 von 7** |

Die beiden Ausnahmen flogen aus dem Job heraus: Hangfire hätte den Lauf als „Failed"
markiert und die API wäre stehengeblieben — aber **ohne Metrikzeile und ohne
Fehlerzähler**, also ohne Circuit-Breaker. Der Ausfall wäre nur im Hangfire-Dashboard
sichtbar gewesen. Behoben (T-POLL-ERR-1/-2/-3); der gemerkte ETag wird jetzt erst **nach**
erfolgreichem Parse gesetzt, sonst hätte ein einmal verstümmelter Feed sich per 304
dauerhaft als „unverändert" festgesetzt.

### J3 — Hamburg-Anteil (Antwort)

Gemessen 24.08.2026 09:42 UTC, gegen den vollständigen Static-Bestand nv + rv + fv:

```
TripUpdates gesamt          79.738
TUs mit ≥1 Halt im KERN      3.566  (4,5 %)
TUs mit ≥1 Halt im UMLAND    3.931  (4,9 %)
distinct Kern-Fahrten        3.566, davon mit Verspätungswert 3.419 (95,9 %)
Kern-Fahrten je Feed:        nv 3.291 · rv 231 · fv 44
```

*Bestätigt:* Hamburg trägt **4,5 %** des Feeds; **3.566 gleichzeitige Fahrten**, davon
**95,9 % mit Echtzeitwert**. Die Werte vom 21.08. (3.740 / 4,5 % / 94,5 %) werden bestätigt;
die Schwankung erklärt sich aus der Tageszeit.

**Antwort auf die Frage, die dahinter steht:** *Ja*, die Datendichte trägt Hamburg als
Startstadt. 3.566 gleichzeitige Fahrten mit 96 % Echtzeitabdeckung sind kein Randfall,
sondern ein tragfähiger Bestand. Die Stadtfestlegung ruht damit auf einer Messung, nicht
mehr auf einer Annahme.

**Eine Einschränkung, die vorher niemand ausgewiesen hat:** `StaticSyncJob` lädt nur
`nv_free`. Damit fehlen **275 von 3.566 Kern-Fahrten (7,7 %)** — Regional- und Fernverkehr
(metronom, Nordbahn, AKN, DB Fernverkehr), die in Hamburg sehr wohl Nahverkehr sind.
Ich habe das **nicht** behoben: `Ingest:StaticZipUrl` ist ein einzelner Wert, mehrere Quellen
zu verschmelzen ist ein Entwurfsschritt (Schlüsselkollisionen zwischen den Feeds, Reihenfolge
beim Swap), kein Einzeiler. Es steht als messbelegte offene Aufgabe.

### J6 — Match-Rate Static ↔ RT (Antwort)

```
TripUpdates                79.738
im Static auflösbar        79.738  (100,0 %)
je Static-Feed             nv 73.220 · rv 6.334 · fv 184
Stop-Lookup ok / miss      909.765 / 0
```

*Bestätigt:* **100,0 %**, kein einziger nicht auflösbarer `trip_id`, kein einziger
unbekannter `stop_id` unter 909.765 Nachschlägen. Die Spec-Annahme, gtfs.de erzeuge Static
und Realtime aus einer Pipeline, **hält** — jetzt gemessen statt angenommen. Zweite,
unabhängige Messung nach dem 21.08. mit demselben Ergebnis.

---

## B — Vertrag: die drei blinden Flecken

`unbeobachtet` ist jetzt **leer**. Alle 20 Endpunkte haben eine beobachtete Form.

| Endpunkt | warum blind | was wirklich dahinter steckte |
|---|---|---|
| `cities.alerts` | Antwort immer `[]` | **Produktdefekt:** Der Poll-Job baute die normalisierte Alert-Liste auf und schrieb sie **nirgendwo hin**. Der Endpunkt war bei 96.663 Alerts je Abruf dauerhaft leer. Behoben (T-ALERT-1/-2). |
| `journeys.warnings` | Antwort immer `[]` | **Aufzeichnungsfehler, Server war korrekt:** abgefragt wurde, *bevor* eine Meldung an der Fahrt hing — und dann vom selben Halt aus, an dem die Meldung lag. `JourneyService` verwirft bewusst alles mit `idx <= iFrom`. |
| `reports.event` | Antwort `null` | **Keine Lücke:** 204 hat per Entwurf keinen Körper. Jetzt als `ohneKoerper: true` ausgewiesen statt als offene Baustelle. |

Der Alert-Filter ist gemessen, nicht geraten: von 96.663 Alerts tragen **99,9 % eine
`trip_id`** und 0,1 % eine `stop_id`; **kein** Hamburg-Treffer kam über `stop_id`, den die
`trip_id` nicht auch gefunden hätte. Ein trip-basierter Filter ist damit vollständig.
Von 29 Hamburg-Meldungen überleben **19** die Noise-Regeln — echte Störungen
(„Reparatur an einem Signal", „Bauarbeiten", „Verspätete Bereitstellung des Zuges").

**Dabei gefunden:** Der handgeschriebene PWA-Typ `ServiceAlert` deklarierte
`severity?: string` — der Server liefert `number`. Genau die Drift, die der aufgezeichnete
Vertrag verhindern soll, hatte sich in dem einen handgeschriebenen Typ eingenistet, den es
noch gab. `ServiceAlert` und `ControlWarning` sind jetzt abgeleitet; `report_id` fehlte im
handgeschriebenen `ControlWarning` ganz.

### Nullbarkeit — kann die Lücke geschlossen werden?

**Nein, und ich sage warum statt es zu umgehen.** Der Vertrag trägt die Grenze jetzt selbst
(`grenzen` im JSON):

- **Aus Servercode ableiten geht nicht.** Die Antworten sind **anonyme Typen** in den
  Controllern (`new { ... }`). Dort gibt es keine Nullable-Annotation, die man lesen könnte.
  Genau deshalb liefert Swashbuckle für 18/18 Operationen kein Antwortschema (Messung
  22.08.). Der Weg ist nicht „noch nicht gemacht", sondern an dieser Codeform verschlossen.
- **Mehr Beobachtungen helfen, beweisen aber nie „nie null".** Beobachtung zeigt immer nur:
  in *diesen* Fällen war der Wert gesetzt. Der Vertrag weist deshalb je Endpunkt
  `beobachtungen: n` aus. Stand jetzt: **17 von 20 Endpunkten sind nur einmal beobachtet.**

**Verbleibende Fehlerklasse, unverändert offen:** Ein Client-Typ nimmt ein Feld als
immer-vorhanden an, weil es in allen Aufzeichnungen gesetzt war; der Server setzt es unter
einer seltenen, nie aufgezeichneten Bedingung auf `null`; der Client fällt zur Laufzeit um.
Genau so ist `route_id` am 21.08. passiert. Das Gegenmittel ist **nicht** mehr Ableitung,
sondern mehr Beobachtungen unter verschiedenen Datenlagen — messbar an `beobachtungen`.

---

## C — Produktionsnahe Laufzeit

### C.1 Hangfire mit Postgres-Auftragsspeicher

Neu: `docs/sql/0006_hangfire.sql` (Schema `hangfire`, Eigentümer `tg_ingest`),
`Hangfire.PostgreSql` 1.21.1, Schalter `Jobs:Storage` (Vorgabe folgt `Data:Provider`).

*Bestätigt (T-HANGFIRE-1):* Der Zeitplan überlebt den Prozess. Entscheidend ist die Messung
**zwischen** zwei Starts — dort läuft keine API, und alle vier Aufträge samt Cron-Ausdruck
stehen trotzdem in der Datenbank. Ein zweiter Start verdoppelt sie nicht.

*Bestätigt:* Die verteilte Sperre trägt auf diesem Speicher: die zweite Anforderung
derselben Sperre scheitert, solange die erste gehalten wird.

*Bestätigt (T-HANGFIRE-5):* `tg_app` kommt an das `hangfire`-Schema **nicht** heran.

**Korrektur durch die Messung:** Ich hatte angenommen, wiederkehrende Aufträge stünden in
`hangfire.set`. Sie stehen in `hangfire.hash` unter `recurring-job:<id>`. Die Prüfung gegen
die echte Datenbank hat mich widerlegt; ich habe die Prüfung angepasst, nicht die Aussage
geschönt.

### C.2 Ingest unter Hangfire, nicht als Direktaufruf

`[DisableConcurrentExecution]` auf `PollRealtimeInvoker.Run`, `StaticSyncInvoker.Run`,
`TtlSweepInvoker.Run`; `[AutomaticRetry(Attempts = 0)]` auf dem Poll (ein verpasster Lauf
wird von der nächsten Minute erledigt; ein Wiederholungsversuch zöge nur veraltete Daten nach).
`WorkerCount = 1`.

Belegt in zwei Teilen, weil **eines allein nicht reicht**: T-HANGFIRE-1 zeigt, dass der
Sperrmechanismus auf diesem Speicher funktioniert; T-HANGFIRE-4 zeigt, dass er auf den
richtigen Methoden sitzt.

Kein Speicheraufbau: zwölf Zyklen, +0,6 % RSS (A.4).

### C.3 Sauberer Start mit allen Produktionsschaltern — und was dabei herauskam

**Zwei gefangene Abhängigkeiten (T-DI-SCOPE), beide launch-relevant:**

```
AccessGate        (Singleton) ← IDeviceRegistry  (Scoped, EF)
EntitlementContext(Singleton) ← IEntitlementStore(Scoped, EF)
```

Ein Singleton, der einen Scoped-Dienst zieht, hält ihn dauerhaft fest: **ein einziger
`DbContext` für den ganzen Prozess**, geteilt über alle gleichzeitigen Anfragen. `DbContext`
ist ausdrücklich nicht threadsicher. Das fällt nicht beim Start auf, sondern unter Last,
sporadisch, mit „A second operation was started on this context". Im Speicher-Modus sind
beide Dienste Singleton — deshalb war es in neun Bausessions unsichtbar. `AccessGate` ist die
Zahlschranke, `EntitlementContext` die Berechtigungsauflösung: beides Wege, die jede Anfrage
berührt. Behoben (beide `Scoped`).

**`deploy/env.example` hätte den Erstdeploy still versenkt (T-ENV-1/-2/-3):**

- `Data__Provider`, `Jobs__Storage`, `Ingest__Enabled` fehlten **vollständig**. Ein Deploy
  nach dieser Vorlage wäre im In-Memory-Modus hochgekommen — mit eingerichteter, aber
  ungenutzter Datenbank und **ohne Ingest**. Nichts hätte gemeldet, dass etwas fehlt; die
  API wäre „grün" gewesen und hätte nur keine Daten gehabt.
- `INGEST__FEED_URL` traf `Ingest:FEED_URL` statt `Ingest:FeedUrl` — still wirkungslos.
- `INGEST__POLL_INTERVAL_S` liest niemand (der Takt ist Zeitplan-Konstante).
- `SIGNALR__TOKEN_SIGNING_KEY`, `OPS__*`, `BILLING__*`, `PUBLIC_URLS__*` liest der Code heute
  nicht. Sie bleiben als Platzhalter, sind jetzt aber als solche gekennzeichnet.

T-ENV-1 vergleicht die Vorlage automatisch gegen **alle** Schlüssel, die der Code liest.

### C.4 SMTP

**Kann ich nicht belegen.** `LoggingAlarmSink` schreibt Alarme ins Log; ein SMTP-Decorator
existiert nicht (`docs/07 §8`, Ticket F-19). Es gibt in dieser Umgebung keinen Mailserver und
keine Zugangsdaten, und ich baue dafür keinen Ersatzbeweis. `OPS__SMTP_*` steht in
`env.example` unter der Überschrift „liest der Code heute nicht".

---

## D — Android-Gerätetest vorbereitet, nicht gefälscht

`docs/29-android-geraetetest.md` (neu, 314 Zeilen): Werkzeuge mit **aus dem Repo abgelesenen**
Versionen (Flutter 3.47.1 / Dart 3.13.1, Gradle 9.3.1, AGP 9.1.0, Kotlin 2.4.0, JDK 21),
Kommandos in Reihenfolge, Erfolgs- und Fehlkriterien je Testfall, `adb logcat`-Muster,
R8-Vorgehen mit Gegenprobe (`-PdisableMinify=true`) und `mapping.txt`/`retrace`.

**Ohne Gerät geschlossen:**

- **Klartext-HTTP.** Android blockiert seit API 28 unverschlüsseltes HTTP vollständig — auch
  zu `127.0.0.1`, auch über `adb reverse`. Ohne Netzwerkkonfiguration wäre **jeder**
  Gerätetest gegen eine lokale API an einem Verbindungsfehler gescheitert, und man hätte es
  für einen App-Fehler gehalten. Neu: `src/debug/res/xml/network_security_config.xml` und
  ein Debug-Manifest, das darauf verweist. Der Release-Build bekommt beides **nicht**.
- **T-ANDROID-1 bis -4** (statisch, laufen im normalen Flutter-Testlauf): Klartext nur im
  Debug; R8/Shrinker im Release an; kein Hintergrund-Standort und keine verkettenden
  Berechtigungen (ADR-0005); Signierschlüssel nicht im Repo.
- **E-0** neu in `integration_test/app_test.dart`: prüft **zuerst**, ob die API erreichbar ist,
  mit einer Fehlermeldung, die den Weg nennt. Ohne diesen Fall hätte ein vergessenes
  `adb reverse` wie ein App-Fehler ausgesehen und alle sechs Folgefälle mitgerissen.

**Unverändert unbelegt:** Laufzeitverhalten unter R8, Plugin-Kanäle auf echter
Android-Laufzeit, Rendering. Die sechs `integration_test`-Fälle sind statisch analysiert
(`flutter analyze` grün), aber **nie gelaufen** — weder auf einem Gerät noch über
`flutter drive` im Browser (CanvasKit wird von `www.gstatic.com` geladen, der Proxy blockt).
Das steht so im Dokument, nicht kleingedruckt.

---

## E — QA-Loop

Zwei neue Gates, jetzt **vierzehn**:

| Gate | prüft |
|---|---|
| Ingest-Fehlerpfade | 7 Fehlerfälle, Job fängt sich, Metrikzeile je Runde |
| Echtdaten-Ingest | Hangfire + Postgres + echter Feed, Persistenz, API stabil |

Der Ingest-Gate behandelt fehlenden Netzzugang oder fehlendes Postgres als
**übersprungen** — nicht grün und nicht rot.

**Harness-Defekt gefunden und behoben:** `gate()` machte aus **jedem** Exit ungleich 0 ein
ROT — auch aus Exit 3, der im ganzen Repo „übersprungen" heißt. Damit hätte ein Lauf ohne
laufende API den **Vertrags-Gate als Fehlschlag** gemeldet. Belegt durch direkten Vergleich
der alten und neuen `gate()`-Funktion an vier Exit-Codes:

```
                     vorher        jetzt
Werkzeug-Exit 0  ⇒   grün          grün
Werkzeug-Exit 1  ⇒   ROT           ROT
Werkzeug-Exit 2  ⇒   ROT           ROT
Werkzeug-Exit 3  ⇒   ROT           übersprungen
```

Die Kontrolle ist wichtiger als der Fix: Exit 1 und 2 bleiben rot.

**Zweiter Harness-Befund: der Vertrags-Gate war flatterig.** Im ersten Loop-Durchlauf meldete
er `rot → durch Fixer behoben`. Das war irreführend: `scripts/qa-fixes.sh` hat für den
Vertrags-Gate **gar keinen Fall** — der Fixer tat nichts, und der *zweite* Lauf desselben
Gates wurde von allein grün.

Ursache, reproduziert gegen eine frisch gestartete API:

```
1. Aufzeichnung (frische API)   →  journeys.search OHNE warnings[]-Inhalt
2. Aufzeichnung (dieselbe API)  →  journeys.search MIT  warnings[]-Inhalt
```

Die Aufzeichnung fragte die Fahrtensuche ab, **bevor** sie Meldungen anlegte. Die verschachtelten
`warnings[]`-Objekte erschienen deshalb nur, wenn zufällig noch Meldungen aus einem früheren
Lauf im Speicher lagen. Der abgelegte Vertrag war also gegen eine „gebrauchte" API
aufgezeichnet und passte auf eine frische nicht.

**Ein Gate, das beim zweiten Versuch von allein grün wird, ist kein Gate.** Behoben, indem die
Aufzeichnung den Zustand selbst herstellt: `journeys.search` wird jetzt dreimal beobachtet
(Direktstrecke ohne Meldungen, Umstiegsstrecke, Direktstrecke **mit** Meldungen),
`stops.departures` zweimal. Beleg der Determiniertheit — dreimal hintereinander gegen eine
frisch gestartete API aufgezeichnet:

```
=== 1 vs 2 ===  IDENTISCH
=== 2 vs 3 ===  IDENTISCH
```

Nebenwirkung, die den Vertrag besser macht: `warnings[]` ist jetzt korrekt als optional
ausgewiesen, und vier Endpunkte haben mehr als eine Beobachtung
(`journeys.search` 3, `cities.alerts` 2, `stops.departures` 2, `stops.nearby` 2) — genau das
Gegenmittel gegen die Nullbarkeits-Lücke aus Abschnitt B.

### Ergebnis

**5/5 Durchläufe, 70/70 Gates grün, Skript-Exit 0.**
09:53:31 · 09:57:13 · 10:00:31 · 10:04:33 · 10:08:57 UTC.

Der Ingest-Gate lief in allen fünf Durchläufen **grün** (nicht übersprungen) — der Feed war
erreichbar, Postgres lief. Bei fehlendem Netz stünde dort „übersprungen"; das ist keine
Aussage über die Korrektheit.

**Was fünf Durchläufe NICHT leisten:** Sie liefen in 19 Minuten. Ein zeitabhängiger Fehler
mit einem Fenster von Stunden — genau wie F.5 — kann darin nicht auffallen. Wiederholung
ersetzt keine Variation.

---

## F — Meine eigenen Fehler in dieser Session

### F.1 Die erste Gegenprobe der Session war ungültig — genau wie in Session 9

Ich baute den ETag-Defekt wieder ein und ließ die Tests mit `--no-build` laufen. Der Build
**scheiterte** (CS9113, `TreatWarningsAsErrors`), `--no-build` lief gegen die **alte, heile**
DLL — und meldete **grün**. Eine Gegenprobe, die grün wird, weil der Defekt gar nicht
kompiliert wurde, ist wertlos.

Korrigiert: Gegenproben laufen ohne `--no-build`, der Build muss **gelingen**, und der
Fehlschlag muss die **Signatur des Defekts** tragen. Beim ETag: `Actual: null` — also
nachweislich kein gesendeter `If-None-Match`-Kopf, kein Fremdfehler.

### F.2 Ein Speichervergleich im selben Prozess ist kein Vergleich

Erste Messung „MemoryStream vs. Datei" ergab −46,7 MB, also scheinbar *zugunsten* des
MemoryStreams. Ursache: beide Varianten liefen im selben Prozess; der Heap der ersten hatte
den RSS-Ausgangswert der zweiten längst aufgebläht. Getrennte Prozesse ergaben 687 MB vs.
418 MB. **Die Messung hatte recht, meine Erwartung war falsch — und die Messmethode auch.**

### F.3 Ein Test, der reflektierend ein Hangfire-Attribut instanziiert

`GetCustomAttribute<DisableConcurrentExecutionAttribute>()` **erzeugt** das Attribut, und
dessen Konstruktor holt Hangfires globalen Log-Anbieter — der in einem Testprozess mit
mehreren Hosts auf eine entsorgte `LoggerFactory` zeigt. Der Test scheiterte an einem
Fremdfehler und sagte nichts über die Aufträge. Jetzt über `CustomAttributeData` **gelesen**
statt instanziiert.

### F.4 Prozessweiter Zustand ist kein Testdetail

Hangfire hält `JobStorage.Current` und den Log-Anbieter prozessweit. Vier getrennte Tests,
die je einen Host hoch- und herunterfahren, greifen sich gegenseitig den globalen Zustand
weg: T-HANGFIRE-1 lief allein grün und im Verbund rot. Zusammengefasst zu **einem** Test mit
klarer Reihenfolge. Die Gegenprobe „MemoryStorage hinterlässt nichts" ist deshalb bewusst
**kein** xUnit-Test, sondern läuft in `verify-ingest-e2e.sh` in einem **eigenen Prozess** —
in-process wäre sie grün geworden, ohne den Defekt je zu berühren.

### F.5 Ein zeitabhängiger Test, der nur durch Glück grün war

`T_TRANSFER_2` verglich feste Zielangaben („Kellinghusenstrasse"). Die Fixture enthält neben
den 72 Taktfahrten auch die Altfahrten `T_HH_1`/`T_HH_3` auf derselben Linie mit Ziel
„Ohlsdorf". Welche die nächste ist, hängt an der **Uhrzeit des Testlaufs**. Um 10:0x fiel er
um, ohne dass sich am Code etwas geändert hatte. **Fünf QA-Durchläufe hintereinander liefen
in Session 9 innerhalb weniger Minuten und konnten das nicht finden** — Wiederholung ersetzt
keine Variation. Jetzt wird die Eigenschaft geprüft (Ziel gehört zur Linie), nicht der
Zufallswert.

### F.6 Ein Test, der bei jeder korrekten Änderung gepflegt werden muss

`T_PG_SCHEMA_1` zählte die Migrationsdateien auf und wurde durch `0006_hangfire.sql` rot —
nicht weil etwas kaputt war, sondern weil die Liste veraltete. Solche Tests werden irgendwann
gedankenlos nachgezogen und prüfen dann nichts mehr. Jetzt liest er `docs/sql/` selbst.

### F.7 Den Vertrag mitten im laufenden QA-Loop geändert

Ich habe die Vertragsaufzeichnung korrigiert, während der Fünf-Pass-Lauf schon lief. Damit
prüften Pass 2 ff. gegen eine Datei, die sich unter ihnen verändert hat — der Lauf war
wertlos. Abgebrochen und neu gestartet. Ein Lauf, dessen Eingaben sich während des Laufs
ändern, beweist nichts, egal was er meldet.

Beim Abbrechen: die Prozessgruppe des Loops wurde **dreimal** neu verwaist (PPID 1), weil
`kill -TERM` auf die Kindprozesse die Subshells von `$(run_gate …)` freigab, die dann
weiterliefen. Erst `kill -KILL` auf jede sichtbare PID und eine anschließende Kontrolle über
`ps` plus Port- und Sperrprüfung hat wirklich aufgeräumt. Das ist dieselbe Klasse wie der
Waisen-Loop aus Session 9 — nur diesmal von mir selbst erzeugt.

### F.8 Zweimal in dieselbe Bash-Falle

`pkill -f "TransitGuard.Api.dll"` traf die eigene Shell (Exit 144) — dieselbe Falle, die
schon in `docs/27` steht. Aufgeschrieben zu haben heißt nicht, danach zu handeln.
