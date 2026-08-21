// ---------------------------------------------------------------------------
// Erzeugt app/lib/contract.gen.dart aus docs/contract/api-contract.json.
//
// Dart kennt keine strukturelle Typprüfung — ein Compile-Fehler wie in der PWA
// ist hier nicht zu haben. Stattdessen wird der Vertrag als DATEN erzeugt und
// von Tests durchgesetzt (app/test/contract_test.dart):
//   * Pflicht-Header je Endpunkt → MockClient-Test prüft, dass Api sie sendet
//     (Launch-Blocker 3: fehlender Idempotency-Key, jede Meldung scheiterte 422)
//   * Pflichtfelder je Endpunkt → Test prüft, dass die DTOs sie lesen
//   * Die aufgezeichneten Beispielantworten laufen durch die echten Parser
//     (Launch-Blocker 2: route_id nicht-nullable, jede Fahrtensuche warf)
//
// Nutzung: node verifikation/contract_gen_dart.mjs [--out <datei>]
// ---------------------------------------------------------------------------
import { readFileSync, writeFileSync } from 'node:fs';

const argOut = process.argv.indexOf('--out');
const OUT = argOut > 0 ? process.argv[argOut + 1] : 'app/lib/contract.gen.dart';
const vertrag = JSON.parse(readFileSync('docs/contract/api-contract.json', 'utf8'));

const dartListe = (xs) => `[${xs.map(x => `'${x.replace(/'/g, "\\'")}'`).join(', ')}]`;

const header = {}, pflicht = {}, optional = {}, unbeobachtet = [];
for (const [k, e] of Object.entries(vertrag.endpunkte)) {
  if (e.erfordert?.headers) header[k] = e.erfordert.headers;
  if (e.unbeobachtet) { unbeobachtet.push(k); continue; }
  const p = [], o = [];
  for (const [pfad, info] of Object.entries(e.felder)) {
    if (pfad === '(wurzel)') continue;
    (info.optional ? o : p).push(pfad);
  }
  pflicht[k] = p; optional[k] = o;
}

const abschnitt = (name, karte, kommentar) =>
  `${kommentar}\nconst ${name} = <String, List<String>>{\n`
  + Object.entries(karte).sort(([a], [b]) => a.localeCompare(b))
      .map(([k, v]) => `  '${k}': ${dartListe(v)},`).join('\n')
  + '\n};\n';

const inhalt = `// =============================================================================
// GENERIERT — NICHT VON HAND ÄNDERN.
// Quelle: docs/contract/api-contract.json (aus der laufenden API aufgezeichnet).
// Neu erzeugen: node verifikation/contract_gen_dart.mjs
// Frischeprüfung: scripts/verify-contract.sh (Gate im QA-Loop)
// =============================================================================

${abschnitt('kVertragPflichtHeader', header,
  '/// Header, ohne die der Server ablehnt — im Vertrag durch Negativproben belegt.')}
${abschnitt('kVertragPflichtFelder', pflicht,
  '/// Feldpfade, die der Server IMMER sendet (in jeder Beobachtung vorhanden).')}
${abschnitt('kVertragOptionaleFelder', optional,
  '/// Feldpfade, die FEHLEN koennen (WhenWritingNull oder anker-/abfrageabhaengig).')}
/// Endpunkte, deren Antwortform die Aufzeichnung NICHT sehen konnte
/// (leere Liste bzw. null). Bewusst ausgewiesen statt stillschweigend als
/// „keine Felder" verbucht — hier schuetzt der Vertrag nichts.
const kVertragUnbeobachtet = <String>${dartListe(unbeobachtet)};
`;

writeFileSync(OUT, inhalt);
console.log(`${OUT} erzeugt: ${Object.keys(pflicht).length} Endpunkte, ${unbeobachtet.length} unbeobachtet`);
