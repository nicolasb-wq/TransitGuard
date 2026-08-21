import 'package:flutter_test/flutter_test.dart';
import 'package:transitguard/api.dart';

/// Läuft mit `flutter test` auf der Dev-Maschine (Sandbox hat kein Flutter-SDK —
/// bewusst keine Fake-Behauptung; Struktur- und Logik-Tests sind deterministisch ohne Netz).
void main() {
  group('DTO-Parsing (Vertrag snake_case, docs/04)', () {
    test('Stop.fromJson versteht nearby-Antwort', () {
      final s = Stop.fromJson({
        'stop_id': 'HHA1',
        'stop_name': 'Jungfernstieg',
        'distance_km': 0.123,
      });
      expect(s.stopId, 'HHA1');
      expect(s.stopName, 'Jungfernstieg');
      expect(s.distanceKm, 0.123);
    });

    test('Departure.fromJson inkl. Warnung (>=1 Haltestelle vorher)', () {
      final d = Departure.fromJson({
        'trip_ref': {'trip_id': 'T_HH_3', 'start_date': '20260821'},
        'route_id': 'R_U1',
        'headsign': 'Ohlsdorf',
        'scheduled_time': '2026-08-21T20:50:00Z',
        'estimated_time': '2026-08-21T20:52:00Z',
        'delay_s': 120,
        'realtime': true,
        'warnings': [
          {
            'affected_stop_id': 'HHA2',
            'affected_stop_sequence': 2,
            'user_eta_seconds': 600,
            'message': 'Achtung Kontrolle – halte deine Fahrkarte bereit'
          }
        ],
      });
      expect(d.tripId, 'T_HH_3');
      expect(d.delayS, 120);
      expect(d.realtime, isTrue);
      final w = d.warnings!.single;
      expect(w.affectedStopId, 'HHA2');
      expect(w.message, contains('halte deine Fahrkarte bereit'));
    });

    test('Me.fromJson: Trial-Stand', () {
      final m = Me.fromJson({'access': 'trial', 'trial_days_remaining': 14});
      expect(m.access, 'trial');
      expect(m.trialDaysRemaining, 14);
    });
  });

  test('Preiskonstante & Trial-Länge passen zu ADR-0014', () {
    expect(kPriceEur, 2.99);
    expect(kTrialDays, 14);
  });
}
