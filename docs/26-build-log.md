# 26 — Build-Log (8. Session, 2026-08-21): QA-Loop 5 Durchläufe

**Auftrag (Übergabe §1, Sofort-Aufgabe 1):** `scripts/qa-loop.sh 5` ausführen, gefundene
Fehler beheben, Berichte in `docs/qa/` führen.

**Ergebnis in einem Satz:** Der Loop lief 5× vollständig grün — aber erst, nachdem im
QA-Harness selbst sieben Defekte behoben waren, von denen drei **falsches Grün**
produziert haben. Der Stand vor dieser Session war damit schwächer belegt, als die
Übergabe angenommen hat.

---

## 1. Umgebung (frischer Container, keine Vorgeschichte)

Das Zielverzeichnis war ein leeres Git-Repo; der Projektstand kam aus dem
Übergabe-Archiv. Toolchains nach §6-Rezept neu aufgesetzt:

| Werkzeug | Version | Marker |
|---|---|---|
| .NET SDK | 8.0.424 | *Bestätigt* (`dotnet --version`) |
| Flutter | 3.47.1 · Dart 3.13.1 | *Bestätigt* (`flutter --version`) |
| Node / npm | 22.22.2 / 10.9.7 | *Bestätigt* |
| RAM / CPU / Disk | 16 GB · 4 Kerne · 30 GB frei | *Bestätigt* (`free -m`, `nproc`, `df -h`) |

**Für Sofort-Aufgabe 3 relevant:** Diese Maschine hat 16 GB RAM. Die in
`25-build-log` dokumentierte R8-Grenze (1,5-GB-Sandbox, 4 OOM-Versuche) gilt hier
**nicht mehr**. Ob der Release-AAB durchläuft, hängt jetzt nur noch am Android-SDK,
nicht am Speicher. *Glaube ich* — nicht ausprobiert, siehe §5.

## 2. Funde im Übergabe-Archiv (vor dem ersten Loop-Lauf)

**A-1 — Execute-Bits verloren (launch-relevant).** Sämtliche `scripts/*.sh`,
`deploy/migrate-remote.sh` und `app/android/gradlew` kamen mit `-rw-r--r--` aus dem
Archiv. `scripts/acceptance.sh` scheiterte mit `Permission denied` (rc 126). Betrifft
auch `deploy.sh` und `migrate.sh` — der Erst-Deploy nach `RUNBOOK-SERVER.md` wäre in
Phase 1 gescheitert. Behoben, Modus jetzt in Git geführt (`git update-index --chmod=+x`).

**A-2 — `.gitignore` deckt Build-Artefakte nicht ab.** `bin/`, `obj/`, `node_modules/`,
`web/dist/`, `app/build/` fehlten; `web/tsconfig.tsbuildinfo` war sogar eingecheckt.
Ergänzt und aus dem Index entfernt. Die Secret-Regeln (`key.properties`,
`upload-keystore.jks`) greifen unverändert — vor dem Basis-Commit gegengeprüft:
keine Secrets im Index.

## 3. Defekte im QA-Harness selbst (`scripts/qa-loop.sh`)

Das Skript war laut Übergabe „betriebsbereit". Es lief — aber es hat teilweise
Grün gemeldet, wo keins war. Alle sieben Punkte behoben:

| # | Defekt | Wirkung | Schwere |
|---|---|---|---|
| B-1 | Flutter-Gates: `… && flutter analyze \| grep -q 'No issues found' \|\| exit 0` | **Strukturell nie rot.** Fehlende Toolchain *und* echte Analyzer-Fehler landen beide im `\|\| exit 0`. | Falsches Grün |
| B-2 | Backend-Tests: `grep -q 'Failed:     0'` | Bei zwei Testprojekten maskiert die Zeile des **grünen** Projekts ein rotes. | Falsches Grün |
| B-3 | Build-Gate: `grep -qE 'Build succeeded'` statt Exit-Code | Warnungen unsichtbar, obwohl `CLAUDE.md` 0 Warnungen verlangt. | Falsches Grün |
| B-4 | `webbuild`/`acceptance` schreiben nach `/dev/null` | Bei Rot war `/tmp/gate-*` **leer** — keine Diagnose möglich. | Beleg-Verlust |
| B-5 | Variablenkollision: innere Schleife überschreibt Pass-Nummer `n` | Konsolenzeile verwies auf `qa-pass-Acceptance.md`. | Kosmetisch |
| B-6 | Angekündigte JSON-Summary existierte nicht; toter `for i in name:build:1 …`-Loop | Kopf-Kommentar versprach mehr als der Code lieferte. | Doku-Schuld |
| B-7 | Kein Exit-Code, kein API-Cleanup bei Abbruch, blindes `sleep 4` | Loop war nicht skriptbar; API-Prozess leckte bei Ctrl-C. | Betrieb |

