import 'dart:async';
import 'dart:convert';
import 'dart:math';
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
  final String tripId, startDate;
  /// Nullable mit Absicht (T-DTO-4): POST /v1/journeys/search liefert route_id
  /// und headsign NICHT auf der Abfahrt — dort stehen sie auf der Verbindung.
  /// GET /v1/stops/{id}/departures liefert beides mit. Am 21.08.2026 gegen eine
  /// laufende API gemessen.
  final String? routeId;
  final String? headsign;
  final DateTime? estimated;
  final DateTime scheduled;
  final int? delayS;
  final bool realtime;
  List<Warning>? warnings;
  Departure(
      {required this.tripId,
      required this.startDate,
      this.routeId,
      this.headsign,
      required this.scheduled,
      required this.estimated,
      required this.delayS,
      required this.realtime});
  /// [routeId]/[headsign] fuellen die Felder, wenn sie nur auf der Verbindung
  /// stehen (Fahrtensuche). Werte aus dem JSON haben Vorrang.
  factory Departure.fromJson(Map<String, dynamic> j, {String? routeId, String? headsign}) {
    final ref = j['trip_ref'] as Map<String, dynamic>;
    return Departure(
        tripId: ref['trip_id'],
        startDate: ref['start_date'],
        routeId: j['route_id'] as String? ?? routeId,
        headsign: j['headsign'] as String? ?? headsign,
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

/// Free-Sicht auf eine Kontroll-Meldung (Pro-Felder fehlen physisch — ADR-0006).
class ReportView {
  final String id, reportType;
  final String? stationName, routeId, headsign;
  final DateTime createdAt, expiresAt;
  ReportView({
    required this.id,
    required this.reportType,
    required this.stationName,
    required this.routeId,
    required this.headsign,
    required this.createdAt,
    required this.expiresAt,
  });
  factory ReportView.fromJson(Map<String, dynamic> j) => ReportView(
        id: j['id'] as String,
        reportType: j['report_type'] as String,
        stationName: j['station_name'] as String?,
        routeId: j['route_id'] as String?,
        headsign: j['headsign'] as String?,
        createdAt: DateTime.parse(j['created_at'] as String),
        expiresAt: DateTime.parse(j['expires_at'] as String),
      );
}

class Me {
  final String access;
  final int trialDaysRemaining;
  Me({required this.access, required this.trialDaysRemaining});
  factory Me.fromJson(Map<String, dynamic> j) =>
      Me(access: j['access'], trialDaysRemaining: j['trial_days_remaining']);
}

/// Ticket-First-Gate (docs/18 H1): 24 h gemerkte Bestaetigung, geraetegebunden.
/// Eine einzige Stelle entscheidet — es gibt keinen zweiten Pfad daran vorbei.
class TicketGate {
  static const _key = 'tg_ticket_confirmed';
  static const _ts = 'tg_ticket_confirmed_at';
  static const ttl = Duration(hours: 24);

  static Future<bool> isConfirmed() async {
    final sp = await SharedPreferences.getInstance();
    if (sp.getBool(_key) != true) return false;
    final at = sp.getInt(_ts) ?? 0;
    return DateTime.now().millisecondsSinceEpoch - at < ttl.inMilliseconds;
  }

  static Future<void> confirm() async {
    final sp = await SharedPreferences.getInstance();
    await sp.setBool(_key, true);
    await sp.setInt(_ts, DateTime.now().millisecondsSinceEpoch);
  }
}

final _zufall = Random.secure();

/// Idempotency-Key im UUID-v4-Format. Pflichtfeld bei POST /v1/reports —
/// ohne ihn antwortet der Server 422 „validation" (docs/04 §4). Bewusst ohne
/// Fremdpaket: 16 Zufallsbytes mit gesetzten Versions-/Variantenbits reichen.
String newIdempotencyKey() {
  final b = List<int>.generate(16, (_) => _zufall.nextInt(256));
  b[6] = (b[6] & 0x0f) | 0x40;   // Version 4
  b[8] = (b[8] & 0x3f) | 0x80;   // Variante 10xx
  String hex(int von, int bis) =>
      b.sublist(von, bis).map((x) => x.toRadixString(16).padLeft(2, '0')).join();
  return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
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

  static Future<dynamic> _call(String method, String path,
      {Object? body, Map<String, String>? extraHeaders}) async {
    final h = await _headers();
    if (extraHeaders != null) h.addAll(extraHeaders);
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
    return ((r['direct_connections'] ?? []) as List).map((c) {
      final routeId = c['route_id'] as String?;
      final headsign = c['headsign'] as String?;
      return Connection(
          routeId: routeId ?? '?',
          headsign: headsign,
          // Linie und Ziel stehen hier auf der VERBINDUNG; sie werden an die
          // Abfahrten durchgereicht, damit die Meldung sie mitschicken kann.
          next: ((c['next_departures'] ?? []) as List)
              .map((d) => Departure.fromJson(d, routeId: routeId, headsign: headsign))
              .toList());
    }).toList();
  }

  /// Kontroll-Lesezugriff — ohne Ticket-Bestaetigung antwortet der Server 403
  /// (T-GATE-2). Das ist Absicht und wird hier nicht umgangen.
  static Future<List<ReportView>> cityReports() async {
    final list = await _call('GET', '/v1/cities/hamburg/reports');
    return (list as List).map((e) => ReportView.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// Meldung mit Stations-Anker (ich stehe am Bahnsteig).
  static Future<void> reportAtStation({
    required String stopId,
    required bool ticketConfirmed,
  }) async {
    await _call('POST', '/v1/reports', body: {
      'city_slug': 'hamburg',
      'anchor_type': 'station',
      'report_type': 'on_platform',
      'vehicle_kind': 'rail',
      'station': {'stop_id': stopId},
      'inspector_count': 1,
      'client': {'ticket_confirmed': ticketConfirmed, 'platform': 'android'},
    }, extraHeaders: {'Idempotency-Key': newIdempotencyKey()});
  }

  /// Kontrollmeldung (1 Tap). GATE: erfordert aktuelle Ticket-Bestätigung (docs/18 H1).
  static Future<void> report({
    required String stopId,
    required String tripId,
    required String startDate,
    String? routeId,
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
    }, extraHeaders: {'Idempotency-Key': newIdempotencyKey()});
  }
}
