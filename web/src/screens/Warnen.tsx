// ---------------------------------------------------------------------------
// Tab „Warnen" — Community-Kontrollhinweise lesen und melden.
// Der gesamte Tab liegt hinter dem Ticket-First-Gate (docs/18 H1): ohne
// Bestätigung gibt es hier weder Lesen noch Melden — das ist keine Design-,
// sondern eine Rechtsentscheidung und darf nie optionalisiert werden.
// ---------------------------------------------------------------------------
import { useCallback, useEffect, useState } from 'react';
import { api, ApiError, fmtTime, ticketConfirmed, type ReportView, type ServiceAlert, type Stop } from '../api';
import { ListSkeleton, LoadingLabel, useToast } from '../ui';

interface Props {
  liveCount: number;
  onNeedTicket: () => void;
  onReportStation: (stop: Stop) => void;
  nearestStop: Stop | null;
  onLocate: () => void;
  locating: boolean;
}

export default function Warnen({ liveCount, onNeedTicket, onReportStation, nearestStop, onLocate, locating }: Props) {
  const toast = useToast();
  const confirmed = ticketConfirmed();
  const [reports, setReports] = useState<ReportView[] | null>(null);
  const [alerts, setAlerts] = useState<ServiceAlert[] | null>(null);

  const load = useCallback(() => {
    if (!ticketConfirmed()) return;
    api.cityReports().then(setReports).catch(e => {
      setReports([]);
      if (e instanceof ApiError && e.code !== 'ticket_gate_blocked') {
        toast('Hinweise konnten nicht geladen werden.', 'err');
      }
    });
  }, [toast]);

  useEffect(load, [load, liveCount]);   // liveCount ändert sich bei jedem SignalR-Ereignis
  useEffect(() => { api.alerts().then(setAlerts).catch(() => setAlerts([])); }, []);

  if (!confirmed) {
    return (
      <div className="empty">
        <span className="glyph" aria-hidden="true">🎫</span>
        <p style={{ maxWidth: 380, margin: '0 auto 18px' }}>
          Community-Hinweise sind für Fahrgäste mit gültigem Fahrschein gedacht.
          Bestätige kurz dein Ticket — dann siehst du, was gerade gemeldet wird.
        </p>
        <button className="btn primary block" onClick={onNeedTicket}>Ticket bestätigen</button>
      </div>
    );
  }

  return (
    <>
      <section className="card">
        <h2>Hinweis geben</h2>
        {nearestStop ? (
          <>
            <button className="btn danger big block" onClick={() => onReportStation(nearestStop)}>
              <span aria-hidden="true">⚠</span> Kontrolle hier melden
            </button>
            <p className="sub" style={{ margin: '10px 0 0' }}>
              Gilt für <strong>{nearestStop.stop_name}</strong>. Sitzt du schon in der Bahn?
              Dann melde im Tab „Fahren" direkt an deiner Fahrt — der Hinweis wandert dann mit.
            </p>
          </>
        ) : (
          <>
            <button className="btn ghost block" onClick={onLocate} disabled={locating}>
              <span aria-hidden="true">📍</span>{locating ? 'Standort wird gesucht…' : 'Haltestelle in der Nähe finden'}
            </button>
            <p className="sub" style={{ margin: '10px 0 0' }}>
              Für einen Hinweis brauchen wir die Haltestelle — sonst weiß niemand, wo er gilt.
            </p>
          </>
        )}
      </section>

      <section className="card">
        <h2>Gerade gemeldet</h2>
        {reports === null && <><LoadingLabel text="Hinweise werden geladen" /><ListSkeleton rows={3} /></>}
        {reports?.length === 0 && (
          <div className="empty" style={{ padding: '14px 4px' }}>
            <span className="glyph" aria-hidden="true">🟢</span>
            Nichts Aktuelles gemeldet.
          </div>
        )}
        {reports && reports.length > 0 && (
          <div className="stack">
            {reports.map(r => (
              <div key={r.id} className="notice">
                <span className="glyph" aria-hidden="true">🚨</span>
                <span>
                  <strong>{r.route_id ?? r.station_name ?? 'Hinweis'}</strong>
                  {r.headsign && <> Richtung {r.headsign}</>}
                  <br />
                  <span className="sub">
                    {LABEL[r.report_type] ?? r.report_type} · gemeldet {fmtTime(r.created_at)} ·
                    {' '}gültig bis {fmtTime(r.expires_at)}
                  </span>
                </span>
              </div>
            ))}
          </div>
        )}
        <p className="sub" style={{ margin: '12px 0 0' }}>
          Alle Angaben stammen von Fahrgästen und sind unverifiziert.
        </p>
      </section>

      {alerts && alerts.length > 0 && (
        <section className="card">
          <h2>Störungen im Netz</h2>
          <div className="stack">
            {alerts.slice(0, 8).map((a, i) => (
              <div key={i}>
                <strong>{a.header ?? 'Störung'}</strong>
                {a.description && <div className="sub">{a.description}</div>}
              </div>
            ))}
          </div>
        </section>
      )}
    </>
  );
}

const LABEL: Record<string, string> = {
  in_vehicle: 'In der Bahn',
  on_platform: 'Am Bahnsteig',
  at_stop: 'An der Haltestelle',
};
