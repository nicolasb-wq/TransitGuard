# 29 — Android-Gerätetest (auszuführen auf deiner Maschine)

**Status: unbelegt.** Diese Anleitung existiert, weil ich den Gerätetest nicht durchführen
kann und keinen Ersatzbeweis dafür bauen will. Im Container gibt es weder `/dev/kvm` noch
CPU-Virtualisierungsflags — kein Emulator, kein Gerät. Alles unten ist **geprüfte Konfiguration
plus Ausführungsanleitung**, kein Ergebnis.

Was ich ohne Gerät belegt habe, steht in `app/test/android_konfiguration_test.dart` (T-ANDROID-1
bis -4) und in `verifikation/flutter_web_smoke.mjs` (die App läuft im echten Browser gegen die
echte API). Was das **nicht** belegt: Verhalten des Android-Release-Artefakts unter R8,
Plugin-Kanäle (`geolocator`, `shared_preferences`) auf echter Android-Laufzeit, Rendering
auf echter GPU.

---

## 0. Werkzeuge und Versionen

Die Versionen unten sind **aus diesem Repo abgelesen** (24.08.2026), nicht empfohlen:

| Werkzeug | Version | Woher |
|---|---|---|
| Flutter | 3.47.1 (stable, Dart 3.13.1) | `flutter --version` in dieser Umgebung |
| Gradle | 9.3.1 | `app/android/gradle/wrapper/gradle-wrapper.properties` |
| Android Gradle Plugin | 9.1.0 | `app/android/settings.gradle.kts` |
| Kotlin | 2.4.0 | `app/android/settings.gradle.kts` |
| JDK | 21 | `compileOptions` sagt Java 17 als *Ziel*; gebaut wird mit JDK 21 |
| minSdk/targetSdk/compileSdk | Flutter-Vorgaben (`flutter.minSdkVersion` usw.) | `app/android/app/build.gradle.kts` |

**Weiß ich nicht:** ob deine Flutter-Installation dieselbe Version hat. Weicht sie ab, prüfe
zuerst `flutter doctor -v`, bevor du einem Fehlschlag Bedeutung gibst.

Zusätzlich nötig:

```bash
# Android SDK Platform-Tools (adb) — Version egal, aktuell reicht
adb --version
# Lizenzen einmalig annehmen
flutter doctor --android-licenses
flutter doctor -v          # muss "Android toolchain" ohne ✗ zeigen
```

Ein **echtes Gerät** ist einem Emulator vorzuziehen: R8-Probleme und Plugin-Kanäle zeigen
sich auf beiden, aber Standort (`geolocator`) verhält sich auf echter Hardware anders.

Auf dem Gerät: *Entwickleroptionen* → *USB-Debugging* an. Prüfen:

```bash
adb devices -l        # muss genau ein Gerät als "device" listen, nicht "unauthorized"
```

---

## 1. Reihenfolge der Kommandos

Alles aus dem Repo-Wurzelverzeichnis, sofern nicht anders angegeben.

### 1.1 API starten (das Gerät muss sie erreichen)

```bash
scripts/pg-dev.sh up                       # Postgres 16 + PostGIS, migriert
export CS="$(scripts/pg-dev.sh env)"
ASPNETCORE_URLS=http://0.0.0.0:5099 \
Data__Provider=postgres DATABASE__APP="$CS" DATABASE__BILLING="$CS" DATABASE__INGEST="$CS" \
Jobs__Storage=postgres Ingest__Enabled=false \
dotnet run --project src/TransitGuard.Api -c Release
```

`0.0.0.0` statt `127.0.0.1` ist wichtig — sonst ist die API vom Gerät aus unsichtbar.
`Ingest__Enabled=false`, weil der Gerätetest gegen die Fixture-Fahrplandaten läuft; ein
40-MB-Feed alle 60 s macht die Messung nur unruhig.

### 1.2 Gerät auf die API zeigen lassen

