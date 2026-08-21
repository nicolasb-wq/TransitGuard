// ---------------------------------------------------------------------------
// App-Hülle: Kopfzeile, drei Tabs, Ticket-Gate, SignalR-Livefeed.
//
// Hierarchie (UX-Auftrag): Fahren > Warnen > Mehr. Genau drei Ziele, immer am
// unteren Rand, damit die App einhändig bedienbar bleibt.
//
// Das Ticket-First-Gate (docs/18 H1) sitzt bewusst HIER und nicht in den
// Screens: es gibt genau eine Stelle, die entscheidet, und keinen Weg daran
// vorbei. „Später"/„Überspringen" existiert nicht — der Fahrplan-Teil bleibt
// ohne Bestätigung nutzbar, der Kontroll-Teil nicht.
// ---------------------------------------------------------------------------
import { useCallback, useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import {
  api, ApiError, confirmTicket, ensureDevice, reportAtStation, reportOnTrip,
  setOn402, ticketConfirmed, type Departure, type Me, type Stop
} from './api';
import { Sheet, ToastHost, useToast } from './ui';
import Fahren from './screens/Fahren';
import Warnen from './screens/Warnen';
import Mehr from './screens/Mehr';

type Tab = 'fahren' | 'warnen' | 'mehr';

const TABS: { id: Tab; glyph: string; label: string }[] = [
  { id: 'fahren', glyph: '🚇', label: 'Fahren' },
  { id: 'warnen', glyph: '⚠️', label: 'Warnen' },
  { id: 'mehr', glyph: '⋯', label: 'Mehr' },
];

export default function App() {
  return <ToastHost><Inner /></ToastHost>;
}

function Inner() {
  const toast = useToast();
  const [tab, setTab] = useState<Tab>('fahren');
  const [me, setMe] = useState<Me | null>(null);
  const [gateOpen, setGateOpen] = useState(false);
  const [liveCount, setLiveCount] = useState(0);
  const [unseen, setUnseen] = useState(0);
  const [nearestStop, setNearestStop] = useState<Stop | null>(null);
  /** Spiegelt die Ticket-Bestätigung als State — sonst merkt der Live-Feed
   *  eine spätere Bestätigung nie (Bug des Altstands: leere Abhängigkeitsliste). */
  const [ticketOk, setTicketOk] = useState(ticketConfirmed);
  const [locating, setLocating] = useState(false);

  /** Nach dem Gate auszuführende Aktion — so kostet „erst bestätigen" keinen Tap extra. */
  const pending = useRef<null | (() => void)>(null);

  const refreshMe = useCallback(() => { api.me().then(setMe).catch(() => {}); }, []);

  useEffect(() => { ensureDevice().then(refreshMe).catch(() => {}); }, [refreshMe]);

  // 402 aus dem API-Kern führt zum Zugangs-Tab statt zu einem stummen Fehlschlag.
  useEffect(() => setOn402(() => { setTab('mehr'); refreshMe(); }), [refreshMe]);

  // --- SignalR: Live-Kontrollhinweise der Stadt ----------------------------
  useEffect(() => {
    if (!ticketOk) return;
    const hub = new signalR.HubConnectionBuilder()
      .withUrl(`${import.meta.env.VITE_API_URL ?? ''}/hubs/v1/realtime`, {
        accessTokenFactory: () => localStorage.getItem('tg_device_token') ?? ''
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    hub.on('report.created', () => {
      setLiveCount(c => c + 1);
      setUnseen(u => u + 1);
    });
    hub.on('control.ticket_gate', () => setGateOpen(true));

    hub.start()
      .then(() => hub.invoke('join', 'city.hamburg.reports.free', true, null).catch(() => {}))
      .catch(() => {});
    return () => { hub.stop().catch(() => {}); };
  }, [ticketOk]);

  useEffect(() => { if (tab === 'warnen') setUnseen(0); }, [tab, liveCount]);

  // --- Ticket-Gate ---------------------------------------------------------
  const requireTicket = useCallback((then?: () => void) => {
    if (ticketConfirmed()) { setTicketOk(true); then?.(); return; }
    setTicketOk(false);
    pending.current = then ?? null;
    setGateOpen(true);
  }, []);

  const onGateConfirm = useCallback(() => {
    confirmTicket();
    setTicketOk(true);
    setGateOpen(false);
    const next = pending.current; pending.current = null;
    next?.();
  }, []);

  // --- Standort (von zwei Tabs gebraucht) ----------------------------------
  const locate = useCallback(() => {
    if (!navigator.geolocation) { toast('Dein Gerät gibt den Standort nicht frei.', 'err'); return; }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      async p => {
        try { setNearestStop((await api.nearby(p.coords.latitude, p.coords.longitude, 1))[0] ?? null); }
        catch { toast('Haltestellen konnten nicht geladen werden.', 'err'); }
        finally { setLocating(false); }
      },
      () => { setLocating(false); toast('Standort nicht freigegeben.', 'err'); },
      { enableHighAccuracy: true, timeout: 8000, maximumAge: 60_000 }
    );
  }, [toast]);

  // --- Melden --------------------------------------------------------------
  const meldeFehler = useCallback((e: unknown) => {
    const code = e instanceof ApiError ? e.code : 'network';
    toast(code === 'rate_limited'
      ? 'Kurz durchatmen — das war zu schnell hintereinander.'
      : code === 'ticket_confirmation_required'
        ? 'Dafür fehlt noch die Ticket-Bestätigung.'
        : 'Der Hinweis kam nicht durch. Bitte noch einmal versuchen.', 'err');
  }, [toast]);

  const meldeAnFahrt = useCallback(
    (d: Departure, boardStopId: string, routeId?: string, headsign?: string | null) => {
      requireTicket(() => {
        reportOnTrip(d, boardStopId, routeId, headsign)
          .then(() => toast('Danke! Dein Hinweis wandert jetzt mit der Fahrt mit.', 'ok'))
          .catch(meldeFehler);
      });
    }, [requireTicket, toast, meldeFehler]);

  const meldeAnHaltestelle = useCallback((stop: Stop) => {
    requireTicket(() => {
      reportAtStation(stop.stop_id)
        .then(() => { toast(`Danke! Hinweis für ${stop.stop_name} ist aktiv.`, 'ok'); setLiveCount(c => c + 1); })
        .catch(meldeFehler);
    });
  }, [requireTicket, toast, meldeFehler]);

  return (
    <div className="shell">
      <header className="topbar">
        <div>
          <div className="brand">TransitGuard</div>
          <span className="where">Hamburg &amp; Umland</span>
        </div>
        <span className="spacer" />
        {me?.access === 'trial' && (
          <span className="pill live">Test · {me.trial_days_remaining} T.</span>
        )}
        {me?.access === 'subscriber' && <span className="pill live">Abo</span>}
      </header>

      <main>
        {me?.access === 'locked' && (
          <div className="banner locked">
            <span aria-hidden="true">🔒</span>
            <span className="grow">Testphase abgelaufen.</span>
            <button className="chip" onClick={() => setTab('mehr')}>Weiter</button>
          </div>
        )}

        {tab === 'fahren' && <Fahren onReport={meldeAnFahrt} />}
        {tab === 'warnen' && (
          <Warnen
            liveCount={liveCount}
            onNeedTicket={() => requireTicket()}
            onReportStation={meldeAnHaltestelle}
            nearestStop={nearestStop}
            onLocate={locate}
            locating={locating}
          />
        )}
        {tab === 'mehr' && <Mehr me={me} onNeedTicket={() => requireTicket()} />}

        <footer>
          <p className="legal">
            Fahrplan- &amp; Echtzeitdaten: gtfs.de / DELFI e.V. / beteiligte Verbünde
            (CC BY-SA 4.0 / CC BY 4.0). Community-Hinweise sind unverifizierte Angaben von Fahrgästen.
          </p>
        </footer>
      </main>

      <nav className="tabbar" aria-label="Hauptbereiche">
        {TABS.map(t => (
          <button
            key={t.id}
            onClick={() => setTab(t.id)}
            aria-current={tab === t.id ? 'page' : undefined}
          >
            <span className="glyph" aria-hidden="true">{t.glyph}</span>
            {t.id === 'warnen' && unseen > 0 && (
              <span className="badge" aria-label={`${unseen} neue Hinweise`}>{unseen > 9 ? '9+' : unseen}</span>
            )}
            {t.label}
          </button>
        ))}
      </nav>

      <Sheet open={gateOpen} onClose={() => setGateOpen(false)} title="Fährst du gerade mit gültigem Ticket?">
        <p>
          Deutschlandticket, hvv-Ticket, Jobticket oder Einzelfahrschein — alles zählt.
          Community-Hinweise sind für Fahrgäste mit gültigem Fahrschein gedacht.
        </p>
        <button className="btn primary block" onClick={onGateConfirm}>
          Ja, ich habe ein gültiges Ticket
        </button>
        <a className="btn ghost block" href="https://www.hvv.de/deutschlandticket"
          target="_blank" rel="noreferrer">Ticket kaufen</a>
        <p className="sub" style={{ margin: '14px 0 0' }}>
          Fahrplan, Abfahrten und Störungen kannst du auch ohne Bestätigung nutzen.
          Die Bestätigung gilt 24 Stunden und bleibt auf deinem Gerät.
        </p>
      </Sheet>
    </div>
  );
}