**Konsequenz für die Bewertung des Altstands:** B-1 bedeutet, dass die in
`25-build-log` verbuchten Flutter-Gates des abgebrochenen Laufs **keine Aussage**
hatten. Die dort separat dokumentierten `flutter analyze` / `flutter test`-Läufe von
Hand sind davon nicht betroffen — die waren echt und sind hier reproduziert.

### Umbau

Vier Gate-Zustände statt zwei: `0 grün · 1 rot→durch Fixer behoben · 2 rot ·
3 übersprungen`. Eine fehlende Toolchain ist **kein Beweis für Korrektheit** und wird
nie mehr als grün verbucht. Gates liefern immer den echten Exit-Code des Werkzeugs;
der vollständige Output landet in `docs/qa/logs/<gate>.log` (auch bei Grün). Die
API-Bereitschaft wird aktiv abgefragt (`curl --retry-connrefused`) statt blind geschlafen.

## 4. Negativproben — dass ein Gate rot werden *kann*

Ein grünes Gate ist nur so viel wert wie sein Vermögen, rot zu werden. Drei Proben,
jeweils mit anschließender Wiederherstellung:

| Probe | Eingriff | Erwartet | Gemessen |
|---|---|---|---|
| A | `FLUTTER_BIN=/nonexistent` | Flutter-Gates *übersprungen* (3) | `flutteranalyze:3, fluttertests:3` ✔ |
| B | Typfehler in `app/lib/api.dart` | Flutter-Gates *rot* (2) | `flutteranalyze:2, fluttertests:2` ✔ |
| C | Ein bewusst fehlschlagender Test **nur** im Ingest-Projekt | Backend-Tests *rot* (2) | `backendtests:2` ✔ |

Probe C ist der direkte Beleg für B-2: Das Log enthielt weiterhin eine
`Failed:     0`-Zeile (vom grünen Core-Projekt) — die alte Logik hätte **grün**
gemeldet, während `TransitGuard.Ingest.Tests` mit `Failed: 1` rot war.

*Nebenbefund aus einer verworfenen Variante von Probe C:* Das Testprojekt hat
xUnit-Analyzer als **Fehler** scharf (`Assert.True(false, …)` → `error xUnit2020`).
Gut so — nur beim Schreiben neuer Negativtests zu wissen.

## 5. Abschlusslauf

`scripts/qa-loop.sh 5` — 5 Passes × 6 Gates, **30/30 grün**, Exit 0.
Berichte: `docs/qa/qa-pass-1..5.md` (+ `.json`), Gate-Ausgaben unter `docs/qa/logs/`.

| Gate | Beleg |
|---|---|
| Backend-Build | 0 Fehler / 0 Warnungen |
| Backend-Tests | **73/73** (65 Core + 8 Ingest) |
| Flutter-Analyze | `No issues found!` |
| Flutter-Tests | **5/5** |
| Web-Build | `tsc -b && vite build` grün, 58 Module |
| Acceptance | **5/5** (health · trial · gate-422 · gate-403 · nearby) |

Das Ticket-First-Gate (H1) ist in beiden Richtungen belegt: `gate-422`
(`ticket_confirmation_required` beim Schreiben) und `gate-403`
(`ticket_gate_blocked` beim Lesen).

**Was der Lauf NICHT belegt** — ehrlich benannt statt weggelassen:
- Die API lief im **Lokal-Modus mit In-Memory-Stores**; kein Postgres im Container.
  RLS, Partitionen und Migrationen sind in `22-build-log` gegen echtes PG belegt, hier
  aber **nicht** erneut geprüft.
