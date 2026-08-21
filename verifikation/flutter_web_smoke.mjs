// ---------------------------------------------------------------------------
// E — Laufzeittest der ECHTEN Flutter-App im echten Browser.
//
// Getrieben wird der RELEASE-Build (app/build/web), ausgeliefert unter
// demselben Origin wie die API — genau der Aufbau, den deploy/Caddyfile für
// Produktion beschreibt. Flutter rendert auf Canvas; bedient wird deshalb über
// Flutters Barrierefreiheits-Baum, der echte DOM-Knoten mit aria-label liefert.
//
// Warum nicht `flutter drive` mit integration_test: Der Treiber wartet in
// diesem Container endlos auf den Debug-Dienst der App. Ursache gemessen —
// Flutter-Web holt CanvasKit von www.gstatic.com, der Proxy blockt das
// (ERR_CONNECTION_RESET), und im DEBUG-Build greift `--no-web-resources-cdn`
// nicht. Der Release-Weg hier umgeht das Problem und prüft obendrein das
// Artefakt, das tatsächlich ausgeliefert würde.
//
// EHRLICHE GRENZE: Das ist das Web-Ziel. Es belegt NICHT das Verhalten des
// Android-Release-Artefakts unter R8 — dafür bräuchte es ein Gerät oder einen
// Emulator, und im Container fehlen /dev/kvm und die CPU-Virtualisierungsflags.
//
// Nutzung: BASE=http://127.0.0.1:8090 node verifikation/flutter_web_smoke.mjs
// Exit 0 = bestanden · 1 = Befunde · 2 = Playwright/Chromium fehlt
// ---------------------------------------------------------------------------
import { execSync } from 'node:child_process';
import { join } from 'node:path';
import { existsSync, readdirSync } from 'node:fs';

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
  for (const w of [process.env.PLAYWRIGHT_BROWSERS_PATH, '/opt/pw-browsers'].filter(Boolean)) {
    if (!existsSync(w)) continue;
    for (const d of readdirSync(w)) {
      const p = join(w, d, 'chrome-linux', 'chrome');
      if (d.startsWith('chromium-') && existsSync(p)) return p;
    }
  }
  return undefined;
}

const BASE = process.env.BASE ?? 'http://127.0.0.1:8090';
const OUT = process.env.OUT;
const { chromium } = await ladePlaywright();
const fails = [];
const log = (...a) => console.log(...a);

const browser = await chromium.launch({ executablePath: findeChromium(), args: ['--no-sandbox'] });
const ctx = await browser.newContext({
  locale: 'de-DE', viewport: { width: 420, height: 900 },
  permissions: ['geolocation'], geolocation: { latitude: 53.554, longitude: 9.991 },
});
// Berechtigung ausdruecklich fuer DIESEN Origin erteilen — ohne Origin-Angabe
// erreicht sie die Seite nicht, und geolocator_web meldet dann "denied".
await ctx.grantPermissions(['geolocation'], { origin: BASE });
const page = await ctx.newPage();
const konsolenfehler = [];
page.on('pageerror', e => konsolenfehler.push(String(e).slice(0, 140)));
const apiRufe = [];
page.on('response', r => {
  const u = new URL(r.url());
  if (u.pathname.startsWith('/v1')) apiRufe.push(`${r.status()} ${r.request().method()} ${u.pathname}`);
});

// Flutter legt Beschriftungen teils als aria-label ab, teils als Textinhalt
// (Knoten mit role=button tragen ihren Text im Element). Beides wird gelesen —
// eine Prüfung, die nur aria-label kennt, sieht die halbe Oberfläche nicht.
const SEMANTIK = `
  (() => {
    const raus = [];
    document.querySelectorAll('flt-semantics-host *').forEach(e => {
      const lab = e.getAttribute('aria-label');
      if (lab) raus.push(lab);
      const txt = (e.textContent || '').trim();
      if (txt && txt.length < 200) raus.push(txt);
    });
    return [...new Set(raus)];
  })()`;

const labels = () => page.evaluate(SEMANTIK);

/** Knoten per Beschriftung antippen (Canvas kennt keine Maus-Treffer im DOM). */
async function tippe(muster, was = muster) {
  // ECHTER Mausklick auf die Geometrie des Semantik-Knotens. Flutter verwirft
  // synthetische click-Ereignisse auf Schaltflaechen (sie kaemen ohne
  // vorangehende Zeigersequenz); die Knoten liegen aber lagerichtig ueber dem
  // Canvas, ein echter Klick trifft deshalb das darunterliegende Widget.
  const kasten = await page.evaluate((m) => {
    const knoten = [...document.querySelectorAll('flt-semantics-host *')];
    const treffer = knoten.filter(e => {
      const lab = e.getAttribute('aria-label') || '';
      const txt = (e.textContent || '').trim();
      const r = e.getBoundingClientRect();
      return (lab.includes(m) || txt.includes(m)) && r.width > 0 && r.height > 0;
    });
    if (!treffer.length) return null;
    // Den SPEZIFISCHSTEN Knoten nehmen: Schaltflaechen/Reiter vor Containern,
    // danach die kleinste Flaeche. Ein grosser Container traegt denselben Text,
    // sein Mittelpunkt liegt aber irgendwo im Leeren — der Klick ginge daneben.
    const rang = e => {
      const rolle = e.getAttribute('role') || '';
      return (rolle === 'button' || rolle === 'tab' || rolle === 'link') ? 0 : 1;
    };
    treffer.sort((a, b) => {
      const d = rang(a) - rang(b);
      if (d) return d;
      const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
      return (ra.width * ra.height) - (rb.width * rb.height);
    });
    const r = treffer[0].getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.y + r.height / 2,
             rolle: treffer[0].getAttribute('role') || '', flaeche: Math.round(r.width * r.height) };
  }, muster);
  if (!kasten) { fails.push(`„${was}" nicht im Semantik-Baum`); return false; }
  await page.mouse.click(kasten.x, kasten.y);
  if (process.env.LAUT) console.log(`    tippe „${was}" → role=${kasten.rolle} flaeche=${kasten.flaeche}`);
  await page.waitForTimeout(1800);
  return true;
}

