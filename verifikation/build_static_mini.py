#!/usr/bin/env python3
"""
Erzeugt tests/fixtures/static_mini.zip reproduzierbar.

Warum ein Generator statt einer von Hand gepflegten ZIP: Die Fixture ist
Vertragsgrundlage für Ingest-, API- und Client-Tests. Bis 22.08.2026 kannte sie
nur EINE Linie über vier Haltestellen — jedes Haltestellenpaar war direkt
verbunden, und Umstiegsverbindungen kamen darin schlicht nicht vor. Genau dort
saß Launch-Blocker 1 (leg_a.RouteId statt leg_a.route_id): unbemerkt, weil
ungetestet.

Neu:
  * Haltestelle HHA5 (Farmsen) und Linie U2, die NUR HHA3 -> HHA5 bedient.
    HHA1 -> HHA5 hat damit KEINE Direktverbindung und erzwingt einen Umstieg.
  * U1 und U2 fahren im 20-Minuten-Takt über den ganzen Betriebstag. Der
    TransferRouter verlangt, dass Bein A im Fenster [jetzt-30s, jetzt+90min]
    abfährt — mit festen Einzelzeiten wäre der Test von der Uhrzeit abhängig.
  * Die alten Fahrten T_HH_1..3 und T_XX_1 bleiben unverändert, damit die
    bestehenden Erwartungen an sie gültig bleiben.

Nutzung: python3 verifikation/build_static_mini.py [ziel.zip]
"""
import sys, zipfile, pathlib

ZIEL = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "tests/fixtures/static_mini.zip")

# --- Haltestellen (HHA* liegen in der Hamburg-Box 53.30-53.85 / 9.55-10.50) ---
STOPS = [
    ("HHA1", "Jungfernstieg",          53.554, 9.991),
    ("HHA2", "Stephansplatz",          53.559, 9.993),
    ("HHA3", "Kellinghusenstrasse",    53.573, 9.986),
    ("HHA4", "Wandsbek Gartenstadt",   53.603, 10.049),
    ("HHA5", "Farmsen",                53.609, 10.114),   # nur von U2 bedient
    ("XX1",  "Ausbesserungswerk Nord", 53.900, 10.600),   # ausserhalb der Box
    ("XX2",  "Suedlich weit weg",      53.200, 9.600),    # ausserhalb der Box
]

ROUTES = [
    ("R_U1", "HHA", "U1", "U1", 1),
    ("R_U2", "HHA", "U2", "U2", 1),
    ("R_REG", "DB", "RE", "Regional", 2),
]

# --- Bestandsfahrten, unveraendert ---
TRIPS = [
    ("T_HH_1", "R_U1", "S1", "Ohlsdorf", 1),
    ("T_HH_2", "R_U1", "S1", "Norderstedt Mitte", 0),
    ("T_HH_3", "R_U1", "S3", "Ohlsdorf (Abend)", 1),
    ("T_XX_1", "R_REG", "S2", "Auswaerts", 1),
]
ZEITEN = [
    ("T_HH_1", [("HHA1", 36000), ("HHA2", 36120), ("HHA3", 36240), ("HHA4", 36360)]),
    ("T_HH_2", [("HHA4", 36600), ("HHA3", 36720), ("HHA2", 36840), ("HHA1", 36960)]),
    ("T_HH_3", [("HHA1", 75000), ("HHA2", 75120), ("HHA3", 75240), ("HHA4", 75360)]),
    ("T_XX_1", [("XX1", 38000), ("XX2", 38120)]),
]

# --- Taktfahrten fuer die Umstiegsstrecke -----------------------------------
TAKT_S = 20 * 60          # 20-Minuten-Takt
UMSTIEG_VERSATZ_S = 8 * 60  # U2 faehrt 8 min nach U1-Abfahrt in HHA3 los
# U1: HHA1(+0) HHA2(+2) HHA3(+4)      → Ankunft HHA3 bei t+240 s
# U2: HHA3(+8) HHA5(+14)              → Abfahrt HHA3 bei t+480 s
# Wartezeit am Umstieg = 240 s > transferBufferSeconds (120 s). Bein A = 4 min < 45 min.
for i, t0 in enumerate(range(0, 24 * 3600, TAKT_S)):
    a, b = f"T_U1_{i:03d}", f"T_U2_{i:03d}"
    TRIPS.append((a, "R_U1", "S1", "Kellinghusenstrasse", 1))
    TRIPS.append((b, "R_U2", "S1", "Farmsen", 1))
    ZEITEN.append((a, [("HHA1", t0), ("HHA2", t0 + 120), ("HHA3", t0 + 240)]))
    ZEITEN.append((b, [("HHA3", t0 + UMSTIEG_VERSATZ_S), ("HHA5", t0 + UMSTIEG_VERSATZ_S + 360)]))

def hms(s): return f"{s // 3600:02d}:{s % 3600 // 60:02d}:{s % 60:02d}"

dateien = {
    "stops.txt": "stop_id,stop_name,stop_lat,stop_lon\n"
        + "".join(f"{i},{n},{la},{lo}\n" for i, n, la, lo in STOPS),
    "routes.txt": "route_id,agency_id,route_short_name,route_long_name,route_type\n"
        + "".join(f"{a},{b},{c},{d},{e}\n" for a, b, c, d, e in ROUTES),
    "trips.txt": "trip_id,route_id,service_id,trip_headsign,direction_id\n"
        + "".join(f"{a},{b},{c},{d},{e}\n" for a, b, c, d, e in TRIPS),
    "stop_times.txt": "trip_id,stop_sequence,stop_id,arrival_time,departure_time\n"
        + "".join(
            f"{trip},{n + 1},{stop},{hms(sek)},{hms(sek)}\n"
            for trip, halte in ZEITEN for n, (stop, sek) in enumerate(halte)),
}

ZIEL.parent.mkdir(parents=True, exist_ok=True)
# Feste Zeitstempel: gleicher Input ⇒ byte-gleiche ZIP (pruefbare Frische).
with zipfile.ZipFile(ZIEL, "w", zipfile.ZIP_DEFLATED) as z:
    for name in sorted(dateien):
        info = zipfile.ZipInfo(name, date_time=(2026, 8, 22, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, dateien[name])

hha = [s for s in STOPS if s[0].startswith("HHA")]
hha_trips = [t for t in TRIPS if not t[0].startswith("T_XX")]
print(f"{ZIEL}: {len(STOPS)} Haltestellen ({len(hha)} in Box), "
      f"{len(TRIPS)} Fahrten ({len(hha_trips)} in Box), {len(ROUTES)} Linien")
