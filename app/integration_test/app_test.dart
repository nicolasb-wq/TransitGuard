// ---------------------------------------------------------------------------
// E — Laufzeittest der ECHTEN App.
//
// Startet main() (nicht einen nachgebauten Widgetbaum) in einem echten Browser
// und bedient die Oberfläche gegen eine LAUFENDE API. Geprüft werden Start,
// Fahrtensuche und ein Meldevorgang samt Ticket-Gate.
//
// Ehrliche Grenze: Das ist das WEB-Ziel. Es beweist, dass App-Code, Plugins
// (geolocator, shared_preferences) und HTTP-Kette zur Laufzeit zusammenspielen —
// es beweist NICHT das Verhalten des Android-Release-Artefakts unter R8.
// Dafür bräuchte es ein Gerät oder einen Emulator; im Container gibt es weder
// /dev/kvm noch CPU-Virtualisierungsflags.
//
// Nutzung (API und App müssen unter EINEM Origin liegen — siehe scripts/flutter-e2e.sh):
//   flutter drive --driver=test_driver/integration_test.dart \
//     --target=integration_test/app_test.dart -d chrome --browser-name=chrome
// ---------------------------------------------------------------------------
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:transitguard/api.dart';
import 'package:transitguard/main.dart' as app;

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  /// Pumpen mit fester Taktung: Skeletons animieren endlos, pumpAndSettle
  /// kehrte nie zurück (gleiche Falle wie in den Widget-Tests, 26-build-log).
  Future<void> takte(WidgetTester t, {int schritte = 24}) async {
    for (var i = 0; i < schritte; i++) {
      await t.pump(const Duration(milliseconds: 120));
    }
  }

  testWidgets('E-1: App startet und zeigt die drei Ziele', (t) async {
    SharedPreferences.setMockInitialValues({});
    app.main();
    await takte(t);

    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.text('Fahren'), findsOneWidget);
    expect(find.text('Warnen'), findsOneWidget);
    expect(find.text('Mehr'), findsOneWidget);
  });

  testWidgets('E-2: Geräte-Token wird von der echten API geholt', (t) async {
    SharedPreferences.setMockInitialValues({});
    app.main();
    await takte(t);

    // Kein Mock: das geht wirklich über HTTP an die laufende API.
    final token = await Api.ensureDeviceToken();
    expect(token, isNotEmpty);

    final me = await Api.me();
    expect(['trial', 'subscriber', 'locked'], contains(me.access));
  });

  testWidgets('E-3: Fahrtensuche liefert eine Verbindung', (t) async {
    SharedPreferences.setMockInitialValues({});
    app.main();
    await takte(t);

    // Über die echte API — Haltestellen kommen aus der Fixture des Servers.
    final ziele = await Api.searchStops('Kelling');
    expect(ziele, isNotEmpty);

    final ergebnis = await Api.journeySearch('HHA1', ziele.first.stopId);
    expect(ergebnis.direct.any((c) => c.next.isNotEmpty), isTrue,
        reason: 'Keine Direktverbindung HHA1 → ${ziele.first.stopId}');
  });

  testWidgets('E-4: Umstiegsverbindung kommt in der App an', (t) async {
    SharedPreferences.setMockInitialValues({});
    app.main();
    await takte(t);

    final ergebnis = await Api.journeySearch('HHA1', 'HHA5');
    expect(ergebnis.direct, isEmpty);
    expect(ergebnis.transfers, isNotEmpty);
    final u = ergebnis.transfers.first;
    expect(u.legA.routeId, isNotEmpty);
    expect(u.legB.routeId, isNotEmpty);
    expect(u.legA.routeId, isNot(u.legB.routeId));
  });

  testWidgets('E-5: Ticket-Gate sperrt den Warnen-Tab und gibt ihn danach frei', (t) async {
    SharedPreferences.setMockInitialValues({});
    app.main();
    await takte(t);

    await t.tap(find.text('Warnen'));
    await takte(t, schritte: 8);
    expect(find.widgetWithText(FilledButton, 'Ticket bestätigen'), findsOneWidget,
        reason: 'Der Kontroll-Teil muss ohne Bestätigung verschlossen sein (docs/18 H1).');

    await t.tap(find.widgetWithText(FilledButton, 'Ticket bestätigen'));
    await takte(t, schritte: 8);
    await t.tap(find.widgetWithText(FilledButton, 'Ja, ich habe ein gültiges Ticket'));
    await takte(t, schritte: 12);

    expect(find.widgetWithText(FilledButton, 'Ticket bestätigen'), findsNothing);
  });

  testWidgets('E-6: Meldung geht über die echte API durch', (t) async {
    SharedPreferences.setMockInitialValues({});
    app.main();
    await takte(t);
    await TicketGate.confirm();

    // Der Weg, der bis 22.08.2026 IMMER mit 422 scheiterte (fehlender
    // Idempotency-Key) — hier ohne Mock, gegen den echten Server.
    await Api.reportAtStation(stopId: 'HHA1', ticketConfirmed: true);

    final liste = await Api.cityReports();
    expect(liste, isNotEmpty);
    expect(liste.any((r) => r.stationName == 'Jungfernstieg'), isTrue,
        reason: 'Die eigene Meldung muss in der Liste auftauchen — mit Klarnamen.');
  });
}
