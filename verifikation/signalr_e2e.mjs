#!/usr/bin/env node
/**
 * SignalR-End-to-End-Beweis (21.08.2026): verbindet sich auf die laufende API,
 * tritt der Free-Gruppe bei (Ticket-Gate-Claim), legt per REST eine Meldung an
 * und erwartet das Live-Event 'report.created' innerhalb 10 s.
 * Nutzung: node verifikation/signalr_e2e.mjs [baseUrl]
 */
import * as signalR from '@microsoft/signalr';

const BASE = process.argv[2] ?? 'http://127.0.0.1:5099';

const dev = await fetch(`${BASE}/v1/devices`, { method: 'POST' }).then(r => r.json());
console.log('device ok:', dev.device_id);

const hub = new signalR.HubConnectionBuilder()
  .withUrl(`${BASE}/hubs/v1/realtime`)
  .build();

let event = null;
hub.on('report.created', e => { event = e; console.log('LIVE-EVENT erhalten:', JSON.stringify(e)); });

await hub.start();
await hub.invoke('join', 'city.hamburg.reports.free', true, null);
console.log('join ok (Ticket-Gate-Claim mitgesendet)');

// Gate-Negativprobe: Join ohne Bestätigung muss abgelehnt werden
try { await hub.invoke('join', 'city.hamburg.reports.pro', false, null); console.log('FEHLER: pro-Join ohne Gate/Token ging durch!'); }
catch (e) { console.log('Negativprobe ok (pro ohne Gate abgelehnt):', String(e.message).slice(0, 60)); }

const res = await fetch(`${BASE}/v1/reports`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json', 'X-Device-Token': dev.device_token, 'Idempotency-Key': crypto.randomUUID() },
  body: JSON.stringify({
    city_slug: 'hamburg', anchor_type: 'station', report_type: 'on_platform', vehicle_kind: 'rail',
    station: { stop_id: 'HHA1' }, inspector_count: 1, client: { ticket_confirmed: true, platform: 'web' }
  })
});
console.log('report POST:', res.status);

const ok = await new Promise(resolve => {
  const t = setTimeout(() => resolve(false), 10000);
  const check = () => { if (event) { clearTimeout(t); resolve(true); } else setTimeout(check, 250); };
  check();
});
await hub.stop();
console.log(ok ? 'ERGEBNIS: PASS (REST -> Hub-Push in <10 s)' : 'ERGEBNIS: FAIL');
process.exit(ok ? 0 : 1);
