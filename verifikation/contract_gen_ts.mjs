// ---------------------------------------------------------------------------
// Erzeugt web/src/contract.gen.ts aus docs/contract/api-contract.json.
// Die PWA leitet ihre Typen daraus ab — dadurch wird jede Vertragsdrift zum
// TypeScript-Compile-Fehler an der Verwendungsstelle (`tsc -b` im Web-Build).
//
// Nutzung: node verifikation/contract_gen_ts.mjs [--out <datei>]
// ---------------------------------------------------------------------------
import { readFileSync, writeFileSync } from 'node:fs';

const argOut = process.argv.indexOf('--out');
const OUT = argOut > 0 ? process.argv[argOut + 1] : 'web/src/contract.gen.ts';
const vertrag = JSON.parse(readFileSync('docs/contract/api-contract.json', 'utf8'));

/** Pfad in Schritte zerlegen: 'a[].b' → [{feld:'a'},{elem:true},{feld:'b'}] */
function schritte(pfad) {
  const out = [];
  for (const teil of pfad.split('.')) {
    let name = teil;
    let tiefen = 0;
    while (name.endsWith('[]')) { name = name.slice(0, -2); tiefen++; }
    if (name) out.push({ feld: name });
    for (let i = 0; i < tiefen; i++) out.push({ elem: true });
  }
  return out;
}

function baueBaum(felder) {
  const wurzel = { typen: [], felder: new Map(), elem: null, optional: false };
  const hole = (pfad) => {
    let n = wurzel;
    for (const s of schritte(pfad)) {
      if (s.elem) n = (n.elem ??= { typen: [], felder: new Map(), elem: null, optional: false });
      else {
        if (!n.felder.has(s.feld)) n.felder.set(s.feld, { typen: [], felder: new Map(), elem: null, optional: false });
        n = n.felder.get(s.feld);
      }
    }
    return n;
  };
  for (const [pfad, info] of Object.entries(felder)) {
    const n = hole(pfad === '(wurzel)' ? '' : pfad);
    n.typen = info.typen;
    n.optional = info.optional === true;
  }
  return wurzel;
}

const TS = { string: 'string', number: 'number', boolean: 'boolean', null: 'null' };

function emit(node, einzug = 0) {
  const t = node.typen ?? [];
  const p = ' '.repeat(einzug);
  if (t.includes('array')) {
    return node.elem ? `Array<${emit(node.elem, einzug)}>` : 'unknown[]';
  }
  if (t.includes('object')) {
    if (node.felder.size === 0) return 'Record<string, unknown>';
    const zeilen = [...node.felder.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([k, v]) => `${p}  ${k}${v.optional ? '?' : ''}: ${emit(v, einzug + 2)};`);
    return `{\n${zeilen.join('\n')}\n${p}}`;
  }
  const skalare = t.filter(x => TS[x]).map(x => TS[x]);
  return skalare.length ? [...new Set(skalare)].join(' | ') : 'unknown';
}

const typName = (k) => k.split(/[.\-_]/).map(s => s[0].toUpperCase() + s.slice(1)).join('') + 'Response';

const kopf = `// =============================================================================
// GENERIERT — NICHT VON HAND ÄNDERN.
// Quelle: docs/contract/api-contract.json (aus der laufenden API aufgezeichnet).
// Neu erzeugen: node verifikation/contract_gen_ts.mjs
// Frischeprüfung: scripts/verify-contract.sh (Gate im QA-Loop)
//
// Zweck: Die PWA leitet ihre Typen HIER ab statt sie zweitzuschreiben. Benennt
// der Server ein Feld um, ändert sich diese Datei — und jede Lesestelle im
// Client wird zum Compile-Fehler. Das ist die strukturelle Antwort auf die drei
// Launch-Blocker vom 21.08.2026, die alle Vertragsdrift waren.
// =============================================================================
/* eslint-disable */
`;

const teile = [kopf];
const erfordert = {};
for (const [k, e] of Object.entries(vertrag.endpunkte)) {
  teile.push(`/** ${e.methode} ${e.pfad}${e.hinweis ? `\n  * ${e.hinweis}` : ''} */`);
  teile.push(`export type ${typName(k)} = ${emit(baueBaum(e.felder))};\n`);
  if (e.erfordert?.headers) erfordert[k] = e.erfordert.headers;
}

teile.push('/** Pflicht-Header je Endpunkt — im Vertrag durch Negativproben belegt. */');
// Nur echte Bezeichner entquoten — Schlüssel wie "reports.create" müssen
// in Anführungszeichen bleiben, sonst ist die Datei kein gültiges TypeScript.
teile.push('export const PFLICHT_HEADER = ' + JSON.stringify(erfordert, null, 2)
  .replace(/"([A-Za-z_$][A-Za-z0-9_$]*)":/g, '$1:') + ' as const;\n');

writeFileSync(OUT, teile.join('\n'));
console.log(`${OUT} erzeugt: ${Object.keys(vertrag.endpunkte).length} Typen`);
