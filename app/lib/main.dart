import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'api.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const TransitGuardApp());
}

class TransitGuardApp extends StatelessWidget {
  const TransitGuardApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'TransitGuard',
        theme: ThemeData.dark(useMaterial3: true).copyWith(
            colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF38BDF8))),
        home: const HomeScreen(),
      );
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});
  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  Me? _me;
  List<Stop> _nearby = [];
  List<Stop> _destResults = [];
  List<Connection> _connections = [];
  String _dest = '';
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    Api.ensureDeviceToken().catchError((Object _) => '');   // catchError braucht Rueckgabetyp Future<String>
    Api.me().then((m) => setState(() => _me = m)).catchError((_) {});
  }

  Future<void> _useLocation() async {
    setState(() => _busy = true);
    try {
      var perm = await Geolocator.checkPermission();
      if (perm == LocationPermission.denied) {
        perm = await Geolocator.requestPermission();
      }
      if (perm == LocationPermission.denied || perm == LocationPermission.deniedForever) {
        _snack('Standort-Zugriff verweigert');
        return;
      }
      final pos = await Geolocator.getCurrentPosition();
      final stops = await Api.nearby(pos.latitude, pos.longitude);
      setState(() => _nearby = stops);
    } on ApiException catch (e) {
      _snack('Fehler: ${e.code == 'trial_expired' ? 'Testphase abgelaufen — Abo 2,99 €/Monat' : e.code}');
    } finally {
      setState(() => _busy = false);
    }
  }

  Future<void> _searchDest() async {
    if (_dest.trim().length < 2) return;
    try {
      final r = await Api.searchStops(_dest.trim());
      setState(() => _destResults = r);
    } on ApiException catch (e) {
      _snack('Fehler: ${e.code}');
    }
  }

  Future<void> _findLine(Stop from, Stop to) async {
    setState(() => _busy = true);
    try {
      final c = await Api.journey(from.stopId, to.stopId);
      setState(() => _connections = c);
    } on ApiException catch (e) {
      _snack(e.code == 'trial_expired' ? 'Testphase abgelaufen — Abo im Store' : 'Fehler: ${e.code}');
    } finally {
      setState(() => _busy = false);
    }
  }

  void _snack(String msg) =>
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('TransitGuard · Hamburg & Umland'), actions: [
          if (_me != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Center(
                child: Text(_me!.access == 'trial'
                    ? 'Test: ${_me!.trialDaysRemaining} T.'
                    : _me!.access == 'subscriber' ? 'Abo' : 'abgelaufen'),
              ),
            ),
        ]),
        body: ListView(padding: const EdgeInsets.all(12), children: [
          const TicketGateBanner(),
          Row(children: [
            FilledButton.tonalIcon(
                onPressed: _busy ? null : _useLocation,
                icon: const Icon(Icons.my_location),
                label: const Text('Standort')),
            const SizedBox(width: 8),
            Expanded(
                child: Text(_nearby.isEmpty
                    ? 'Start: per Standort wählen'
                    : 'Start: ${_nearby.first.stopName}${_nearby.first.distanceKm != null ? ' (${_nearby.first.distanceKm} km)' : ''}')),
          ]),
          Wrap(
              children: _nearby
                  .map((s) => ActionChip(label: Text(s.stopName), onPressed: () => _destResults.isEmpty ? _snack('Bitte erst Ziel suchen') : _findLine(s, _destResults.first)))
                  .toList()),
          const SizedBox(height: 8),
          Row(children: [
            Expanded(
                child: TextField(
                    decoration: const InputDecoration(hintText: 'Ziel: Haltestelle suchen…', border: OutlineInputBorder()),
                    onChanged: (v) => _dest = v,
                    onSubmitted: (_) => _searchDest())),
            const SizedBox(width: 8),
            FilledButton(onPressed: _searchDest, child: const Text('Suchen')),
          ]),
          Wrap(
              children: _destResults
                  .map((s) => ChoiceChip(label: Text(s.stopName), selected: s == _destResults.first, onSelected: (_) => setState(() => _destResults = [s, ..._destResults.where((x) => x != s)])))
                  .toList()),
          if (_busy) const LinearProgressIndicator(),
          ..._connections.map((c) => Card(
                child: Padding(
                  padding: const EdgeInsets.all(10),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text('${c.routeId} → ${c.headsign ?? ''}',
                        style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
                    ...c.next.map((d) => _DepartureTile(departure: d, boardStop: _nearby.isEmpty ? null : _nearby.first)),
                  ]),
                ),
              )),
          const AttributionFooter(),
        ]),
      );
}

class _DepartureTile extends StatelessWidget {
  final Departure departure;
  final Stop? boardStop;
  const _DepartureTile({required this.departure, this.boardStop});

