import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:transitguard/api.dart';
import 'package:transitguard/contract.gen.dart';

/// Vertragstests der Flutter-App.
///
/// Grundlage ist der aus der LAUFENDEN API aufgezeichnete Vertrag
/// (docs/contract/). Drei der vier Launch-Blocker vom 21.08.2026 waren
/// Vertragsdrift; diese Tests decken die Fehlerklasse ab statt der Einzelfälle:
///   * Antwortseite: die echten Beispielantworten laufen durch die echten Parser
///   * Anfrageseite: ein MockClient prüft, dass die App die Pflicht-Header sendet
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  /// Beispiel aus docs/contract/samples/ laden. `flutter test` läuft auf der VM,
  /// dart:io ist verfügbar; das Arbeitsverzeichnis ist app/.
  dynamic beispiel(String schluessel) {
    final f = File('../docs/contract/samples/$schluessel.json');
    expect(f.existsSync(), isTrue,
        reason: 'Beispiel $schluessel fehlt — verifikation/contract_capture.mjs laufen lassen.');
    return jsonDecode(f.readAsStringSync());
  }

  group('Antwortseite — echte Serverantworten durch die echten Parser', () {
    test('stops.nearby: Stop.fromJson', () {
      final liste = beispiel('stops.nearby') as List;
      expect(liste, isNotEmpty);
      final stops = liste.map((e) => Stop.fromJson(e as Map<String, dynamic>)).toList();
      expect(stops.first.stopId, isNotEmpty);
      expect(stops.first.stopName, isNotEmpty);
    });

    test('stops.nearby ohne Standort: distance_km fehlt und darf fehlen', () {
      // Der Vertrag führt distance_km als optional (Namenssuche liefert es nicht).
      expect(kVertragOptionaleFelder['stops.nearby'], contains('[].distance_km'));
      final s = Stop.fromJson({'stop_id': 'HHA1', 'stop_name': 'Jungfernstieg'});
      expect(s.distanceKm, isNull);
    });

    test('stops.departures: Departure.fromJson MIT route_id', () {
      final liste = beispiel('stops.departures') as List;
      expect(liste, isNotEmpty);
      final d = Departure.fromJson(liste.first as Map<String, dynamic>);
      expect(d.tripId, isNotEmpty);
      expect(d.routeId, isNotNull, reason: 'Die Abfahrtstafel liefert route_id mit.');
    });

    test('journeys.search: Abfahrten OHNE route_id — Blocker 2', () {
      // Genau die Asymmetrie, an der die Fahrtensuche einen TypeError warf.
      final j = beispiel('journeys.search') as Map<String, dynamic>;
      final verbindungen = (j['direct_connections'] as List);
      for (final c in verbindungen) {
        final m = c as Map<String, dynamic>;
        for (final d in (m['next_departures'] as List)) {
          final dep = (d as Map<String, dynamic>);
          expect(dep.containsKey('route_id'), isFalse,
              reason: 'Der Vertrag sagt: route_id steht auf der VERBINDUNG, nicht auf der Abfahrt.');
          // Muss ohne Ausnahme parsen — und die Linie von der Verbindung erben.
          final parsed = Departure.fromJson(dep, routeId: m['route_id'] as String?);
          expect(parsed.routeId, m['route_id']);
        }
      }
    });

    test('journeys.search: Umstiegsbeine tragen route_id in snake_case — Blocker 1', () {
      // Beobachtung 2 ist die Umstiegssuche (HHA1 → HHA5); Beobachtung 1 ist die
      // Direktsuche und hat naturgemaess keine transfer_connections.
      final j = beispiel('journeys.search.2') as Map<String, dynamic>;
      final umstiege = j['transfer_connections'] as List;
      expect(umstiege, isNotEmpty,
          reason: 'Die Fixture muss eine Umstiegsverbindung erzeugen (build_static_mini.py).');
      for (final u in umstiege) {
        for (final bein in ['leg_a', 'leg_b']) {
          final l = (u as Map<String, dynamic>)[bein] as Map<String, dynamic>;
          expect(l['route_id'], isA<String>());
          expect(l.containsKey('RouteId'), isFalse, reason: 'PascalCase kommt im Vertrag nicht vor.');
        }
      }
    });

    test('reports.list: ReportView.fromJson für beide Ankerarten', () {
      final liste = beispiel('reports.list') as List;
      expect(liste, isNotEmpty);
      final views = liste.map((e) => ReportView.fromJson(e as Map<String, dynamic>)).toList();
      expect(views.every((v) => v.id.isNotEmpty), isTrue);
      // route_id/headsign sind laut Vertrag optional (nur bei Trip-Anker).
      expect(kVertragOptionaleFelder['reports.list'], contains('[].route_id'));
    });

    test('devices.me: Me.fromJson', () {
      final m = Me.fromJson(beispiel('devices.me') as Map<String, dynamic>);
      expect(['trial', 'subscriber', 'locked'], contains(m.access));
    });

    test('Der Vertrag weist Beobachtungsluecken aus, statt sie zu verschweigen', () {
      // Am 24.08.2026 wurden die letzten drei Luecken geschlossen: cities.alerts
      // (der Poll-Job schrieb die Alerts nirgendwo hin), journeys.warnings (die
      // Aufzeichnung fragte vor der Meldung und vom falschen Halt aus) und
      // reports.event (204 hat per Entwurf keinen Koerper — Vollstaendigkeit,
      // keine Luecke). kVertragUnbeobachtet ist seither leer.
      //
      // Der Test prueft deshalb nicht mehr, DASS eine bestimmte Luecke drinsteht,
      // sondern dass der Mechanismus lebt: die Liste existiert, und was drinsteht,
      // ist ein echter Endpunktname. Waere sie einfach weggefallen, wuerde eine
      // neue Luecke stumm entstehen.
      expect(kVertragUnbeobachtet, isA<List<String>>());
      for (final e in kVertragUnbeobachtet) {
        expect(kVertragPflichtFelder.keys, contains(e),
            reason: '„$e" ist als unbeobachtet gemeldet, kommt im Vertrag aber nicht vor.');
      }
    });

    test('cities.alerts hat jetzt eine beobachtete Form', () {
      // Gegenprobe zum obigen: der Endpunkt, der am laengsten „unbeobachtet" war,
      // muss echte Pflichtfelder tragen — sonst ist die Luecke nur umbenannt.
      expect(kVertragUnbeobachtet, isNot(contains('cities.alerts')));
      expect(kVertragPflichtFelder['cities.alerts'], contains('[].header'));
      expect(kVertragPflichtFelder['cities.alerts'], contains('[].severity'));
    });
  });

  group('Anfrageseite — sendet die App, was der Server verlangt?', () {
    late List<http.Request> gesendet;

    setUp(() {
      SharedPreferences.setMockInitialValues({
        'tg_device_token': 'DEV-TOKEN',
        'tg_ticket_confirmed': true,
        'tg_ticket_confirmed_at': DateTime.now().millisecondsSinceEpoch,
      });
      gesendet = [];
      Api.client = MockClient((req) async {
        gesendet.add(req);
        return http.Response(jsonEncode({'id': 'x', 'report_type': 'on_platform',
          'created_at': '2026-08-22T10:00:00Z', 'expires_at': '2026-08-22T10:20:00Z'}), 201);
      });
    });

    tearDown(() => Api.client = http.Client());

    test('reports.create sendet ALLE Pflicht-Header — Blocker 3', () async {
      await Api.reportAtStation(stopId: 'HHA1', ticketConfirmed: true);
      expect(gesendet, hasLength(1));
      for (final h in kVertragPflichtHeader['reports.create']!) {
        expect(gesendet.single.headers.keys.map((k) => k.toLowerCase()),
            contains(h.toLowerCase()),
            reason: 'Pflicht-Header „$h" fehlt — der Server antwortet dann 422.');
      }
      // Der Schlüssel muss je Meldung neu sein, sonst liefert der Server die
      // zwischengespeicherte Antwort der VORIGEN Meldung zurück.
      final ersterKey = gesendet.single.headers.entries
          .firstWhere((e) => e.key.toLowerCase() == 'idempotency-key').value;
      gesendet.clear();
      await Api.reportAtStation(stopId: 'HHA1', ticketConfirmed: true);
      final zweiterKey = gesendet.single.headers.entries
          .firstWhere((e) => e.key.toLowerCase() == 'idempotency-key').value;
      expect(zweiterKey, isNot(ersterKey));
    });

    test('reports.create mit Trip-Anker sendet den Trip-Block', () async {
      await Api.report(stopId: 'HHA1', tripId: 'T_U1_000', startDate: '20260822',
          routeId: 'R_U1', ticketConfirmed: true);
      final body = jsonDecode(gesendet.single.body) as Map<String, dynamic>;
      expect(body['anchor_type'], 'trip');
      expect((body['trip'] as Map)['trip_id'], 'T_U1_000');
      expect((body['client'] as Map)['ticket_confirmed'], isTrue);
    });

    test('reports.list sendet die Ticket-Bestätigung — sonst 403', () async {
      Api.client = MockClient((req) async {
        gesendet.add(req);
        return http.Response('[]', 200);
      });
      await Api.cityReports();
      for (final h in kVertragPflichtHeader['reports.list']!) {
        expect(gesendet.single.headers.keys.map((k) => k.toLowerCase()),
            contains(h.toLowerCase()), reason: 'Pflicht-Header „$h" fehlt.');
      }
    });

    test('ohne gültige Ticket-Bestätigung wird der Header NICHT gesendet', () async {
      // Das Gate darf nicht durch einen dauerhaft gesetzten Header ausgehebelt werden.
      SharedPreferences.setMockInitialValues({'tg_device_token': 'DEV-TOKEN'});
      Api.client = MockClient((req) async {
        gesendet.add(req);
        return http.Response('[]', 200);
      });
      await Api.cityReports();
      expect(gesendet.single.headers.keys.map((k) => k.toLowerCase()),
          isNot(contains('x-ticket-confirmed')));
    });
  });
}
