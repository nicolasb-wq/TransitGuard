// ---------------------------------------------------------------------------
// T-ANDROID — Statische Pruefungen der Android-Konfiguration.
//
// Diese Tests ersetzen KEINEN Geraetetest. Sie pruefen genau das, was ohne Geraet
// pruefbar ist: dass die Konfiguration den Geraetetest ueberhaupt zulaesst und dass
// die Debug-Lockerungen nicht in den Release-Build sickern.
// Was sie NICHT belegen: Laufzeitverhalten unter R8, Plugin-Kanaele, Rendering.
// Dafuer gibt es docs/29-android-geraetetest.md — auszufuehren auf einer Maschine
// mit echtem Geraet.
// ---------------------------------------------------------------------------
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';

File _datei(String relativ) {
  var d = Directory.current;
  while (!File('${d.path}/pubspec.yaml').existsSync() && d.parent.path != d.path) {
    d = d.parent;
  }
  return File('${d.path}/$relativ');
}

void main() {
  group('T-ANDROID: Konfiguration', () {
    test('T-ANDROID-1: Klartext-HTTP ist NUR im Debug-Build erlaubt', () {
      final debugManifest = _datei('android/app/src/debug/AndroidManifest.xml');
      final debugConfig = _datei('android/app/src/debug/res/xml/network_security_config.xml');
      final mainManifest = _datei('android/app/src/main/AndroidManifest.xml');

      expect(debugConfig.existsSync(), isTrue,
          reason: 'Ohne Debug-Netzwerkkonfiguration scheitert jeder Geraetetest gegen eine '
              'lokale API — Android blockiert Klartext seit API 28.');
      expect(debugManifest.readAsStringSync(), contains('networkSecurityConfig'));

      final main = mainManifest.readAsStringSync();
      expect(main.contains('usesCleartextTraffic'), isFalse,
          reason: 'Der Release-Build darf Klartext NIE erlauben.');
      expect(main.contains('networkSecurityConfig'), isFalse,
          reason: 'Die Debug-Lockerung darf nicht in src/main/ stehen.');
      expect(_datei('android/app/src/main/res/xml/network_security_config.xml').existsSync(), isFalse);
    });

    test('T-ANDROID-2: R8 und Ressourcen-Shrinker sind im Release an', () {
      final gradle = _datei('android/app/build.gradle.kts').readAsStringSync();
      expect(gradle, contains('isMinifyEnabled'));
      expect(gradle, contains('isShrinkResources'));
      // Der Notausgang -PdisableMinify existiert bewusst, darf aber nicht der Normalfall sein.
      expect(gradle, contains('disableMinify'));
      expect(RegExp(r'isMinifyEnabled\s*=\s*false').hasMatch(gradle), isFalse,
          reason: 'R8 darf nicht fest abgeschaltet sein.');
    });

    test('T-ANDROID-3: Standort nur im Vordergrund (ADR-0005)', () {
      final main = _datei('android/app/src/main/AndroidManifest.xml').readAsStringSync();
      expect(main, contains('ACCESS_COARSE_LOCATION'));
      expect(main.contains('ACCESS_BACKGROUND_LOCATION'), isFalse,
          reason: 'Hintergrund-Standort waere eine Geraeteverkettung (ADR-0005).');
      // Keine Berechtigung, die eine Verkettung ermoeglicht.
      for (final verboten in const [
        'READ_PHONE_STATE', 'GET_ACCOUNTS', 'READ_CONTACTS', 'AD_ID',
      ]) {
        expect(main.contains(verboten), isFalse, reason: '$verboten widerspricht ADR-0005.');
      }
    });

    test('T-ANDROID-4: Signierschluessel liegt nicht im Repo', () {
      // key.properties und der Keystore sind sandbox-erzeugt und ignoriert;
      // Produktion braucht einen eigenen Schluessel + Play App Signing.
      final ignore = _datei('../.gitignore').existsSync()
          ? _datei('../.gitignore').readAsStringSync()
          : _datei('.gitignore').readAsStringSync();
      expect(ignore, contains('key.properties'));
      expect(ignore, contains('upload-keystore.jks'));
    });
  });
}
