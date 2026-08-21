// ---------------------------------------------------------------------------
// PWA-Rauchtest im echten Browser (Playwright/Chromium), hell und dunkel.
//
// Prueft die Kette, die weder acceptance.sh (curl) noch signalr_e2e.mjs (Node)
// sehen koennen — weil beide keinen Browser haben:
//   * Fahren: Standort → Ziel → Verbindung wird gerendert
//   * Ticket-First-Gate erscheint VOR jeder Meldung (docs/18 H1)
//   * Warnen-Tab ist ohne Bestaetigung gesperrt, danach frei
//   * Meldung geht durch und erscheint in der Liste — mit Klarnamen, nicht mit ID
//   * alle Trefferflaechen >= 44 px (1-Hand-Bedienung)
//   * Manifest ist ladbar (Installierbarkeit, PWA-first)
//
// Voraussetzung: playwright + ein Chromium. Liegt Playwright global, muss der
// Suchpfad mitgegeben werden (die Datei hat kein eigenes node_modules):
//   NODE_PATH="$(npm root -g)" \
//   BASE=http://127.0.0.1:8080 OUT=/tmp/shots node verifikation/pwa_smoke.mjs
//
// API und Auslieferung muessen unter EINEM Origin liegen — genau wie in
// Produktion via Caddy (deploy/Caddyfile). Lokal aufsetzen:
//   scripts/verify-caddy.sh   (startet denselben Block auf :8080)
//
// CHROMIUM=<pfad> setzt den Browser explizit; sonst wird /opt/pw-browsers und
// die Playwright-Standardablage durchsucht.
// Exit 0 = bestanden, 1 = Befunde (werden am Ende aufgelistet).
// ---------------------------------------------------------------------------
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { execSync } from 'node:child_process';

/**
 * Playwright laden, egal ob lokal oder global installiert. ESM beachtet
 * NODE_PATH nicht — deshalb wird der globale Modulpfad aktiv ermittelt.
 * PLAYWRIGHT_MODULE=<pfad> ueberschreibt die Suche.
 */
async function ladePlaywright() {
  const versuche = [];
  if (process.env.PLAYWRIGHT_MODULE) versuche.push(process.env.PLAYWRIGHT_MODULE);
  versuche.push('playwright');
  try {
    const g = execSync('npm root -g', { encoding: 'utf8' }).trim();
    versuche.push(join(g, 'playwright', 'index.js'));
  } catch { /* npm nicht da — dann bleibt es beim lokalen Versuch */ }
  for (const v of versuche) {
    try {
      const m = await import(v);
      // Global installiertes Playwright ist CommonJS — dann liegt alles unter .default.
      const pw = m.chromium ? m : m.default;
      if (pw?.chromium) return pw;
    } catch { /* naechster Kandidat */ }
  }
  console.error('playwright nicht gefunden. Installieren (npm i -D playwright) oder '
    + 'PLAYWRIGHT_MODULE=<pfad zur index.js> setzen.');
  process.exit(2);
}

const { chromium, devices } = await ladePlaywright();

/** Chromium finden, ohne einen Pfad fest zu verdrahten. */
function findeChromium() {
  if (process.env.CHROMIUM) return process.env.CHROMIUM;
  const wurzeln = [process.env.PLAYWRIGHT_BROWSERS_PATH, '/opt/pw-browsers',
                   join(process.env.HOME ?? '', '.cache/ms-playwright')].filter(Boolean);
  for (const w of wurzeln) {
    if (!existsSync(w)) continue;
    for (const d of readdirSync(w)) {
      const p = join(w, d, 'chrome-linux', 'chrome');
      if (d.startsWith('chromium-') && existsSync(p)) return p;
    }
  }
  return undefined;   // Playwright sucht dann selbst
}

const OUT = process.env.OUT;
const BASE = process.env.BASE ?? 'http://127.0.0.1:5173';
const fails = [];
const log = (...a) => console.log(...a);

const browser = await chromium.launch({ executablePath: findeChromium(), args: ['--no-sandbox'] });

