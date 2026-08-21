// ---------------------------------------------------------------------------
// API-Client. Basis-URL via VITE_API_URL (Prod) oder gleicher Origin/Dev-Proxy.
// Die Typen bilden den SERVER-Vertrag ab (docs/04, snake_case) — gegen eine
// laufende Instanz nachgemessen, nicht aus dem Controller abgeschrieben.
// ---------------------------------------------------------------------------
export const API_BASE = import.meta.env.VITE_API_URL ?? '';
export const CITY = 'hamburg';

export interface Stop { stop_id: string; stop_name: string; lat: number; lon: number; distance_km?: number }

export interface ControlWarning {
  affected_stop_id: string;
  affected_stop_sequence: number;
  user_eta_seconds?: number | null;
  message: string;
}

export interface Departure {
  trip_ref: { trip_id: string; start_date: string };
  /**
   * ACHTUNG Vertrags-Asymmetrie (gemessen 21.08.2026):
   * GET /v1/stops/{id}/departures liefert route_id/headsign MIT,
   * POST /v1/journeys/search liefert sie NICHT — dort stehen sie auf der
   * Verbindung (Connection), nicht auf der Abfahrt. Deshalb optional; die
   * Oberfläche reicht die Werte der Verbindung durch (siehe Fahren-Screen).
   */
  route_id?: string;
  headsign?: string | null;
  scheduled_time: string;
  estimated_time?: string | null;
  delay_s?: number | null;   // fehlt im JSON, wenn null (WhenWritingNull)
  realtime: boolean;
  warnings?: ControlWarning[] | null;
}

export interface Connection {
  route_id: string; direction_id?: number | null; headsign?: string | null;
  ride_seconds: number; stops_count: number; next_departures: Departure[];
}

/** Ein Bein einer Umstiegsverbindung. Server-Schlüssel sind snake_case. */
export interface TransferLeg {
  route_id: string; headsign?: string | null;
  board_stop: string; alight_stop: string;
  board_at: string; alight_at: string;
}

export interface TransferConnection {
  total_seconds: number; transfer_stop_id: string; wait_seconds: number;
  leg_a: TransferLeg; leg_b: TransferLeg;
}

export interface JourneyResult {
  from_stop_id: string; to_stop_id: string;
  direct_connections: Connection[];
  transfer_connections: TransferConnection[];
}

export interface Me {
  trust_score: number; rank: string; total_reports: number;
  access: 'trial' | 'subscriber' | 'locked';
  trial_days_remaining: number; subscription_price_eur: number;
}

/** Free-Sicht auf eine Kontroll-Meldung (Pro-Felder fehlen physisch — ADR-0006). */
export interface ReportView {
  id: string; city_slug: string; anchor_type: string;
  station_name?: string | null; route_id?: string | null; headsign?: string | null;
  report_type: string; created_at: string; expires_at: string;
  status: string; origin: string;
  trip_id?: string | null; trip_start_date?: string | null;
}

export interface ServiceAlert {
  header?: string | null; description?: string | null; url?: string | null;
  severity?: string | null; route_refs?: string[] | null;
}

export class ApiError extends Error {
  constructor(public code: string, public status: number, public body: unknown) { super(code); }
}

// --- Geräte-Token: Besitz-Entitlement ohne Konto (ADR-0005) -----------------
const TOKEN_KEY = 'tg_device_token';
export const deviceToken = (): string | null => localStorage.getItem(TOKEN_KEY);

let devicePromise: Promise<string> | null = null;
export function ensureDevice(): Promise<string> {
  const existing = deviceToken();
  if (existing) return Promise.resolve(existing);
  // Ein einziger Flug: sonst legen parallele Aufrufe mehrere Geräte an.
  devicePromise ??= fetch(`${API_BASE}/v1/devices`, { method: 'POST' })
    .then(async r => {
      if (!r.ok) throw new ApiError('device_failed', r.status, null);
      const j = await r.json();
      localStorage.setItem(TOKEN_KEY, j.device_token);
      return j.device_token as string;
    })
    .finally(() => { devicePromise = null; });
  return devicePromise;
}

// --- Ticket-First-Gate (docs/18 H1): 24 h gemerkte Bestätigung --------------
const TICKET_KEY = 'tg_ticket_confirmed';
const TICKET_TS = 'tg_ticket_confirmed_at';
export const TICKET_TTL_MS = 24 * 3600e3;

export function ticketConfirmed(): boolean {
  if (localStorage.getItem(TICKET_KEY) !== '1') return false;
  return Date.now() - Number(localStorage.getItem(TICKET_TS) ?? 0) < TICKET_TTL_MS;
}
export function confirmTicket(): void {
  localStorage.setItem(TICKET_KEY, '1');
  localStorage.setItem(TICKET_TS, String(Date.now()));
}

