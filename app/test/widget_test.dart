import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:transitguard/main.dart';

/// Widget-Vertrag der App-Hülle. Prüft nicht „ein Banner ist da", sondern die
/// Zusage selbst: Der Kontroll-Teil ist ohne Ticket-Bestätigung verschlossen
/// (docs/18 H1) und das Ticket-Gate lässt sich nicht wegtippen, ohne dass er
/// verschlossen bleibt.
void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

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
