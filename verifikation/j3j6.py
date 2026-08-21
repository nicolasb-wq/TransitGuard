#!/usr/bin/env python3
"""J3 (HVV-Anteil) + J6 (Static/RT-Match-Rate) — 2026-08-21."""
import csv, datetime as dt, json
from collections import Counter, defaultdict
import requests
from google.transit import gtfs_realtime_pb2

# ---- Static laden (nv+rv+fv) ----
trip2route = {}          # trip_id -> (feed, route_id)
for feed in ("nv","rv","fv"):
    with open(f"{feed}/trips.txt", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            trip2route[row["trip_id"]] = (feed, row.get("route_id",""))
print(f"Static trips gesamt: {len(trip2route):,}")

route2agency, agency_names = {}, {}
for feed in ("nv","rv","fv"):
    with open(f"{feed}/agency.txt", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            agency_names[(feed, row["agency_id"])] = row.get("agency_name","")
    with open(f"{feed}/routes.txt", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            route2agency[(feed, row["route_id"])] = row.get("agency_id","")

stops = {}               # stop_id -> (lat, lon)
with open("nv/stops.txt", encoding="utf-8-sig") as f:
    for row in csv.DictReader(f):
        try: stops[row["stop_id"]] = (float(row["stop_lat"]), float(row["stop_lon"]))
        except: pass
with open("rv/stops.txt", encoding="utf-8-sig") as f:
    for row in csv.DictReader(f):
        try: stops[row["stop_id"]] = (float(row["stop_lat"]), float(row["stop_lon"]))
        except: pass
print(f"Static stops geladen: {len(stops):,}")

KERN  = (53.40, 53.75, 9.73, 10.32)     # Hamburg Kern (wie cities-Seed)
UMGEB = (53.30, 53.85, 9.55, 10.50)     # Hamburg + Umland
def inbox(ll, box): return box[0] <= ll[0] <= box[1] and box[2] <= ll[1] <= box[3]

# ---- RT-Feed ----
r = requests.get("https://realtime.gtfs.de/realtime-free.pb", timeout=120,
                 headers={"User-Agent":"TransitGuard-J3J6/1.0"})
print(f"RT-Feed: {len(r.content):,} B @ {dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')}")
feed = gtfs_realtime_pb2.FeedMessage(); feed.ParseFromString(r.content)

n_tu = n_matched = n_kern = n_umgeb = n_delay = 0
stop_lookup_ok = stop_lookup_miss = 0
agency_kern = Counter(); feed_kern = Counter(); matched_feeds = Counter()
hh_trips_with_delay = 0
seen = set()
for ent in feed.entity:
    if not ent.HasField("trip_update"): continue
    tu = ent.trip_update; n_tu += 1
    tid = tu.trip.trip_id
    sr = trip2route.get(tid)
    if sr: n_matched += 1; matched_feeds[sr[0]] += 1
    stus = list(tu.stop_time_update)
    has_delay = any((s.HasField("arrival") and s.arrival.HasField("delay")) or
                    (s.HasField("departure") and s.departure.HasField("delay")) for s in stus)
    if has_delay: n_delay += 1
    loc_kern = loc_umgeb = False
    for s in stus:
        sid = s.stop_id
        if sid in stops:
            stop_lookup_ok += 1; ll = stops[sid]
            if inbox(ll, KERN): loc_kern = True
            if inbox(ll, UMGEB): loc_umgeb = True
        else:
            stop_lookup_miss += 1
    if loc_kern:
        n_kern += 1
        key = (tid, tu.trip.start_date)
        if key not in seen:
            seen.add(key)
            if sr:
                feed_kern[sr[0]] += 1
                ag = agency_names.get((sr[0], route2agency.get(sr,"")), "?")
                agency_kern[ag] += 1
            if has_delay: hh_trips_with_delay += 1
    if loc_umgeb: n_umgeb += 1

print(f"\n=== J6: MATCH-RATE Static<->RT ===")
print(f"TUs: {n_tu:,} | im Static auflosbar: {n_matched:,} ({100*n_matched/n_tu:.1f} %) | nach Feed: {dict(matched_feeds)}")
print(f"TUs mit Delay: {n_delay:,} ({100*n_delay/n_tu:.1f} %) | Stop-Lookup ok/miss: {stop_lookup_ok:,}/{stop_lookup_miss:,}")
print(f"\n=== J3: HAMBURG-ANTEIL (geografisch via STU-Stop-IDs) ===")
print(f"TUs mit >=1 Stop im KERN-bbox:    {n_kern:,} ({100*n_kern/n_tu:.1f} % aller TUs)")
print(f"TUs mit >=1 Stop im UMGEB-bbox:   {n_umgeb:,} ({100*n_umgeb/n_tu:.1f} % aller TUs)")
print(f"distinct HH-Kern-Trips: {len(seen):,} | davon mit Delay: {hh_trips_with_delay:,} ({100*hh_trips_with_delay/max(1,len(seen)):.1f} %)")
print(f"\nHH-Kern Trips nach Static-Feed: {dict(feed_kern)}")
print("Top-12 Agenturen im HH-Kern (distinct Trips):")
for ag, c in agency_kern.most_common(12): print(f"   {c:5,}  {ag[:70]}")