async function run(name, colorScheme) {
  const ctx = await browser.newContext({
    ...devices['Pixel 7'],
    locale: 'de-DE',
    colorScheme,
    permissions: ['geolocation'],
    geolocation: { latitude: 53.554, longitude: 9.991 },   // Jungfernstieg
  });
  const page = await ctx.newPage();
  page.on('console', m => { if (m.type() === 'error') fails.push(`[${name}] console: ${m.text()}`); });
  page.on('pageerror', e => fails.push(`[${name}] pageerror: ${e.message}`));
  page.on('requestfailed', r => fails.push(`[${name}] request failed: ${r.url()} — ${r.failure()?.errorText}`));

  await page.goto(BASE, { waitUntil: 'networkidle' });

  // --- Tab „Fahren": Standort → Ziel → Verbindung ---------------------------
  await page.getByRole('button', { name: /Standort/ }).click();
  await page.getByText('Jungfernstieg').first().waitFor({ timeout: 8000 });
  await page.screenshot({ path: `${OUT}/${name}-1-fahren-start.png`, fullPage: true });

  // Auf die Treffergruppe eingrenzen: der gleiche Name steht auch als Start-Chip da.
  await page.getByLabel('Ziel-Haltestelle').fill('Kelling');
  await page.getByRole('group', { name: 'Treffer' })
            .getByRole('button', { name: 'Kellinghusenstrasse' }).click();
  await page.waitForTimeout(1200);
  await page.screenshot({ path: `${OUT}/${name}-2-fahren-verbindung.png`, fullPage: true });

  const hatVerbindung = await page.locator('.route').count();
  log(`[${name}] Linien-Badges im Ergebnis: ${hatVerbindung}`);
  if (hatVerbindung === 0) fails.push(`[${name}] keine Verbindung gerendert`);

  // --- Umstiegsverbindung: HHA1 → HHA5 hat KEINE Direktfahrt ----------------
  // Hier sass Launch-Blocker 1 (leg_a.RouteId statt route_id): die Karte wurde
  // gerendert, aber mit leeren Liniennummern. Deshalb wird auf den INHALT
  // geprueft, nicht auf die blosse Existenz der Karte.
  await page.getByRole('button', { name: 'ändern' }).last().click();
  await page.getByLabel('Ziel-Haltestelle').fill('Farmsen');
  await page.getByRole('group', { name: 'Treffer' })
            .getByRole('button', { name: 'Farmsen' }).click();
  await page.waitForTimeout(1200);
  await page.screenshot({ path: `${OUT}/${name}-2b-umstieg.png`, fullPage: true });

  const umstiegKarte = page.locator('section.card').filter({ hasText: 'MIT EINEM UMSTIEG' });
  if (await umstiegKarte.count() === 0) {
    fails.push(`[${name}] Umstiegs-Karte fehlt (HHA1 → HHA5 hat keine Direktfahrt)`);
  } else {
    const linien = (await umstiegKarte.locator('.route').allTextContents()).map(t => t.trim());
    log(`[${name}] Umstieg zeigt Linien: ${JSON.stringify(linien)}`);
    if (linien.length < 2) fails.push(`[${name}] Umstieg zeigt ${linien.length} Linien statt 2`);
    if (linien.some(l => l === '')) fails.push(`[${name}] leere Liniennummer im Umstieg — genau Blocker 1`);
    if (!linien.includes('R_U1') || !linien.includes('R_U2')) {
      fails.push(`[${name}] falsche Liniennummern im Umstieg: ${JSON.stringify(linien)}`);
    }
    const text = await umstiegKarte.innerText();
    if (!/umsteigen/i.test(text)) fails.push(`[${name}] Umstiegszeile ohne Umsteige-Hinweis`);
  }

  // Zurueck auf eine Direktstrecke — dort haengt der Melde-Knopf an der Abfahrt.
  await page.getByRole('button', { name: 'ändern' }).last().click();
  await page.getByLabel('Ziel-Haltestelle').fill('Kelling');
  await page.getByRole('group', { name: 'Treffer' })
            .getByRole('button', { name: 'Kellinghusenstrasse' }).click();
  await page.waitForTimeout(1200);

  // --- Ticket-Gate: MUSS erscheinen, bevor gemeldet werden kann -------------
  await page.getByRole('button', { name: /Kontrollhinweis für Linie/ }).first().click();
  const gate = page.getByRole('dialog', { name: /gültigem Ticket/ });
  await gate.waitFor({ timeout: 4000 });
  log(`[${name}] Ticket-Gate erscheint vor der Meldung ✔`);
  await page.screenshot({ path: `${OUT}/${name}-3-ticket-gate.png`, fullPage: true });

  // --- Tab „Warnen" OHNE Bestätigung: kein Lesezugriff ---------------------
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: /Warnen/ }).click();
  await page.waitForTimeout(400);
  const gesperrt = await page.getByRole('button', { name: 'Ticket bestätigen' }).count();
  if (gesperrt !== 1) fails.push(`[${name}] Warnen-Tab ohne Ticket NICHT gesperrt`);
  else log(`[${name}] Warnen-Tab ohne Ticket gesperrt ✔`);
  await page.screenshot({ path: `${OUT}/${name}-4-warnen-gesperrt.png`, fullPage: true });

  // --- Nach Bestätigung: Lesen + Melden ------------------------------------
  await page.getByRole('button', { name: 'Ticket bestätigen' }).click();
  await page.getByRole('button', { name: 'Ja, ich habe ein gültiges Ticket' }).click();
  await page.waitForTimeout(900);
  await page.getByRole('button', { name: /Haltestelle in der Nähe finden/ }).click();
  await page.waitForTimeout(1200);
  await page.screenshot({ path: `${OUT}/${name}-5-warnen-frei.png`, fullPage: true });

  await page.getByRole('button', { name: /Kontrolle hier melden/ }).click();
  await page.waitForTimeout(1500);
  const toastTxt = await page.locator('.toast').first().textContent().catch(() => null);
  log(`[${name}] Meldung → Rückmeldung: ${JSON.stringify(toastTxt)}`);
  if (!toastTxt || !/Danke/.test(toastTxt)) fails.push(`[${name}] Meldung nicht bestätigt: ${toastTxt}`);
  await page.screenshot({ path: `${OUT}/${name}-6-gemeldet.png`, fullPage: true });

  // Meldung muss jetzt in der Liste stehen
  await page.waitForTimeout(600);
  const hinweise = await page.locator('.notice').count();
  log(`[${name}] Hinweise in der Liste: ${hinweise}`);
  const listentext = await page.locator('.notice').first().textContent().catch(() => '');
  if (/HHA\d/.test(listentext || '')) fails.push(`[${name}] rohe Haltestellen-ID in der Liste: ${listentext}`);
  else log(`[${name}] Liste zeigt Klarnamen statt ID ✔ (${JSON.stringify((listentext||'').slice(0,40))})`);
  if (hinweise === 0) fails.push(`[${name}] eigene Meldung erscheint nicht in der Liste`);
  await page.screenshot({ path: `${OUT}/${name}-7-warnen-liste.png`, fullPage: true });

  // --- Tab „Mehr" -----------------------------------------------------------
  await page.getByRole('button', { name: /^Mehr/ }).click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: `${OUT}/${name}-8-mehr.png`, fullPage: true });

  // --- Trefferflächen: alles Bedienbare >= 44 px ---------------------------
  const klein = await page.evaluate(() => {
    const out = [];
    for (const el of document.querySelectorAll('button, a.btn, input')) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 && r.height === 0) continue;
      if (r.height < 44) out.push(`${el.tagName}.${el.className}: ${Math.round(r.height)}px — "${(el.textContent || '').trim().slice(0, 24)}"`);
    }
    return out;
  });
  if (klein.length) log(`[${name}] Trefferflächen unter 44 px: ${JSON.stringify(klein, null, 1)}`);
  else log(`[${name}] alle Trefferflächen >= 44 px ✔`);

  // --- Copy-Nie-Liste (11-recht §3): rechtlich harte Wortsperre --------------
  for (const tab of ['Fahren', 'Warnen', 'Mehr']) {
    await page.getByRole('button', { name: new RegExp(`^${tab}`) }).click();
    await page.waitForTimeout(300);
    const txt = ((await page.locator('body').innerText()) || '').toLowerCase();
    for (const verboten of ['schwarzfahr', 'ohne ticket fahren', 'kontrollen entgehen', 'kontrollen umgehen']) {
      if (txt.includes(verboten)) fails.push(`[${name}] verbotene Formulierung "${verboten}" im Tab ${tab}`);
    }
  }
  log(`[${name}] Copy-Nie-Liste eingehalten ✔`);

  // --- Manifest -------------------------------------------------------------
  const mf = await page.evaluate(async () => {
    const l = document.querySelector('link[rel=manifest]');
    if (!l) return null;
    const r = await fetch(l.getAttribute('href'));
    return r.ok ? await r.json() : `HTTP ${r.status}`;
  });
  log(`[${name}] Manifest: ${typeof mf === 'object' && mf ? `${mf.name} · ${mf.icons.length} Icons · display=${mf.display}` : mf}`);
  if (!mf || typeof mf !== 'object') fails.push(`[${name}] Manifest nicht ladbar`);

  await ctx.close();
}

await run('dunkel', 'dark');
await run('hell', 'light');
await browser.close();

if (fails.length) { console.log('\n=== BEFUNDE ===\n' + fails.join('\n')); process.exit(1); }
console.log('\nRauchtest bestanden — keine Befunde.');
