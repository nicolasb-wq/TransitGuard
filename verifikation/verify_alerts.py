#!/usr/bin/env python3
"""J2-Teilanalyse: Alert-Dedup ueber header+description, Volltext-Stichprobe."""
import datetime as dt, hashlib
from collections import Counter
import requests
from google.transit import gtfs_realtime_pb2

r = requests.get("https://realtime.gtfs.de/realtime-free.pb", timeout=120,
                 headers={"User-Agent": "TransitGuard-Verification/1.0"})
print(f"gtfs.de HTTP {r.status_code} | {len(r.content):,} B | {dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')}")
feed = gtfs_realtime_pb2.FeedMessage(); feed.ParseFromString(r.content)

alerts = []
for ent in feed.entity:
    if ent.HasField("alert"):
        a = ent.alert
        hdr = next((t.text for t in a.header_text.translation), "").strip()
        dsc = next((t.text for t in a.description_text.translation), "").strip()
        alerts.append((hdr, dsc, a))
n = len(alerts)
both_empty = sum(1 for h, d, _ in alerts if not h and not d)
key = lambda h, d: hashlib.sha256((h + "|" + d[:150]).encode()).hexdigest()[:16]
distinct = Counter(key(h, d) for h, d, _ in alerts)
print(f"Alerts: {n:,} | Dedup-Schluessel (header+desc[:150]) distinct: {len(distinct):,} ({100*len(distinct)/n:.2f} %)")
print(f"Header UND Description leer: {both_empty:,} ({100*both_empty/n:.1f} %)")
print("Top-10 Dedup-Gruppen:")
grp = {}
for h, d, _ in alerts: grp.setdefault(key(h, d), (h, d))[0:0]
tops = distinct.most_common(10)
by_key = {}
for h, d, _ in alerts: by_key.setdefault(key(h, d), (h, d))
for k, c in tops:
    h, d = by_key[k]
    print(f"   {c:6,}x | {h[:70] or '(leer)'} | desc: {d[:80] or '(leer)'}")
print("\nVolltext-Stichprobe (12 zufaellig verteilte Nicht-Standard-Alerts):")
import random; random.seed(42)
nonstd = [(h, d, a) for h, d, a in alerts if h and h not in ("Komfort Check-in verfügbar - wenn möglichst möglich bitte einchecken",)]
sample = random.sample([x for x in alerts if x[0]], min(12, len([x for x in alerts if x[0]])))
for h, d, a in sample:
    ents = sum(1 for _ in a.informed_entity)
    print(f"--- cause={a.cause} effect={a.effect} severity={a.severity_level if a.HasField('severity_level') else '-'} informed_entities={ents}")
    print(f"    H: {h[:110]}")
    if d: print(f"    D: {d[:160]}")
