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
