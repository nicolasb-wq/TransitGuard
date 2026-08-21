import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { api, ensureDevice, fmtDelay, fmtTime, type JourneyResult, type Me, type Stop } from './api';

// ---------- Ticket-First-Gate (docs/18 H1): 24 h gemerkte Bestätigung ----------
function useTicketGate() {
  const [ask, setAsk] = useState(false);
  const KEY = 'tg_ticket_confirmed';
  const TS = 'tg_ticket_confirmed_at';
  useEffect(() => {
    const ok = localStorage.getItem(KEY) === '1';
    const fresh = Date.now() - Number(localStorage.getItem(TS) ?? 0) < 24 * 3600e3;
    if (!ok || !fresh) setAsk(true);
  }, []);
  const confirm = () => { localStorage.setItem(KEY, '1'); localStorage.setItem(TS, String(Date.now())); setAsk(false); };
  return { ask, confirm, buy: () => { location.hash = '#ticket'; } };
}

function TicketGateDialog({ onConfirm, onBuy }: { onConfirm: () => void; onBuy: () => void }) {
  return (
    <div className="overlay">
      <div className="card gate">
        <h2>Bist du gerade mit gültigem Ticket unterwegs?</h2>
        <p className="muted">Deutschlandticket, hvv-Ticket, Jobticket oder Einzelticket. Kontrollhinweise sind für Fahrgäste mit gültigem Fahrschein gedacht — danke fürs Fairplay.</p>
        <button className="primary" onClick={onConfirm}>✓ Ja, ich habe ein gültiges Ticket</button>
        <button className="ghost" onClick={onBuy}>Ticket kaufen →</button>
        <p className="tiny">Der Fahrplan-Companion (Abfahrten, Störungen, Barrierefreiheit) bleibt ohne Bestätigung nutzbar.</p>
      </div>
    </div>
  );
}

