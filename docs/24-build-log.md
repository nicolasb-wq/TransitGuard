# TransitGuard — Build-Log 6 (21.08.2026, 6. Bausession): Flutter KOMPILIERT + APK gebaut

**Auftrag:** „Flutter auf der Dev-Maschine kompilieren und in den Play-Track bringen; alles durchgehen, Fehler suchen/beheben." Dev-Maschine = diese Sandbox: Flutter SDK 3.47.1 (Dart 3.13.1) + OpenJDK 17 (Micromamba) + Android SDK 36 wurden real installiert (unter ~/.cache — turn-lokal).

## 1. Ergebnisse
| Schritt | Beweis |
|---|---|
| `flutter pub get` | ✅ (pub.dev-Zugriff real) |
| `flutter analyze` | ✅ **0 Issues** (nach 3 Fixes, s. §2) |
| `flutter test` | ✅ **5/5** — inkl. Widget-Test, der HomeScreen mit Ticket-Gate, Attribution, Ziel-Suche wirklich rendert |
| `flutter build apk` | ✅ **app/transitguard-debug.apk** (80 MB, fat) — apksigner-verifiziert (Debug-Cert), Badging: `de.transitguard.transitguard` v0.1.0, Label „TransitGuard", genau INTERNET + COARSE/FINE_LOCATION (**kein** Hintergrund-Standort, ADR-0005) |
| Release-Bau (R8) | ❌ OOM in 1,5-GB-Sandbox (Gradle-Daemon gekillt, 2 Versuche mit -Xmx600–900m, workers=1, in-process) → **Release-APK/AAB gehört auf die echte Dev-Maschine** (≥4 GB RAM); Befehl steht in PLAY-CHECKLISTE |

## 2. Fehler gefunden & behoben (Sweep wie befohlen)
| # | Fund | Fix |
|---|---|---|
| 1 | `catchError((_) {})` auf `Future<String>` — Analyze-WARNING (Rückgabewert) | `(Object _) => ''` |
| 2 | `const ColorScheme.fromSeed(...)` — fromSeed ist Factory, kein const (mein Fix-Versuch erzeugte erst den Fehler — selbst gefangen) | `const Color(…)` nur innen |
| 3 | **`Departure.fromJson` verschluckte `warnings`** — Test deckte auf, dass der Vertrag das Feld im journey-Endpoint liefert, der Parser es aber ignorierte | mit Parse + Cascade `..warnings=` |
| 4 | `flutter create`-Default-Test referenzierte nicht existierendes `MyApp` | echter Widget-Test ersetzt |
| 5 | Mein vorab geschriebenes android/ ⇒ „deleted Android v1 embedding" | Plattformverzeichnis frisch generieren + Berechtigungen in neues Manifest übertragen (sauberer als Mischen) |
| 6 | Gradle OOM (Release) | Tuning dokumentiert (jvmargs/workers/in-process); Grenze ehrlich benannt |

## 3. Play-Track-Stand
Debug-APK = installierbarer Interner-Test-Build. **Vor Play-Produktiv bleibt die Torgate-Reihenfolge aus PLAY-CHECKLISTE.md:** Rechts-Gate (F-1) → Release-AAB auf echter Dev-Maschine (`flutter build appbundle --release --dart-define=API_BASE=…`, Signierung via Play App Signing) → Interner Test → Geschlossener Track → Produktion. Backend unverändert (73/73 aus Session 5, kein C#-Code berührt).

## 4. Hinweis Ressourcen
APK liegt als `app/transitguard-debug.apk` im Repo (~80 MB; Gesamt-Snapshot ~105 MB < 128 MB Cap). Für schlankere Artefakte: `flutter build apk --split-per-abi` (arm64 ~30 MB).
