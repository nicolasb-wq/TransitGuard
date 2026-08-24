using System.Globalization;
using System.Text.Json;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Geo;
using TransitGuard.Core.Normalize;
using TransitGuard.Ingest.GtfsRt;
using TransitGuard.Ingest.Jobs;
using TransitGuard.Ingest.Static;
using TransitGuard.Verifikation.IngestProbe;

// ---------------------------------------------------------------------------
// IngestProbe — misst den ECHTEN Ingest gegen den ECHTEN Feed.
// Bewusst gegen den Produktionscode (FeedFetcher/FeedReader/CityExtractor/
// PollRealtimeJob), nicht gegen eine Nachbildung: eine Nachbildung würde
// Fehler des Produktionscodes gerade nicht finden.
// ---------------------------------------------------------------------------

const string RtUrl = "https://realtime.gtfs.de/realtime-free.pb";
const string StaticNvUrl = "https://download.gtfs.de/germany/nv_free/latest.zip";
const string StaticRvUrl = "https://download.gtfs.de/germany/rv_free/latest.zip";
const string StaticFvUrl = "https://download.gtfs.de/germany/fv_free/latest.zip";

// Hamburg + Umland, identisch zu CityRegistry.All["hamburg"].BoundingBox (ApiServices.cs)
var HamburgBox = new GeoBoundingBox(53.30, 53.85, 9.55, 10.50);
var HamburgKern = new GeoBoundingBox(53.40, 53.75, 9.73, 10.32);   // wie cities-Seed / j3j6.py

var cacheDir = Environment.GetEnvironmentVariable("TG_PROBE_CACHE")
               ?? Path.Combine(Path.GetTempPath(), "tg-ingest-probe");
Directory.CreateDirectory(cacheDir);

var befehl = args.Length > 0 ? args[0] : "hilfe";
var ergebnis = new Dictionary<string, object?>();
var jsonZiel = Environment.GetEnvironmentVariable("TG_PROBE_JSON");

