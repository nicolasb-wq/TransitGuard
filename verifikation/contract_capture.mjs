// ---------------------------------------------------------------------------
// Vertrags-Aufzeichnung: fährt die LAUFENDE API ab und leitet aus den echten
// Antworten die Feldform ab. Ergebnis: docs/contract/api-contract.json
// (maßgebliches Artefakt) plus normalisierte Beispiel-Antworten unter
// docs/contract/samples/ für die Client-Tests.
//
// Warum aufgezeichnet statt aus dem Server generiert: Swashbuckle liefert für
// 18 von 18 Operationen KEIN Antwortschema, weil alle Controller IActionResult
// mit anonym geformten Antworten zurückgeben (am 21.08.2026 gemessen, Beleg in
// docs/27-build-log.md §A). Ein aufgezeichneter Vertrag misst, was der Server
// TATSÄCHLICH sendet — genau die Fehlerklasse, die dreimal zugeschlagen hat.
//
// Nutzung:  API=http://127.0.0.1:5099 node verifikation/contract_capture.mjs [--out <verzeichnis>]
// ---------------------------------------------------------------------------
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const API = process.env.API ?? 'http://127.0.0.1:5099';
const argOut = process.argv.indexOf('--out');
const OUT = argOut > 0 ? process.argv[argOut + 1] : 'docs/contract';

// --- Formableitung ---------------------------------------------------------
/** Ein Container ist ein Objekt; jedes Array-Element ist eine eigene Instanz. */
function walk(value, pfad, typen, instanzen) {
  const merke = (p, t) => (typen[p] ??= new Set()).add(t);

  if (value === null) { merke(pfad, 'null'); return; }
  if (Array.isArray(value)) {
    merke(pfad, 'array');
    for (const v of value) walk(v, `${pfad}[]`, typen, instanzen);
    return;
  }
  if (typeof value === 'object') {
    merke(pfad, 'object');
    const inst = (instanzen[pfad] ??= { anzahl: 0, schluessel: {} });
    inst.anzahl++;
    for (const [k, v] of Object.entries(value)) {
      inst.schluessel[k] = (inst.schluessel[k] ?? 0) + 1;
      walk(v, pfad ? `${pfad}.${k}` : k, typen, instanzen);
    }
    return;
  }
  merke(pfad, typeof value);   // string | number | boolean
}

/** Mehrere Beobachtungen desselben Endpunkts zu einer Feldkarte verdichten. */
function verdichte(beobachtungen) {
  const typen = {}, instanzen = {};
  for (const b of beobachtungen) walk(b, '', typen, instanzen);

  const felder = {};
  for (const [pfad, ts] of Object.entries(typen)) {
    const punkt = pfad.lastIndexOf('.');
    const container = punkt >= 0 ? pfad.slice(0, punkt) : '';
    const schluessel = punkt >= 0 ? pfad.slice(punkt + 1) : null;
    let optional = false;
    if (schluessel !== null && instanzen[container]) {
      // Optional heißt: mindestens eine Instanz des Containers hatte das Feld NICHT.
      optional = (instanzen[container].schluessel[schluessel] ?? 0) < instanzen[container].anzahl;
    }
    felder[pfad || '(wurzel)'] = {
      typen: [...ts].sort(),
      ...(optional ? { optional: true } : {}),
    };
  }
  return Object.fromEntries(Object.entries(felder).sort(([a], [b]) => a.localeCompare(b)));
}

// --- Normalisierung für stabile Beispiel-Dateien ----------------------------
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ISO = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/;
const FIXE_UUID = '00000000-0000-4000-8000-000000000000';
const FIXE_ZEIT = '2026-08-21T20:00:00+00:00';
const TOKEN_FELDER = new Set(['device_token', 'access_token', 'restore_code', 'device_id']);

function normalisiere(v, schluessel = null) {
  if (Array.isArray(v)) return v.map(x => normalisiere(x));
  if (v && typeof v === 'object') {
    return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, normalisiere(x, k)]));
  }
  if (typeof v === 'string') {
    if (schluessel && TOKEN_FELDER.has(schluessel)) return schluessel === 'device_id' ? FIXE_UUID : 'TOKEN';
    if (UUID.test(v)) return FIXE_UUID;
    if (ISO.test(v)) return FIXE_ZEIT;
  }
  return v;
}

