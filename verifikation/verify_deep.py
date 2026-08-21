#!/usr/bin/env python3
"""Vertiefung: vehicle-Feld-Inhalt, Delay-Magnituden, Alert-Regionen (gtfs.de)."""
import datetime as dt, statistics
from collections import Counter
import requests
from google.transit import gtfs_realtime_pb2

r = requests.get("https://realtime.gtfs.de/realtime-free.pb", timeout=120,
                 headers={"User-Agent": "TransitGuard-Verification/1.0"})
print(f"gtfs.de HTTP {r.status_code} | {len(r.content):,} B | {dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')}")
feed = gtfs_realtime_pb2.FeedMessage(); feed.ParseFromString(r.content)

veh_samples, veh_with_id, veh_with_label, veh_with_pos = [], 0, 0, 0
delay_secs, trip_id_samples, hd = [], [], Counter()
regions = Counter()
KEYS = ["hamburg","hvv","berlin","bvg","münchen","muenchen","mvg","stuttgart","vvs","köln","koeln","kvb",
        "rhein","ruhr","frankfurt","rmv","hessen","bayern","sachsen","leipzig","dresden","bremen","bsag",
        "niedersachsen","hannover","üstra","uestra","baden","nvbw","dress","nuernberg","nürnberg","augsburg"]
headers_distinct = {}
for ent in feed.entity:
    if ent.HasField("trip_update"):
        tu = ent.trip_update
        if tu.HasField("vehicle"):
            v = tu.vehicle
            if v.HasField("id") or v.id: veh_with_id += 1
            if v.label: veh_with_label += 1
            if v.license_plate: veh_with_pos += 1
            if len(veh_samples) < 5: veh_samples.append(f"id={v.id!r} label={v.label!r}")
        if len(trip_id_samples) < 5: trip_id_samples.append(tu.trip.trip_id[:60])
        for s in tu.stop_time_update:
            if s.HasField("departure") and s.departure.HasField("delay"):
                if len(delay_secs) < 200000: delay_secs.append(s.departure.delay)
    elif ent.HasField("alert"):
        hdr = next((t.text for t in ent.alert.header_text.translation), "")
        desc = next((t.text for t in ent.alert.description_text.translation), "")
        h = hdr.strip()
        headers_distinct[h] = headers_distinct.get(h, 0) + 1
        blob = (hdr + " " + desc[:200]).lower()
        for k in KEYS:
            if k in blob: regions[k] += 1

print(f"TUs mit vehicle-Feld: id={veh_with_id}, label={veh_with_label}, position={veh_with_pos}")
print("vehicle-Stichproben:", *veh_samples, sep="\n  ")
print("trip_id-Stichproben:", *trip_id_samples, sep="\n  ")
if delay_secs:
    ds = sorted(delay_secs)
    n = len(ds)
    print(f"Departure-Delays (n={n:,}): median {statistics.median(ds):.0f}s | mean {statistics.mean(ds):.0f}s | p10 {ds[n//10]}s | p90 {ds[9*n//10]}s | min {ds[0]}s max {ds[-1]}s")
print(f"Distinct Header-Texte gesamt: {len(headers_distinct):,}")
print("Alerts je Regionsschluessel (Header+Desc, Mehrfachtreffer moeglich):")
for k, v in regions.most_common(15): print(f"   {k}: {v:,}")
print("Top-15 Header nach Haeufigkeit:")
for h, c in sorted(headers_distinct.items(), key=lambda x: -x[1])[:15]: print(f"   {c:6,}x  {h[:90]}")
print("Alle 411 nicht sinnvoll druckbar -> speichere Vollliste")
with open("verifikation/alert_headers_distinct.txt","w") as f:
    for h, c in sorted(headers_distinct.items(), key=lambda x: -x[1]): f.write(f"{c}\t{h}\n")
