/**
 * Registrierung der Offline-Hülle.
 *
 * Bewusst nach dem Laden und mit stiller Fehlerbehandlung: Ein Browser ohne
 * Service-Worker-Unterstützung oder ein blockiertes Registrieren darf die App
 * nicht beeinträchtigen — sie funktioniert online vollständig ohne Worker.
 *
 * Der Worker selbst cacht nach Allowlist (public/sw.js): Meldungsdaten können
 * dort konstruktionsbedingt nicht landen (CLAUDE.md, harte Regel).
 */
export function registriereServiceWorker(): void {
  if (!('serviceWorker' in navigator)) return;
  // Kein Worker im Dev-Server: sonst werden Hot-Reload-Dateien festgehalten.
  if (import.meta.env.DEV) return;
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(() => {
      /* Offline-Hülle ist eine Zugabe, kein Betriebsmittel. */
    });
  });
}
