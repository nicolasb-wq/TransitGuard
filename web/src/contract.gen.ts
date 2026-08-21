// =============================================================================
// GENERIERT — NICHT VON HAND ÄNDERN.
// Quelle: docs/contract/api-contract.json (aus der laufenden API aufgezeichnet).
// Neu erzeugen: node verifikation/contract_gen_ts.mjs
// Frischeprüfung: scripts/verify-contract.sh (Gate im QA-Loop)
//
// Zweck: Die PWA leitet ihre Typen HIER ab statt sie zweitzuschreiben. Benennt
// der Server ein Feld um, ändert sich diese Datei — und jede Lesestelle im
// Client wird zum Compile-Fehler. Das ist die strukturelle Antwort auf die drei
// Launch-Blocker vom 21.08.2026, die alle Vertragsdrift waren.
// =============================================================================
/* eslint-disable */

/** POST /v1/billing/activate */
export type BillingActivateResponse = {
  access_token: string;
  restore_code: string;
  tier: string;
  valid_until: string;
};

/** POST /v1/billing/activate (Beleg erneut) */
export type BillingActivateKonfliktResponse = {
  error: {
    code: string;
    message: string;
  };
};

/** POST /v1/billing/restore
  * Restore ROTIERT den Zugangstoken — der Token des alten Geraets wird dabei ungueltig. Beabsichtigt (ein Abo, ein Geraet), aber ein Client darf nach einem Restore nicht mehr mit dem alten Token weiterarbeiten. */
export type BillingRestoreResponse = {
  access_token: string;
  tier: string;
};

/** POST /v1/billing/validate */
export type BillingValidateResponse = {
  status: string;
  tier: string;
  valid_until: string;
};

/** POST /v1/billing/validate (alter Token nach Restore) */
export type BillingValidateAbgelaufenResponse = {
  error: {
    code: string;
    message: string;
  };
};

/** GET /v1/cities/{slug}/alerts */
export type CitiesAlertsResponse = unknown[];

/** GET /v1/cities/{slug}/events */
export type CitiesEventsResponse = Array<{
  created_at: string;
  event_id: number;
  report_id: string;
  type: string;
}>;

/** GET /v1/cities */
export type CitiesListResponse = Array<{
  display_name: string;
  is_active: boolean;
  slug: string;
}>;

/** POST /v1/devices */
export type DevicesIssueResponse = {
  device_id: string;
  device_token: string;
};

/** GET /v1/devices/me */
export type DevicesMeResponse = {
  access: string;
  rank: string;
  subscription_price_eur: number;
  total_reports: number;
  trial_days_remaining: number;
  trust_score: number;
};

/** GET /v1/cities/{slug}/stops/nearby (Fehlerfall) */
export type FehlerValidationResponse = {
  error: {
    code: string;
    message: string;
  };
};

/** GET /health/live */
export type HealthLiveResponse = {
  status: string;
};

/** GET /health/ready */
export type HealthReadyResponse = {
  status: string;
};

/** POST /v1/journeys/search
  * next_departures traegt KEIN route_id/headsign — die stehen auf der Verbindung. Genau diese Asymmetrie war Launch-Blocker 2 (Flutter-TypeError). */
export type JourneysSearchResponse = {
  direct_connections: Array<{
    direction_id: number;
    headsign: string;
    next_departures: Array<{
      realtime: boolean;
      scheduled_time: string;
      trip_ref: {
        start_date: string;
        trip_id: string;
      };
      warnings: unknown[];
    }>;
    ride_seconds: number;
    route_id: string;
    stops_count: number;
  }>;
  from_stop_id: string;
  to_stop_id: string;
  transfer_connections: Array<{
    leg_a: {
      alight_at: string;
      alight_stop: string;
      board_at: string;
      board_stop: string;
      headsign: string;
      route_id: string;
    };
    leg_b: {
      alight_at: string;
      alight_stop: string;
      board_at: string;
      board_stop: string;
      headsign: string;
      route_id: string;
    };
    total_seconds: number;
    transfer_stop_id: string;
    wait_seconds: number;
  }>;
};

/** GET /v1/journeys/{tripId}/warnings */
export type JourneysWarningsResponse = unknown[];

/** POST /v1/reports */
export type ReportsCreateResponse = {
  anchor_type: string;
  city_slug: string;
  created_at: string;
  expires_at: string;
  id: string;
  origin: string;
  report_type: string;
  station_name: string;
  status: string;
};

/** POST /v1/reports/{id}/events */
export type ReportsEventResponse = null;

/** GET /v1/cities/{slug}/reports
  * route_id/headsign/trip_id nur bei Trip-Anker — bei Stations-Anker fehlen sie (WhenWritingNull). Deshalb optional. */
export type ReportsListResponse = Array<{
  anchor_type: string;
  city_slug: string;
  created_at: string;
  expires_at: string;
  headsign?: string;
  id: string;
  origin: string;
  report_type: string;
  route_id?: string;
  station_name: string;
  status: string;
  trip_id?: string;
  trip_start_date?: string;
}>;

/** GET /v1/stops/{stopId}/departures */
export type StopsDeparturesResponse = Array<{
  headsign: string;
  realtime: boolean;
  route_id: string;
  scheduled_time: string;
  trip_ref: {
    start_date: string;
    trip_id: string;
  };
  warnings: unknown[];
}>;

/** GET /v1/cities/{slug}/stops/nearby
  * distance_km nur bei lat/lon-Suche — Namenssuche liefert es nicht. */
export type StopsNearbyResponse = Array<{
  distance_km?: number;
  lat: number;
  lon: number;
  stop_id: string;
  stop_name: string;
}>;

/** Pflicht-Header je Endpunkt — im Vertrag durch Negativproben belegt. */
export const PFLICHT_HEADER = {
  "billing.restore": [
    "X-Device-Token"
  ],
  "cities.events": [
    "X-Device-Token",
    "X-Ticket-Confirmed"
  ],
  "devices.me": [
    "X-Device-Token"
  ],
  "journeys.warnings": [
    "X-Device-Token",
    "X-Ticket-Confirmed"
  ],
  "reports.create": [
    "X-Device-Token",
    "Idempotency-Key"
  ],
  "reports.list": [
    "X-Device-Token",
    "X-Ticket-Confirmed"
  ]
} as const;
