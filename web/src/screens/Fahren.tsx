// ---------------------------------------------------------------------------
// Tab „Fahren" — der Hauptweg: Start wählen, Ziel tippen, losfahren.
// Zwei Taps bis zum Ergebnis: Standort → Ziel antippen. Kein Suchen-Knopf mehr
// (die Suche läuft entprellt beim Tippen), keine Zwischenschritte.
// ---------------------------------------------------------------------------
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  api, ApiError, fmtCountdown, fmtDelay, fmtTime, minutesUntil,
  type Connection, type Departure, type JourneyResult, type Stop
} from '../api';
import { DepartureSkeleton, LoadingLabel, useToast } from '../ui';

interface Props {
  /** Meldung anstoßen — die Ticket-Prüfung sitzt in der App-Hülle, nicht hier. */
  onReport: (d: Departure, boardStopId: string, routeId?: string, headsign?: string | null) => void;
}

export default function Fahren({ onReport }: Props) {
  const toast = useToast();
  const [from, setFrom] = useState<Stop | null>(null);
  const [nearby, setNearby] = useState<Stop[]>([]);
  const [locating, setLocating] = useState(false);
  const [pickingFrom, setPickingFrom] = useState(false);

  const [q, setQ] = useState('');
  const [hits, setHits] = useState<Stop[]>([]);
  const [to, setTo] = useState<Stop | null>(null);
  const [searching, setSearching] = useState(false);

  const [result, setResult] = useState<JourneyResult | null>(null);
  const [loading, setLoading] = useState(false);

  const say = useCallback((e: unknown) => {
    const code = e instanceof ApiError ? e.code : 'network';
    toast(ERRORS[code] ?? 'Das hat gerade nicht geklappt. Bitte noch einmal versuchen.', 'err');
  }, [toast]);

  // --- Start per Standort ---------------------------------------------------
  const locate = useCallback(() => {
    if (!navigator.geolocation) { toast('Dein Gerät gibt den Standort nicht frei.', 'err'); return; }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      async p => {
        try {
          const stops = await api.nearby(p.coords.latitude, p.coords.longitude);
          setNearby(stops);
          setFrom(stops[0] ?? null);
          setPickingFrom(false);
        } catch (e) { say(e); } finally { setLocating(false); }
      },
      () => { setLocating(false); toast('Standort nicht freigegeben — Start bitte per Suche wählen.', 'err'); },
      { enableHighAccuracy: true, timeout: 8000, maximumAge: 60_000 }
    );
  }, [say, toast]);

  // --- Zielsuche, entprellt -------------------------------------------------
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => {
    window.clearTimeout(timer.current);
    if (q.trim().length < 2) { setHits([]); return; }
    setSearching(true);
    timer.current = window.setTimeout(async () => {
      try { setHits(await api.searchStops(q.trim())); }
      catch (e) { say(e); }
      finally { setSearching(false); }
    }, 280);
    return () => window.clearTimeout(timer.current);
  }, [q, say]);

  // --- Verbindungen suchen, sobald Start UND Ziel stehen --------------------
  useEffect(() => {
    if (!from || !to) return;
    let abgebrochen = false;
    setLoading(true); setResult(null);
    api.journey(from.stop_id, null, null, to.stop_id)
      .then(r => { if (!abgebrochen) setResult(r); })
      .catch(e => { if (!abgebrochen) say(e); })
      .finally(() => { if (!abgebrochen) setLoading(false); });
    return () => { abgebrochen = true; };
  }, [from, to, say]);

  const nothingFound = result !== null
    && result.direct_connections.every(c => c.next_departures.length === 0)
    && result.transfer_connections.length === 0;

  return (
    <>
      <section className="card">
        <h2>Deine Fahrt</h2>

        <div className="stack">
          {/* Ist der Start gesetzt, verschwindet die Auswahlliste wieder — sonst
              schiebt sie das Zielfeld aus der Daumenzone und zwei gleichnamige
              Chips (Start vs. Treffer) stehen verwechselbar untereinander. */}
          {!from || pickingFrom ? (
            <>
              <div className="rowline">
                <button className="btn ghost" onClick={locate} disabled={locating}>
                  <span aria-hidden="true">📍</span>{locating ? 'Suche…' : 'Standort'}
                </button>
                <span className="grow truncate sub">
                  {nearby.length ? 'Haltestelle wählen' : 'Start noch offen'}
                </span>
              </div>
              {nearby.length > 0 && (
                <div className="chips" role="group" aria-label="Haltestelle in deiner Nähe">
                  {nearby.map(s => (
                    <button key={s.stop_id} className="chip" aria-pressed={from?.stop_id === s.stop_id}
                      onClick={() => { setFrom(s); setPickingFrom(false); }}>
                      {s.stop_name}
                    </button>
                  ))}
                </div>
              )}
            </>
          ) : (
            <div className="rowline">
              <span className="grow truncate">
                <span className="sub">Ab </span>
                <strong>{from.stop_name}</strong>
                {from.distance_km != null && <span className="sub"> · {fmtDistance(from.distance_km)}</span>}
              </span>
              <button className="chip" onClick={() => setPickingFrom(true)}>ändern</button>
            </div>
          )}

          {/* Nach der Zielwahl weicht das Suchfeld der Zielzeile — sonst stehen
              Eingabe und Ergebnis doppelt untereinander. */}
          {!to ? (
            <>
              <div className="field">
                <span aria-hidden="true">🎯</span>
                <input
                  value={q} onChange={e => setQ(e.target.value)}
                  placeholder="Wohin? Haltestelle eingeben"
                  aria-label="Ziel-Haltestelle" enterKeyHint="search" autoComplete="off"
                />
                {q && (
                  <button className="chip" style={{ minHeight: 32, padding: '0 10px' }}
                    onClick={() => { setQ(''); setHits([]); setResult(null); }}
                    aria-label="Ziel löschen">✕</button>
                )}
              </div>

              {searching && q.trim().length >= 2 && <span className="sub">Suche Haltestellen…</span>}

              {hits.length > 0 && (
                <div className="chips" role="group" aria-label="Treffer">
                  {hits.map(s => (
                    <button key={s.stop_id} className="chip" onClick={() => setTo(s)}>{s.stop_name}</button>
                  ))}
                </div>
              )}

              {!searching && q.trim().length >= 2 && hits.length === 0 && (
                <span className="sub">Keine Haltestelle mit diesem Namen gefunden.</span>
              )}
            </>
          ) : (
            <div className="rowline">
              <span className="grow truncate">
                <span className="sub">Nach </span><strong>{to.stop_name}</strong>
              </span>
              <button className="chip" onClick={() => { setTo(null); setResult(null); }}>ändern</button>
            </div>
          )}
        </div>
      </section>

      {loading && (
        <section className="card">
          <h2>Verbindungen</h2>
          <LoadingLabel text="Verbindungen werden gesucht" />
          <DepartureSkeleton rows={3} />
        </section>
      )}

      {!loading && !from && (
        <div className="empty">
          <span className="glyph" aria-hidden="true">🧭</span>
          Tippe auf <strong>Standort</strong> — wir finden die Haltestelle neben dir.
        </div>
      )}

      {!loading && from && !to && !pickingFrom && (
        <div className="empty">
          <span className="glyph" aria-hidden="true">🎯</span>
          Jetzt noch das Ziel eingeben.
        </div>
      )}

      {!loading && nothingFound && (
        <div className="empty">
          <span className="glyph" aria-hidden="true">🌙</span>
          Von hier fährt heute nichts mehr direkt dorthin.
        </div>
      )}

      {!loading && result && result.transfer_connections.length > 0 && (
        <section className="card">
          <h2>Mit einem Umstieg</h2>
          <div className="stack">
            {result.transfer_connections.map((tr, i) => (
              <div key={i} className="conn">
                <div className="rowline" style={{ marginBottom: 6 }}>
                  {/* Server-Schlüssel ist route_id (snake_case) — vor dem 21.08.
                      las die Oberfläche hier leer aus (leg_a.RouteId). */}
                  <span className="route">{tr.leg_a.route_id}</span>
                  <span aria-hidden="true">→</span>
                  <span className="route">{tr.leg_b.route_id}</span>
                  <span className="grow" />
                  <span className="sub">{Math.round(tr.total_seconds / 60)} min</span>
                </div>
                <div className="sub">
                  Ab {fmtTime(tr.leg_a.board_at)} · umsteigen {fmtTime(tr.leg_a.alight_at)}
                  {' '}({Math.round(tr.wait_seconds / 60)} min warten) · an {fmtTime(tr.leg_b.alight_at)}
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      {!loading && result && result.direct_connections.some(c => c.next_departures.length > 0) && (
        <section className="card">
          <h2>Direkt</h2>
          {result.direct_connections
            .filter(c => c.next_departures.length > 0)
            .map((c, i) => (
              <ConnectionBlock key={`${c.route_id}-${c.direction_id ?? i}`}
                conn={c} boardStopId={result.from_stop_id} onReport={onReport} />
            ))}
        </section>
      )}
    </>
  );
}

function ConnectionBlock(
  { conn, boardStopId, onReport }:
  { conn: Connection; boardStopId: string; onReport: Props['onReport'] }
) {
  return (
    <div className="conn">
      <div className="rowline" style={{ marginBottom: 4 }}>
        <span className="route">{conn.route_id}</span>
        <span className="grow truncate dest">{conn.headsign ?? 'Richtung unbekannt'}</span>
        <span className="sub">{conn.stops_count} Halte</span>
      </div>
      {conn.next_departures.map((d, j) => (
        <DepartureRow key={`${d.trip_ref.trip_id}-${j}`} d={d} conn={conn}
          boardStopId={boardStopId} onReport={onReport} />
      ))}
    </div>
  );
}

function DepartureRow(
  { d, conn, boardStopId, onReport }:
  { d: Departure; conn: Connection; boardStopId: string; onReport: Props['onReport'] }
) {
  const when = d.estimated_time ?? d.scheduled_time;
  const min = minutesUntil(when);
  const late = (d.delay_s ?? 0) >= 60;

  return (
    <>
      <div className="dep">
        <div className="when">
          <div className={`big ${d.realtime ? (late ? 'late' : 'live') : ''}`}>{fmtCountdown(min)}</div>
          <div className="clock">{fmtTime(when)}</div>
        </div>
        <div className="grow">
          <span className={`pill ${d.realtime ? 'live' : 'plan'}`}>
            {d.realtime ? `Live · ${fmtDelay(d.delay_s)}` : 'Fahrplan'}
          </span>
        </div>
        <button
          className="chip"
          onClick={() => onReport(d, boardStopId, conn.route_id, conn.headsign)}
          aria-label={`Kontrollhinweis für Linie ${conn.route_id} um ${fmtTime(when)} melden`}
        >
          <span aria-hidden="true">⚠</span> Melden
        </button>
      </div>
      {(d.warnings ?? []).map((w, k) => (
        <div key={k} className="notice">
          <span className="glyph" aria-hidden="true">🚨</span>
          <span>
            {w.message}
            {w.user_eta_seconds != null && <> — in etwa {Math.max(1, Math.round(w.user_eta_seconds / 60))} min</>}
          </span>
        </div>
      ))}
    </>
  );
}

const fmtDistance = (km: number) => km < 1 ? `${Math.round(km * 1000)} m` : `${km.toFixed(1)} km`;

/** Server-Fehlercodes in Sätze übersetzen, die unterwegs weiterhelfen. */
const ERRORS: Record<string, string> = {
  validation: 'Start und Ziel passen so nicht zusammen.',
  city_inactive: 'Für diese Stadt sind wir noch nicht am Netz.',
  feature_disabled: 'Diese Funktion ist gerade abgeschaltet.',
  trial_expired: 'Die Testphase ist vorbei — im Tab „Mehr" geht es weiter.',
  ticket_gate_blocked: 'Dafür fehlt noch die Ticket-Bestätigung.',
  ticket_confirmation_required: 'Dafür fehlt noch die Ticket-Bestätigung.',
  rate_limited: 'Kurz durchatmen — das war zu schnell hintereinander.',
  network: 'Keine Verbindung. Sobald das Netz zurück ist, klappt es wieder.',
};
