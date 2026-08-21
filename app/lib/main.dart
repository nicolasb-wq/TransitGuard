// ---------------------------------------------------------------------------
// TransitGuard (Android) — App-Hülle mit drei Zielen: Fahren > Warnen > Mehr.
//
// Gleiche Hierarchie und gleiche Mikro-Texte wie die PWA (web/), damit sich
// beide Kanäle gleich anfühlen. Navigation unten: einhändig erreichbar.
//
// Das Ticket-First-Gate (docs/18 H1) sitzt an EINER Stelle — `_verlangeTicket`.
// Es gibt kein „Später", kein „Überspringen" und keinen zweiten Pfad daran
// vorbei. Der Fahrplan-Teil bleibt ohne Bestätigung nutzbar, der Kontroll-Teil
// nicht. Das ist eine Rechts-, keine Designentscheidung.
// ---------------------------------------------------------------------------
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'api.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const TransitGuardApp());
}

class TransitGuardApp extends StatelessWidget {
  const TransitGuardApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'TransitGuard',
        debugShowCheckedModeBanner: false,
        theme: TgTheme.of(Brightness.light),
        darkTheme: TgTheme.of(Brightness.dark),
        themeMode: ThemeMode.system,
        home: const HomeScreen(),
      );
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});
  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  int _tab = 0;
  Me? _me;
  bool _ticketOk = false;

  // Fahren
  List<Stop> _nearby = [];
  Stop? _von, _nach;
  bool _startWaehlen = false;
  List<Stop> _treffer = [];
  List<Connection> _verbindungen = [];
  bool _sucheLaeuft = false, _ortet = false, _sucheZiel = false;

  // Warnen
  List<ReportView>? _meldungen;

  @override
  void initState() {
    super.initState();
    Api.ensureDeviceToken().then((_) => _ladeMe()).catchError((Object _) {});
    TicketGate.isConfirmed().then((ok) {
      if (mounted) setState(() => _ticketOk = ok);
    });
  }

  Future<void> _ladeMe() async {
    try {
      final m = await Api.me();
      if (mounted) setState(() => _me = m);
    } on ApiException catch (_) {/* Zugangsstand ist nicht kritisch für die Fahrt */}
  }

  // --- Rückmeldung ---------------------------------------------------------
  void _sag(String text, {bool fehler = false}) {
    if (!mounted) return;
    final s = Theme.of(context).colorScheme;
    ScaffoldMessenger.of(context)
      ..clearSnackBars()
      ..showSnackBar(SnackBar(
        content: Text(text),
        backgroundColor: fehler ? s.errorContainer : null,
        showCloseIcon: true,
      ));
  }

  void _sagFehler(Object e) => _sag(_klartext(e), fehler: true);

  static String _klartext(Object e) {
    final code = e is ApiException ? e.code : 'network';
    return switch (code) {
      'validation' => 'Start und Ziel passen so nicht zusammen.',
      'trial_expired' => 'Die Testphase ist vorbei — weiter unter „Mehr".',
      'ticket_gate_blocked' ||
      'ticket_confirmation_required' =>
        'Dafür fehlt noch die Ticket-Bestätigung.',
      'rate_limited' => 'Kurz durchatmen — das war zu schnell hintereinander.',
      'trip_not_resolvable' => 'Diese Fahrt läuft gerade nicht — bitte eine andere wählen.',
      'city_inactive' => 'Für diese Stadt sind wir noch nicht am Netz.',
      'feature_disabled' => 'Diese Funktion ist gerade abgeschaltet.',
      _ => 'Das hat gerade nicht geklappt. Bitte noch einmal versuchen.',
    };
  }

  // --- Ticket-Gate: die eine Stelle ---------------------------------------
  Future<bool> _verlangeTicket() async {
    if (await TicketGate.isConfirmed()) {
      if (mounted) setState(() => _ticketOk = true);
      return true;
    }
    if (!mounted) return false;
    final bestaetigt = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _TicketSheet(),
    );
    if (bestaetigt == true) {
      await TicketGate.confirm();
      if (mounted) setState(() => _ticketOk = true);
      return true;
    }
    return false;
  }

  // --- Standort ------------------------------------------------------------
  Future<void> _orten() async {
    setState(() => _ortet = true);
    try {
      var recht = await Geolocator.checkPermission();
      if (recht == LocationPermission.denied) recht = await Geolocator.requestPermission();
      if (recht == LocationPermission.denied || recht == LocationPermission.deniedForever) {
        _sag('Standort nicht freigegeben — Start bitte per Suche wählen.', fehler: true);
        return;
      }
      final pos = await Geolocator.getCurrentPosition();
      final stops = await Api.nearby(pos.latitude, pos.longitude);
      if (!mounted) return;
      setState(() {
        _nearby = stops;
        _von = stops.isEmpty ? null : stops.first;
        _startWaehlen = false;
      });
    } on ApiException catch (e) {
      _sagFehler(e);
    } finally {
      if (mounted) setState(() => _ortet = false);
    }
  }

  // --- Fahrtensuche --------------------------------------------------------
  Future<void> _sucheZiele(String q) async {
    if (q.trim().length < 2) {
      setState(() => _treffer = []);
      return;
    }
    setState(() => _sucheZiel = true);
    try {
      final r = await Api.searchStops(q.trim());
      if (mounted) setState(() => _treffer = r);
    } on ApiException catch (e) {
      _sagFehler(e);
    } finally {
      if (mounted) setState(() => _sucheZiel = false);
    }
  }

  Future<void> _sucheVerbindung() async {
    final von = _von, nach = _nach;
    if (von == null || nach == null) return;
    setState(() { _sucheLaeuft = true; _verbindungen = []; });
    try {
      final c = await Api.journey(von.stopId, nach.stopId);
      if (mounted) setState(() => _verbindungen = c);
    } on ApiException catch (e) {
      _sagFehler(e);
    } catch (e) {
      // Auch Nicht-ApiException darf die Suche nicht stumm verschlucken.
      _sagFehler(e);
    } finally {
      if (mounted) setState(() => _sucheLaeuft = false);
    }
  }

  // --- Melden --------------------------------------------------------------
  Future<void> _meldeAnFahrt(Departure d, Connection c) async {
    if (!await _verlangeTicket()) return;
    try {
      await Api.report(
        stopId: _von?.stopId ?? '',
        tripId: d.tripId,
        startDate: d.startDate,
        routeId: d.routeId ?? c.routeId,
        ticketConfirmed: true,
      );
      _sag('Danke! Dein Hinweis wandert jetzt mit der Fahrt mit.');
    } catch (e) {
      _sagFehler(e);
    }
  }

  Future<void> _meldeAnHaltestelle(Stop s) async {
    if (!await _verlangeTicket()) return;
    try {
      await Api.reportAtStation(stopId: s.stopId, ticketConfirmed: true);
      _sag('Danke! Hinweis für ${s.stopName} ist aktiv.');
      await _ladeMeldungen();
    } catch (e) {
      _sagFehler(e);
    }
  }

  Future<void> _ladeMeldungen() async {
    if (!await TicketGate.isConfirmed()) return;
    try {
      final r = await Api.cityReports();
      if (mounted) setState(() => _meldungen = r);
    } catch (_) {
      if (mounted) setState(() => _meldungen = const []);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('TransitGuard', style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
            Text('Hamburg & Umland', style: TextStyle(fontSize: 12)),
          ],
        ),
        actions: [
          if (_me != null)
            Padding(
              padding: const EdgeInsets.only(right: 14),
              child: Chip(
                label: Text(switch (_me!.access) {
                  'trial' => 'Test · ${_me!.trialDaysRemaining} T.',
                  'subscriber' => 'Abo',
                  _ => 'abgelaufen',
                }),
                visualDensity: VisualDensity.compact,
              ),
            ),
        ],
      ),
      body: SafeArea(
        child: switch (_tab) {
          0 => _fahren(),
          1 => _warnen(),
          _ => _mehr(),
        },
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: (i) {
          setState(() => _tab = i);
          if (i == 1) _ladeMeldungen();
        },
        destinations: const [
          NavigationDestination(
              icon: Icon(Icons.directions_transit_outlined),
              selectedIcon: Icon(Icons.directions_transit),
              label: 'Fahren'),
          NavigationDestination(
              icon: Icon(Icons.warning_amber_outlined),
              selectedIcon: Icon(Icons.warning_amber),
              label: 'Warnen'),
          NavigationDestination(
              icon: Icon(Icons.more_horiz_outlined),
              selectedIcon: Icon(Icons.more_horiz),
              label: 'Mehr'),
        ],
      ),
      backgroundColor: s.surface,
    );
  }

  // --- Tab „Fahren" --------------------------------------------------------
  Widget _fahren() => ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
        children: [
          _karte('Deine Fahrt', [
            if (_von == null || _startWaehlen) ...[
              Row(children: [
                OutlinedButton.icon(
                  onPressed: _ortet ? null : _orten,
                  icon: const Icon(Icons.my_location),
                  label: Text(_ortet ? 'Suche…' : 'Standort'),
                ),
                const SizedBox(width: 10),
                Expanded(
                    child: Text(_nearby.isEmpty ? 'Start noch offen' : 'Haltestelle wählen',
                        style: Theme.of(context).textTheme.bodySmall)),
              ]),
              if (_nearby.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: _nearby
                        .map((s) => ChoiceChip(
                              label: Text(s.stopName),
                              selected: _von?.stopId == s.stopId,
                              onSelected: (_) {
                                setState(() { _von = s; _startWaehlen = false; });
                                _sucheVerbindung();
                              },
                            ))
                        .toList(),
                  ),
                ),
            ] else
              Row(children: [
                Expanded(
                  child: Text.rich(TextSpan(children: [
                    const TextSpan(text: 'Ab '),
                    TextSpan(
                        text: _von!.stopName,
                        style: const TextStyle(fontWeight: FontWeight.w700)),
                    if (_von!.distanceKm != null)
                      TextSpan(text: ' · ${_abstand(_von!.distanceKm!)}'),
                  ]), overflow: TextOverflow.ellipsis),
                ),
                TextButton(
                    onPressed: () => setState(() => _startWaehlen = true),
                    child: const Text('ändern')),
              ]),
            const SizedBox(height: 12),
            if (_nach == null) ...[
              TextField(
                decoration: const InputDecoration(
                  hintText: 'Wohin? Haltestelle eingeben',
                  prefixIcon: Icon(Icons.place_outlined),
                ),
                textInputAction: TextInputAction.search,
                onChanged: _sucheZiele,
              ),
              if (_sucheZiel)
                const Padding(padding: EdgeInsets.only(top: 10), child: Skeleton(hoehe: 30)),
              if (_treffer.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: _treffer
                        .map((s) => ActionChip(
                              label: Text(s.stopName),
                              onPressed: () {
                                setState(() => _nach = s);
                                _sucheVerbindung();
                              },
                            ))
                        .toList(),
                  ),
                ),
            ] else
              Row(children: [
                Expanded(
                  child: Text.rich(TextSpan(children: [
                    const TextSpan(text: 'Nach '),
                    TextSpan(
                        text: _nach!.stopName,
                        style: const TextStyle(fontWeight: FontWeight.w700)),
                  ]), overflow: TextOverflow.ellipsis),
                ),
                TextButton(
                  onPressed: () => setState(() { _nach = null; _verbindungen = []; }),
                  child: const Text('ändern'),
                ),
              ]),
          ]),

          if (_sucheLaeuft)
            _karte('Verbindungen', const [
              Skeleton(hoehe: 30),
              SizedBox(height: 10),
              Skeleton(hoehe: 14, breite: 180),
              SizedBox(height: 16),
              Skeleton(hoehe: 30),
              SizedBox(height: 10),
              Skeleton(hoehe: 14, breite: 140),
            ])
          else if (_von == null)
            _leer(Icons.explore_outlined, 'Tippe auf „Standort" — wir finden die Haltestelle neben dir.')
          else if (_nach == null)
            _leer(Icons.place_outlined, 'Jetzt noch das Ziel eingeben.')
          else if (_verbindungen.every((c) => c.next.isEmpty))
            _leer(Icons.nightlight_outlined, 'Von hier fährt heute nichts mehr direkt dorthin.')
          else
            _karte('Direkt', [
              for (final c in _verbindungen.where((c) => c.next.isNotEmpty))
                _verbindung(c),
            ]),
        ],
      );

  Widget _verbindung(Connection c) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(children: [
            RoutePlakette(c.routeId),
            const SizedBox(width: 10),
            Expanded(
                child: Text(c.headsign ?? 'Richtung unbekannt',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                    overflow: TextOverflow.ellipsis)),
          ]),
          for (final d in c.next) _abfahrt(d, c),
          const SizedBox(height: 8),
        ],
      );

  Widget _abfahrt(Departure d, Connection c) {
    final s = Theme.of(context).colorScheme;
    final dunkel = Theme.of(context).brightness == Brightness.dark;
    final wann = (d.estimated ?? d.scheduled).toLocal();
    final min = wann.difference(DateTime.now()).inMinutes;
    final spaet = (d.delayS ?? 0) >= 60;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          SizedBox(
            width: 82,
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(_countdown(min),
                  style: TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w700,
                    color: !d.realtime
                        ? null
                        : spaet
                            ? (dunkel ? TgTheme.warnDark : TgTheme.warn)
                            : (dunkel ? TgTheme.okDark : TgTheme.ok),
                  )),
              Text(_uhr(wann), style: Theme.of(context).textTheme.bodySmall),
            ]),
          ),
          Expanded(
            child: Chip(
              label: Text(d.realtime ? 'Live · ${_verspaetung(d.delayS)}' : 'Fahrplan'),
              visualDensity: VisualDensity.compact,
              side: BorderSide(color: s.outlineVariant),
            ),
          ),
          TextButton.icon(
            onPressed: () => _meldeAnFahrt(d, c),
            icon: const Icon(Icons.warning_amber, size: 18),
            label: const Text('Melden'),
            style: TextButton.styleFrom(minimumSize: const Size(0, TgTheme.tap)),
          ),
        ]),
        for (final w in d.warnings ?? const <Warning>[]) _hinweis(
          '${w.message}${w.etaSeconds != null ? ' — in etwa ${(w.etaSeconds! / 60).ceil()} min' : ''}',
        ),
      ]),
    );
  }

  // --- Tab „Warnen" --------------------------------------------------------
  Widget _warnen() {
    if (!_ticketOk) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.confirmation_number_outlined, size: 46),
            const SizedBox(height: 14),
            const Text(
              'Community-Hinweise sind für Fahrgäste mit gültigem Fahrschein gedacht. '
              'Bestätige kurz dein Ticket — dann siehst du, was gerade gemeldet wird.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 20),
            FilledButton(
              onPressed: () async {
                if (await _verlangeTicket()) await _ladeMeldungen();
              },
              child: const Text('Ticket bestätigen'),
            ),
          ]),
        ),
      );
    }

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
      children: [
        _karte('Hinweis geben', [
          if (_von != null) ...[
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: () => _meldeAnHaltestelle(_von!),
                icon: const Icon(Icons.warning_amber),
                label: const Text('Kontrolle hier melden'),
                style: FilledButton.styleFrom(
                  minimumSize: const Size(0, 60),
                  backgroundColor: Theme.of(context).colorScheme.error,
                  foregroundColor: Theme.of(context).colorScheme.onError,
                ),
              ),
            ),
            const SizedBox(height: 10),
            Text(
              'Gilt für ${_von!.stopName}. Sitzt du schon in der Bahn? Dann melde unter '
              '„Fahren" direkt an deiner Fahrt — der Hinweis wandert dann mit.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ] else ...[
            SizedBox(
              width: double.infinity,
              child: OutlinedButton.icon(
                onPressed: _ortet ? null : _orten,
                icon: const Icon(Icons.my_location),
                label: Text(_ortet ? 'Standort wird gesucht…' : 'Haltestelle in der Nähe finden'),
              ),
            ),
            const SizedBox(height: 10),
            Text('Für einen Hinweis brauchen wir die Haltestelle — sonst weiß niemand, wo er gilt.',
                style: Theme.of(context).textTheme.bodySmall),
          ],
        ]),
        _karte('Gerade gemeldet', [
          if (_meldungen == null) ...const [
            Skeleton(hoehe: 30),
            SizedBox(height: 10),
            Skeleton(hoehe: 30),
          ] else if (_meldungen!.isEmpty)
            Row(children: [
              const Icon(Icons.check_circle_outline),
              const SizedBox(width: 10),
              Expanded(child: Text('Nichts Aktuelles gemeldet.',
                  style: Theme.of(context).textTheme.bodyMedium)),
            ])
          else
            for (final r in _meldungen!)
              _hinweis('${r.routeId ?? r.stationName ?? 'Hinweis'}'
                  '${r.headsign != null ? ' Richtung ${r.headsign}' : ''}\n'
                  '${_meldeArt(r.reportType)} · gemeldet ${_uhr(r.createdAt.toLocal())}'
                  ' · gültig bis ${_uhr(r.expiresAt.toLocal())}'),
          const SizedBox(height: 8),
          Text('Alle Angaben stammen von Fahrgästen und sind unverifiziert.',
              style: Theme.of(context).textTheme.bodySmall),
        ]),
      ],
    );
  }

  // --- Tab „Mehr" ----------------------------------------------------------
  Widget _mehr() => ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
        children: [
          _karte('Zugang', [
            if (_me == null)
              const Skeleton(hoehe: 20, breite: 200)
            else ...[
              Row(children: [
                Expanded(child: Text(switch (_me!.access) {
                  'trial' => 'Testphase läuft',
                  'subscriber' => 'Abo aktiv — danke!',
                  _ => 'Testphase abgelaufen',
                })),
                if (_me!.access == 'trial')
                  Text('${_me!.trialDaysRemaining} Tage',
                      style: const TextStyle(fontWeight: FontWeight.w700)),
              ]),
              if (_me!.access != 'subscriber') ...[
                const SizedBox(height: 8),
                Text(
                  'Danach ${kPriceEur.toStringAsFixed(2).replaceAll('.', ',')} € im Monat, '
                  'monatlich kündbar über den Store. Ohne Konto, ohne Anmeldung — dein '
                  'Zugang hängt am Gerät, nicht an einer Identität.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ],
          ]),
          _karte('Ticket', [
            SizedBox(
              width: double.infinity,
              child: OutlinedButton(
                onPressed: _verlangeTicket,
                child: Text(_ticketOk ? 'Ticket-Bestätigung erneuern' : 'Ticket bestätigen'),
              ),
            ),
            const SizedBox(height: 10),
            Text(
              'Die Bestätigung gilt 24 Stunden und bleibt auf deinem Gerät. '
              'Tickets gibt es beim hvv (Deutschlandticket, hvv-Ticket, Einzelfahrschein).',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ]),
          _karte('Rechtliches', [
            Text(
              'Fahrplan- & Echtzeitdaten: gtfs.de / DELFI e.V. / beteiligte Verbünde '
              '(CC BY-SA 4.0 / CC BY 4.0). Community-Hinweise stammen von Fahrgästen und '
              'sind unverifiziert. Nutzungsbedingungen und Impressum: siehe Betreiberseite.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ]),
        ],
      );

  // --- Bausteine -----------------------------------------------------------
  Widget _karte(String titel, List<Widget> kinder) => Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(titel.toUpperCase(),
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w700,
                  letterSpacing: .8,
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                )),
            const SizedBox(height: 12),
            ...kinder,
          ]),
        ),
      );

  Widget _hinweis(String text) {
    final s = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: s.errorContainer,
        border: Border.all(color: s.error),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Icon(Icons.report_outlined, color: s.onErrorContainer, size: 20),
        const SizedBox(width: 9),
        Expanded(
            child: Text(text,
                style: TextStyle(color: s.onErrorContainer, fontWeight: FontWeight.w500))),
      ]),
    );
  }

  Widget _leer(IconData symbol, String text) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 34, horizontal: 12),
        child: Column(children: [
          Icon(symbol, size: 40, color: Theme.of(context).colorScheme.onSurfaceVariant),
          const SizedBox(height: 12),
          Text(text,
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
        ]),
      );

  static String _abstand(double km) =>
      km < 1 ? '${(km * 1000).round()} m' : '${km.toStringAsFixed(1)} km';

  static String _uhr(DateTime t) =>
      '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  static String _countdown(int min) => min <= 0
      ? 'jetzt'
      : min < 60
          ? '$min min'
          : '${min ~/ 60} h ${min % 60} min';

  static String _verspaetung(int? d) {
    if (d == null || d.abs() < 60) return 'pünktlich';
    final m = (d / 60).round();
    return m > 0 ? '+$m min' : '−${m.abs()} min';
  }

  static String _meldeArt(String art) => switch (art) {
        'in_vehicle' => 'In der Bahn',
        'on_platform' => 'Am Bahnsteig',
        'at_stop' => 'An der Haltestelle',
        _ => art,
      };
}

/// Ticket-First-Gate als Bottom-Sheet — in der Daumenzone, nicht bildschirmmittig.
class _TicketSheet extends StatelessWidget {
  const _TicketSheet();

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 4, 20, 20 + MediaQuery.viewInsetsOf(context).bottom),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text('Fährst du gerade mit gültigem Ticket?',
              style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 10),
          Text(
            'Deutschlandticket, hvv-Ticket, Jobticket oder Einzelfahrschein — alles zählt. '
            'Community-Hinweise sind für Fahrgäste mit gültigem Fahrschein gedacht.',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 18),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text('Ja, ich habe ein gültiges Ticket'),
          ),
          const SizedBox(height: 10),
          OutlinedButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text('Später'),
          ),
          const SizedBox(height: 14),
          Text(
            'Fahrplan, Abfahrten und Störungen kannst du auch ohne Bestätigung nutzen. '
            'Die Bestätigung gilt 24 Stunden und bleibt auf deinem Gerät.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ]),
      );
}
