#!/usr/bin/env python3
"""TransitGuard-Unabhaengige Verifikation der Dossier-Messwerte, 2026-08-21."""
import datetime as dt, hashlib, json, statistics, sys
from collections import Counter
import requests
from google.transit import gtfs_realtime_pb2

UA = {"User-Agent": "TransitGuard-Verification/1.0 (Spec-Handhabung 2026-08-21)"}
FEEDS = {
    "gtfs.de": "https://realtime.gtfs.de/realtime-free.pb",
    "VBB": "https://production.gtfsrt.vbb.de/data",
}

def fetch(url, headers=None):
    return requests.get(url, timeout=120, headers={**UA, **(headers or {})})

for name, url in FEEDS.items():
    print(f"\n===== FEED {name} =====")
    r1 = fetch(url)
    print(f"HTTP {r1.status_code} | size {len(r1.content):,} B | fetched {dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')}")
    print(f"Content-Type: {r1.headers.get('Content-Type','-')}")
    etag = r1.headers.get('ETag')
    print(f"ETag: {etag} | Cache-Control: {r1.headers.get('Cache-Control','-')}")
    if r1.status_code != 200:
        continue
    feed = gtfs_realtime_pb2.FeedMessage()
    feed.ParseFromString(r1.content)
    ts = feed.header.timestamp
    age = (dt.datetime.now(dt.timezone.utc) - dt.datetime.fromtimestamp(ts, dt.timezone.utc)).total_seconds() if ts else -1
    print(f"Feed-Alter: {age:.0f}s | Incrementality: {feed.header.incrementality}")
    counts, tu_delay, tu_no_delay = Counter(), 0, 0
    stu_counts, route_empty, route_set, vehicle_present, start_date_set = [], 0, 0, 0, 0
    alert_hashes, alert_active_periods, alert_causes = Counter(), [], Counter()
    ie_kinds = Counter()
    alert_texts = []
    for ent in feed.entity:
        if ent.HasField("trip_update"):
            counts["TU"] += 1
            tu = ent.trip_update
            rid = tu.trip.route_id
            if rid: route_set += 1
            else: route_empty += 1
            if tu.trip.HasField("start_date"): start_date_set += 1
            if tu.HasField("vehicle"): vehicle_present += 1
            nstu = len(tu.stop_time_update)
            if len(stu_counts) < 60000: stu_counts.append(nstu)
            has_delay = any((s.HasField("arrival") and s.arrival.HasField("delay")) or
                            (s.HasField("departure") and s.departure.HasField("delay"))
                            for s in tu.stop_time_update)
            if has_delay: tu_delay += 1
            else: tu_no_delay += 1
        elif ent.HasField("vehicle"):
            counts["VP"] += 1
        elif ent.HasField("alert"):
            counts["Alert"] += 1
            a = ent.alert
            hdr = ""
            for t in a.header_text.translation:
                hdr = t.text; break
            alert_hashes[hashlib.sha256(hdr.encode()).hexdigest()[:16]] += 1
            alert_causes[a.cause] += 1
            if len(a.active_period) == 1 and a.active_period[0].HasField("end"):
                ap = a.active_period[0]
                dur_h = (ap.end - (ap.start or ts or 0)) / 3600.0
                alert_active_periods.append(dur_h)
            for ie in a.informed_entity:
                if ie.HasField("trip"): ie_kinds["trip"] += 1
                if ie.HasField("route_id") and ie.route_id: ie_kinds["route"] += 1
                if ie.HasField("stop_id") and ie.stop_id: ie_kinds["stop"] += 1
                if ie.HasField("agency_id") and ie.agency_id: ie_kinds["agency"] += 1
            if len(alert_texts) < 12 and hdr:
                alert_texts.append((ent.id, hdr[:160]))
    print(f"Entities: {sum(counts.values()):,} | TU {counts['TU']:,} | VP {counts['VP']:,} | Alert {counts['Alert']:,}")
    print(f"TU mit Delay-Wert: {tu_delay:,} ({100*tu_delay/max(1,counts['TU']):.1f} %)")
    print(f"TU route_id: gesetzt {route_set:,} / leer {route_empty:,} | vehicle-Feld: {vehicle_present} | start_date gesetzt: {start_date_set:,}")
    if stu_counts:
        print(f"STU je TU: median {statistics.median(stu_counts):.0f}, mean {statistics.mean(stu_counts):.1f}, p10 {sorted(stu_counts)[len(stu_counts)//10]}, p90 {sorted(stu_counts)[9*len(stu_counts)//10]}")
    if counts["Alert"]:
        n = counts["Alert"]
        distinct = len(alert_hashes)
        top3 = alert_hashes.most_common(3)
        dur_gt30d = sum(1 for d in alert_active_periods if d > 24*30)
        dur_gt7d = sum(1 for d in alert_active_periods if d > 24*7)
        print(f"Alerts: {n:,} | distinct header_text: {distinct:,} ({100*distinct/n:.1f} %) | Top-Header-Anteil: {top3[0][1]:,}x ({100*top3[0][1]/n:.1f} %)")
        print(f"Alerts mit active_period.end: {len(alert_active_periods):,} | davon >7d: {dur_gt7d:,} | >30d: {dur_gt30d:,}")
        print(f"informed_entity-Kinds (Mehrfachnennung moeglich): {dict(ie_kinds)}")
        print("Top-Causes:", dict(alert_causes.most_common(5)))
        print("Stichprobe Header-Texte:")
        for eid, h in alert_texts:
            print(f"   [{eid[:40]}] {h}")

    # J1: ETag/304-Test — sofortiges Replay und nach 5s
    if etag:
        for delay, label in [(0, "sofort"), (5, "nach 5s")]:
            if delay: __import__('time').sleep(delay)
            r2 = fetch(url, {"If-None-Match": etag})
            print(f"J1 If-None-Match ({label}): HTTP {r2.status_code} | body {len(r2.content):,} B | ETag neu: {r2.headers.get('ETag','-')}")