// --- HTTP ------------------------------------------------------------------
async function ruf(methode, pfad, { headers = {}, body, erwartet } = {}) {
  const r = await fetch(API + pfad, {
    method: methode,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await r.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* kein JSON */ }
  if (erwartet !== undefined && r.status !== erwartet) {
    throw new Error(`${methode} ${pfad}: erwartet ${erwartet}, bekam ${r.status} — ${text.slice(0, 160)}`);
  }
  return { status: r.status, json };
}

const uuid = () => crypto.randomUUID();

// --- Szenarien -------------------------------------------------------------
const vertrag = { erzeugtVon: 'verifikation/contract_capture.mjs', endpunkte: {} };
const beispiele = {};

function erfasse(schluessel, methode, pfad, beobachtungen, extra = {}) {
  const felder = verdichte(beobachtungen);
  // Eine leere Liste (oder null) sagt NICHTS ueber die Elementform. Das wird
  // ausgewiesen statt stillschweigend als "keine Felder" verbucht — sonst
  // entstuenden aus einer Luecke scheinbar gueltige Client-Typen.
  const nurWurzel = Object.keys(felder).filter(k => k !== '(wurzel)').length === 0;
  const unbeobachtet = nurWurzel && (felder['(wurzel)']?.typen ?? []).some(t => t === 'array' || t === 'null');
  vertrag.endpunkte[schluessel] = {
    methode, pfad,
    ...(unbeobachtet ? { unbeobachtet: true } : {}),
    ...extra,
    felder,
  };
  // ALLE Beobachtungen werden abgelegt: <schluessel>.json ist die erste,
  // <schluessel>.2.json die zweite usw. Eine Heuristik "nimm die groesste"
  // greift daneben — bei der Fahrtensuche ist die Direktsuche mit vielen
  // Taktabfahrten laenger als die Umstiegssuche, und ausgerechnet der Umstieg
  // ist die interessante Beobachtung (am 22.08.2026 so aufgefallen).
  beobachtungen.forEach((b, i) => {
    beispiele[i === 0 ? schluessel : `${schluessel}.${i + 1}`] = normalisiere(b);
  });
}

const geraet = async () => (await ruf('POST', '/v1/devices', { erwartet: 201 })).json.device_token;

async function main() {
  // --- Infrastruktur ---
  erfasse('health.live', 'GET', '/health/live', [(await ruf('GET', '/health/live', { erwartet: 200 })).json]);
  erfasse('health.ready', 'GET', '/health/ready', [(await ruf('GET', '/health/ready', { erwartet: 200 })).json]);

  // --- Gerät ---
  const a = await ruf('POST', '/v1/devices', { erwartet: 201 });
  erfasse('devices.issue', 'POST', '/v1/devices', [a.json], { status: 201 });
  const T = a.json.device_token;
  const H = { 'X-Device-Token': T };
  const HT = { ...H, 'X-Ticket-Confirmed': 'true' };

  erfasse('devices.me', 'GET', '/v1/devices/me', [(await ruf('GET', '/v1/devices/me', { headers: H, erwartet: 200 })).json], {
    erfordert: { headers: ['X-Device-Token'] },
    belegtDurch: [{ ohne: 'X-Device-Token', status: (await ruf('GET', '/v1/devices/me')).status }],
  });

  erfasse('cities.list', 'GET', '/v1/cities', [(await ruf('GET', '/v1/cities', { headers: H, erwartet: 200 })).json]);

  // --- Fahrplan: zwei Beobachtungen, damit optionale Felder sichtbar werden ---
  const nahGeo = (await ruf('GET', '/v1/cities/hamburg/stops/nearby?lat=53.554&lon=9.991&take=4', { headers: H, erwartet: 200 })).json;
  const nahName = (await ruf('GET', '/v1/cities/hamburg/stops/nearby?q=Jung&take=4', { headers: H, erwartet: 200 })).json;
  erfasse('stops.nearby', 'GET', '/v1/cities/{slug}/stops/nearby', [nahGeo, nahName], {
    hinweis: 'distance_km nur bei lat/lon-Suche — Namenssuche liefert es nicht.',
  });

  const stopId = nahGeo[0].stop_id;
  const abf = (await ruf('GET', `/v1/stops/${stopId}/departures?limit=5`, { headers: H, erwartet: 200 })).json;
  erfasse('stops.departures', 'GET', '/v1/stops/{stopId}/departures', [abf]);

  // ZWEI Beobachtungen: eine Direktstrecke und eine, die einen Umstieg erzwingt.
  // Ohne die zweite bliebe transfer_connections leer und die Umstiegsform
  // unbeobachtet — genau die Luecke, in der Launch-Blocker 1 sass.
  const ziel = nahGeo.find(s => s.stop_id !== stopId).stop_id;
  const direkt = (await ruf('POST', '/v1/journeys/search', { headers: H, body: { from_stop_id: stopId, to_stop_id: ziel }, erwartet: 200 })).json;
  const mitUmstieg = (await ruf('POST', '/v1/journeys/search', { headers: H, body: { from_stop_id: 'HHA1', to_stop_id: 'HHA5' }, erwartet: 200 })).json;
  const fahrt = direkt;
  erfasse('journeys.search', 'POST', '/v1/journeys/search', [direkt, mitUmstieg], {
    hinweis: 'next_departures traegt KEIN route_id/headsign — die stehen auf der Verbindung. '
           + 'Genau diese Asymmetrie war Launch-Blocker 2 (Flutter-TypeError).',
  });

  const eineFahrt = fahrt.direct_connections.flatMap(c => c.next_departures)[0];
  if (eineFahrt) {
    const wPfad = `/v1/journeys/${eineFahrt.trip_ref.trip_id}/warnings`
      + `?start_date=${eineFahrt.trip_ref.start_date}&from_stop_id=${stopId}`;
    // Auch dieser Endpunkt liegt hinter dem Ticket-Gate (bei der Aufzeichnung
    // am 21.08.2026 aufgefallen — er stand in keiner Doku als Gate-Endpunkt).
    const wOhne = await ruf('GET', wPfad, { headers: H });
    const w = (await ruf('GET', wPfad, { headers: HT, erwartet: 200 })).json;
    erfasse('journeys.warnings', 'GET', '/v1/journeys/{tripId}/warnings', [w], {
      erfordert: { headers: ['X-Device-Token', 'X-Ticket-Confirmed'] },
      belegtDurch: [{ ohne: 'X-Ticket-Confirmed', status: wOhne.status, code: wOhne.json?.error?.code }],
    });
  }

  erfasse('cities.alerts', 'GET', '/v1/cities/{slug}/alerts',
    [(await ruf('GET', '/v1/cities/hamburg/alerts', { headers: H, erwartet: 200 })).json]);

  // --- Meldungen: Pflicht-Header werden BEWIESEN, nicht behauptet ---
  const meldung = {
    city_slug: 'hamburg', anchor_type: 'station', report_type: 'on_platform', vehicle_kind: 'rail',
    station: { stop_id: stopId }, inspector_count: 1,
    client: { ticket_confirmed: true, platform: 'web' },
  };
  const ohneIdem = await ruf('POST', '/v1/reports', { headers: HT, body: meldung });
  const ohneTicket = await ruf('POST', '/v1/reports', {
    headers: { ...HT, 'Idempotency-Key': uuid() },
    body: { ...meldung, client: { ...meldung.client, ticket_confirmed: false } },
  });
  const erstellt = await ruf('POST', '/v1/reports', {
    headers: { ...HT, 'Idempotency-Key': uuid() }, body: meldung, erwartet: 201,
  });
  erfasse('reports.create', 'POST', '/v1/reports', [erstellt.json], {
    status: 201,
    erfordert: { headers: ['X-Device-Token', 'Idempotency-Key'], body: ['client.ticket_confirmed = true'] },
    belegtDurch: [
      { ohne: 'Idempotency-Key', status: ohneIdem.status, code: ohneIdem.json?.error?.code },
      { ohne: 'client.ticket_confirmed', status: ohneTicket.status, code: ohneTicket.json?.error?.code },
    ],
  });

  // ZWEITE Meldung mit TRIP-Anker: nur dort tragen die Meldungen route_id und
  // headsign. Mit ausschliesslich stationsverankerten Meldungen fehlten beide im
  // Vertrag — und die Oberflaeche, die sie anzeigt, waere faelschlich als
  // vertragswidrig gemeldet worden (am 22.08.2026 genau so aufgefallen).
  const naechste = (await ruf('GET', `/v1/stops/${stopId}/departures?limit=1`, { headers: H, erwartet: 200 })).json[0];
  if (naechste) {
    await ruf('POST', '/v1/reports', {
      headers: { ...HT, 'Idempotency-Key': uuid() },
      body: {
        ...meldung, anchor_type: 'trip', report_type: 'in_vehicle',
        trip: {
          trip_id: naechste.trip_ref.trip_id, start_date: naechste.trip_ref.start_date,
          route_id: naechste.route_id, headsign: naechste.headsign,
        },
      },
    });
  }

  const ohneTicketHeader = await ruf('GET', '/v1/cities/hamburg/reports', { headers: H });
  erfasse('reports.list', 'GET', '/v1/cities/{slug}/reports',
    [(await ruf('GET', '/v1/cities/hamburg/reports', { headers: HT, erwartet: 200 })).json], {
      hinweis: 'route_id/headsign/trip_id nur bei Trip-Anker — bei Stations-Anker '
             + 'fehlen sie (WhenWritingNull). Deshalb optional.',
      erfordert: { headers: ['X-Device-Token', 'X-Ticket-Confirmed'] },
      belegtDurch: [{ ohne: 'X-Ticket-Confirmed', status: ohneTicketHeader.status, code: ohneTicketHeader.json?.error?.code }],
    });

  const ereignisseOhne = await ruf('GET', '/v1/cities/hamburg/events', { headers: H });
  erfasse('cities.events', 'GET', '/v1/cities/{slug}/events',
    [(await ruf('GET', '/v1/cities/hamburg/events', { headers: HT, erwartet: 200 })).json], {
      erfordert: { headers: ['X-Device-Token', 'X-Ticket-Confirmed'] },
      belegtDurch: [{ ohne: 'X-Ticket-Confirmed', status: ereignisseOhne.status, code: ereignisseOhne.json?.error?.code }],
    });

  // Bestaetigen braucht ein ZWEITES Geraet — die eigene Meldung wird abgelehnt.
  const T2 = await geraet();
  const H2 = { 'X-Device-Token': T2, 'X-Ticket-Confirmed': 'true' };
  const ereignis = await ruf('POST', `/v1/reports/${erstellt.json.id}/events`, { headers: H2, body: { type: 'confirm' } });
  erfasse('reports.event', 'POST', '/v1/reports/{id}/events', [ereignis.json], { status: ereignis.status });

  // --- Abrechnung ---
  // Beleg je Lauf eindeutig: sonst antwortet ein zweiter Lauf gegen dieselbe
  // laufende API mit 409 receipt_already_used und der Erfolgsfall bliebe
  // unaufgezeichnet (am 22.08.2026 genau so passiert).
  const beleg = `SANDBOX-${uuid()}`;
  const akt = await ruf('POST', '/v1/billing/activate', { headers: H, body: { platform: 'web', receipt: beleg }, erwartet: 201 });
  erfasse('billing.activate', 'POST', '/v1/billing/activate', [akt.json], { status: 201 });

  // Zweiter Versuch mit demselben Beleg: dokumentierter Konfliktfall.
  const doppelt = await ruf('POST', '/v1/billing/activate', { headers: H, body: { platform: 'web', receipt: beleg } });
  erfasse('billing.activate.konflikt', 'POST', '/v1/billing/activate (Beleg erneut)', [doppelt.json], { status: doppelt.status });

  // Wiederherstellung auf einem ANDEREN Geraet — der eigentliche Zweck des Codes.
  const T3 = await geraet();
  const wieder = await ruf('POST', '/v1/billing/restore',
    { headers: { 'X-Device-Token': T3 }, body: { restore_code: akt.json.restore_code }, erwartet: 201 });
  erfasse('billing.restore', 'POST', '/v1/billing/restore', [wieder.json], {
    status: 201,
    erfordert: { headers: ['X-Device-Token'] },
    hinweis: 'Restore ROTIERT den Zugangstoken — der Token des alten Geraets wird '
           + 'dabei ungueltig. Beabsichtigt (ein Abo, ein Geraet), aber ein Client darf '
           + 'nach einem Restore nicht mehr mit dem alten Token weiterarbeiten.',
    belegtDurch: [{ ohne: 'X-Device-Token', status: (await ruf('POST', '/v1/billing/restore', { body: { restore_code: 'X' } })).status }],
  });

  // Alter Token nach dem Restore: muss abgelehnt werden (Beleg fuer den Hinweis oben).
  const altTot = await ruf('POST', '/v1/billing/validate', { headers: H, body: { token: akt.json.access_token } });
  erfasse('billing.validate.abgelaufen', 'POST', '/v1/billing/validate (alter Token nach Restore)',
    [altTot.json], { status: altTot.status });

  const val = await ruf('POST', '/v1/billing/validate', { headers: H, body: { token: wieder.json.access_token }, erwartet: 200 });
  erfasse('billing.validate', 'POST', '/v1/billing/validate', [val.json], { status: 200 });

  // --- Fehlerform (die Clients lesen error.code) ---
  const fehler = await ruf('GET', '/v1/cities/hamburg/stops/nearby', { headers: H });   // lat/lon+q fehlen
  erfasse('fehler.validation', 'GET', '/v1/cities/{slug}/stops/nearby (Fehlerfall)', [fehler.json], { status: fehler.status });

  // --- Schreiben ---
  mkdirSync(join(OUT, 'samples'), { recursive: true });
  vertrag.endpunkte = Object.fromEntries(Object.entries(vertrag.endpunkte).sort(([a], [b]) => a.localeCompare(b)));
  writeFileSync(join(OUT, 'api-contract.json'), JSON.stringify(vertrag, null, 2) + '\n');
  for (const [k, v] of Object.entries(beispiele)) {
    writeFileSync(join(OUT, 'samples', `${k}.json`), JSON.stringify(v, null, 2) + '\n');
  }
  console.log(`Vertrag geschrieben: ${Object.keys(vertrag.endpunkte).length} Endpunkte, `
    + `${Object.keys(beispiele).length} Beispiele → ${OUT}`);
}

await main();