- Kein Echtdaten-StaticSync (Prod-RAM), kein Release-AAB, kein Server-Deploy.
- `flutter test` deckt DTO-Parsing und den Ticket-Gate-Banner ab — keine E2E-Strecke
  auf einem Gerät.

## 6. Offen (unverändert aus Übergabe §7, plus Neues)

Neu aus dieser Session: Android-SDK für den Release-AAB fehlt im Container (RAM ist
kein Hindernis mehr, §1). Unverändert: Echtdaten-StaticSync auf Prod-RAM ·
Server-Erstdeploy · Anwalts-Gate F-1/F-18 · SMTP-Alarm-Dekorator ·
Hangfire-Postgres-Storage · T2.4-DB-Teil.

---

# Teil 2 — UX-Überarbeitung (Sofort-Aufgabe 2)

**Auftrag:** App aus Nutzersicht moderner und extrem leicht bedienbar machen —
PWA zuerst, dann Flutter. 1-Hand-Bedienung, klare Hierarchie (Fahren > Warnen >
Mehr), Skeleton-Loading, deutschsprachige Mikro-Texte, Copy-Nie-Liste einhalten.

## 7. Was sich geändert hat (beide Kanäle gleich)

| Vorher | Jetzt |
|---|---|
| Alles auf einer Seite untereinander, Bedienung oben | Drei Ziele in einer Leiste am **unteren** Rand — Daumenzone |
| Zentrierte Dialoge | Bottom-Sheets, die aus der Daumenzone aufsteigen |
| `alert()` bzw. rohe Fehlercodes | Toasts/Snackbars mit deutschen Sätzen je Servercode |
| „Suche…" als Text | Skeletons in der Form des erwarteten Inhalts |
| Zielsuche mit extra „Suchen"-Knopf | entprellte Suche beim Tippen — ein Tap weniger |
| Nur dunkel, Google-Fonts-Abruf | Hell **und** dunkel, Systemschrift (kein externer Abruf) |
| Keine Installierbarkeit | Manifest + Icons (maskable), `display: standalone` |
| Ticket-Gate an mehreren Stellen geprüft | **Eine** Stelle entscheidet; die ausgelöste Aktion läuft danach weiter |

Die PWA-Oberfläche liegt jetzt in `web/src/screens/` und `web/src/ui/`; das
Design-System steckt vollständig in Tokens (`web/src/styles.css`). Flutter
spiegelt es in `app/lib/theme.dart`, damit sich beide Kanäle gleich anfühlen.

**Ticket-First-Gate (H1) blieb unangetastet und wurde härter festgeschrieben:**
Der Warnen-Tab ist ohne Bestätigung geschlossen — kein Lesen, kein Melden —, und
„Später" im Gate ist kein Schlüssel. Beides ist als Test verankert
(`app/test/widget_test.dart`, T-GATE) und wird im Browser-Rauchtest nachgefahren.

## 8. Fehler, die der Browser-Rauchtest ans Licht gebracht hat

Alle vier wären erst in Nutzerhand aufgefallen. Keiner war für `acceptance.sh`
(curl), `signalr_e2e.mjs` (Node) oder die Unit-Tests sichtbar.

**C-1 — Auslieferung: PWA und API auf verschiedenen Origins (launch-blockierend).**
`deploy/Caddyfile` lieferte die PWA unter `app.example.de`, die API unter
`api.example.de`. Der Runbook baut die PWA aber **ohne** `VITE_API_URL`, also mit
relativen Pfaden — `try_files {path} /index.html` hätte damit jeden `/v1/`-Aufruf
mit der **HTML-Seite** beantwortet (JSON-Parse-Fehler in jedem Request). Eine
API auf eigener Domain hätte stattdessen CORS gebraucht, das nirgends
konfiguriert ist (`grep -rn cors src/ deploy/` → 0 Treffer).