int rc;
try
{
    rc = befehl switch
    {
        "feed" => await Feed(),
        "etag" => await Etag(),
        "static" => Statik(),
        "cycle" => await Zyklen(1),
        "cycles" => await Zyklen(args.Length > 1 ? int.Parse(args[1]) : 10),
        "j3j6" => await J3J6(),
        "staticjob" => await StaticJob(false),
        "staticfile" => await StaticJob(true),
        "alerts" => await Alerts(),
        "errors" => await Fehlerpfade(),
        _ => Hilfe(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ABBRUCH: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    ergebnis["abbruch"] = ex.GetType().Name + ": " + ex.Message;
    rc = 2;
}

if (jsonZiel is not null)
{
    ergebnis["befehl"] = befehl;
    ergebnis["exit"] = rc;
    ergebnis["gemessen_utc"] = DateTimeOffset.UtcNow.ToString("O");
    File.WriteAllText(jsonZiel, JsonSerializer.Serialize(ergebnis, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"\nJSON: {jsonZiel}");
}
return rc;

int Hilfe()
{
    Console.WriteLine("""
        IngestProbe <befehl>
          feed        Ein Abruf des echten RT-Feeds: Kopfzeilen, Entities, Speicher, CPU.
          etag        J1: If-None-Match funktional + Regenerationstakt des Feeds.
          static      Static-GTFS laden (nv+rv+fv), Hamburg extrahieren, Whitelist cachen.
          j3j6        J3 (Hamburg-Anteil) + J6 (Match-Rate Static<->RT) gegen Live-Feed.
          cycle       Ein vollstaendiger Poll-Zyklus ueber PollRealtimeJob.
          cycles N    N aufeinanderfolgende Zyklen im 60-s-Takt.
          errors      Fehlerpfade: unerreichbar, abgeschnitten, kaputtes Protobuf, unveraendert.
        Umgebung: TG_PROBE_CACHE (Cache-Verzeichnis), TG_PROBE_JSON (JSON-Ausgabe).
        """);
    return 0;
}

static HttpClient NeuerClient() => new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
{
    Timeout = TimeSpan.FromMinutes(5),
    DefaultRequestHeaders = { { "User-Agent", "TransitGuard-IngestProbe/1.0 (+kontakt via repo)" } }
};

// =========================================================================== feed
async Task<int> Feed()
{
    Console.WriteLine("=== A: Ein Abruf des echten RT-Feeds ===\n");
    using var http = NeuerClient();

    // Kopfzeilen zuerst (getrennter Request, damit die Zahlen zum Body-Abruf danach passen)
    using (var kopf = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, RtUrl)))
    {
        Console.WriteLine($"HEAD {RtUrl}");
        Console.WriteLine($"  Status          {(int)kopf.StatusCode}");
        Console.WriteLine($"  Content-Length  {kopf.Content.Headers.ContentLength:N0} Bytes");
        Console.WriteLine($"  ETag            {kopf.Headers.ETag?.Tag ?? "(fehlt)"}");
        Console.WriteLine($"  Last-Modified   {kopf.Content.Headers.LastModified?.ToString("O") ?? "(fehlt)"}");
        Console.WriteLine($"  Cache-Control   {(kopf.Headers.CacheControl?.ToString() is { Length: > 0 } cc ? cc : "(nicht gesetzt)")}");
        ergebnis["content_length"] = kopf.Content.Headers.ContentLength;
        ergebnis["etag_vorhanden"] = kopf.Headers.ETag is not null;
        ergebnis["cache_control_gesetzt"] = kopf.Headers.CacheControl is not null;
    }

    var fetcher = new FeedFetcher(http);
    FeedFetchResult? abruf = null;
    var pAbruf = Mess.Messen("Abruf (Download)", () => { abruf = fetcher.FetchAsync(RtUrl, null).GetAwaiter().GetResult(); });
    Console.WriteLine($"\nGET  Status {abruf!.StatusCode}, {abruf.Body!.Length:N0} Bytes, {abruf.ElapsedMs} ms bis Header");
    Mess.Zeile(pAbruf);
    ergebnis["bytes"] = abruf.Body.Length;

    // Parse + Zaehlung ueber den Produktions-Reader
    long entities = 0, tu = 0, al = 0, vp = 0;
    long tuMitDelay = 0, tuRouteIdGesetzt = 0, tuVehicleFeld = 0, tuVehicleFeldLeer = 0, tuOhneTripId = 0;
    long stuGesamt = 0;
    var stuJeTu = new List<int>(60000);
    var delays = new List<int>(60000);
    DateTimeOffset feedZeit = default;
    var alertKeys = new HashSet<string>(StringComparer.Ordinal);
    long alertsGesamt = 0;

    var pParse = Mess.Messen("Parse + Zaehlung (alle Entities)", () =>
    {
        var r = FeedReader.ForEachEntity(abruf.Body!,
            raw =>
            {
                if (string.IsNullOrEmpty(raw.TripId)) tuOhneTripId++;
                if (!string.IsNullOrWhiteSpace(raw.RouteIdFromFeed)) tuRouteIdGesetzt++;
                if (raw.VehicleFieldPresent) { tuVehicleFeld++; if (string.IsNullOrEmpty(raw.VehicleId)) tuVehicleFeldLeer++; }
                stuGesamt += raw.Stops.Count;
                stuJeTu.Add(raw.Stops.Count);
                int? d = null;
                foreach (var s in raw.Stops) { var x = s.DepartureDelay ?? s.ArrivalDelay; if (x is { } v) d = v; }
                if (d is { } dd) { tuMitDelay++; delays.Add(dd); }
            },
            raw =>
            {
                alertsGesamt++;
                alertKeys.Add(AlertNormalizer.ComputeDedupKey(raw.Header, raw.Description));
            },
            t => feedZeit = t);
        entities = r.Entities; tu = r.TripUpdates; al = r.Alerts; vp = r.VehiclePositions;
    });

    var alter = (int)(DateTimeOffset.UtcNow - feedZeit).TotalSeconds;
    Console.WriteLine($"\nFeedHeader.timestamp  {feedZeit:O}  ⇒ Feed-Alter bei Abruf {alter} s");
    Console.WriteLine($"GTFS-RT-Version       {FeedReader.ReadMeta(abruf.Body!).Version}");
    Console.WriteLine($"\nEntities gesamt       {entities:N0}");
    Console.WriteLine($"  TripUpdates         {tu:N0}");
    Console.WriteLine($"  Alerts              {al:N0}");
    Console.WriteLine($"  VehiclePositions    {vp:N0}");
    Console.WriteLine($"\nTripUpdates im Detail");
    Console.WriteLine($"  mit Verspaetungswert     {tuMitDelay:N0}  ({Proz(tuMitDelay, tu)})");
    Console.WriteLine($"  route_id im Feed gesetzt {tuRouteIdGesetzt:N0}  ({Proz(tuRouteIdGesetzt, tu)})");
    Console.WriteLine($"  trip_id leer             {tuOhneTripId:N0}");
    Console.WriteLine($"  vehicle-Feld vorhanden   {tuVehicleFeld:N0}  ({Proz(tuVehicleFeld, tu)})");
    Console.WriteLine($"    davon leere id         {tuVehicleFeldLeer:N0}");
    Console.WriteLine($"  StopTimeUpdates gesamt   {stuGesamt:N0}   Median je TU {Median(stuJeTu)}");
    if (delays.Count > 0)
    {
        delays.Sort();
        Console.WriteLine($"  Delay Median {delays[delays.Count / 2]} s, p05 {delays[(int)(delays.Count * 0.05)]} s, " +
                          $"p95 {delays[(int)(delays.Count * 0.95)]} s, min {delays[0]} s, max {delays[^1]} s");
        Console.WriteLine($"  Delay ausserhalb Clamp [-120,+7200]: {delays.Count(d => d < -120 || d > 7200):N0}");
        ergebnis["delay_median_s"] = delays[delays.Count / 2];
        ergebnis["delay_min_s"] = delays[0];
        ergebnis["delay_max_s"] = delays[^1];
    }
    Console.WriteLine($"\nAlerts: {alertsGesamt:N0} gesamt, {alertKeys.Count:N0} verschiedene Dedup-Keys " +
                      $"⇒ {Proz(alertsGesamt - alertKeys.Count, alertsGesamt)} Duplikate");

    Console.WriteLine("\nSpeicher/CPU je Phase:");
    Mess.Zeile(pAbruf);
    Mess.Zeile(pParse);
    Console.WriteLine($"\n  Prozess-Peak-RSS gesamt: {Mess.Mb(Mess.PeakRssKb())}   (RSS jetzt {Mess.Mb(Mess.RssKb())})");

    ergebnis["entities"] = entities; ergebnis["trip_updates"] = tu; ergebnis["alerts"] = al;
    ergebnis["vehicle_positions"] = vp; ergebnis["tu_mit_delay"] = tuMitDelay;
    ergebnis["tu_route_id_gesetzt"] = tuRouteIdGesetzt; ergebnis["tu_vehicle_feld"] = tuVehicleFeld;
    ergebnis["feed_alter_s"] = alter;
    ergebnis["alerts_dedup_keys"] = alertKeys.Count;
    ergebnis["parse_ms"] = pParse.WallMs;
    ergebnis["parse_peak_rss_kb"] = pParse.PeakRssKb;
    ergebnis["parse_alloc_bytes"] = pParse.AllocBytes;
    return 0;
}

static string Proz(long teil, long ganz) => ganz == 0 ? "n/a" : (100.0 * teil / ganz).ToString("F1", CultureInfo.InvariantCulture) + " %";
static int Median(List<int> xs) { if (xs.Count == 0) return 0; xs.Sort(); return xs[xs.Count / 2]; }

// =========================================================================== etag (J1)
async Task<int> Etag()
{
    Console.WriteLine("=== J1: If-None-Match funktional + Regenerationstakt ===\n");
    using var http = NeuerClient();
    var fetcher = new FeedFetcher(http);

    var erst = await fetcher.FetchAsync(RtUrl, null);
    Console.WriteLine($"1. Abruf ohne ETag : Status {erst.StatusCode}, {erst.Body?.Length ?? 0:N0} Bytes, ETag {erst.ETag ?? "(fehlt)"}");
    if (erst.ETag is null) { Console.WriteLine("FEHLER: kein ETag ⇒ If-None-Match nicht pruefbar."); return 2; }

    // Sofort danach: der Feed kann sich in Millisekunden nicht regeneriert haben.
    var sofort = await fetcher.FetchAsync(RtUrl, erst.ETag);
    Console.WriteLine($"2. Abruf mit ETag  : Status {sofort.StatusCode}, NotModified={sofort.NotModified}, " +
                      $"Body {(sofort.Body?.Length ?? 0):N0} Bytes");
    ergebnis["304_unterstuetzt"] = sofort.NotModified;

    // Gegenprobe: ein absichtlich falscher ETag MUSS 200 + Body liefern.
    // Ohne diese Probe koennte "304" auch von einem Server kommen, der immer 304 sagt.
    var falsch = await fetcher.FetchAsync(RtUrl, "\"garantiert-kein-gueltiger-etag\"");
    Console.WriteLine($"3. Abruf mit FALSCHEM ETag (Gegenprobe): Status {falsch.StatusCode}, " +
                      $"NotModified={falsch.NotModified}, Body {(falsch.Body?.Length ?? 0):N0} Bytes");
    ergebnis["gegenprobe_falscher_etag_liefert_200"] = falsch is { StatusCode: 200, NotModified: false, Body: not null };

    if (!sofort.NotModified)
        Console.WriteLine("   ⇒ Server ignoriert If-None-Match ODER Feed wurde in der Zwischenzeit regeneriert.");
    if (falsch.NotModified)
        Console.WriteLine("   ⇒ WARNUNG: Server antwortet auch auf falschen ETag mit 304 — der 304 oben ist wertlos.");

    // Regenerationstakt: wie lange bleibt ein ETag gueltig?
    Console.WriteLine("\nRegenerationstakt (bis zu 90 s, Abfrage alle 2 s):");
    var t0 = DateTimeOffset.UtcNow;
    var aktuell = erst.ETag;
    int treffer304 = 0, wechsel = 0;
    var wechselAbstaende = new List<double>();
    var letzterWechsel = t0;
    while ((DateTimeOffset.UtcNow - t0).TotalSeconds < 90)
    {
        await Task.Delay(2000);
        var r = await fetcher.FetchAsync(RtUrl, aktuell);
        if (r.NotModified) { treffer304++; continue; }
        if (r.StatusCode != 200) { Console.WriteLine($"   {(DateTimeOffset.UtcNow - t0).TotalSeconds,5:F0} s: Status {r.StatusCode}"); continue; }
        wechsel++;
        var jetzt = DateTimeOffset.UtcNow;
        wechselAbstaende.Add((jetzt - letzterWechsel).TotalSeconds);
        Console.WriteLine($"   {(jetzt - t0).TotalSeconds,5:F0} s: neuer Inhalt ({r.Body!.Length:N0} B), " +
                          $"Abstand zum vorigen Wechsel {(jetzt - letzterWechsel).TotalSeconds:F1} s");
        letzterWechsel = jetzt; aktuell = r.ETag;
    }
    Console.WriteLine($"\n  304-Antworten: {treffer304}, Inhaltswechsel: {wechsel}");
    if (wechselAbstaende.Count > 1)
        Console.WriteLine($"  Mittlerer Abstand zwischen Wechseln: {wechselAbstaende.Skip(1).Average():F1} s");
    Console.WriteLine($"  ⇒ Bei 60-s-Poll ist ein 304 {(wechselAbstaende.Count > 1 && wechselAbstaende.Skip(1).Average() < 60 ? "praktisch ausgeschlossen" : "moeglich")}.");
    ergebnis["treffer_304"] = treffer304;
    ergebnis["inhaltswechsel"] = wechsel;
    ergebnis["wechsel_abstand_s"] = wechselAbstaende.Count > 1 ? wechselAbstaende.Skip(1).Average() : (double?)null;

    return sofort.NotModified && falsch is { StatusCode: 200, NotModified: false } ? 0 : 1;
}

// =========================================================================== static
int Statik()
{
    Console.WriteLine("=== Static-GTFS → Hamburg-Extrakt (C.3.1-Whitelist) ===\n");
    using var http = NeuerClient();

    var quellen = new[] { ("nv", StaticNvUrl), ("rv", StaticRvUrl), ("fv", StaticFvUrl) };
    var whitelistGesamt = new Dictionary<string, TripLookup>(StringComparer.Ordinal);
    var stopsGesamt = 0; var tripsGesamt = 0;
    var proQuelle = new Dictionary<string, object?>();

    foreach (var (name, url) in quellen)
    {
        var datei = Path.Combine(cacheDir, $"static_{name}.zip");
        if (!File.Exists(datei))
        {
            Console.WriteLine($"[{name}] Download {url} …");
            var pDl = Mess.Messen($"[{name}] Download → Datei", () =>
            {
                using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                using var q = resp.Content.ReadAsStream();
                using var z = File.Create(datei + ".teil");
                q.CopyTo(z, 1 << 20);
            });
            File.Move(datei + ".teil", datei, overwrite: true);
            Mess.Zeile(pDl);
        }
        var groesse = new FileInfo(datei).Length;
        Console.WriteLine($"[{name}] ZIP {Mess.MbB(groesse)} ({groesse:N0} B) — {datei}");

        CityExtractor.CityExtract? ex = null;
        var pEx = Mess.Messen($"[{name}] CityExtractor (Hamburg-bbox)", () =>
        {
            using var fs = File.OpenRead(datei);
            using var arc = new GtfsStaticArchive(fs);
            ex = CityExtractor.Extract(arc, HamburgBox);
        });
        Mess.Zeile(pEx);
        Console.WriteLine($"[{name}] Stops in bbox {ex!.StopsInBox:N0}, Trips in bbox {ex.TripsInBox:N0}, " +
                          $"Whitelist {ex.Whitelist.Count:N0}, Fahrplaene {ex.Schedules.Count:N0}");
        // Fuer J6/J3 zusaetzlich die VOLLSTAENDIGEN Mengen (nicht nur bbox) ablegen.
        var pAll = Mess.Messen($"[{name}] alle trips/stops fuer J3/J6", () =>
        {
            using var fs2 = File.OpenRead(datei);
            using var arc2 = new GtfsStaticArchive(fs2);
            using var wT = new StreamWriter(Path.Combine(cacheDir, $"trips_{name}.tsv"));
            foreach (var t in arc2.Trips()) wT.Write($"{t.TripId}\t{t.RouteId}\n");
            using var wS = new StreamWriter(Path.Combine(cacheDir, $"stops_{name}.tsv"));
            foreach (var st in arc2.Stops()) wS.Write($"{st.StopId}\t{st.Lat.ToString(CultureInfo.InvariantCulture)}\t{st.Lon.ToString(CultureInfo.InvariantCulture)}\n");
        });
        Mess.Zeile(pAll);

        foreach (var kv in ex.Whitelist) whitelistGesamt[kv.Key] = kv.Value;
        stopsGesamt += ex.StopsInBox; tripsGesamt += ex.TripsInBox;
        proQuelle[name] = new Dictionary<string, object?>
        {
            ["zip_bytes"] = groesse, ["stops_in_box"] = ex.StopsInBox, ["trips_in_box"] = ex.TripsInBox,
            ["whitelist"] = ex.Whitelist.Count, ["extract_peak_rss_kb"] = pEx.PeakRssKb, ["extract_ms"] = pEx.WallMs,
        };
    }

    Console.WriteLine($"\nWhitelist gesamt (nv+rv+fv): {whitelistGesamt.Count:N0} Fahrten");
    var ziel = Path.Combine(cacheDir, "whitelist_hamburg.json");
    File.WriteAllText(ziel, JsonSerializer.Serialize(
        whitelistGesamt.ToDictionary(k => k.Key, v => new[] { v.Value.RouteId, v.Value.DirectionId?.ToString(), v.Value.LastStopId, v.Value.EndTimeSeconds?.ToString() })));
    Console.WriteLine($"Whitelist zwischengespeichert: {ziel} ({Mess.MbB(new FileInfo(ziel).Length)})");
    Console.WriteLine($"\nProzess-Peak-RSS gesamt: {Mess.Mb(Mess.PeakRssKb())}");

    ergebnis["whitelist_gesamt"] = whitelistGesamt.Count;
    ergebnis["stops_in_box"] = stopsGesamt;
    ergebnis["pro_quelle"] = proQuelle;
    ergebnis["peak_rss_kb"] = Mess.PeakRssKb();
    return whitelistGesamt.Count > 0 ? 0 : 2;
}

// =========================================================================== J3 + J6
async Task<int> J3J6()
{
    Console.WriteLine("=== J3 (Hamburg-Anteil) + J6 (Match-Rate Static↔RT) ===\n");
    var tripDateien = new[] { "nv", "rv", "fv" }.Select(n => Path.Combine(cacheDir, $"trips_{n}.tsv")).ToArray();
    if (tripDateien.Any(f => !File.Exists(f)))
    {
        Console.Error.WriteLine($"Static-Cache fehlt in {cacheDir} — erst `IngestProbe static` laufen lassen.");
        return 3;
    }

    var trip2feed = new Dictionary<string, string>(2_000_000, StringComparer.Ordinal);
    var stopPos = new Dictionary<string, (double La, double Lo)>(800_000, StringComparer.Ordinal);
    var pLaden = Mess.Messen("Static-Mengen laden (trips + stops)", () =>
    {
        foreach (var n in new[] { "nv", "rv", "fv" })
        {
            foreach (var z in File.ReadLines(Path.Combine(cacheDir, $"trips_{n}.tsv")))
            { var i = z.IndexOf('\t'); if (i > 0) trip2feed[z[..i]] = n; }
            var sd = Path.Combine(cacheDir, $"stops_{n}.tsv");
            if (!File.Exists(sd)) continue;
            foreach (var z in File.ReadLines(sd))
            {
                var p = z.Split('\t');
                if (p.Length == 3 && double.TryParse(p[1], CultureInfo.InvariantCulture, out var la)
                                  && double.TryParse(p[2], CultureInfo.InvariantCulture, out var lo))
                    stopPos[p[0]] = (la, lo);
            }
        }
    });
    Mess.Zeile(pLaden);
    Console.WriteLine($"  Static-Fahrten gesamt {trip2feed.Count:N0}, Haltestellen mit Koordinate {stopPos.Count:N0}\n");

    using var http = NeuerClient();
    var abruf = await new FeedFetcher(http).FetchAsync(RtUrl, null);
    if (abruf.StatusCode != 200 || abruf.Body is null) { Console.Error.WriteLine($"Feed-Abruf {abruf.StatusCode}"); return 3; }
    Console.WriteLine($"RT-Feed {abruf.Body.Length:N0} B @ {DateTimeOffset.UtcNow:O}\n");

    long tu = 0, aufloesbar = 0, mitDelay = 0, kern = 0, umgeb = 0, kernMitDelay = 0;
    long stopOk = 0, stopMiss = 0;
    var nachFeed = new Dictionary<string, long>(); var kernNachFeed = new Dictionary<string, long>();
    var kernTrips = new HashSet<string>(StringComparer.Ordinal);
    var pRt = Mess.Messen("RT parsen + zuordnen", () =>
        FeedReader.ForEachEntity(abruf.Body!,
            raw =>
            {
                if (raw.TripId.Length == 0) return;
                tu++;
                var imStatic = trip2feed.TryGetValue(raw.TripId, out var feedName);
                if (imStatic) { aufloesbar++; nachFeed[feedName!] = nachFeed.GetValueOrDefault(feedName!) + 1; }
                bool hatDelay = raw.Stops.Any(s => (s.DepartureDelay ?? s.ArrivalDelay) is not null);
                if (hatDelay) mitDelay++;
                bool imKern = false, imUmgeb = false;
                foreach (var s in raw.Stops)
                {
                    if (s.StopId is null) continue;
                    if (!stopPos.TryGetValue(s.StopId, out var pos)) { stopMiss++; continue; }
                    stopOk++;
                    if (HamburgKern.Contains(pos.La, pos.Lo)) imKern = true;
                    if (HamburgBox.Contains(pos.La, pos.Lo)) imUmgeb = true;
                }
                if (imKern) { kern++; kernTrips.Add(raw.TripId); if (hatDelay) kernMitDelay++;
                              if (imStatic) kernNachFeed[feedName!] = kernNachFeed.GetValueOrDefault(feedName!) + 1; }
                if (imUmgeb) umgeb++;
            },
            _ => { }, _ => { }));
    Mess.Zeile(pRt);

    Console.WriteLine($"\n--- J6: MATCH-RATE Static↔RT ---");
    Console.WriteLine($"  TripUpdates                {tu:N0}");
    Console.WriteLine($"  im Static aufloesbar       {aufloesbar:N0}  ({Proz(aufloesbar, tu)})");
    Console.WriteLine($"  je Static-Feed             {string.Join(", ", nachFeed.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value:N0}"))}");
    Console.WriteLine($"  Stop-Lookup ok / miss      {stopOk:N0} / {stopMiss:N0}");
    Console.WriteLine($"  TUs mit Verspaetungswert   {mitDelay:N0}  ({Proz(mitDelay, tu)})");

    Console.WriteLine($"\n--- J3: HAMBURG-ANTEIL (geografisch ueber STU-Stop-IDs) ---");
    Console.WriteLine($"  TUs mit ≥1 Stop im KERN    {kern:N0}  ({Proz(kern, tu)})");
    Console.WriteLine($"  TUs mit ≥1 Stop im UMLAND  {umgeb:N0}  ({Proz(umgeb, tu)})");
    Console.WriteLine($"  distinct Kern-Trips        {kernTrips.Count:N0}, davon mit Delay {kernMitDelay:N0} ({Proz(kernMitDelay, kern)})");
    Console.WriteLine($"  Kern-Trips je Static-Feed  {string.Join(", ", kernNachFeed.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value:N0}"))}");
    var nurNv = kernNachFeed.GetValueOrDefault("nv");
    Console.WriteLine($"\n  ⇒ Wuerde StaticSync NUR nv_free laden (aktueller Stand), fehlten " +
                      $"{kern - nurNv:N0} von {kern:N0} Kern-Fahrten ({Proz(kern - nurNv, kern)}).");

    ergebnis["j6_tu"] = tu; ergebnis["j6_aufloesbar"] = aufloesbar;
    ergebnis["j6_match_rate"] = tu > 0 ? (double)aufloesbar / tu : 0;
    ergebnis["j6_stop_lookup_miss"] = stopMiss;
    ergebnis["j3_kern"] = kern; ergebnis["j3_umland"] = umgeb;
    ergebnis["j3_kern_mit_delay"] = kernMitDelay;
    ergebnis["j3_kern_je_feed"] = kernNachFeed;
    ergebnis["j3_nur_nv_fehlend"] = kern - nurNv;
    return 0;
}

