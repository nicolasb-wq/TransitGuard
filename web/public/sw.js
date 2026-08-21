/* =============================================================================
 * TransitGuard — Service Worker.
 *
 * HARTE REGEL (CLAUDE.md): Meldungsdaten dürfen NIEMALS in einem Client-Cache
 * liegen. Deshalb arbeitet dieser Worker mit einer ALLOWLIST, nicht mit einer
 * Sperrliste: gecacht wird ausschließlich, was ausdrücklich erlaubt ist. Alles
 * andere — insbesondere /v1/*, /hubs/* und /health/* — wird gar nicht erst
 * angefasst und geht immer live ans Netz. Eine vergessene Ausnahme kann damit
 * konstruktionsbedingt keine Meldung in den Cache spülen.
 *
 * Was gecacht wird:  die App-Hülle (HTML, JS, CSS, Manifest, Icons)
 * Was NIE gecacht wird:  jede API-Antwort, jeder Hub-Verkehr, alles
 *                        Personenbezogene, alles außerhalb dieses Origins,
 *                        jede Nicht-GET-Anfrage
 *
 * Geprüft durch verifikation/sw_offline.mjs (echter Browser): nach einer
 * echten Meldung darf im Cache keine /v1-Antwort stehen, und offline muss die
 * Hülle laden, während ein /v1-Aufruf fehlschlägt statt alte Daten zu liefern.
 * ========================================================================== */

const HUELLE = 'tg-huelle';          // Namenspräfix; die Version kommt aus den Asset-Namen

/** Pfade, die in den Cache dürfen. Bewusst eng und ohne Platzhalter für /v1. */
function darfInDenCache(pfad) {
  return pfad === '/'
      || pfad === '/index.html'
      || pfad === '/manifest.webmanifest'
      || pfad.startsWith('/assets/')
      || /^\/icon-[\w-]+\.png$/.test(pfad);
}

/** Aus der Startseite die tatsächlich referenzierten Build-Dateien lesen. */
async function huellenListe() {
  const res = await fetch('/index.html', { cache: 'no-store' });
  const html = await res.text();
  const treffer = [...html.matchAll(/["'](\/assets\/[^"']+)["']/g)].map(m => m[1]);
  return ['/', '/index.html', '/manifest.webmanifest',
          '/icon-192.png', '/icon-512.png', '/icon-maskable-512.png',
          ...new Set(treffer)];
}

/** Cache-Name aus den Asset-Namen ableiten: neue Build-Hashes ⇒ neuer Cache. */
async function cacheName(liste) {
  const roh = new TextEncoder().encode(liste.join('|'));
  const hash = await crypto.subtle.digest('SHA-256', roh);
  const hex = [...new Uint8Array(hash)].slice(0, 8).map(b => b.toString(16).padStart(2, '0')).join('');
  return `${HUELLE}-${hex}`;
}

self.addEventListener('install', (e) => {
  e.waitUntil((async () => {
    const liste = await huellenListe();
    const name = await cacheName(liste);
    const cache = await caches.open(name);
    // Einzeln, damit eine fehlende Datei nicht die ganze Installation kippt.
    await Promise.all(liste.map(p => cache.add(p).catch(() => {})));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (e) => {
  e.waitUntil((async () => {
    const liste = await huellenListe();
    const aktuell = await cacheName(liste);
    for (const name of await caches.keys()) {
      if (name.startsWith(HUELLE) && name !== aktuell) await caches.delete(name);
    }
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (e) => {
  const anfrage = e.request;
  if (anfrage.method !== 'GET') return;                       // nur Lesezugriffe
  const url = new URL(anfrage.url);
  if (url.origin !== self.location.origin) return;            // fremde Herkunft: nie anfassen

  // Navigationen (Deep-Links) offline aus der gecachten Hülle bedienen.
  if (anfrage.mode === 'navigate') {
    e.respondWith((async () => {
      try { return await fetch(anfrage); }
      catch { return (await caches.match('/index.html')) ?? Response.error(); }
    })());
    return;
  }

  if (!darfInDenCache(url.pathname)) return;                  // /v1, /hubs, /health: unberührt

  e.respondWith((async () => {
    const treffer = await caches.match(anfrage);
    if (treffer) return treffer;
    const antwort = await fetch(anfrage);
    // Nur Erfolgreiches und nur Erlaubtes wandert in den Cache.
    if (antwort.ok && antwort.type === 'basic' && darfInDenCache(new URL(anfrage.url).pathname)) {
      const liste = await huellenListe();
      const cache = await caches.open(await cacheName(liste));
      cache.put(anfrage, antwort.clone());
    }
    return antwort;
  })());
});