*Erster Fix war falsch.* Ein Pfad-Matcher plus `reverse_proxy` reichte nicht:
Caddy ordnet Direktiven nach einer festen Reihenfolge, in der `try_files` (ein
Rewrite) **vor** `reverse_proxy` läuft — der Rewrite zog `/v1/...` bereits auf
`/index.html`, bevor der Matcher greifen konnte. Aufgefallen, weil ich die
Konfiguration nicht nur validiert, sondern **funktional durchgemessen** habe.
Richtig sind sich gegenseitig ausschließende `handle`-Blöcke.

Warum Same-Origin und nicht CORS: Die App schickt drei eigene Header
(`X-Device-Token`, `X-Ticket-Confirmed`, `Idempotency-Key`). Cross-origin kostete
das je Aufruf einen zusätzlichen OPTIONS-Preflight — im Mobilfunknetz spürbar.
Same-Origin ist hier zugleich die engere und die schnellere Wahl.

Abgesichert durch `scripts/verify-caddy.sh` (6 Prüfungen). Gegenprobe gemacht:
gegen die alte Form meldet das Skript korrekt rot.

**C-2 — Server: Haltestellen-ID statt Name in jeder Meldung.**
`ReportsController` schrieb `StationName = req.Station.StopId`. Die Meldeliste
zeigte Nutzenden wörtlich „HHA1" statt „Jungfernstieg" — in PWA und Flutter.
Behoben über `StopLookup.DisplayName` (Core, 4 Tests T-STOP-1..4; unbekannte ID
bleibt sichtbar die ID, es wird nichts erfunden).

**C-3 — Flutter: jede Fahrtensuche warf einen TypeError.**
`Departure.routeId` war nicht-nullable, aber `POST /v1/journeys/search` liefert
`route_id` **nicht** auf der Abfahrt (dort steht es auf der Verbindung) —
`GET /v1/stops/{id}/departures` dagegen schon. Ergebnis:
`type 'Null' is not a subtype of type 'String'`, und `_findLine` fängt nur
`ApiException`, also verpuffte der Fehler stumm. Test T-DTO-4 reproduziert das,
die Verbindungswerte werden jetzt an die Abfahrten durchgereicht.

**C-4 — Flutter: jede Meldung scheiterte mit 422.**
`POST /v1/reports` verlangt einen `Idempotency-Key` (Server antwortet sonst
`validation`). Die Flutter-App schickte keinen — die PWA schon. Jetzt erzeugt
`newIdempotencyKey()` einen UUID-v4 ohne Fremdpaket (2 Tests: Form und
Eindeutigkeit über 500 Aufrufe).

Dazu drei kleinere PWA-Fehler des Altstands: `leg_a.RouteId` statt
`leg_a.route_id` gelesen (Umstiege ohne Liniennummer — durch eine gezielte
Serialisierungsprobe bewiesen, weil die Fixture keine Umstiege liefert);
SignalR verband nie, wenn das Ticket erst **nach** dem Laden bestätigt wurde
(leere Abhängigkeitsliste); der Live-Block war doppelt gerendert.

## 9. Neuer Beleg: `verifikation/pwa_smoke.mjs`

Fährt die Kette im echten Chromium ab, in **hell und dunkel**, gegen die
**echte Caddy-Konfiguration**: Standort → Ziel → Verbindung · Ticket-Gate
erscheint vor jeder Meldung · Warnen-Tab ohne Bestätigung gesperrt, danach frei ·
Meldung geht durch und erscheint mit Klarnamen in der Liste · alle
Trefferflächen ≥ 44 px · Copy-Nie-Liste eingehalten · Manifest ladbar.
**Bestanden, keine Befunde.**

*Was er nicht kann:* Er ersetzt kein Gerät. Standort ist gestellt, das Netz ist
lokal, und die Fixture kennt vier Haltestellen — Umstiegsverbindungen kommen
darin nicht vor und sind daher **nicht** durchgefahren.

---

# Teil 3 — Release-AAB (Sofort-Aufgabe 3)

**Ergebnis: gebaut.** Die vier dokumentierten OOM-Versuche waren tatsächlich
reine Speichergrenze, keine Konfigurationsfrage.

