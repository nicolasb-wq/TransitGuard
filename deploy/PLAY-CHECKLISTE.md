# Play-Store-Einreichung — Checkliste (Flutter-Kanal)

**Vorbedingungen (Reihenfolge ist Vertragslage, nicht Geschmack):** T7.4 Rechts-Gate ✅ (Anwalt F-1, AGB/Impressum/Charta live) · PWA läuft stabil im Betatest · `app/` kompiliert (`flutter build apk --release --dart-define=API_BASE=https://api.example.de`).

## Store-Metadaten (F-10)
- [ ] Titel: „TransitGuard — ÖPNV Hamburg" · Kategorie: Reisen · Altersfreigabe: USK/PEGI „Alle" (IAB: nicht deklariert)
- [ ] Beschreibung: Fahrplan/Live-Abfahrte/Störungen/Barrierefreiheit VORN; Kontroll-Feature als EIN Punkt unter mehreren (Nie-Liste 11-recht §3 gilt!)
- [ ] Screenshots: Abfahrten, Fahrtensuche, Störungen — NICHT die Warnungs-Banner als Held
- [ ] Data-Safety-Formular: Standort „Nur in der App, nicht gesammelt" · Keine Daten geteilt · Keine Werbung
- [ ] Review-Notes: Dossier aus docs/store/review-notes (bei M9 erstellt): Companion-Zweck, Ticket-Kauf-CTA, Kill-Switch, Ticket-Gate (18-rechtliche-haertung)

## Build & Release
- [x] Basis erfüllt: `flutter analyze` 0 Issues, `flutter test` **13/13** (Session 8), Debug-APK verifiziert
- [x] **R8/Shrinker sind wieder AN** (`app/android/app/build.gradle.kts`) — die frühere Deaktivierung war eine 1,5-GB-Sandbox-Notlösung, keine Produktentscheidung. Am 21.08.2026 auf einer 16-GB-Maschine gebaut: **AAB 50,4 MB**, Gradle-Task 325 s, kein OOM. Auf sehr kleinen Maschinen notfalls `-PdisableMinify=true`.
- [x] **R8-Rückbau geprüft:** `mapping.txt` belegt, dass `FlutterActivity`, `GeolocatorPlugin`, `SharedPreferencesPlugin` erhalten (nur umbenannt) sind und `MainActivity` seinen Namen behält. 1.418 Klassen im Mapping. Kein Keep-Regelbedarf gefunden.
- [ ] Version bumpen (`pubspec.yaml`: `version: 0.1.0+1` → nächster Stand; `versionCode` muss je Upload steigen)
- [ ] **Release-AAB mit API-Ziel bauen — der `--dart-define` ist Pflicht:**
      `flutter build appbundle --release --dart-define=API_BASE=https://api.example.de`
      `API_BASE` ist eine **Compile-Zeit**-Konstante (`String.fromEnvironment`). Ohne den Schalter
      landet der Debug-Default `http://10.0.2.2:5099` (Emulator-Loopback) im Bundle und die App
      erreicht in Nutzerhand **keinen** Server. Gegenprobe vor dem Upload — die Konstante liegt im
      **Dart-AOT-Code**, nicht im Java-Dex (dort steht sie nie, das führt beim Prüfen in die Irre):
      ```
      unzip -p <aab> base/lib/arm64-v8a/libapp.so | strings | grep -c 'api\.example\.de'   # > 0
      unzip -p <aab> base/lib/arm64-v8a/libapp.so | strings | grep -c '10\.0\.2\.2'        # muss 0 sein
      ```
      Am 21.08.2026 so gemessen: 2 Treffer für die Produktions-URL, 0 für den Emulator-Default.
- [ ] **Eigenen Upload-Key erzeugen.** Der beiliegende `android/app/upload-keystore.jks` ist
      sandbox-generiert und liegt bewusst **nicht** in Git (`.gitignore`). Für Produktion:
      neuer Key + **Play App Signing**. Ein mit dem Sandbox-Key signiertes Bundle darf nicht hoch.
- [ ] **`mapping.txt` mit hochladen** (`app/build/app/outputs/mapping/release/mapping.txt`, ~7,7 MB).
      Ohne sie sind Play-Vitals-Abstürze durch R8 unlesbar — und das fällt erst auf, wenn der erste
      echte Absturz analysiert werden soll.
- [ ] Signiertes Release-Bundle (Play App Signing) · internes Testing → geschlossener Track (20 Tester, 3 Tage) → Produktiv
- [ ] Server-Rollout vor Store-Rollout: API zuerst (Abwärtskompatibilität /v1 additiv — 04-api §5)

## Nach dem Rollout
- [ ] store-spezifische Metriken: Crashes (Play Vitals), 402-Rate (trial_expired) im Ops-Dashboard gegen Ökonomie-Modell (08 §5) — Nachjustierung Preis/Trial-Länge nur nach Datenlage (ADR-0014 §3)
