import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

/// API-Client (Vertrag: docs/04-api.md, snake_case). Basis-URL je Flavor
/// (--dart-define=API_BASE=https://api.example.de); Debug-Default = lokale API.
const String kApiBase = String.fromEnvironment('API_BASE',
    defaultValue: 'http://10.0.2.2:5099'); // Android-Emulator → Host loopback

const kTrialDays = 14;
const kPriceEur = 2.99;

class Stop {
  final String stopId, stopName;
  final double? distanceKm;
  Stop({required this.stopId, required this.stopName, this.distanceKm});
  factory Stop.fromJson(Map<String, dynamic> j) => Stop(
      stopId: j['stop_id'], stopName: j['stop_name'], distanceKm: j['distance_km']);
}

class Warning {
  final String affectedStopId, message;
  final int? etaSeconds;
  Warning({required this.affectedStopId, required this.message, this.etaSeconds});
  factory Warning.fromJson(Map<String, dynamic> j) => Warning(
      affectedStopId: j['affected_stop_id'],
      message: j['message'],
      etaSeconds: j['user_eta_seconds']);
}

class Departure {
  final String tripId, startDate, routeId;
  final String? headsign;
  final DateTime? estimated;
  final DateTime scheduled;
  final int? delayS;
  final bool realtime;
  List<Warning>? warnings;
  Departure(
      {required this.tripId,
      required this.startDate,
      required this.routeId,
      required this.headsign,
      required this.scheduled,
      required this.estimated,
      required this.delayS,
      required this.realtime});
  factory Departure.fromJson(Map<String, dynamic> j) {
    final ref = j['trip_ref'] as Map<String, dynamic>;
    return Departure(
        tripId: ref['trip_id'],
        startDate: ref['start_date'],
        routeId: j['route_id'],
        headsign: j['headsign'],
        scheduled: DateTime.parse(j['scheduled_time']),
        estimated: j['estimated_time'] != null ? DateTime.parse(j['estimated_time']) : null,
        delayS: j['delay_s'],
        realtime: j['realtime'] == true)
      ..warnings = ((j['warnings'] ?? []) as List)
          .map((w) => Warning.fromJson(w as Map<String, dynamic>))
          .toList();
  }
}

class Connection {
  final String routeId;
  final String? headsign;
  final List<Departure> next;
  Connection({required this.routeId, required this.headsign, required this.next});
}

class Me {
  final String access;
  final int trialDaysRemaining;
  Me({required this.access, required this.trialDaysRemaining});
  factory Me.fromJson(Map<String, dynamic> j) =>
      Me(access: j['access'], trialDaysRemaining: j['trial_days_remaining']);
}

class ApiException implements Exception {
  final String code;
  ApiException(this.code);
  @override
  String toString() => code;
}

class Api {
  static Future<String> ensureDeviceToken() async {
    final sp = await SharedPreferences.getInstance();
    final existing = sp.getString('tg_device_token');
    if (existing != null) return existing;
    final r = await http.post(Uri.parse('$kApiBase/v1/devices'));
    if (r.statusCode != 201) throw ApiException('device_failed');
    final token = jsonDecode(r.body)['device_token'] as String;
    await sp.setString('tg_device_token', token);
    return token;
  }

  static Future<Map<String, String>> _headers({bool ticketConfirmed = true}) async {
    final sp = await SharedPreferences.getInstance();
    final token = await ensureDeviceToken();
    return {
      'Content-Type': 'application/json',
      'X-Device-Token': token,
      if (ticketConfirmed && sp.getBool('tg_ticket_confirmed') == true)
        'X-Ticket-Confirmed': 'true',
      if (sp.getString('tg_access_token') case final sub?) 'Authorization': 'Bearer $sub',
    };
  }

  static Future<dynamic> _call(String method, String path, {Object? body}) async {
    final h = await _headers();
    final r = method == 'GET'
        ? await http.get(Uri.parse(kApiBase + path), headers: h)
        : await http.post(Uri.parse(kApiBase + path),
            headers: h, body: body == null ? null : jsonEncode(body));
    final decoded = jsonDecode(r.body.isEmpty ? '{}' : r.body);
    if (r.statusCode == 402) throw ApiException('trial_expired');
    if (r.statusCode >= 400) throw ApiException(decoded['error']?['code'] ?? 'http_${r.statusCode}');
    return decoded;
  }

  static Future<Me> me() async => Me.fromJson(await _call('GET', '/v1/devices/me'));

  static Future<List<Stop>> nearby(double lat, double lon) async {
    final list = await _call('GET',
        '/v1/cities/hamburg/stops/nearby?lat=$lat&lon=$lon&take=6');
    return (list as List).map((e) => Stop.fromJson(e)).toList();
  }

  static Future<List<Stop>> searchStops(String q) async {
    final list = await _call(
        'GET', '/v1/cities/hamburg/stops/nearby?q=${Uri.encodeQueryComponent(q)}&take=8');
    return (list as List).map((e) => Stop.fromJson(e)).toList();
  }

  static Future<List<Connection>> journey(String fromStopId, String toStopId) async {
    final r = await _call('POST', '/v1/journeys/search',
        body: {'from_stop_id': fromStopId, 'to_stop_id': toStopId});
    return ((r['direct_connections'] ?? []) as List)
        .map((c) => Connection(
            routeId: c['route_id'],
            headsign: c['headsign'],
            next: ((c['next_departures'] ?? []) as List).map((d) {
              final dep = Departure.fromJson(d);
              dep.warnings = ((d['warnings'] ?? []) as List)
                  .map((w) => Warning.fromJson(w))
                  .toList();
              return dep;
            }).toList()))
        .toList();
  }

  /// Kontrollmeldung (1 Tap). GATE: erfordert aktuelle Ticket-Bestätigung (docs/18 H1).
  static Future<void> report({
    required String stopId,
    required String tripId,
    required String startDate,
    required String routeId,
    required bool ticketConfirmed,
  }) async {
    await _call('POST', '/v1/reports', body: {
      'city_slug': 'hamburg',
      'anchor_type': 'trip',
      'report_type': 'in_vehicle',
      'vehicle_kind': 'rail',
      'station': {'stop_id': stopId},
      'trip': {'trip_id': tripId, 'start_date': startDate, 'route_id': routeId},
      'inspector_count': 1,
      'client': {'ticket_confirmed': ticketConfirmed, 'platform': 'android'},
    });
  }
}
