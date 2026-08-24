// =============================================================================
// GENERIERT — NICHT VON HAND ÄNDERN.
// Quelle: docs/contract/api-contract.json (aus der laufenden API aufgezeichnet).
// Neu erzeugen: node verifikation/contract_gen_dart.mjs
// Frischeprüfung: scripts/verify-contract.sh (Gate im QA-Loop)
// =============================================================================

/// Header, ohne die der Server ablehnt — im Vertrag durch Negativproben belegt.
const kVertragPflichtHeader = <String, List<String>>{
  'billing.restore': ['X-Device-Token'],
  'cities.events': ['X-Device-Token', 'X-Ticket-Confirmed'],
  'devices.me': ['X-Device-Token'],
  'journeys.warnings': ['X-Device-Token', 'X-Ticket-Confirmed'],
  'reports.create': ['X-Device-Token', 'Idempotency-Key'],
  'reports.list': ['X-Device-Token', 'X-Ticket-Confirmed'],
};

/// Feldpfade, die der Server IMMER sendet (in jeder Beobachtung vorhanden).
const kVertragPflichtFelder = <String, List<String>>{
  'billing.activate': ['access_token', 'restore_code', 'tier', 'valid_until'],
  'billing.activate.konflikt': ['error', 'error.code', 'error.message'],
  'billing.restore': ['access_token', 'tier'],
  'billing.validate': ['status', 'tier', 'valid_until'],
  'billing.validate.abgelaufen': ['error', 'error.code', 'error.message'],
  'cities.alerts': ['[]', '[].description', '[].header', '[].route_refs', '[].severity'],
  'cities.events': ['[]', '[].created_at', '[].event_id', '[].report_id', '[].type'],
  'cities.list': ['[]', '[].display_name', '[].is_active', '[].slug'],
  'devices.issue': ['device_id', 'device_token'],
  'devices.me': ['access', 'rank', 'subscription_price_eur', 'total_reports', 'trial_days_remaining', 'trust_score'],
  'fehler.validation': ['error', 'error.code', 'error.message'],
  'health.live': ['status'],
  'health.ready': ['status'],
  'journeys.search': ['direct_connections', 'direct_connections[]', 'direct_connections[].direction_id', 'direct_connections[].headsign', 'direct_connections[].next_departures', 'direct_connections[].next_departures[].realtime', 'direct_connections[].next_departures[].scheduled_time', 'direct_connections[].next_departures[].trip_ref', 'direct_connections[].next_departures[].trip_ref.start_date', 'direct_connections[].next_departures[].trip_ref.trip_id', 'direct_connections[].next_departures[].warnings', 'direct_connections[].next_departures[].warnings[].affected_stop_id', 'direct_connections[].next_departures[].warnings[].affected_stop_sequence', 'direct_connections[].next_departures[].warnings[].message', 'direct_connections[].next_departures[].warnings[].user_eta_seconds', 'direct_connections[].ride_seconds', 'direct_connections[].route_id', 'direct_connections[].stops_count', 'from_stop_id', 'to_stop_id', 'transfer_connections', 'transfer_connections[]', 'transfer_connections[].leg_a', 'transfer_connections[].leg_a.alight_at', 'transfer_connections[].leg_a.alight_stop', 'transfer_connections[].leg_a.board_at', 'transfer_connections[].leg_a.board_stop', 'transfer_connections[].leg_a.headsign', 'transfer_connections[].leg_a.route_id', 'transfer_connections[].leg_b', 'transfer_connections[].leg_b.alight_at', 'transfer_connections[].leg_b.alight_stop', 'transfer_connections[].leg_b.board_at', 'transfer_connections[].leg_b.board_stop', 'transfer_connections[].leg_b.headsign', 'transfer_connections[].leg_b.route_id', 'transfer_connections[].total_seconds', 'transfer_connections[].transfer_stop_id', 'transfer_connections[].wait_seconds'],
  'journeys.warnings': ['[]', '[].affected_stop_id', '[].affected_stop_sequence', '[].message', '[].report_id', '[].user_eta_seconds'],
  'reports.create': ['anchor_type', 'city_slug', 'created_at', 'expires_at', 'id', 'origin', 'report_type', 'station_name', 'status'],
  'reports.event': [],
  'reports.list': ['[]', '[].anchor_type', '[].city_slug', '[].created_at', '[].expires_at', '[].id', '[].origin', '[].report_type', '[].station_name', '[].status'],
  'stops.departures': ['[]', '[].headsign', '[].realtime', '[].route_id', '[].scheduled_time', '[].trip_ref', '[].trip_ref.start_date', '[].trip_ref.trip_id', '[].warnings'],
  'stops.nearby': ['[]', '[].lat', '[].lon', '[].stop_id', '[].stop_name'],
};

/// Feldpfade, die FEHLEN koennen (WhenWritingNull oder anker-/abfrageabhaengig).
const kVertragOptionaleFelder = <String, List<String>>{
  'billing.activate': [],
  'billing.activate.konflikt': [],
  'billing.restore': [],
  'billing.validate': [],
  'billing.validate.abgelaufen': [],
  'cities.alerts': ['[].route_refs[]', '[].url'],
  'cities.events': [],
  'cities.list': [],
  'devices.issue': [],
  'devices.me': [],
  'fehler.validation': [],
  'health.live': [],
  'health.ready': [],
  'journeys.search': ['direct_connections[].next_departures[]', 'direct_connections[].next_departures[].warnings[]'],
  'journeys.warnings': [],
  'reports.create': [],
  'reports.event': [],
  'reports.list': ['[].headsign', '[].route_id', '[].trip_id', '[].trip_start_date'],
  'stops.departures': [],
  'stops.nearby': ['[].distance_km'],
};

/// Endpunkte, deren Antwortform die Aufzeichnung NICHT sehen konnte
/// (leere Liste bzw. null). Bewusst ausgewiesen statt stillschweigend als
/// „keine Felder" verbucht — hier schuetzt der Vertrag nichts.
const kVertragUnbeobachtet = <String>[];