// =========================================================================== cycles
Dictionary<string, TripLookup> WhitelistLaden()
{
    var datei = Path.Combine(cacheDir, "whitelist_hamburg.json");
    if (!File.Exists(datei)) throw new FileNotFoundException($"Whitelist-Cache fehlt: {datei} — erst `IngestProbe static` laufen lassen.");
    var roh = JsonSerializer.Deserialize<Dictionary<string, string?[]>>(File.ReadAllText(datei))!;
    var wl = new Dictionary<string, TripLookup>(roh.Count, StringComparer.Ordinal);
    foreach (var kv in roh)
        wl[kv.Key] = new TripLookup(kv.Value[0] ?? "",
            int.TryParse(kv.Value[1], out var d) ? d : null, kv.Value[2],
            int.TryParse(kv.Value[3], out var e) ? e : null);
    return wl;
}

async Task<int> Zyklen(int anzahl)
{
    Console.WriteLine($"=== Poll-Zyklus ueber PollRealtimeJob — {anzahl} Durchlauf/Durchlaeufe im 60-s-Takt ===\n");
    var wl = WhitelistLaden();
    Console.WriteLine($"Whitelist geladen: {wl.Count:N0} Fahrten (C.3.1 Ebene 1, in-memory)\n");

    using var http = NeuerClient();
    var whitelist = new MessWhitelist(); whitelist.Replace("hamburg", wl);
    var alertSpeicher = new MessAlertSpeicher();
    var tripState = new MessTripStateStore();
    var metriken = new MessMetrikSenke();
    var job = new PollRealtimeJob(
        new FeedFetcher(http), tripState, metriken, new FeedHealthTracker(),
        new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()),
        whitelist, SystemClock.Instance, new FeedEtagStore(), alertSpeicher);

    // --- route_id-Hot-Path separat vermessen (Aufgabe A.3) --------------------
    if (anzahl == 1)
    {
        var vorlauf = await new FeedFetcher(http).FetchAsync(RtUrl, null);
        if (vorlauf is { StatusCode: 200, Body: not null })
        {
            var ids = new List<string>(60000);
            FeedReader.ForEachEntity(vorlauf.Body, r => { if (r.TripId.Length > 0) ids.Add(r.TripId); }, _ => { }, _ => { });
            long treffer = 0, fehl = 0, ohneRoute = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var id in ids)
                if (wl.TryGetValue(id, out var lk)) { treffer++; if (string.IsNullOrEmpty(lk.RouteId)) ohneRoute++; }
                else fehl++;
            sw.Stop();
            var nsJeLookup = sw.Elapsed.TotalNanoseconds / Math.Max(1, ids.Count);
            Console.WriteLine("--- A.3: route_id-Aufloesung (C.3.1 In-Memory-Dictionary) ---");
            Console.WriteLine($"  Lookups          {ids.Count:N0}");
            Console.WriteLine($"  Treffer          {treffer:N0}  ({Proz(treffer, ids.Count)})  ⇒ Stadt-Fahrten");
            Console.WriteLine($"  Fehlschlaege     {fehl:N0}  ({Proz(fehl, ids.Count)})  ⇒ andere Verbuende, erwartbar");
            Console.WriteLine($"  Treffer OHNE route_id (Datenfehler, T-NORM-2): {ohneRoute:N0}");
            Console.WriteLine($"  Gesamtdauer      {sw.Elapsed.TotalMilliseconds:F2} ms   ⇒ {nsJeLookup:F0} ns je Lookup");
            Console.WriteLine($"  Dictionary-Groesse im Speicher (grob): {Mess.MbB(GC.GetTotalMemory(false))} Heap gesamt\n");
            ergebnis["route_lookups"] = ids.Count; ergebnis["route_treffer"] = treffer;
            ergebnis["route_fehl"] = fehl; ergebnis["route_ohne_routeid"] = ohneRoute;
            ergebnis["route_ns_je_lookup"] = nsJeLookup;
            ergebnis["route_gesamt_ms"] = sw.Elapsed.TotalMilliseconds;
        }
    }

    // --- Zyklen -------------------------------------------------------------
    var laeufe = new List<Dictionary<string, object?>>();
    long rssStart = 0;
    for (int i = 1; i <= anzahl; i++)
    {
        var t0 = DateTimeOffset.UtcNow;
        PollRealtimeJob.PollResult? r = null;
        var p = Mess.Messen($"Zyklus {i}", () => { r = job.RunAsync(RtUrl, "hamburg").GetAwaiter().GetResult(); });
        var m = metriken.Zeilen[^1];
        if (i == 1) rssStart = p.RssKb;
        Console.WriteLine(
            $"Zyklus {i,2}/{anzahl}  {t0:HH:mm:ss}  Status {m.HttpStatus}  {Mess.MbB(m.Bytes),9}  " +
            $"Alter {m.FeedAgeSeconds,3} s  Entities {m.Entities,7:N0}  Stadt-TUs {m.CityTripsMatched,6:N0}  " +
            $"Alerts {alertSpeicher.Count,4:N0}  Deltas {r!.Changed,6:N0}  Match {m.MatchRate,6:P1}  Wand {p.WallMs,6} ms  CPU {p.CpuMs,6:F0} ms  " +
            $"Peak-RSS {Mess.Mb(p.PeakRssKb),9}  RSS {Mess.Mb(p.RssKb),9}  TripState {tripState.Count,6:N0}");
        laeufe.Add(new Dictionary<string, object?>
        {
            ["nr"] = i, ["status"] = m.HttpStatus, ["bytes"] = m.Bytes, ["feed_alter_s"] = m.FeedAgeSeconds,
            ["entities"] = m.Entities, ["stadt_tus"] = m.CityTripsMatched, ["deltas"] = r.Changed,
            ["match_rate"] = m.MatchRate, ["route_misses"] = m.RouteMisses, ["delay_clamped"] = m.DelayClamped,
            ["wand_ms"] = p.WallMs, ["cpu_ms"] = p.CpuMs, ["peak_rss_kb"] = p.PeakRssKb, ["rss_kb"] = p.RssKb,
            ["tripstate"] = tripState.Count, ["gesund"] = r.FeedHealthy,
        });
        if (i < anzahl)
        {
            var rest = TimeSpan.FromSeconds(60) - (DateTimeOffset.UtcNow - t0);
            if (rest > TimeSpan.Zero) await Task.Delay(rest);
            else Console.WriteLine($"   ACHTUNG: Zyklus {i} dauerte laenger als 60 s — der Takt haelt nicht.");
        }
    }

    if (anzahl > 1)
    {
        var rssEnde = laeufe[^1]["rss_kb"] as long? ?? 0;
        var wand = laeufe.Select(l => (long)l["wand_ms"]!).ToList();
        var quoten = laeufe.Select(l => (double)l["match_rate"]!).ToList();
        var stadt = laeufe.Select(l => (long)l["stadt_tus"]!).ToList();
        Console.WriteLine($"\n--- Stabilitaet ueber {anzahl} Zyklen ---");
        Console.WriteLine($"  RSS Zyklus 1 → {anzahl}:   {Mess.Mb(rssStart)} → {Mess.Mb(rssEnde)}   " +
                          $"Zuwachs {Mess.Mb(rssEnde - rssStart)} ({(rssStart > 0 ? (rssEnde - rssStart) * 100.0 / rssStart : 0):F1} %)");
        Console.WriteLine($"  Zyklusdauer min/median/max: {wand.Min()} / {Median(wand.Select(x => (int)x).ToList())} / {wand.Max()} ms");
        Console.WriteLine($"  Match-Rate min/max:        {quoten.Min():P2} / {quoten.Max():P2}");
        Console.WriteLine($"  Stadt-TUs min/max:         {stadt.Min():N0} / {stadt.Max():N0}");
        Console.WriteLine($"  Prozess-Peak-RSS gesamt:   {Mess.Mb(Mess.PeakRssKb())}");
        ergebnis["rss_start_kb"] = rssStart; ergebnis["rss_ende_kb"] = rssEnde;
        ergebnis["rss_zuwachs_kb"] = rssEnde - rssStart;
        ergebnis["wand_min_ms"] = wand.Min(); ergebnis["wand_max_ms"] = wand.Max();
        ergebnis["match_min"] = quoten.Min(); ergebnis["match_max"] = quoten.Max();
        ergebnis["peak_rss_kb"] = Mess.PeakRssKb();
    }
    ergebnis["zyklen"] = laeufe;
    ergebnis["whitelist"] = wl.Count;
    return laeufe.All(l => (int)l["status"]! is 200 or 304) ? 0 : 2;
}