**Der zuverlässige Weg ist `adb reverse`** — dann gilt auf dem Gerät `127.0.0.1:5099`,
unabhängig von WLAN, Hotspot oder Firewall:

```bash
adb reverse tcp:5099 tcp:5099
adb shell curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5099/health/ready
# Erwartet: 200. Kommt hier nichts, ist alles Weitere sinnlos.
```

Hat das Gerät kein `curl`, prüfe stattdessen später im Log (Abschnitt 3).

### 1.3 Debug-Lauf zuerst

```bash
cd app
flutter pub get
flutter run --dart-define=API_BASE=http://127.0.0.1:5099 -d <geräte-id>
```

Der Debug-Lauf trennt zwei Fehlerklassen: geht er schon schief, liegt es **nicht** an R8.

### 1.4 Der eigentliche Integrationstest

```bash
cd app
flutter drive \
  --driver=test_driver/integration_test.dart \
  --target=integration_test/app_test.dart \
  --dart-define=API_BASE=http://127.0.0.1:5099 \
  -d <geräte-id>
```

### 1.5 Release-Artefakt — das ist der eigentliche Zweck

```bash
cd app
flutter build apk --release --dart-define=API_BASE=https://api.example.de
adb install -r build/app/outputs/flutter-apk/app-release.apk
# Danach von Hand starten und durchklicken: Fahren → Warnen → Mehr.
```

Und das Auslieferungsartefakt:

```bash
flutter build appbundle --release
ls -l build/app/outputs/bundle/release/app-release.aab
```

> **Wichtig für den Release-Lauf gegen eine lokale API:** Der Release-Build erlaubt
> **kein** Klartext-HTTP (siehe Abschnitt 4). Entweder du testest ihn gegen eine
> HTTPS-Adresse, oder du testest lokal nur den Debug-Build.

---

## 2. Was der Test prüft und woran du Erfolg erkennst

`app/integration_test/app_test.dart` enthält sechs Fälle:

| Fall | Prüft | Erfolg |
|---|---|---|
| E-1 | App startet, Navigation steht | `NavigationBar` mit *Fahren*, *Warnen*, *Mehr* |
| E-2 | Geräte-Token von der echten API | `POST /v1/devices` liefert Token, `/v1/devices/me` einen Zugriffszustand |
| E-3 | Fahrtensuche | mindestens eine Direktverbindung mit Abfahrten |
| E-4 | Umstiege | `direct` leer, `transfers` nicht leer, **beide** Liniennummern gesetzt |
| E-5 | **Ticket-Gate** | *Warnen* ist ohne Bestätigung verschlossen und danach offen |
| E-6 | Meldung durchs Gate | Meldung erscheint in der Liste, mit Klarnamen der Haltestelle |

**Erfolg:** `flutter drive` endet mit `All tests passed!` und Exit-Code 0.
**Fehlschlag:** jeder andere Exit-Code. Die Ausgabe nennt den Fall und die Zeile.

E-5 ist der Fall, den du nicht übergehen darfst: er prüft das Ticket-First-Gate
(`docs/18` H1). Wird er rot, ist das ein Launch-Blocker, kein Testproblem.

**Ehrlich benannt:** Diese sechs Fälle sind in dieser Umgebung **nie gelaufen** —
weder auf einem Gerät noch im Browser über `flutter drive` (der Browserweg scheiterte
am 22.08.2026 daran, dass CanvasKit von `www.gstatic.com` geladen wird und der Proxy
das blockt). Sie sind statisch analysiert (`flutter analyze` ist grün) und gegen die
API-Vertragsdatei geschrieben, aber ihr Laufzeitverhalten ist unbelegt. Rechne damit,
dass beim ersten echten Lauf Kleinigkeiten klemmen — Wartezeiten, Textfindung, Reihenfolge.

---

## 3. Wenn es schiefgeht

### 3.1 Logs mitlesen

