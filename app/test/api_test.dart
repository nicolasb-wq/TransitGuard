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

    // T-DTO-4: Vertrags-Asymmetrie, gemessen am 21.08.2026 gegen eine laufende
    // API. POST /v1/journeys/search liefert route_id/headsign NICHT auf der
    // Abfahrt (dort stehen sie auf der Verbindung) — GET /v1/stops/{id}/departures
    // dagegen schon. Vorher war routeId nicht-nullable: jede Fahrtensuche warf
    // einen TypeError, den _findLine nicht faengt (nur ApiException).
    test('Departure.fromJson ohne route_id/headsign (Antwort der Fahrtensuche)', () {
      final d = Departure.fromJson({
        'trip_ref': {'trip_id': 'T_HH_3', 'start_date': '20260821'},
        'scheduled_time': '2026-08-21T20:50:00Z',
        'realtime': false,
        'warnings': const [],
      });
      expect(d.tripId, 'T_HH_3');
      expect(d.routeId, isNull);
      expect(d.headsign, isNull);
      expect(d.delayS, isNull);
      expect(d.estimated, isNull);
      expect(d.realtime, isFalse);
    });

    test('Me.fromJson: Trial-Stand', () {
      final m = Me.fromJson({'access': 'trial', 'trial_days_remaining': 14});
      expect(m.access, 'trial');
      expect(m.trialDaysRemaining, 14);
    });
  });

  group('Idempotency-Key (docs/04 §4 — Server antwortet ohne ihn 422)', () {
    test('hat UUID-v4-Form', () {
      final k = newIdempotencyKey();
      expect(
        RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')
            .hasMatch(k),
        isTrue,
        reason: 'unerwartete Form: $k',
      );
    });

    test('ist je Aufruf verschieden', () {
      final keys = {for (var i = 0; i < 500; i++) newIdempotencyKey()};
      expect(keys.length, 500);
    });
  });

  test('Preiskonstante & Trial-Länge passen zu ADR-0014', () {
    expect(kPriceEur, 2.99);
    expect(kTrialDays, 14);
  });
}
