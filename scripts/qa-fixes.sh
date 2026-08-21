#!/usr/bin/env bash
# ============================================================================
# qa-fixes.sh — deterministische Auto-Fixer des QA-Loops (Hook pro Gate-Name).
# Empfängt Gate-Namen als $1 und Gate-Output auf stdin. Fixer sind NUR
# deterministische, versionskontrollierbare Korrekturen — niemals heuristische
# Code-Rewrites. Jeder ausgeführte Fix schreibt eine Zeile nach docs/qa/fixes.log.
# ============================================================================
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GATE="${1:-?}"; INPUT=$(cat); mkdir -p "$ROOT/docs/qa"
log() { echo "$(date -u +%FT%TZ) gate=$GATE fix=$1" >> "$ROOT/docs/qa/fixes.log"; }

case "$GATE" in
  flutteranalyze)
    # deterministisch: triviale Format-Korrekturen sind erlaubt; alles andere → Bericht
    if echo "$INPUT" | grep -q "prefer_const_constructors"; then
      log "hint-only(prefer_const) — Analyzer-Infos werden nicht auto-gefixt (Style-Entscheidung)"; fi
    ;;
  build)
    # NuGet-Speichernot ist in kleinen Umgebungen deterministisch behebbar (Cache-Umzug)
    if echo "$INPUT" | grep -q "No space left on device"; then
      mkdir -p /home/user/.cache/nuget; rm -rf /tmp/nuget; log "nuget-cache-relocate"; fi
    ;;
  *)
    log "kein automatischer Fix fuer Gate '$GATE' — Fehler dokumentiert, menschliche Prüfung folgt (Prozess 26-build-log)"
    ;;
esac
exit 0
