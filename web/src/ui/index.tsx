// ---------------------------------------------------------------------------
// Gemeinsame UI-Bausteine: Toast, Bottom-Sheet, Skeleton.
// Bewusst klein gehalten — die App braucht keine Komponentenbibliothek.
// ---------------------------------------------------------------------------
import {
  createContext, useCallback, useContext, useEffect, useMemo, useRef, useState,
  type ReactNode
} from 'react';

// --- Toast ------------------------------------------------------------------
type ToastKind = 'info' | 'ok' | 'err';
interface Toast { id: number; text: string; kind: ToastKind }

const ToastCtx = createContext<(text: string, kind?: ToastKind) => void>(() => {});
export const useToast = () => useContext(ToastCtx);

export function ToastHost({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<Toast[]>([]);
  const seq = useRef(0);

  const push = useCallback((text: string, kind: ToastKind = 'info') => {
    const id = ++seq.current;
    setItems(l => [...l, { id, text, kind }]);
    window.setTimeout(() => setItems(l => l.filter(t => t.id !== id)), 4200);
  }, []);

  return (
    <ToastCtx.Provider value={push}>
      {children}
      {/* aria-live: Bestätigungen erreichen auch Screenreader-Nutzende. */}
      <div className="toasts" aria-live="polite" aria-atomic="false">
        {items.map(t => (
          <div key={t.id} className={`toast ${t.kind === 'info' ? '' : t.kind}`} role="status">{t.text}</div>
        ))}
      </div>
    </ToastCtx.Provider>
  );
}

// --- Bottom-Sheet -----------------------------------------------------------
export function Sheet(
  { open, onClose, title, children, dismissable = true }:
  { open: boolean; onClose: () => void; title: string; children: ReactNode; dismissable?: boolean }
) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    ref.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && dismissable) onClose(); };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = prev; };
  }, [open, onClose, dismissable]);

  if (!open) return null;
  return (
    <div className="scrim" onClick={() => dismissable && onClose()}>
      <div
        className="sheet" role="dialog" aria-modal="true" aria-label={title}
        tabIndex={-1} ref={ref} onClick={e => e.stopPropagation()}
      >
        {dismissable && <div className="handle" aria-hidden="true" />}
        <h2>{title}</h2>
        {children}
      </div>
    </div>
  );
}

// --- Skeleton ---------------------------------------------------------------
/** Platzhalter in der Form des erwarteten Inhalts — kein Spinner, kein Sprung. */
export function DepartureSkeleton({ rows = 3 }: { rows?: number }) {
  const widths = useMemo(() => ['62%', '48%', '55%', '40%', '58%'], []);
  return (
    <div aria-hidden="true">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="conn">
          <div className="rowline" style={{ marginBottom: 10 }}>
            <div className="sk tall" style={{ width: 52 }} />
            <div className="sk line grow" style={{ maxWidth: widths[i % widths.length] }} />
          </div>
          <div className="dep">
            <div className="when"><div className="sk tall" style={{ width: 56 }} /></div>
            <div className="sk line grow" style={{ maxWidth: '45%' }} />
          </div>
        </div>
      ))}
    </div>
  );
}

export function ListSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div className="stack" aria-hidden="true">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="rowline">
          <div className="sk round" style={{ width: 30, height: 30 }} />
          <div className="sk line grow" style={{ maxWidth: `${70 - i * 9}%` }} />
        </div>
      ))}
    </div>
  );
}

/** Sichtbarer Ladehinweis für Screenreader, während Skeletons laufen. */
export const LoadingLabel = ({ text }: { text: string }) => (
  <span className="sr-only" role="status" aria-live="polite" style={{
    position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap'
  }}>{text}</span>
);
