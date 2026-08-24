#!/usr/bin/env bash
# verify-ingest-e2e.sh — Ende-zu-Ende-Beleg des Echtdaten-Ingests.
#
# Faehrt die API mit ALLEN Produktionsschaltern gleichzeitig hoch
#   Data:Provider=postgres · Jobs:Storage=postgres · Ingest:Enabled=true
# und belegt am laufenden System:
#   1. Hangfire fuehrt rt-poll wirklich aus (nicht nur: registriert ihn)
#   2. der Lauf schreibt eine Zeile nach ingest_metrics (Persistenz)
#   3. die API bleibt dabei erreichbar (der Job reisst sie nicht mit)
#   4. Spitzenspeicher des API-Prozesses unter echter Last (Prod-RAM-Frage)
#
# Vier Zustaende wie im QA-Loop: 0 gruen · 2 rot · 3 uebersprungen.
# Kein Netz zum echten Feed ⇒ 3 (uebersprungen), NIE gruen und NIE rot.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="/tmp/dotnet:$PATH"

FEED="${TG_E2E_FEED:-https://realtime.gtfs.de/realtime-free.pb}"
PORT="${TG_E2E_PORT:-5199}"
API="http://127.0.0.1:$PORT"
LOG="$(mktemp -d)/api.log"
API_PID=""
STATIC="${TG_E2E_STATIC:-0}"      # 1 = StaticSync vorher ausloesen (251 MB Download, ~2 min)

