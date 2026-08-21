// ---------------------------------------------------------------------------
// Service-Worker-Prüfung im echten Browser.
//
// Die harte Projektregel lautet: Meldungsdaten dürfen NIEMALS in einem
// Client-Cache liegen (CLAUDE.md). Dieses Skript prüft das als VERHALTEN,
// nicht als Kommentar im Quelltext:
//
//   1. Der Worker wird aktiv und cacht die App-Hülle.
//   2. Nach einer ECHTEN Meldung steht keine einzige /v1-, /hubs- oder
//      /health-Antwort im Cache — der Cache wird vollständig aufgezählt.
//   3. Offline lädt die Hülle weiterhin (Deep-Link inklusive).
//   4. Offline schlägt ein /v1-Aufruf FEHL, statt alte Daten zu liefern.
//      Genau das ist der Unterschied zwischen "kein Cache" und "stiller Cache".
//
// Nutzung (API und Auslieferung unter EINEM Origin — scripts/verify-caddy.sh):
//   BASE=http://127.0.0.1:8080 node verifikation/sw_offline.mjs
// Exit 0 = bestanden · 1 = Befunde · 2 = Playwright/Chromium fehlt
// ---------------------------------------------------------------------------
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { execSync } from 'node:child_process';

async function ladePlaywright() {
  const versuche = [process.env.PLAYWRIGHT_MODULE, 'playwright'].filter(Boolean);
  try { versuche.push(join(execSync('npm root -g', { encoding: 'utf8' }).trim(), 'playwright', 'index.js')); } catch {}
  for (const v of versuche) {
    try { const m = await import(v); const pw = m.chromium ? m : m.default; if (pw?.chromium) return pw; } catch {}
  }
  console.error('playwright nicht gefunden.'); process.exit(2);
}
function findeChromium() {
  if (process.env.CHROMIUM) return process.env.CHROMIUM;
  for (const w of [process.env.PLAYWRIGHT_BROWSERS_PATH, '/opt/pw-browsers',
                   join(process.env.HOME ?? '', '.cache/ms-playwright')].filter(Boolean)) {
    if (!existsSync(w)) continue;
    for (const d of readdirSync(w)) {
      const p = join(w, d, 'chrome-linux', 'chrome');
      if (d.startsWith('chromium-') && existsSync(p)) return p;
    }
  }
  return undefined;
}

const BASE = process.env.BASE ?? 'http://127.0.0.1:8080';
const { chromium, devices } = await ladePlaywright();
const fails = [];
const log = (...a) => console.log(...a);

const browser = await chromium.launch({ executablePath: findeChromium(), args: ['--no-sandbox'] });
const ctx = await browser.newContext({
  ...devices['Pixel 7'], locale: 'de-DE',
  permissions: ['geolocation'], geolocation: { latitude: 53.554, longitude: 9.991 },
});
const page = await ctx.newPage();

// --- 1. Worker aktivieren ---------------------------------------------------
await page.goto(BASE, { waitUntil: 'networkidle' });
const aktiv = await page.evaluate(async () => {
  if (!('serviceWorker' in navigator)) return 'nicht unterstuetzt';
  const reg = await navigator.serviceWorker.ready;
  return reg.active ? 'aktiv' : 'nicht aktiv';
});
log(`Service Worker: ${aktiv}`);
if (aktiv !== 'aktiv') fails.push(`Service Worker wurde nicht aktiv (${aktiv})`);

// --- 2. Echte Meldung erzeugen, damit /v1-Antworten im Spiel sind -----------
await page.getByRole('button', { name: /Standort/ }).click();
await page.getByText('Jungfernstieg').first().waitFor({ timeout: 8000 });
await page.getByRole('button', { name: /^Warnen/ }).click();
await page.getByRole('button', { name: 'Ticket bestätigen' }).click();
await page.getByRole('button', { name: 'Ja, ich habe ein gültiges Ticket' }).click();
await page.waitForTimeout(900);
await page.getByRole('button', { name: /Haltestelle in der Nähe finden/ }).click();
await page.waitForTimeout(1200);
await page.getByRole('button', { name: /Kontrolle hier melden/ }).click();
await page.waitForTimeout(1500);
const bestaetigung = await page.locator('.toast').first().textContent().catch(() => null);
log(`Meldung abgesetzt: ${JSON.stringify(bestaetigung)}`);
if (!bestaetigung || !/Danke/.test(bestaetigung)) fails.push('Meldung ging nicht durch — Prüfung wäre wertlos');

