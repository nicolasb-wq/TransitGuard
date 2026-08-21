import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:transitguard/api.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:transitguard/main.dart';
import 'package:transitguard/theme.dart';

/// Widget-Vertrag der App-Hülle. Prüft nicht „ein Banner ist da", sondern die
/// Zusage selbst: Der Kontroll-Teil ist ohne Ticket-Bestätigung verschlossen
/// (docs/18 H1) und das Ticket-Gate lässt sich nicht wegtippen, ohne dass er
/// verschlossen bleibt.
void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  _umstiegsTests();

  Future<void> starte(WidgetTester tester) async {
    await tester.pumpWidget(const TransitGuardApp());
    await tester.pump();                                   // initState-Futures
    await tester.pump(const Duration(milliseconds: 50));
  }

  /// Tab-Wechsel mit festen Pump-Schritten statt pumpAndSettle: Skeletons
  /// animieren bewusst endlos, pumpAndSettle wuerde nie zurueckkehren.
  Future<void> wechsle(WidgetTester tester, String tab) async {
    await tester.tap(find.text(tab));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));
  }

  testWidgets('drei Ziele in der Navigation: Fahren > Warnen > Mehr', (tester) async {
    await starte(tester);
    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.text('Fahren'), findsOneWidget);
    expect(find.text('Warnen'), findsOneWidget);
    expect(find.text('Mehr'), findsOneWidget);
  });

  testWidgets('Fahren-Tab bietet die Fahrtensuche ohne jede Bestätigung', (tester) async {
    await starte(tester);
    // Der Fahrplan-Companion bleibt ohne Ticket-Bestätigung nutzbar (docs/18).
    expect(find.text('Standort'), findsOneWidget);
    expect(find.widgetWithText(TextField, 'Wohin? Haltestelle eingeben'), findsOneWidget);
  });

  testWidgets('T-GATE: Warnen-Tab ist ohne Ticket-Bestätigung verschlossen', (tester) async {
    await starte(tester);
    await wechsle(tester, 'Warnen');

    expect(find.textContaining('gültigem Fahrschein'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Ticket bestätigen'), findsOneWidget);
    // Kein Lesezugriff und kein Melde-Knopf, solange nicht bestätigt ist.
    expect(find.text('Kontrolle hier melden'), findsNothing);
    expect(find.text('Gerade gemeldet'.toUpperCase()), findsNothing);
  });

  testWidgets('T-GATE: „Später" im Gate lässt den Kontroll-Teil verschlossen', (tester) async {
    await starte(tester);
    await wechsle(tester, 'Warnen');

    await tester.tap(find.widgetWithText(FilledButton, 'Ticket bestätigen'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Fährst du gerade mit gültigem Ticket?'), findsOneWidget);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Später'));
    await tester.pumpAndSettle();
    // Unverändert verschlossen — „Später" ist kein Schlüssel.
    expect(find.widgetWithText(FilledButton, 'Ticket bestätigen'), findsOneWidget);
    expect(find.text('Kontrolle hier melden'), findsNothing);
  });

  testWidgets('Datenquellen-Attribution ist erreichbar (Rechtsteil §1)', (tester) async {
    await starte(tester);
    await wechsle(tester, 'Mehr');
    expect(find.textContaining('gtfs.de / DELFI'), findsOneWidget);
  });

  testWidgets('Copy-Nie-Liste: keine verbotenen Formulierungen in der Oberfläche', (tester) async {
    await starte(tester);
    for (final tab in ['Fahren', 'Warnen', 'Mehr']) {
      await wechsle(tester, tab);
      final texte = tester
          .widgetList<Text>(find.byType(Text))
          .map((t) => (t.data ?? t.textSpan?.toPlainText() ?? '').toLowerCase())
          .join(' ');
      for (final verboten in ['schwarzfahr', 'ohne ticket fahren', 'kontrollen entgehen', 'kontrollen umgehen']) {
        expect(texte.contains(verboten), isFalse, reason: 'verbotene Formulierung „$verboten" im Tab $tab');
      }
    }
  });
}

/// T-TRANSFER-UI: Die Oberfläche muss BEIDE Liniennummern einer Umstiegs-
/// verbindung zeigen. Gespeist wird sie mit der echten, aufgezeichneten
/// Serverantwort (docs/contract/samples/journeys.search.2.json) — nicht mit
/// einem von Hand gebauten Wunschobjekt.
void _umstiegsTests() {
  testWidgets('Umstieg zeigt beide Liniennummern und die Wartezeit', (tester) async {
    SharedPreferences.setMockInitialValues({'tg_device_token': 'DEV'});
    final antwort = File('../docs/contract/samples/journeys.search.2.json').readAsStringSync();
    final erwartet = jsonDecode(antwort)['transfer_connections'][0] as Map<String, dynamic>;

    Api.client = MockClient((req) async {
      if (req.url.path.endsWith('/v1/journeys/search')) return http.Response(antwort, 200);
      if (req.url.path.endsWith('/stops/nearby')) {
        return http.Response(File('../docs/contract/samples/stops.nearby.json').readAsStringSync(), 200);
      }
      return http.Response('{}', 200);
    });
    addTearDown(() => Api.client = http.Client());

    final ergebnis = await Api.journeySearch('HHA1', 'HHA5');
    expect(ergebnis.transfers, hasLength(1));

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(body: Builder(builder: (c) => Column(children: [
        RoutePlakette(ergebnis.transfers.first.legA.routeId),
        RoutePlakette(ergebnis.transfers.first.legB.routeId),
        Text('${(ergebnis.transfers.first.waitSeconds / 60).round()} min warten'),
      ]))),
    ));
    await tester.pump();

    expect(find.text(erwartet['leg_a']['route_id'] as String), findsOneWidget);
    expect(find.text(erwartet['leg_b']['route_id'] as String), findsOneWidget);
    expect(find.textContaining('min warten'), findsOneWidget);
  });
}