// ---------- Haupt-App ----------
export default function App() {
  const gate = useTicketGate();
  const [me, setMe] = useState<Me | null>(null);
  const [nearby, setNearby] = useState<Stop[]>([]);
  const [dest, setDest] = useState('');
  const [destResults, setDestResults] = useState<Stop[]>([]);
  const [result, setResult] = useState<JourneyResult | null>(null);
  const [live, setLive] = useState<string[]>([]);
  const hubRef = useRef<signalR.HubConnection | null>(null);
  const [fromLabel, setFromLabel] = useState('—');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');

  useEffect(() => { api.me().then(setMe).catch(() => {}); }, []);
  useEffect(() => { ensureDevice().catch(() => {}); }, []);

  // SignalR-Live-Feed: neue Kontrollhinweise der Stadt (Ticket-Gate-Claim mitgesendet, docs/06)
  useEffect(() => {
    if (localStorage.getItem('tg_ticket_confirmed') !== '1') return;
    const hub = new signalR.HubConnectionBuilder()
      .withUrl(`${import.meta.env.VITE_API_URL ?? ''}/hubs/v1/realtime`, { accessTokenFactory: () => localStorage.getItem('tg_device_token') ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();
    hub.on('report.created', () =>
      setLive(l => [`Neuer Kontrollhinweis (${new Date().toLocaleTimeString('de-DE')})`, ...l].slice(0, 5)));
    hub.on('control.ticket_gate', () => setLive(l => ['Ticket-Bestätigung erforderlich', ...l].slice(0, 5)));
    hubRef.current = hub;
    hub.start().then(() => hub.invoke('join', 'city.hamburg.reports.free', true, null).catch(() => {})).catch(() => {});
    return () => { hub.stop().catch(() => {}); };
  }, []);

  const useLocation = () => {
    if (!navigator.geolocation) { setErr('Standort nicht verfügbar'); return; }
    setBusy(true);
    navigator.geolocation.getCurrentPosition(
      async p => {
        const stops = await api.nearby(p.coords.latitude, p.coords.longitude);
        setNearby(stops); setFromLabel(stops[0] ? `${stops[0].stop_name} (${stops[0].distance_km} km)` : '—');
        setBusy(false);
      },
      () => { setErr('Standort-Zugriff verweigert'); setBusy(false); },
      { enableHighAccuracy: true, timeout: 8000 }
    );
  };

  const searchDest = async () => {
    if (dest.trim().length < 2) return;
    setDestResults(await api.searchStops(dest.trim()).catch(() => []));
  };

  const findLine = async (fromStopId: string) => {
    const target = destResults[0]?.stop_id ?? destResults.find(s => s.stop_id)?.stop_id;
    if (!target) { setErr('Bitte Ziel auswählen'); return; }
    setBusy(true); setErr('');
    try {
      const r = await api.journey(fromStopId, null, null, target);
      setResult(r);
    } catch (e) { setErr((e as Error).message); }
    setBusy(false);
  };

  return (
    <div className="app">
      {gate.ask && <TicketGateDialog onConfirm={gate.confirm} onBuy={gate.buy} />}
      <header>
        <h1>TransitGuard <span className="city">Hamburg & Umland</span></h1>
        {me && me.access === 'trial' && <div className="trial">Testphase: {me.trial_days_remaining} Tage · danach {me.subscription_price_eur.toFixed(2)} €/Monat <a href="#abo">Abo</a></div>}
        {me && me.access === 'locked' && <div className="trial locked">Testphase abgelaufen — <a href="#abo">jetzt abonnieren</a></div>}
      </header>

      <section className="panel">
        <h2>Deine Fahrt</h2>
        <div className="row">
          <button className="ghost" onClick={useLocation}>📍 Standort</button>
          <span className="muted">Start: {fromLabel}</span>
        </div>
        <div className="nearby">
          {nearby.map(s => (
            <button key={s.stop_id} className="chip" onClick={() => findLine(s.stop_id)}>{s.stop_name}</button>
          ))}
        </div>
        <div className="row">
          <input placeholder="Ziel: Haltestelle suchen…" value={dest} onChange={e => setDest(e.target.value)} />
          <button className="primary" onClick={searchDest}>Suchen</button>
        </div>
        <div className="nearby">
          {destResults.map(s => (
            <button key={s.stop_id} className={`chip ${destResults[0]?.stop_id === s.stop_id ? 'sel' : ''}`}
              onClick={() => setDestResults([s, ...destResults.filter(x => x.stop_id !== s.stop_id)])}>{s.stop_name}</button>
          ))}
        </div>
        {busy && <p className="muted">Suche…</p>}
        {err && <p className="err">{err}</p>}
      </section>

      {result !== null && (
        <section className="panel">
          <h2>Linien & nächste Abfahrten</h2>
          {result.direct_connections.length === 0 && result.transfer_connections.length === 0 && <p className="muted">Keine Verbindung gefunden.</p>}
          {result.transfer_connections.length > 0 && (
            <div className="connection">
              <div className="line">🔁 Mit 1 Umstieg</div>
              {result.transfer_connections.map((tr, i) => (
                <div key={i} className="dep">
                  <b>{tr.leg_a.RouteId}</b> → {fmtTime(tr.leg_a.alight_at)} umsteigen <b>{tr.leg_b.RouteId}</b> → Ankunft {fmtTime(tr.leg_b.alight_at)}
                  <span className="muted">({Math.round(tr.total_seconds / 60)} min, Wartezeit {Math.round(tr.wait_seconds / 60)} min)</span>
                </div>
              ))}
            </div>
          )}
          {result.direct_connections.map((c, i) => (
            <div key={i} className="connection">
              <div className="line"><b>{c.route_id}</b> → {c.headsign} <span className="muted">({c.stops_count} Halte)</span></div>
              {c.next_departures.length === 0 && <p className="muted">Heute keine weitere Fahrt.</p>}
              {c.next_departures.map((d, j) => (
                <div key={j} className="dep">
                  <span className={d.realtime ? 'rt' : ''}>{fmtTime(d.estimated_time ?? d.scheduled_time)}</span>
                  {d.delay_s ? <span className="delay">{fmtDelay(d.delay_s)}</span> : null}
                  {!d.realtime && <span className="muted">Fahrplan</span>}
                  <button className="chip" onClick={() => reportOnTrip(d)}>⚠ Kontrolle melden</button>
                  {(d.warnings ?? []).map((w, k) => (
                    <div key={k} className="warning">🚨 {w.message} — Halt {w.affected_stop_id}{w.user_eta_seconds != null ? ` (in ~${Math.round(w.user_eta_seconds / 60)} min)` : ''}</div>
                  ))}
                </div>
              ))}
            </div>
          ))}
        </section>
      )}

      {live.length > 0 && (
        <section className="panel">
          <h2>Live-Kontrollhinweise</h2>
          {live.map((l, i) => <div key={i} className="warning">🚨 {l}</div>)}
        </section>
      )}

      {live.length > 0 && (
        <section className="panel">
          <h2>Live-Kontrollhinweise</h2>
          {live.map((l, i) => <div key={i} className="warning">🚨 {l}</div>)}
        </section>
      )}

      <AboScreen me={me} />
      <TicketScreen />
      <footer>
        <p className="tiny">Fahrplan- & Echtzeitdaten: gtfs.de / DELFI e.V. / beteiligte Verbünde (CC BY-SA 4.0 / CC BY 4.0) · Nutzungsbedingungen & Impressum: siehe Betreiberseite · Kontrollhinweise sind unverifizierte Community-Angaben.</p>
      </footer>
    </div>
  );
}

async function reportOnTrip(d: import('./api').Departure) {
  if (localStorage.getItem('tg_ticket_confirmed') !== '1') { location.hash = '#ticket'; return; }
  await api.report({
    city_slug: 'hamburg', anchor_type: 'trip', report_type: 'in_vehicle', vehicle_kind: 'rail',
    station: { stop_id: d.trip_ref.trip_id },   // Station wird clientseitig aus Kontext gefüllt (v1: Trip-Anker)
    trip: { trip_id: d.trip_ref.trip_id, start_date: d.trip_ref.start_date, route_id: d.route_id, headsign: d.headsign },
    inspector_count: 1,
    client: { ticket_confirmed: true, platform: 'web' }
  }, crypto.randomUUID()).catch(e => alert(`Meldung fehlgeschlagen: ${(e as Error).message}`));
  alert('Danke! Meldung ist aktiv und wandert mit der Fahrt mit.');
}

function AboScreen({ me }: { me: Me | null }) {
  const [receipt, setReceipt] = useState('');
  const [msg, setMsg] = useState('');
  if (location.hash !== '#abo') return null;
  const price = (me?.subscription_price_eur ?? 2.99).toFixed(2);
  return (
    <div className="overlay" onClick={() => location.hash = ''}>
      <div className="card" onClick={e => e.stopPropagation()}>
        <h2>TransitGuard Abo — {price} €/Monat</h2>
        <p className="muted">14 Tage kostenlose Testphase ab erster Nutzung. Monatlich kündbar über den Store. dein Entitlement-Token bleibt anonym (Besitz-Modell).</p>
        <div className="row">
          <input placeholder="Store-Receipt (Sandbox)…" value={receipt} onChange={e => setReceipt(e.target.value)} />
          <button className="primary" onClick={async () => {
            try {
              const r = await api.activate('web' as never, receipt.trim());
              localStorage.setItem('tg_access_token', r.access_token);
              localStorage.setItem('tg_restore_code', r.restore_code);
              setMsg(`Aktiviert! Restore-Code notieren: ${r.restore_code}`);
            } catch (e) { setMsg((e as Error).message); }
          }}>Aktivieren</button>
        </div>
        {msg && <p className="muted">{msg}</p>}
      </div>
    </div>
  );
}

function TicketScreen() {
  if (location.hash !== '#ticket') return null;
  return (
    <div className="overlay" onClick={() => location.hash = ''}>
      <div className="card" onClick={e => e.stopPropagation()}>
        <h2>Ticket kaufen</h2>
        <p className="muted">hvv switch App (Deutschlandticket 63 €/Monat, monatlich kündbar) oder hvv Onlineshop:</p>
        <a className="primary btn-link" href="https://www.hvv.de/deutschlandticket" target="_blank" rel="noreferrer">hvv Deutschlandticket öffnen →</a>
        <p className="tiny">Preisstand hvv.de, ohne Gewähr. Kauf und Gültigkeit beim Verbund.</p>
      </div>
    </div>
  );
}