| | |
|---|---|
| Maschine | 16 GB RAM, 4 Kerne |
| Android-SDK | cmdline-tools 11076708, Platform 36, Build-Tools 36.0.0, NDK r28c |
| `gradle.properties` | `-Xmx640m` → `-Xmx4g`; `workers.max=1` und `daemon=false` entfernt |
| R8 / Ressourcen-Shrinker | **wieder aktiviert** (`-PdisableMinify=true` als Notausgang) |
| Ergebnis | `app-release.aab`, **50,4 MB**, Gradle-Task **325 s**, kein OOM |
| Signatur | `jar verified`, CN=TransitGuard Upload (Sandbox-Key, selbstsigniert) |
| ABIs | arm64-v8a, armeabi-v7a, x86_64 · targetSdk 36 |
| Universal-APK | über bundletool 1.17.2 erzeugt und `apksigner`-verifiziert |

**R8-Rückbau geprüft.** `apkanalyzer` meldete zunächst drei fehlende Klassen —
das war ein Messfehler von mir: R8 **benennt um**, es entfernt nicht. Die
Mapping-Datei ist die verbindliche Quelle und zeigt alle erhalten:
`FlutterActivity → vb`, `GeolocatorPlugin → rf`, `SharedPreferencesPlugin → lu`;
`MainActivity` behält seinen Namen (Manifest-Einstiegspunkt). 1.418 Klassen im
Mapping, kein Bedarf an eigenen Keep-Regeln.

**Zwei Gründe, warum dieses Bundle NICHT hochgeladen werden darf** — beide in
die Play-Checkliste geschrieben:
1. Signiert mit dem **sandbox-generierten** Upload-Key. Produktion braucht einen
   eigenen Key plus Play App Signing.
2. `API_BASE` ist eine **Compile-Zeit**-Konstante. Der erste Bau ohne
   `--dart-define` trug den Emulator-Loopback `http://10.0.2.2:5099` im Bundle —
   eine App, die in Nutzerhand keinen Server erreicht.

Der zweite Bau **mit** `--dart-define=API_BASE=https://api.example.de` wurde
gegengeprüft: 2 Treffer für die Produktions-URL, 0 für den Emulator-Default.
*Auch mein erster Prüfbefehl war falsch* — die Konstante liegt im Dart-AOT-Code
(`base/lib/*/libapp.so`), nicht im Java-Dex (`classes.dex`: 0 Treffer). Die
Checkliste nennt jetzt den richtigen Befehl; der falsche hätte zuverlässig zu
dem Schluss geführt, der Schalter habe nicht gewirkt.

**Nicht belegt:** Die App lief auf keinem Gerät und in keinem Emulator. Dass das
Bundle baut, signiert und in ein installierbares APK konvertierbar ist, sagt
nichts darüber, ob R8 zur Laufzeit etwas bricht. Vor dem geschlossenen Track
gehört ein Start auf echtem Gerät dazu.

---

## 10. Stand am Ende dieser Session

QA-Loop 5/5 grün (Skript-Exit 0): Build 0 Fehler/0 Warnungen · Backend **79/79**
(71 Core + 8 Ingest) · `flutter analyze` 0 Issues · `flutter test` **13/13** ·
Web-Build · Acceptance 5/5. Dazu PWA-Rauchtest bestanden und
`scripts/verify-caddy.sh` 6/6.

**Testabdeckung gewachsen:** Backend 73 → 79, Flutter 5 → 13.

### Offen (aktualisiert)
- **Gerätetest der Flutter-App** — neu, aus Teil 3.
- Server-Erstdeploy nach `deploy/RUNBOOK-SERVER.md` (Phasen 1–6).
- Echtdaten-StaticSync einmalig auf Prod-RAM.
- Anwalts-Gate F-1/F-18 — weiterhin die harte Hürde vor dem Play-Upload.
- Eigener Upload-Key + Play App Signing.
- SMTP-Alarm-Dekorator · Hangfire-Postgres-Storage · T2.4-DB-Teil.
- Service-Worker für Offline-Nutzung untertage: bewusst **nicht** gebaut. Ein
  Cache, der versehentlich Meldungsdaten hält, verletzt eine harte Projektregel
  (CLAUDE.md); das verdient einen eigenen, geprüften Durchgang mit
  Allowlist-Precaching statt eines nebenbei mitgelieferten Entwurfs.
