# Play-Store-Einreichung — Checkliste (Flutter-Kanal)

**Vorbedingungen (Reihenfolge ist Vertragslage, nicht Geschmack):** T7.4 Rechts-Gate ✅ (Anwalt F-1, AGB/Impressum/Charta live) · PWA läuft stabil im Betatest · `app/` kompiliert (`flutter build apk --release --dart-define=API_BASE=https://api.example.de`).

## Store-Metadaten (F-10)
- [ ] Titel: „TransitGuard — ÖPNV Hamburg" · Kategorie: Reisen · Altersfreigabe: USK/PEGI „Alle" (IAB: nicht deklariert)
- [ ] Beschreibung: Fahrplan/Live-Abfahrte/Störungen/Barrierefreiheit VORN; Kontroll-Feature als EIN Punkt unter mehreren (Nie-Liste 11-recht §3 gilt!)
- [ ] Screenshots: Abfahrten, Fahrtensuche, Störungen — NICHT die Warnungs-Banner als Held
- [ ] Data-Safety-Formular: Standort „Nur in der App, nicht gesammelt" · Keine Daten geteilt · Keine Werbung
- [ ] Review-Notes: Dossier aus docs/store/review-notes (bei M9 erstellt): Companion-Zweck, Ticket-Kauf-CTA, Kill-Switch, Ticket-Gate (18-rechtliche-haertung)

## Build & Release
- [x] Basis erfüllt (Session 6): analyze 0, test 5/5, Debug-APK verifiziert · [ ] Version bumpen · [ ] Release-AAB auf Dev-Maschine ≥4 GB RAM: `flutter build appbundle --release --dart-define=API_BASE=https://api.example.de` — Upload-Key liegt bei (android/app/upload-keystore.jks + key.properties, sandbox-generiert; besser eigener) und in build.gradle.kts die 2 minify-Deaktivierungen entfernen (25-build-log)
- [ ] Signiertes Release-Bundle (Play App Signing) · internes Testing → geschlossener Track (20 Tester, 3 Tage) → Produktiv
- [ ] Server-Rollout vor Store-Rollout: API zuerst (Abwärtskompatibilität /v1 additiv — 04-api §5)

## Nach dem Rollout
- [ ] store-spezifische Metriken: Crashes (Play Vitals), 402-Rate (trial_expired) im Ops-Dashboard gegen Ökonomie-Modell (08 §5) — Nachjustierung Preis/Trial-Länge nur nach Datenlage (ADR-0014 §3)
