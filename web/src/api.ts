// API-Client: Basis-URL via VITE_API_URL (Prod) oder gleicher Origin/Dev-Proxy.
export const API_BASE = import.meta.env.VITE_API_URL ?? '';

export interface Stop { stop_id: string; stop_name: string; lat: number; lon: number; distance_km?: number }

export interface Departure {
  trip_ref: { trip_id: string; start_date: string };
  route_id: string; headsign?: string | null;
  scheduled_time: string; estimated_time?: string | null;
  delay_s?: number | null; realtime: boolean;
  warnings?: { affected_stop_id: string; affected_stop_sequence: number; user_eta_seconds?: number | null; message: string }[] | null;
}

export interface Connection {
  route_id: string; direction_id?: number | null; headsign?: string | null;
  ride_seconds: number; stops_count: number; next_departures: Departure[];
}

export interface TransferConnection {
  total_seconds: number; transfer_stop_id: string; wait_seconds: number;
  leg_a: { RouteId: string; Headsign?: string | null; board_stop: string; alight_stop: string; board_at: string; alight_at: string };
  leg_b: { RouteId: string; Headsign?: string | null; board_stop: string; alight_stop: string; board_at: string; alight_at: string };
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

const tokenKey = 'tg_device_token';
export const deviceToken = (): string | null => localStorage.getItem(tokenKey);

export async function ensureDevice(): Promise<string> {
  const existing = deviceToken();
  if (existing) return existing;
  const r = await fetch(`${API_BASE}/v1/devices`, { method: 'POST' });
  if (!r.ok) throw new Error('Geräte-Token konnte nicht geholt werden');
  const j = await r.json();
  localStorage.setItem(tokenKey, j.device_token);
  return j.device_token as string;
}

async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  const t = await ensureDevice();
  const headers = new Headers(init.headers ?? {});
  headers.set('X-Device-Token', t);
  headers.set('Content-Type', 'application/json');
  const sub = localStorage.getItem('tg_access_token');
  if (sub) headers.set('Authorization', `Bearer ${sub}`);
  if (localStorage.getItem('tg_ticket_confirmed') === '1') headers.set('X-Ticket-Confirmed', 'true');
  const r = await fetch(`${API_BASE}${path}`, { ...init, headers });
  if (r.status === 402) location.hash = '#abo';
  const body = await r.json().catch(() => ({}));
  if (!r.ok) throw Object.assign(new Error(body?.error?.code ?? r.statusText), { body, status: r.status });
  return body as T;
}

export const api = {
  me: () => call<Me>('/v1/devices/me'),
  nearby: (lat: number, lon: number) => call<Stop[]>(`/v1/cities/hamburg/stops/nearby?lat=${lat}&lon=${lon}&take=6`),
  searchStops: (q: string) => call<Stop[]>(`/v1/cities/hamburg/stops/nearby?q=${encodeURIComponent(q)}&take=8`),
  departures: (stopId: string) => call<Departure[]>(`/v1/stops/${stopId}/departures`),
  journey: (fromStopId: string | null, fromLat: number | null, fromLon: number | null, toStopId: string) =>
    call<JourneyResult>('/v1/journeys/search', {
      method: 'POST',
      body: JSON.stringify({ from_stop_id: fromStopId, from_lat: fromLat, from_lon: fromLon, to_stop_id: toStopId })
    }),
  report: (body: object, idemKey: string) =>
    call<object>('/v1/reports', { method: 'POST', headers: { 'Idempotency-Key': idemKey }, body: JSON.stringify(body) }),
  activate: (platform: 'ios' | 'android' | 'web', receipt: string) =>
    call<{ access_token: string; restore_code: string }>('/v1/billing/activate', {
      method: 'POST', body: JSON.stringify({ platform, receipt })
    }),
};

export const fmtTime = (iso: string) => new Date(iso).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
export const fmtDelay = (d?: number | null) => !d ? '' : d >= 60 ? `+${Math.floor(d / 60)} min` : d > 0 ? `+${d} s` : `${Math.floor(d / 60)} min`;