await page.goto(BASE, { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(3500);

// --- 1. Start ---------------------------------------------------------------
const platzhalter = page.locator('flt-semantics-placeholder');
if (await platzhalter.count() === 0) { fails.push('App hat nicht gebootet (kein Flutter-DOM)'); }
else { await platzhalter.dispatchEvent('click'); await page.waitForTimeout(2500); }

let l = await labels();
log(`E-1 Beschriftungen nach dem Start: ${JSON.stringify(l)}`);
for (const ziel of ['Fahren', 'Warnen', 'Mehr']) {
  if (!l.some(x => x.includes(ziel))) fails.push(`Navigationsziel „${ziel}" fehlt`);
}
if (OUT) await page.screenshot({ path: `${OUT}/flutter-1-start.png` });

// --- 2. Echte API-Aufrufe beim Start ----------------------------------------
log(`E-2 API-Aufrufe der App: ${JSON.stringify(apiRufe)}`);
if (!apiRufe.some(r => r.includes('POST /v1/devices'))) fails.push('App hat kein Geräte-Token geholt');
if (!apiRufe.some(r => r.includes('GET /v1/devices/me'))) fails.push('App hat den Zugangsstand nicht abgefragt');

// --- 3. Ticket-Gate: Warnen ist zu -----------------------------------------
await tippe('Warnen');
l = await labels();
log(`E-3 Warnen ohne Bestätigung: ${JSON.stringify(l)}`);
if (!l.some(x => x.includes('Ticket bestätigen'))) {
  fails.push('Warnen-Tab ist ohne Ticket-Bestätigung NICHT verschlossen (docs/18 H1)');
}
if (l.some(x => x.includes('Kontrolle hier melden'))) {
  fails.push('Melden-Knopf ohne Ticket-Bestätigung sichtbar');
}
if (OUT) await page.screenshot({ path: `${OUT}/flutter-2-gate.png` });

// --- 4. Bestätigen öffnet den Bereich ---------------------------------------
await tippe('Ticket bestätigen');
l = await labels();
log(`E-4 Gate-Dialog: ${JSON.stringify(l.filter(x => /Ticket|gültig|Später/.test(x)))}`);
await tippe('Ja, ich habe ein gültiges Ticket');
l = await labels();
if (l.some(x => x.includes('Ticket bestätigen'))) fails.push('Bereich blieb nach der Bestätigung verschlossen');
else log('E-4 Kontroll-Bereich nach Bestätigung offen ✔');
if (OUT) await page.screenshot({ path: `${OUT}/flutter-3-offen.png` });

// --- 5. Standort + Fahrtensuche ---------------------------------------------
await tippe('Fahren');
await tippe('Standort');
await page.waitForTimeout(2500);   // Standortabfrage + Netzrunde
l = await labels();
log(`E-5 nach Standortsuche: ${JSON.stringify(l.slice(0, 12))}`);
if (!l.some(x => x.includes('Jungfernstieg'))) fails.push('Keine Haltestelle nach der Standortsuche');
if (!apiRufe.some(r => r.includes('/v1/cities/hamburg/stops/nearby'))) fails.push('Nachbarschaftssuche ging nicht ans Netz');
if (OUT) await page.screenshot({ path: `${OUT}/flutter-4-standort.png` });

// --- 6. Meldung über die echte API ------------------------------------------
await tippe('Warnen');
l = await labels();
if (l.some(x => x.includes('Kontrolle hier melden'))) {
  await tippe('Kontrolle hier melden');
  const nachher = apiRufe.filter(r => r.includes('POST /v1/reports'));
  log(`E-6 Melde-Aufrufe: ${JSON.stringify(nachher)}`);
  if (!nachher.some(r => r.startsWith('201'))) {
    fails.push(`Meldung nicht angenommen: ${JSON.stringify(nachher)}`);
  }
  const l2 = await labels();
  if (!l2.some(x => /Danke/.test(x))) log('  (Hinweis: Bestätigungstext nicht im Semantik-Baum — Toast ist kurzlebig)');
} else {
  fails.push('Melden-Knopf nach Standortwahl nicht erreichbar');
}
if (OUT) await page.screenshot({ path: `${OUT}/flutter-5-gemeldet.png` });

log(`Seitenfehler: ${konsolenfehler.length ? JSON.stringify(konsolenfehler.slice(0, 3)) : 'keine'}`);
if (konsolenfehler.length) fails.push(`Laufzeitfehler in der App: ${konsolenfehler[0]}`);

await browser.close();
if (fails.length) { console.log('\n=== BEFUNDE ===\n' + fails.join('\n')); process.exit(1); }
console.log('\nFlutter-Laufzeittest bestanden — App läuft, spricht die echte API, Gate hält.');
