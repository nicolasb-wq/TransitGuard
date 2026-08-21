// ---------------------------------------------------------------------------
// Tab „Mehr" — Abo, Ticketkauf, Rechtliches. Alles, was nicht zur Fahrt gehört.
// ---------------------------------------------------------------------------
import { useState } from 'react';
import { api, ApiError, ticketConfirmed, type Me } from '../api';
import { useToast } from '../ui';

export default function Mehr({ me, onNeedTicket }: { me: Me | null; onNeedTicket: () => void }) {
  const toast = useToast();
  const [receipt, setReceipt] = useState('');
  const [busy, setBusy] = useState(false);
  const [restoreCode, setRestoreCode] = useState(localStorage.getItem('tg_restore_code') ?? '');
  const price = (me?.subscription_price_eur ?? 2.99).toFixed(2).replace('.', ',');

  const activate = async () => {
    if (!receipt.trim()) { toast('Bitte zuerst den Store-Beleg einsetzen.', 'err'); return; }
    setBusy(true);
    try {
      const r = await api.activate('web', receipt.trim());
      localStorage.setItem('tg_access_token', r.access_token);
      localStorage.setItem('tg_restore_code', r.restore_code);
      setRestoreCode(r.restore_code);
      toast('Abo aktiv. Notiere dir den Wiederherstellungs-Code.', 'ok');
    } catch (e) {
      toast(e instanceof ApiError ? `Aktivierung abgelehnt (${e.code}).` : 'Aktivierung fehlgeschlagen.', 'err');
    } finally { setBusy(false); }
  };

  return (
    <>
      <section className="card">
        <h2>Zugang</h2>
        {me ? (
          <div className="stack">
            <div className="rowline">
              <span className="grow">{ACCESS[me.access]}</span>
              {me.access === 'trial' && <strong>{me.trial_days_remaining} Tage</strong>}
            </div>
            {me.access !== 'subscriber' && (
              <p className="sub" style={{ margin: 0 }}>
                Danach {price} € im Monat, monatlich kündbar. Ohne Konto, ohne Anmeldung —
                dein Zugang hängt am Gerät, nicht an einer Identität.
              </p>
            )}
          </div>
        ) : <span className="sub">Zugangsstand wird geladen…</span>}
      </section>

      {me?.access !== 'subscriber' && (
        <section className="card">
          <h2>Abo aktivieren</h2>
          <div className="stack">
            <div className="field">
              <input
                value={receipt} onChange={e => setReceipt(e.target.value)}
                placeholder="Store-Beleg einsetzen" aria-label="Store-Beleg" autoComplete="off"
              />
            </div>
            <button className="btn primary block" onClick={activate} disabled={busy}>
              {busy ? 'Wird geprüft…' : `Für ${price} € im Monat freischalten`}
            </button>
          </div>
          {restoreCode && (
            <p className="sub" style={{ marginBottom: 0 }}>
              Wiederherstellungs-Code: <strong>{restoreCode}</strong> — sicher notieren.
              Ohne Konto ist er der einzige Weg zurück auf ein neues Gerät.
            </p>
          )}
        </section>
      )}

      <section className="card">
        <h2>Ticket</h2>
        <div className="stack">
          <a className="btn primary block" href="https://www.hvv.de/deutschlandticket"
            target="_blank" rel="noreferrer">
            Ticket beim hvv kaufen
          </a>
          <button className="btn ghost block" onClick={onNeedTicket}>
            {ticketConfirmed() ? 'Ticket-Bestätigung erneuern' : 'Ticket bestätigen'}
          </button>
          <p className="sub" style={{ margin: 0 }}>
            Preise und Gültigkeit beim Verbund, ohne Gewähr. Die Bestätigung gilt 24 Stunden
            und bleibt auf deinem Gerät.
          </p>
        </div>
      </section>

      <section className="card">
        <h2>Rechtliches</h2>
        <p className="legal" style={{ margin: 0 }}>
          Fahrplan- und Echtzeitdaten: gtfs.de / DELFI e.V. / beteiligte Verbünde
          (CC BY-SA 4.0 / CC BY 4.0). Community-Hinweise stammen von Fahrgästen und sind
          unverifiziert. Nutzungsbedingungen und Impressum: siehe Betreiberseite.
        </p>
      </section>
    </>
  );
}

const ACCESS: Record<Me['access'], string> = {
  trial: 'Testphase läuft',
  subscriber: 'Abo aktiv — danke!',
  locked: 'Testphase abgelaufen',
};