// =========================================================================== errors
async Task<int> Fehlerpfade()
{
    Console.WriteLine("=== A.6: Fehlerpfade — der Poll-Job darf die API nie mitreissen ===\n");
    var wlDatei = Path.Combine(cacheDir, "whitelist_hamburg.json");
    var wl = File.Exists(wlDatei) ? WhitelistLaden() : new Dictionary<string, TripLookup>();
    Console.WriteLine($"Whitelist: {wl.Count:N0} Fahrten {(wl.Count == 0 ? "(Cache fehlt — Fehlerpfade brauchen sie nicht)" : "")}\n");

    // Echten Feed einmal holen, damit „abgeschnitten" auf echten Bytes beruht.
    byte[]? echt = null;
    try
    {
        using var h = NeuerClient();
        var a = await new FeedFetcher(h).FetchAsync(RtUrl, null);
        if (a is { StatusCode: 200, Body: not null }) echt = a.Body;
    }
    catch { /* offline: Fall wird unten uebersprungen */ }
    if (echt is null)
        Console.WriteLine("Hinweis: echter Feed nicht erreichbar — 'abgeschnitten' laeuft gegen synthetische Bytes.\n");
    var basis = echt ?? new byte[] { 0x0a, 0x0c, 0x0a, 0x03, 0x32, 0x2e, 0x30, 0x10, 0x00, 0x18, 0x00 };

    var port = 18099 + Random.Shared.Next(0, 400);
    var lauscher = new System.Net.HttpListener();
    lauscher.Prefixes.Add($"http://127.0.0.1:{port}/");
    lauscher.Start();
    var modus = "ok";
    var bedient = 0;
    _ = Task.Run(async () =>
    {
        while (lauscher.IsListening)
        {
            System.Net.HttpListenerContext ctx;
            try { ctx = await lauscher.GetContextAsync(); } catch { return; }
            bedient++;
            try
            {
                switch (modus)
                {
                    case "ok":
                        ctx.Response.StatusCode = 200; ctx.Response.Headers["ETag"] = "\"probe-1\"";
                        ctx.Response.OutputStream.Write(basis, 0, basis.Length); break;
                    case "abgeschnitten":
                        // Content-Length verspricht die volle Laenge, geliefert wird die Haelfte,
                        // dann wird die Verbindung hart geschlossen.
                        ctx.Response.StatusCode = 200; ctx.Response.ContentLength64 = basis.Length;
                        ctx.Response.OutputStream.Write(basis, 0, basis.Length / 2);
                        ctx.Response.OutputStream.Flush(); ctx.Response.Abort(); continue;
                    case "muell":
                        ctx.Response.StatusCode = 200;
                        var muell = new byte[4096]; Random.Shared.NextBytes(muell);
                        muell[0] = 0xFF; muell[1] = 0xFF;   // kein gueltiger Protobuf-Tag
                        ctx.Response.OutputStream.Write(muell, 0, muell.Length); break;
                    case "500":
                        ctx.Response.StatusCode = 500; break;
                    case "304":
                        ctx.Response.StatusCode = 304; ctx.Response.Headers["ETag"] = "\"probe-1\""; break;
                    case "leer":
                        ctx.Response.StatusCode = 200; ctx.Response.ContentLength64 = 0; break;
                }
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    });

    var http2 = NeuerClient();
    http2.Timeout = TimeSpan.FromSeconds(10);
    var metriken = new MessMetrikSenke();
    var whitelist = new MessWhitelist(); whitelist.Replace("hamburg", wl);
    var alertSpeicher = new MessAlertSpeicher();
    var tracker = new FeedHealthTracker();
    var job = new PollRealtimeJob(new FeedFetcher(http2), new MessTripStateStore(), metriken, tracker,
        new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()), whitelist, SystemClock.Instance, new FeedEtagStore(), alertSpeicher);
    var url = $"http://127.0.0.1:{port}/feed.pb";

    var faelle = new List<Dictionary<string, object?>>();
    async Task<bool> Fall(string name, string m, string erwartung, Func<PollRealtimeJob.PollResult, bool> pruefung, string? eigeneUrl = null)
    {
        modus = m;
        string ausgang; bool ok;
        try
        {
            var r = await job.RunAsync(eigeneUrl ?? url, "hamburg");
            ok = pruefung(r);
            ausgang = $"Ergebnis Success={r.Success} NotModified={r.NotModified} Entities={r.Entities}";
        }
        catch (Exception ex) { ok = false; ausgang = $"AUSNAHME {ex.GetType().Name}: {ex.Message}"; }
        Console.WriteLine($"  {(ok ? "OK  " : "ROT ")} {name,-34} erwartet: {erwartung,-42} {ausgang}");
        faelle.Add(new Dictionary<string, object?> { ["fall"] = name, ["ok"] = ok, ["ausgang"] = ausgang });
        return ok;
    }

    bool alles = true;
    alles &= await Fall("Server nicht erreichbar", "ok", "Success=false, keine Ausnahme",
        r => !r.Success, $"http://127.0.0.1:{port + 977}/tot.pb");
    alles &= await Fall("HTTP 500", "500", "Success=false, keine Ausnahme", r => !r.Success);
    alles &= await Fall("Antwort abgeschnitten", "abgeschnitten", "Success=false, keine Ausnahme", r => !r.Success);
    alles &= await Fall("Body leer (0 Bytes, Status 200)", "leer", "kein Absturz", r => true);
    alles &= await Fall("kaputtes Protobuf", "muell", "kein Absturz (Success egal)", r => true);
    alles &= await Fall("304 Not Modified", "304", "Success=true, NotModified=true", r => r is { Success: true, NotModified: true });
    alles &= await Fall("gueltiger Feed danach", "ok", "Success=true (Erholung nach Fehlern)", r => r.Success);

    Console.WriteLine($"\n  Anfragen am Testserver: {bedient}");
    Console.WriteLine($"  FeedHealthTracker.ConsecutiveFailures nach der Serie: {tracker.ConsecutiveFailures}");
    Console.WriteLine($"  Metrikzeilen geschrieben: {metriken.Zeilen.Count} (jede Runde muss eine Zeile hinterlassen)");
    var mitFehler = metriken.Zeilen.Count(z => z.Error is not null);
    Console.WriteLine($"    davon mit Error-Feld: {mitFehler}");

    lauscher.Stop(); http2.Dispose();
    ergebnis["fehlerpfade"] = faelle;
    ergebnis["metrikzeilen"] = metriken.Zeilen.Count;
    ergebnis["alle_ok"] = alles;
    return alles ? 0 : 2;
}

// =========================================================================== staticjob
/// <summary>
/// Misst den ECHTEN StaticSyncJob (nicht den CityExtractor allein): er liest das ZIP zuerst
/// vollstaendig in einen MemoryStream. Genau das ist der Unterschied, der auf einer kleinen
/// VM ueber Erfolg und OOM entscheidet — deshalb wird er gemessen, nicht geschaetzt.
/// Das ZIP kommt aus dem Cache ueber einen lokalen Server, damit die Messung nicht am
/// Netzdurchsatz haengt.
/// </summary>
async Task<int> StaticJob(bool nurDatei)
{
    var zip = Path.Combine(cacheDir, "static_nv.zip");
    if (!File.Exists(zip)) { Console.Error.WriteLine($"Cache fehlt: {zip} — erst `static` laufen lassen."); return 3; }
    var bytes = new FileInfo(zip).Length;
    Console.WriteLine($"=== StaticSyncJob (Produktionspfad) gegen {Mess.MbB(bytes)} ZIP ===\n");

    var port = 18500 + Random.Shared.Next(0, 300);
    var l = new System.Net.HttpListener();
    l.Prefixes.Add($"http://127.0.0.1:{port}/");
    l.Start();
    _ = Task.Run(async () =>
    {
        while (l.IsListening)
        {
            System.Net.HttpListenerContext c;
            try { c = await l.GetContextAsync(); } catch { return; }
            try
            {
                c.Response.StatusCode = 200; c.Response.ContentType = "application/zip";
                c.Response.ContentLength64 = bytes;
                using var fs = File.OpenRead(zip);
                await fs.CopyToAsync(c.Response.OutputStream);
                c.Response.Close();
            }
            catch { try { c.Response.Abort(); } catch { } }
        }
    });

    // WICHTIG: nur EINE Variante je Prozess. Beide im selben Prozess zu messen ist ungueltig —
    // der Heap der ersten Variante bleibt dem Prozess erhalten und blaeht den RSS-Ausgangswert
    // der zweiten auf. Genau daran ist die erste Fassung dieser Messung gescheitert (24.08.2026).
    if (nurDatei)
    {
        var pd = Mess.Messen("CityExtractor direkt aus DATEI (ohne MemoryStream)", () =>
        {
            using var fs = File.OpenRead(zip);
            using var arc = new GtfsStaticArchive(fs);
            var e = CityExtractor.Extract(arc, HamburgBox);
            Console.WriteLine($"  (Stops {e.StopsInBox:N0}, Trips {e.TripsInBox:N0}, Whitelist {e.Whitelist.Count:N0})");
        });
        Mess.Zeile(pd);
        Console.WriteLine($"  Prozess-Peak-RSS: {Mess.Mb(Mess.PeakRssKb())}");
        l.Stop();
        ergebnis["datei_peak_rss_kb"] = pd.PeakRssKb;
        ergebnis["datei_ms"] = pd.WallMs;
        await Task.CompletedTask;
        return 0;
    }

    var wl = new MessWhitelist();
    var job = new StaticSyncJob(wl, new MessFahrplaene(), new MessHalte(), new InMemoryStaticBuildStore(), new MessAlarme());
    using var http = NeuerClient();
    StaticSyncJob.SyncResult? r = null;
    var p = Mess.Messen("StaticSyncJob.RunAsync (Produktionspfad)", () =>
        r = job.RunAsync(http, $"http://127.0.0.1:{port}/nv.zip", "hamburg", HamburgBox).GetAwaiter().GetResult());
    Mess.Zeile(p);
    Console.WriteLine($"\n  Erfolg {r!.Success}, {r.Bytes:N0} B, Stops {r.Stops:N0}, Trips {r.Trips:N0}, Whitelist {wl.Count:N0}");
    Console.WriteLine($"  Prozess-Peak-RSS: {Mess.Mb(Mess.PeakRssKb())}");
    l.Stop();
    ergebnis["job_peak_rss_kb"] = p.PeakRssKb;
    ergebnis["job_ms"] = p.WallMs;
    ergebnis["whitelist"] = wl.Count;
    await Task.CompletedTask;
    return r.Success ? 0 : 2;
}

// =========================================================================== alerts
/// <summary>
/// Wieviele Alerts gehen Hamburg ueberhaupt etwas an? Entscheidet, was
/// /v1/cities/{slug}/alerts liefern soll — und ob ein Trip-basierter Filter reicht.
/// </summary>
async Task<int> Alerts()
{
    Console.WriteLine("=== B: Alerts — Zuordnung zu Hamburg ===\n");
    var wl = WhitelistLaden();
    var stadtStops = new HashSet<string>(StringComparer.Ordinal);
    foreach (var n in new[] { "nv", "rv", "fv" })
    {
        var d = Path.Combine(cacheDir, $"stops_{n}.tsv");
        if (!File.Exists(d)) continue;
        foreach (var z in File.ReadLines(d))
        {
            var p = z.Split('\t');
            if (p.Length == 3 && double.TryParse(p[1], CultureInfo.InvariantCulture, out var la)
                              && double.TryParse(p[2], CultureInfo.InvariantCulture, out var lo)
                              && HamburgBox.Contains(la, lo)) stadtStops.Add(p[0]);
        }
    }
    Console.WriteLine($"Whitelist {wl.Count:N0} Fahrten, {stadtStops.Count:N0} Halte in der Stadt-Box\n");

    using var http = NeuerClient();
    var abruf = await new FeedFetcher(http).FetchAsync(RtUrl, null);
    if (abruf is not { StatusCode: 200, Body: not null }) { Console.Error.WriteLine("Feed nicht erreichbar"); return 3; }

    var normalisierer = AlertNormalizer.CreateDefault();
    var zaehler = new NormalizerCounters();
    var stadtEchtKeys = new HashSet<string>(StringComparer.Ordinal);
    var echtBeispiele = new List<string>();
    long alle = 0, ohneEntity = 0, mitTrip = 0, mitStop = 0, stadtTrip = 0, stadtStop = 0, stadtBeides = 0;
    var alleKeys = new HashSet<string>(StringComparer.Ordinal);
    var stadtKeys = new HashSet<string>(StringComparer.Ordinal);
    var beispiele = new List<(string H, string D, int Sev, int Trips, int Stops)>();
    FeedReader.ForEachEntity(abruf.Body!, _ => { }, a =>
    {
        alle++;
        var key = AlertNormalizer.ComputeDedupKey(a.Header, a.Description);
        alleKeys.Add(key);
        if (a.InformedTripIds.Count == 0 && a.InformedStopIds.Count == 0) { ohneEntity++; return; }
        if (a.InformedTripIds.Count > 0) mitTrip++;
        if (a.InformedStopIds.Count > 0) mitStop++;
        bool t = a.InformedTripIds.Any(wl.ContainsKey);
        bool s = a.InformedStopIds.Any(stadtStops.Contains);
        if (t) stadtTrip++;
        if (s) stadtStop++;
        if (t || s)
        {
            stadtBeides++;
            if (stadtKeys.Add(key) && beispiele.Count < 8)
                beispiele.Add((a.Header, a.Description, a.Severity, a.InformedTripIds.Count, a.InformedStopIds.Count));
            // Und jetzt dieselbe Meldung durch die Noise-Regeln, die auch die API benutzt.
            var n = normalisierer.Normalize(a.Header, a.Description, a.Url, a.Cause, a.Effect, a.Severity,
                a.InformedTripIds, a.InformedStopIds, zaehler);
            if (!n.IsNoise && stadtEchtKeys.Add(n.DedupKey) && echtBeispiele.Count < 10)
                echtBeispiele.Add($"[sev {n.Severity}] {Kurz(n.HeaderText, 60)} | {Kurz(n.DescriptionText, 80)}");
        }
    }, _ => { });

    Console.WriteLine($"Alerts gesamt                     {alle:N0}   ({alleKeys.Count:N0} verschiedene Dedup-Keys)");
    Console.WriteLine($"  ohne InformedEntity             {ohneEntity:N0}  ({Proz(ohneEntity, alle)})  ⇒ nicht zuordenbar");
    Console.WriteLine($"  mit trip_id                     {mitTrip:N0}  ({Proz(mitTrip, alle)})");
    Console.WriteLine($"  mit stop_id                     {mitStop:N0}  ({Proz(mitStop, alle)})");
    Console.WriteLine($"  Hamburg ueber trip_id           {stadtTrip:N0}  ({Proz(stadtTrip, alle)})");
    Console.WriteLine($"  Hamburg ueber stop_id           {stadtStop:N0}  ({Proz(stadtStop, alle)})");
    Console.WriteLine($"  Hamburg gesamt (trip ODER stop) {stadtBeides:N0}  ⇒ {stadtKeys.Count:N0} verschiedene Meldungen");
    Console.WriteLine($"\n  ⇒ Ein NUR trip-basierter Filter verlöre {stadtStop - stadtTrip:N0} Treffer, wenn stop-basiert mehr findet.");
    Console.WriteLine("\nBeispiele (verschiedene Dedup-Keys):");
    foreach (var b in beispiele)
        Console.WriteLine($"  [sev {b.Sev}] {Kurz(b.H, 70)} | trips {b.Trips}, stops {b.Stops}\n      {Kurz(b.D, 90)}");

    Console.WriteLine($"\n--- Nach den Noise-Regeln (AlertNormalizer.CreateDefault, wie in der API) ---");
    Console.WriteLine($"  Hamburg-Meldungen ohne Rauschen: {stadtEchtKeys.Count:N0} von {stadtKeys.Count:N0}");
    foreach (var b in echtBeispiele) Console.WriteLine("    " + b);
    if (stadtEchtKeys.Count == 0)
        Console.WriteLine("    (keine — /v1/cities/{slug}/alerts bliebe gerade jetzt leer, aber aus dem RICHTIGEN Grund)");
    ergebnis["alerts_hamburg_echt_keys"] = stadtEchtKeys.Count;

    ergebnis["alerts_gesamt"] = alle; ergebnis["alerts_keys"] = alleKeys.Count;
    ergebnis["alerts_ohne_entity"] = ohneEntity;
    ergebnis["alerts_hamburg"] = stadtBeides; ergebnis["alerts_hamburg_keys"] = stadtKeys.Count;
    ergebnis["alerts_hamburg_trip"] = stadtTrip; ergebnis["alerts_hamburg_stop"] = stadtStop;
    return 0;
}

static string Kurz(string s, int n) => s.Length <= n ? s : s[..n] + "…";
