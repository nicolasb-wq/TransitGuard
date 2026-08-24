// ---------------------------------------------------------------------------
// API-Client der PWA.
//
// Die Typen werden NICHT hier zweitgeschrieben, sondern aus dem aufgezeichneten
// Server-Vertrag abgeleitet (docs/contract/api-contract.json →
// src/contract.gen.ts). Benennt der Server ein Feld um, ändert sich die
// generierte Datei und jede Lesestelle im Client wird zum Compile-Fehler.
// Das ist die strukturelle Antwort auf drei Launch-Blocker vom 21.08.2026,
// die alle Vertragsdrift waren.
//
// Grenze, die der Vertrag NICHT abdeckt: Er kennt nur, was die Aufzeichnung
// gesehen hat. Ein Feld, das der Server als nullable führt, aber in der Fixture
// nie null war, erscheint hier als nicht-nullable. Der Vertrag fängt also
// Umbenennungen und Strukturbrüche — nicht jede denkbare Nullability.
// Defensive Prüfungen im UI bleiben deshalb richtig.
// ---------------------------------------------------------------------------
import type * as V from './contract.gen';

export const API_BASE = import.meta.env.VITE_API_URL ?? '';
export const CITY = 'hamburg';

// --- Aus dem Vertrag abgeleitet --------------------------------------------
export type Stop = V.StopsNearbyResponse[number];
export type Me = V.DevicesMeResponse;
export type ReportView = V.ReportsListResponse[number];

export type TransferConnection = V.JourneysSearchResponse['transfer_connections'][number];
export type TransferLeg = TransferConnection['leg_a'];

/**
 * Verbindung — Feldnamen aus dem Vertrag, nur `next_departures` bewusst
 * aufgeweitet: das Element von `warnings` ist im Lokal-Modus nicht
 * aufzeichenbar (kein Ingest, keine passende aktive Meldung), der Vertrag führt
 * es deshalb als `unknown[]`. Die Namen der Warnfelder sind stattdessen
 * serverseitig durch T-RTSHAPE-2 festgenagelt.
 */
export type Connection =
  Omit<V.JourneysSearchResponse['direct_connections'][number], 'next_departures'>
  & { next_departures: Departure[] };

/** Antwort der Fahrtensuche mit der aufgeweiteten Verbindung. */
export type JourneyResult =
  Omit<V.JourneysSearchResponse, 'direct_connections'> & { direct_connections: Connection[] };

/**
 * Kontrollhinweis auf der Fahrt. Seit 24.08.2026 AUS DEM VERTRAG abgeleitet:
 * `/v1/journeys/{tripId}/warnings` galt bis dahin als unbeobachtet, weil die
 * Aufzeichnung die Warnungen abfragte, BEVOR eine Meldung an der Fahrt hing —
 * und dann nochmal mit from_stop_id == Meldungs-Halt, was der Server bewusst
 * verwirft (idx <= iFrom). Der Server war nie kaputt, die Beobachtung war es.
 * `report_id` fehlte im handgeschriebenen Typ vollstaendig.
 */
export type ControlWarning = V.JourneysWarningsResponse[number];

/**
 * Abfahrt. Basis ist die Form aus der Fahrtensuche; die Echtzeit-Anteile
 * (delay_s, estimated_time, warnings[]) und route_id/headsign sind ergänzt,
 * weil sie im Lokal-Modus nicht aufzeichenbar sind — dort läuft kein Ingest.
 * Ihre Feldnamen sind stattdessen serverseitig durch T-RTSHAPE festgenagelt
 * (tests/TransitGuard.Api.Tests/RealtimeShapeTests.cs).
 *
 * ACHTUNG Vertrags-Asymmetrie: POST /v1/journeys/search liefert route_id und
 * headsign NICHT auf der Abfahrt (dort stehen sie auf der Verbindung),
 * GET /v1/stops/{id}/departures dagegen schon. Deshalb beide optional.
 */
export type Departure =
  Omit<V.JourneysSearchResponse['direct_connections'][number]['next_departures'][number], 'warnings'> & {
    route_id?: string;
    headsign?: string | null;
    delay_s?: number | null;
    estimated_time?: string | null;
    warnings?: ControlWarning[] | null;
  };

/**
 * Stoerungsmeldungen. Seit 24.08.2026 AUS DEM VERTRAG abgeleitet statt handgeschrieben:
 * `/v1/cities/{slug}/alerts` lieferte bis dahin immer eine leere Liste, weil der
 * PollRealtimeJob die normalisierten Alerts zwar baute, aber nirgendwo hinschrieb.
 * Der handgeschriebene Typ hatte dabei `severity: string` — der Server liefert `number`.
 * Genau diese Drift kann nur eine echte Beobachtung finden.
 */
export type ServiceAlert = V.CitiesAlertsResponse[number];

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