// --- Aufrufkern ------------------------------------------------------------
export type On402 = () => void;
let on402: On402 = () => {};
export const setOn402 = (fn: On402) => { on402 = fn; };

async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  const t = await ensureDevice();
  const headers = new Headers(init.headers ?? {});
  headers.set('X-Device-Token', t);
  headers.set('Content-Type', 'application/json');
  const sub = localStorage.getItem('tg_access_token');
  if (sub) headers.set('Authorization', `Bearer ${sub}`);
  if (ticketConfirmed()) headers.set('X-Ticket-Confirmed', 'true');

  const r = await fetch(`${API_BASE}${path}`, { ...init, headers });
  const body: unknown = await r.json().catch(() => ({}));
  if (r.status === 402) on402();
  if (!r.ok) {
    const code = (body as { error?: { code?: string } })?.error?.code ?? r.statusText;
    throw new ApiError(code, r.status, body);
  }
  return body as T;
}

export const api = {
  me: () => call<Me>('/v1/devices/me'),
  nearby: (lat: number, lon: number, take = 6) =>
    call<Stop[]>(`/v1/cities/${CITY}/stops/nearby?lat=${lat}&lon=${lon}&take=${take}`),
  searchStops: (q: string, take = 8) =>
    call<Stop[]>(`/v1/cities/${CITY}/stops/nearby?q=${encodeURIComponent(q)}&take=${take}`),
  departures: (stopId: string, limit = 8) =>
    call<Departure[]>(`/v1/stops/${encodeURIComponent(stopId)}/departures?limit=${limit}`),
  journey: (fromStopId: string | null, fromLat: number | null, fromLon: number | null, toStopId: string) =>
    call<JourneyResult>('/v1/journeys/search', {
      method: 'POST',
      body: JSON.stringify({ from_stop_id: fromStopId, from_lat: fromLat, from_lon: fromLon, to_stop_id: toStopId })
    }),
  alerts: () => call<ServiceAlert[]>(`/v1/cities/${CITY}/alerts`),
  /** Kontroll-Lesezugriff — nur mit Ticket-Bestätigung, sonst 403 (T-GATE-2). */
  cityReports: () => call<ReportView[]>(`/v1/cities/${CITY}/reports`),
  report: (body: object) =>
    call<ReportView>('/v1/reports', {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify(body)
    }),
  activate: (platform: 'ios' | 'android' | 'web', receipt: string) =>
    call<{ access_token: string; restore_code: string }>('/v1/billing/activate', {
      method: 'POST', body: JSON.stringify({ platform, receipt })
    }),
};

/** Meldung mit Stations-Anker (ich stehe am Bahnsteig). */
export const reportAtStation = (stopId: string, vehicleKind: 'rail' | 'bus' = 'rail') =>
  api.report({
    city_slug: CITY, anchor_type: 'station', report_type: 'on_platform', vehicle_kind: vehicleKind,
    station: { stop_id: stopId }, inspector_count: 1,
    client: { ticket_confirmed: true, platform: 'web' }
  });

/** Meldung mit Trip-Anker (ich sitze in der Fahrt) — wandert mit der Fahrt mit. */
export const reportOnTrip = (
  d: Departure, boardStopId: string, routeId?: string, headsign?: string | null,
  vehicleKind: 'rail' | 'bus' = 'rail'
) =>
  api.report({
    city_slug: CITY, anchor_type: 'trip', report_type: 'in_vehicle', vehicle_kind: vehicleKind,
    station: { stop_id: boardStopId },
    trip: {
      trip_id: d.trip_ref.trip_id, start_date: d.trip_ref.start_date,
      route_id: routeId ?? d.route_id, headsign: headsign ?? d.headsign
    },
    inspector_count: 1,
    client: { ticket_confirmed: true, platform: 'web' }
  });

// --- Formatierung ----------------------------------------------------------
export const fmtTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });

/** „+3 min" / „−1 min" / „pünktlich". Unter einer Minute gilt als pünktlich. */
export function fmtDelay(d?: number | null): string {
  if (d == null || Math.abs(d) < 60) return 'pünktlich';
  const min = Math.round(d / 60);
  return min > 0 ? `+${min} min` : `−${Math.abs(min)} min`;
}

/** Minuten bis zur Abfahrt, für die große Zahl in der Abfahrtszeile. */
export function minutesUntil(iso: string, now = Date.now()): number {
  return Math.round((new Date(iso).getTime() - now) / 60000);
}

export const fmtCountdown = (min: number): string =>
  min <= 0 ? 'jetzt' : min < 60 ? `${min} min` : `${Math.floor(min / 60)} h ${min % 60} min`;