// Die Liste noch einmal laden, damit auch GET /v1/.../reports durchgelaufen ist.
await page.reload({ waitUntil: 'networkidle' });
await page.waitForTimeout(800);

// --- 3. Cache VOLLSTÄNDIG aufzählen ----------------------------------------
const inhalt = await page.evaluate(async () => {
  const out = [];
  for (const name of await caches.keys()) {
    const c = await caches.open(name);
    for (const req of await c.keys()) out.push({ cache: name, url: req.url });
  }
  return out;
});
log(`Einträge im Cache: ${inhalt.length}`);
const verboten = inhalt.filter(e => /\/(v1|hubs|health)\//.test(new URL(e.url).pathname));
if (verboten.length) {
  fails.push(`VERBOTENE Einträge im Cache: ${JSON.stringify(verboten.map(v => v.url))}`);
} else {
  log('Keine API-, Hub- oder Health-Antwort im Cache ✔');
}
const huelle = inhalt.filter(e => /\/(assets|index\.html|manifest|icon-)/.test(e.url) || new URL(e.url).pathname === '/');
if (huelle.length === 0) fails.push('App-Hülle wurde gar nicht gecacht — dann ist auch nichts offline nutzbar');
else log(`App-Hülle gecacht: ${huelle.length} Dateien ✔`);

// --- 4. Offline: Hülle lädt, API nicht -------------------------------------
await ctx.setOffline(true);
try {
  await page.reload({ waitUntil: 'domcontentloaded', timeout: 15000 });
  const marke = await page.locator('.topbar .brand').first().textContent().catch(() => null);
  log(`Offline geladen — Kopfzeile: ${JSON.stringify(marke)}`);
  if (!marke || !/TransitGuard/.test(marke)) fails.push('App-Hülle lädt offline nicht');
} catch (e) {
  fails.push(`Offline-Reload fehlgeschlagen: ${e.message}`);
}

// Deep-Link offline (Navigation aus dem Cache)
try {
  await page.goto(`${BASE}/irgendein/tiefer/pfad`, { waitUntil: 'domcontentloaded', timeout: 15000 });
  const marke2 = await page.locator('.topbar .brand').first().textContent().catch(() => null);
  log(`Offline-Deep-Link: ${JSON.stringify(marke2)}`);
  if (!marke2) fails.push('Deep-Link lädt offline nicht aus der Hülle');
} catch (e) {
  fails.push(`Offline-Deep-Link fehlgeschlagen: ${e.message}`);
}

// Der entscheidende Punkt: ein /v1-Aufruf muss offline SCHEITERN.
const apiOffline = await page.evaluate(async () => {
  try {
    const r = await fetch('/v1/cities', { headers: { 'X-Device-Token': 'x' } });
    return { erreichbar: true, status: r.status, text: (await r.text()).slice(0, 80) };
  } catch (e) { return { erreichbar: false, fehler: String(e).slice(0, 80) }; }
});
log(`Offline-Aufruf /v1/cities: ${JSON.stringify(apiOffline)}`);
if (apiOffline.erreichbar) {
  fails.push(`/v1 wurde offline BEANTWORTET (${apiOffline.status}) — es liegen Daten im Cache`);
} else {
  log('Offline liefert die API nichts aus dem Cache ✔');
}

await ctx.setOffline(false);
await browser.close();

if (fails.length) { console.log('\n=== BEFUNDE ===\n' + fails.join('\n')); process.exit(1); }
console.log('\nService-Worker-Prüfung bestanden — keine Meldungsdaten im Cache, Hülle offline nutzbar.');