```bash
adb logcat -c                                  # Puffer leeren, DANN starten
adb logcat | grep -iE "flutter|AndroidRuntime|transitguard"
```

`AndroidRuntime: FATAL EXCEPTION` ist ein Absturz auf Java/Kotlin-Seite (Plugin, R8).
`[ERROR:flutter/...]` ist die Dart-Seite.

### 3.2 Typische Muster

| Symptom | Wahrscheinliche Ursache | Prüfen |
|---|---|---|
| `SocketException: Connection refused` bei jedem API-Aufruf | `adb reverse` fehlt oder API lauscht nur auf 127.0.0.1 | `adb shell curl …/health/ready` |
| `SocketException` **nur im Release-Build** | Klartext-HTTP im Release verboten | HTTPS verwenden (Abschnitt 4) |
| App startet, bleibt weiß | Dart-Ausnahme vor dem ersten Frame | `adb logcat` nach `[ERROR:flutter` |
| `MissingPluginException` | Plugin nicht registriert — fast immer nach unvollständigem Rebuild | `flutter clean && flutter pub get`, neu bauen |
| Standort-Aufruf hängt | Berechtigung nicht erteilt | App-Einstellungen → Berechtigungen → Standort |
| `NoSuchMethodError` / `ClassNotFoundException` **nur im Release** | R8 hat etwas entfernt | Abschnitt 4 |
| Test scheitert an `pumpAndSettle` | Skeleton-Animationen laufen endlos | die Tests takten bewusst mit `pump()`; nicht auf `pumpAndSettle` umstellen |

---

## 4. R8 — worauf du achten musst

R8 ist im Release **an** (`isMinifyEnabled = true`, `isShrinkResources = true` in
`app/android/app/build.gradle.kts`). Es gibt einen Notausgang für kleine Maschinen:

```bash
flutter build apk --release -PdisableMinify=true
```

**Diesen Schalter darfst du für den Abnahmetest nicht benutzen** — er baut ein anderes
Artefakt als das ausgelieferte. Er existiert nur, um Speicherprobleme beim Bauen von
R8-Problemen zu trennen.

### Was R8 kaputt machen kann

Dart-Code wird AOT kompiliert und ist von R8 nicht betroffen. Betroffen ist der
**Java/Kotlin-Anteil**: der Flutter-Embedder und die Plugins. R8 entfernt, was es für
unerreichbar hält — und Code, der nur über Reflexion oder über den Namen gefunden wird,
sieht für R8 unerreichbar aus.

Konkret in diesem Projekt betroffen: `geolocator` und `shared_preferences`. Beide bringen
eigene `consumer-rules.pro` mit; das reicht erfahrungsgemäß. Es gibt **keine eigene**
`proguard-rules.pro` in diesem Repo — bewusst, weil ohne belegtes Problem eine
Keep-Regel nur blinden Ballast bedeutet.

**Glaube ich:** Die Standardregeln reichen für diesen Stand.
**Weiß ich nicht:** ob das stimmt — genau das soll dieser Test klären.

### Wenn R8 zuschlägt

Symptom: Debug läuft, Release stürzt ab oder ein Feature tut nichts. Dann:

1. Gegenprobe, dass es wirklich R8 ist:
   ```bash
   flutter build apk --release -PdisableMinify=true
   adb install -r build/app/outputs/flutter-apk/app-release.apk
   ```
   Läuft es jetzt, ist R8 die Ursache. Läuft es weiterhin nicht, ist es das **nicht** —
   dann such nicht in den Keep-Regeln.
2. Erst dann eine gezielte Keep-Regel schreiben, für **genau** die Klasse aus dem
   Stacktrace, nicht pauschal:
   ```
   # app/android/app/proguard-rules.pro
   -keep class com.baseflow.geolocator.** { *; }
   ```
   und in `build.gradle.kts` im `release`-Block ergänzen:
   ```kotlin
   proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
   ```