  Future<void> _report(BuildContext context) async {
    final sp = await SharedPreferences.getInstance();
    final confirmed = sp.getBool('tg_ticket_confirmed') ?? false;
    if (!confirmed) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Bitte zuerst Ticket-Bestätigung (oben).')));
      }
      return;
    }
    try {
      await Api.report(
        stopId: boardStop?.stopId ?? '',
        tripId: departure.tripId,
        startDate: departure.startDate,
        routeId: departure.routeId,
        ticketConfirmed: true,
      );
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Danke! Meldung aktiv — wandert mit der Fahrt.')));
      }
    } on ApiException catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Fehler: ${e.code}')));
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Text((departure.estimated ?? departure.scheduled).toLocal().toString().substring(11, 16),
              style: TextStyle(
                  fontWeight: FontWeight.bold,
                  color: departure.realtime ? const Color(0xFF4ADE80) : null)),
          const SizedBox(width: 8),
          if (departure.delayS != null && departure.delayS! > 60)
            Text('+${departure.delayS! ~/ 60} min',
                style: const TextStyle(color: Color(0xFFF87171))),
          const Spacer(),
          TextButton.icon(
              onPressed: () => _report(context),
              icon: const Icon(Icons.warning_amber),
              label: const Text('Kontrolle melden')),
        ]),
        ...?departure.warnings?.map((w) => Container(
              margin: const EdgeInsets.symmetric(vertical: 2),
              padding: const EdgeInsets.all(8),
              decoration: BoxDecoration(
                  color: const Color(0xFF450A0A),
                  border: Border.all(color: const Color(0xFFF87171)),
                  borderRadius: BorderRadius.circular(8)),
              child: Text(
                  '🚨 ${w.message} — Halt ${w.affectedStopId}${w.etaSeconds != null ? ' (in ~${w.etaSeconds! ~/ 60} min)' : ''}',
                  style: const TextStyle(fontWeight: FontWeight.w600)),
            )),
      ]);
}

/// Ticket-First-Gate (docs/18 H1): 24 h gemerkte Bestätigung, keine „ohne Ticket"-Option mit Zugriff.
class TicketGateBanner extends StatefulWidget {
  const TicketGateBanner({super.key});
  @override
  State<TicketGateBanner> createState() => _TicketGateBannerState();
}

class _TicketGateBannerState extends State<TicketGateBanner> {
  bool _decided = false;

  @override
  void initState() {
    super.initState();
    SharedPreferences.getInstance().then((sp) {
      final ok = sp.getBool('tg_ticket_confirmed') ?? false;
      final ts = sp.getInt('tg_ticket_confirmed_at') ?? 0;
      final fresh = DateTime.now().millisecondsSinceEpoch - ts < 24 * 3600e3;
      setState(() => _decided = ok && fresh);
    });
  }

  Future<void> _confirm() async {
    final sp = await SharedPreferences.getInstance();
    await sp.setBool('tg_ticket_confirmed', true);
    await sp.setInt('tg_ticket_confirmed_at', DateTime.now().millisecondsSinceEpoch);
    setState(() => _decided = true);
  }

  @override
  Widget build(BuildContext context) {
    if (_decided) return const SizedBox.shrink();
    return Card(
      color: const Color(0xFF172554),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Bist du gerade mit gültigem Ticket unterwegs?',
              style: TextStyle(fontWeight: FontWeight.bold)),
          const SizedBox(height: 4),
          const Text(
              'Kontrollhinweise sind für Fahrgäste mit gültigem Fahrschein gedacht — danke fürs Fairplay.',
              style: TextStyle(fontSize: 12)),
          const SizedBox(height: 8),
          Wrap(spacing: 8, children: [
            FilledButton(onPressed: _confirm, child: const Text('✓ Ja, gültiges Ticket')),
            OutlinedButton(
                onPressed: () => launchTicketPurchase(), child: const Text('Ticket kaufen →')),
          ]),
        ]),
      ),
    );
  }
}

void launchTicketPurchase() {
  // Absichtlich KEIN erfundenes URL-Schema (18 §1.3): offizielle Web-URL; hvv-switch-Deep-Link nach F-4-Antwort.
  // url_launcher nachrüsten (Ticket M-Flutter-2) — bis dahin Hinweis-Dialog in der UI.
}

class AttributionFooter extends StatelessWidget {
  const AttributionFooter({super.key});
  @override
  Widget build(BuildContext context) => const Padding(
        padding: EdgeInsets.only(top: 16),
        child: Text(
          'Fahrplan- & Echtzeitdaten: gtfs.de / DELFI e.V. / beteiligte Verbünde (CC BY-SA 4.0 / CC BY 4.0) · '
          'Kontrollhinweise sind unverifizierte Community-Angaben.',
          style: TextStyle(fontSize: 10, color: Colors.white54),
        ),
      );
}