aufraeumen() {
  [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null
  [ -n "$API_PID" ] && wait "$API_PID" 2>/dev/null
}
trap aufraeumen EXIT

sagen() { printf '%s\n' "$*"; }

# --- Vorbedingung 1: Postgres --------------------------------------------
CS="$("$ROOT/scripts/pg-dev.sh" env 2>/dev/null)"
if [ -z "$CS" ]; then
  sagen "UEBERSPRUNGEN: keine Postgres-Instanz (scripts/pg-dev.sh up)."
  exit 3
fi
sagen "Postgres: $CS"

# --- Vorbedingung 2: Feed erreichbar -------------------------------------
if ! curl -sfI --max-time 60 "$FEED" >/dev/null 2>&1; then
  sagen "UEBERSPRUNGEN: Feed $FEED nicht erreichbar. Das ist KEIN gruener und KEIN roter Gate."
  exit 3
fi
FEEDBYTES=$(curl -sI --max-time 60 "$FEED" | awk '/[Cc]ontent-[Ll]ength/{print $2}' | tr -d '\r')
sagen "Feed erreichbar: $FEEDBYTES Bytes"

PSQL="$HOME/.cache/pgenv/bin/psql"
[ -x "$PSQL" ] || PSQL="psql"
DBURL="postgresql://postgres@127.0.0.1:5433/transitguard"
sql() { "$PSQL" "$DBURL" -tAc "$1" 2>/dev/null; }

# Ausgangsstand merken, damit „neue Zeile" wirklich neu heisst.
VORHER=$(sql "SELECT count(*) FROM ingest_metrics")
VORHER=${VORHER:-0}
sagen "ingest_metrics vor dem Start: $VORHER Zeilen"

# --- API mit allen Produktionsschaltern ----------------------------------
dotnet build src/TransitGuard.Api -c Release --nologo -v q >/dev/null 2>&1 || { sagen "ROT: Build fehlgeschlagen"; exit 2; }
DLL="src/TransitGuard.Api/bin/Release/net8.0/TransitGuard.Api.dll"

# --- Gegenprobe zur Persistenz, in EIGENEM Prozess -----------------------
# Warum hier und nicht als xUnit-Test: Hangfire setzt JobStorage.Current prozessweit.
# Ein Wechsel Postgres → Memory INNERHALB eines Testprozesses schreibt weiter in den
# zuerst gesetzten Speicher — der Test waere gruen geworden, ohne den Defekt zu beruehren.
sagen "Gegenprobe: derselbe Start mit Jobs__Storage=memory darf in der DB NICHTS hinterlassen …"
"$PSQL" "$DBURL" -qc "DELETE FROM hangfire.hash WHERE key LIKE 'recurring-job:%'" >/dev/null 2>&1
ASPNETCORE_URLS="http://127.0.0.1:$((PORT+1))" \
Data__Provider=postgres DATABASE__APP="$CS" DATABASE__BILLING="$CS" DATABASE__INGEST="$CS" \
Jobs__Storage=memory Ingest__Enabled=true Ingest__FeedUrl="http://127.0.0.1:1/kein-feed.pb" \
nohup dotnet "$DLL" >"$LOG.gegenprobe" 2>&1 &
GP_PID=$!
curl -sf --retry 40 --retry-connrefused --retry-delay 1 --max-time 90 \
  "http://127.0.0.1:$((PORT+1))/health/ready" >/dev/null 2>&1
GP_N=$(sql "SELECT count(DISTINCT key) FROM hangfire.hash WHERE key LIKE 'recurring-job:%'")
kill "$GP_PID" 2>/dev/null; wait "$GP_PID" 2>/dev/null
if [ "${GP_N:-0}" != "0" ]; then
  sagen "ROT: Gegenprobe untauglich — mit MemoryStorage standen ${GP_N} Auftraege in der DB."
  sagen "     Damit sagt der Postgres-Nachweis unten nichts aus."
  exit 2
fi
sagen "  Gegenprobe sauber: 0 Auftraege in der DB. Der Nachweis unten ist damit aussagekraeftig."


ASPNETCORE_URLS="$API" \
Data__Provider=postgres \
DATABASE__APP="$CS" DATABASE__BILLING="$CS" DATABASE__INGEST="$CS" \
Jobs__Storage=postgres \
Ingest__Enabled=true Ingest__FeedUrl="$FEED" Ingest__City=hamburg \
nohup dotnet "$DLL" >"$LOG" 2>&1 &
API_PID=$!
sagen "API gestartet (PID $API_PID), Log: $LOG"

if ! curl -sf --retry 40 --retry-connrefused --retry-delay 1 --max-time 90 "$API/health/ready" >/dev/null 2>&1; then
  sagen "ROT: API wurde nicht bereit."; tail -30 "$LOG"; exit 2
fi
sagen "API bereit."

# --- Belegt der Auftragsspeicher wirklich Postgres? ----------------------
WIEDERKEHREND=$(sql "SELECT count(DISTINCT key) FROM hangfire.hash WHERE key LIKE 'recurring-job:%'")
sagen "wiederkehrende Auftraege in hangfire.hash: ${WIEDERKEHREND:-0}"
if [ "${WIEDERKEHREND:-0}" -lt 4 ]; then
  sagen "ROT: Zeitplan steht nicht im Postgres-Auftragsspeicher."; tail -30 "$LOG"; exit 2
fi

# --- optional: StaticSync ausloesen, damit die Whitelist steht -----------
if [ "$STATIC" = "1" ]; then
  sagen "Loese static-sync ueber das Hangfire-Dashboard aus (251 MB Download) …"
  curl -sf -X POST "$API/admin/hangfire/recurring/trigger" --data "jobs[]=static-sync" >/dev/null \
    || sagen "  (Ausloesen fehlgeschlagen — weiter ohne Whitelist)"
  for _ in $(seq 1 40); do
    N=$(sql "SELECT count(*) FROM hangfire.job WHERE statename='Succeeded'")
    [ "${N:-0}" -ge 1 ] && break
    sleep 5
  done
fi

# --- Auf einen echten Poll-Durchlauf warten ------------------------------
sagen "Warte auf einen rt-poll-Durchlauf (Zeitplan: Sekunde 0 jeder Minute) …"
NEU=0
for i in $(seq 1 30); do
  JETZT=$(sql "SELECT count(*) FROM ingest_metrics")
  JETZT=${JETZT:-0}
  if [ "$JETZT" -gt "$VORHER" ]; then NEU=$((JETZT - VORHER)); break; fi
  sleep 6
done

if [ "$NEU" -eq 0 ]; then
  sagen "ROT: nach $((i*6)) s keine neue ingest_metrics-Zeile — Hangfire hat den Poll nicht ausgefuehrt."
  tail -40 "$LOG"; exit 2
fi
sagen "Neue ingest_metrics-Zeilen: $NEU"

# --- Inhalt der Zeile pruefen (nicht nur: es gibt eine) ------------------
# Spaltennamen kommen aus docs/sql/0001_core.sql (feed_age_s, tu_count, alert_count) —
# nicht aus den C#-Eigenschaftsnamen. Erster Anlauf las die C#-Namen und bekam still NULL.
ZEILE=$(sql "SELECT coalesce(http_status,-1)||'|'||coalesce(bytes,0)||'|'||coalesce(entities,0)||'|'||coalesce(tu_count,0)||'|'||coalesce(alert_count,0)||'|'||coalesce(vp_count,-1)||'|'||coalesce(city_trips_matched,0)||'|'||coalesce(parse_ms,0)||'|'||coalesce(feed_age_s,-1) FROM ingest_metrics ORDER BY ts DESC LIMIT 1")
IFS='|' read -r ST BY EN TU AL VP CT PM AGE <<<"$ZEILE"
sagen "letzte Zeile: status=$ST bytes=$BY entities=$EN tu=$TU alerts=$AL vp=$VP stadt_tus=$CT parse_ms=$PM feed_alter=${AGE}s"
FEHLER=0
[ "${ST:-0}" = "200" ]      || { sagen "ROT: http_status $ST statt 200"; FEHLER=1; }
[ "${BY:-0}" -gt 1000000 ]  || { sagen "ROT: bytes=$BY — das war kein echter Feed"; FEHLER=1; }
[ "${EN:-0}" -gt 10000 ]    || { sagen "ROT: entities=$EN — das war kein echter Feed"; FEHLER=1; }
[ "${TU:-0}" -gt 1000 ]     || { sagen "ROT: tu_count=$TU"; FEHLER=1; }
[ "${AL:-0}" -gt 100 ]      || { sagen "ROT: alert_count=$AL"; FEHLER=1; }
if [ "$STATIC" = "1" ]; then
  [ "${CT:-0}" -gt 100 ] || { sagen "ROT: city_trips_matched=$CT trotz StaticSync"; FEHLER=1; }
else
  sagen "  Hinweis: city_trips_matched=$CT ist ohne StaticSync ERWARTET (leere Whitelist)."
  sagen "  Mit TG_E2E_STATIC=1 laeuft der StaticSync vorher und der Wert muss > 100 sein."
fi

# --- Blieb die API waehrend des Polls erreichbar? ------------------------
if ! curl -sf --max-time 15 "$API/health/ready" >/dev/null 2>&1; then
  sagen "ROT: API nach dem Poll nicht mehr bereit — der Job hat sie mitgerissen."; FEHLER=1
fi
# Echter Fachaufruf, nicht nur der Health-Endpunkt: /v1/* verlangt einen Geraete-Token
# (Device-Auth-Middleware). Ein 401 hier waere ein Skriptfehler, kein API-Fehler —
# der erste Anlauf ist genau darueber gestolpert.
TOK=$(curl -sf -X POST --max-time 15 "$API/v1/devices" | sed -n 's/.*"device_token":"\([^"]*\)".*/\1/p')
if [ -z "$TOK" ]; then
  sagen "ROT: POST /v1/devices lieferte keinen Token"; FEHLER=1
else
  CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 -H "X-Device-Token: $TOK" "$API/v1/cities")
  [ "$CODE" = "200" ] || { sagen "ROT: /v1/cities antwortet $CODE"; FEHLER=1; }
  CODE2=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 -H "X-Device-Token: $TOK" "$API/v1/devices/me")
  [ "$CODE2" = "200" ] || { sagen "ROT: /v1/devices/me antwortet $CODE2"; FEHLER=1; }
fi

# --- Spitzenspeicher des API-Prozesses (Prod-RAM-Frage) ------------------
HWM=$(awk '/VmHWM/{print $2}' "/proc/$API_PID/status" 2>/dev/null)
RSS=$(awk '/VmRSS/{print $2}' "/proc/$API_PID/status" 2>/dev/null)
sagen "API-Prozess: Spitzen-RSS $((${HWM:-0}/1024)) MB, aktuell $((${RSS:-0}/1024)) MB"

if grep -qiE "unhandled exception|CronFormatException|Unable to resolve service" "$LOG"; then
  sagen "ROT: schwerer Fehler im API-Log:"; grep -iE "unhandled exception|CronFormatException|Unable to resolve service" "$LOG" | head -5
  FEHLER=1
fi

[ "$FEHLER" = 0 ] && { sagen "GRUEN: Ingest laeuft unter Hangfire gegen den echten Feed, Persistenz belegt, API stabil."; exit 0; }
exit 2