3. Danach **ohne** `-PdisableMinify` neu bauen und erneut prüfen. Eine Keep-Regel, die
   nicht am kaputten Zustand geprüft wurde, ist geraten, nicht belegt.

### Stacktrace lesbar machen (`mapping.txt`)

R8 benennt Klassen und Methoden um. Ein Release-Stacktrace sieht dann so aus:

```
at a.b.c.d(SourceFile:1)
```

Die Zuordnungsdatei entsteht bei **jedem** Release-Build und wird beim nächsten
**überschrieben**:

```
app/build/app/outputs/mapping/release/mapping.txt
```

> **Sichere sie zu jedem Build, den du verteilst.** Ohne die passende `mapping.txt` ist
> ein Absturzbericht aus dem Feld nicht auswertbar — und sie gehört zum Build, nicht zum
> Quelltext: ein neuer Build erzeugt eine andere Zuordnung.

Zurückübersetzen mit dem `retrace`-Werkzeug aus den Android-Build-Tools:

```bash
# Stacktrace in datei.txt speichern (z. B. aus `adb logcat`), dann:
$ANDROID_HOME/cmdline-tools/latest/bin/retrace \
    app/build/app/outputs/mapping/release/mapping.txt datei.txt
```

Alternativ direkt aus der Zwischenablage:

```bash
adb logcat -d | $ANDROID_HOME/cmdline-tools/latest/bin/retrace \
    app/build/app/outputs/mapping/release/mapping.txt
```

Für die Play Console: `mapping.txt` beim Upload des AAB mit hochladen (Play akzeptiert
sie zum Bundle), dann sind die Absturzberichte dort direkt lesbar.

**Dart-Stacktraces sind eine andere Baustelle.** Sie werden nicht von R8 verschleiert,
sondern von der AOT-Kompilierung. Dafür brauchst du `--split-debug-info`:

```bash
flutter build appbundle --release \
  --obfuscate --split-debug-info=build/symbols
```
und `flutter symbolize -i stacktrace.txt -d build/symbols/app.android-arm64.symbols`.
**Der aktuelle Build nutzt `--obfuscate` NICHT** — Dart-Stacktraces sind also lesbar, und
`--split-debug-info` ist heute nicht nötig.

---

## 5. Klartext-HTTP: warum der Release-Build lokal nicht funktioniert

Android blockiert seit API 28 unverschlüsseltes HTTP vollständig — auch zu `127.0.0.1`,
auch über `adb reverse`. Deshalb liegt seit dem 24.08.2026 eine Netzwerkkonfiguration
**nur** im Debug-Zweig:

```
app/android/app/src/debug/res/xml/network_security_config.xml
app/android/app/src/debug/AndroidManifest.xml   (verweist darauf)
```

Der Release-Build bekommt sie nicht. `T-ANDROID-1` sichert genau das ab und wird rot,
sobald jemand `usesCleartextTraffic` oder die Konfiguration nach `src/main/` schiebt
(beide Gegenproben am 24.08.2026 durchgeführt).

Folge für dich: **Debug-Build gegen `http://127.0.0.1:5099`, Release-Build nur gegen HTTPS.**
Willst du den Release-Build lokal prüfen, brauchst du eine HTTPS-Adresse — z. B. Caddy
mit lokalem Zertifikat vor der API (`deploy/Caddyfile` als Vorlage).

---

## 6. Wenn du fertig bist

Bitte notiere in `docs/27-build-log.md` (oder als Antwort), was tatsächlich lief:

- Gerät (Modell, Android-Version), Flutter-Version aus `flutter doctor -v`
- Ergebnis von `flutter drive` (Exit-Code, welcher Fall ggf. rot)
- ob der **Release-APK** startet und die drei Ziele bedienbar sind
- ob R8 Probleme gemacht hat, und wenn ja: welche Klasse im Stacktrace stand

Erst danach darf in `CLAUDE.md` aus „Android/R8 **unbelegt**" etwas anderes werden.
