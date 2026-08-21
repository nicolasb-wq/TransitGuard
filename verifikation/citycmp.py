#!/usr/bin/env python3
"""Staedtevergleich des gtfs.de-RT (Kontext fuer J3) — korrigierte Fassung 2026-08-21.
Zaehlt TUs je Stadt-BBox und davon die mit Delay-Wert (je TU genau einmal).
Voraussetzung: entpackte Static stops.txt unter ./nv/ und ./rv/ (gtfs.de free).
Hinweis: erste Messrunde hatte einen Zaehler-Bug (Delay je Stop statt je TU);
diese Fassung zaehlt korrekt. Hamburg-Delay-Referenzwert aus j3j6.py: 94,5 %.
"""
import csv, datetime as dt
from collections import Counter
import requests
from google.transit import gtfs_realtime_pb2

stops = {}
for feed in ("nv", "rv"):
    with open(f"{feed}/stops.txt", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            try:
                stops[row["stop_id"]] = (float(row["stop_lat"]), float(row["stop_lon"]))
            except (ValueError, KeyError):
                pass

CITIES = {
    "Hamburg (Kern)": (53.40, 53.75, 9.73, 10.32),
    "Berlin (Stadt)": (52.33, 52.68, 13.08, 13.76),
    "Muenchen":       (48.08, 48.25, 11.36, 11.72),
    "Koeln":          (50.83, 51.06, 6.83, 7.10),
    "Frankfurt":      (50.02, 50.22, 8.54, 8.80),
    "Dresden":        (50.95, 51.15, 13.60, 13.90),
}

r = requests.get("https://realtime.gtfs.de/realtime-free.pb", timeout=120,
                 headers={"User-Agent": "TransitGuard-J3J6/1.0"})
feed = gtfs_realtime_pb2.FeedMessage()
feed.ParseFromString(r.content)

counts, delays = Counter(), Counter()
n_tu = 0
for ent in feed.entity:
    if not ent.HasField("trip_update"):
        continue
    n_tu += 1
    tu = ent.trip_update
    has_delay = any((s.HasField("arrival") and s.arrival.HasField("delay")) or
                    (s.HasField("departure") and s.departure.HasField("delay"))
                    for s in tu.stop_time_update)
    hit = set()
    for s in tu.stop_time_update:
        ll = stops.get(s.stop_id)
        if not ll:
            continue
        for name, (a, b, c, d) in CITIES.items():
            if a <= ll[0] <= b and c <= ll[1] <= d:
                hit.add(name)
                break
    for name in hit:
        counts[name] += 1
        if has_delay:
            delays[name] += 1

print(f"RT: {len(r.content):,} B, {n_tu:,} TUs @ {dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')}")
print(f"{'Stadt':<17}{'TUs jetzt':>10}{'mit Delay':>11}{'Delay-%':>9}")
for name in CITIES:
    print(f"{name:<17}{counts[name]:>10,}{delays[name]:>11,}{100*delays[name]/max(1,counts[name]):>8.1f}%")
