import 'package:flutter_test/flutter_test.dart';
import 'package:transitguard/main.dart';

void main() {
  testWidgets('HomeScreen zeigt Ticket-Gate und Attribution (Companion-Vertrag)',
      (tester) async {
    await tester.pumpWidget(const TransitGuardApp());
    await tester.pump();
    expect(find.textContaining('gültigem Ticket'), findsOneWidget);          // Ticket-First-Gate (H1)
    expect(find.text('✓ Ja, gültiges Ticket'), findsOneWidget);
    expect(find.textContaining('gtfs.de / DELFI'), findsOneWidget);           // Attribution (Rechtsteil §1)
    expect(find.textContaining('Ziel: Haltestelle'), findsOneWidget);         // Fahrtensuche vorhanden
  });
}
