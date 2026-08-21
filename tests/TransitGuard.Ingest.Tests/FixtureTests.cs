using System.Text.Json;
using TransitGuard.Core.Geo;
using TransitGuard.Core.Normalize;
using TransitGuard.Ingest.GtfsRt;
using TransitGuard.Ingest.Static;
using Xunit;

namespace TransitGuard.Ingest.Tests;

/// <summary>
/// Echtdaten-Tests: rt_sample.pb ist ein Live-Ausschnitt des gtfs.de-Feeds (21.08.2026),
/// Erwartungswerte aus manifest.json (von Python berechnet — Kreuzprüfung C#↔Python).
/// </summary>
public sealed class FixtureParsingTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static JsonDocument Manifest() => JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDir, "manifest.json")));

    [Fact]
    public void Feed_zaehlt_Entities_wie_Python()
    {
        var m = Manifest().RootElement;
        var body = File.ReadAllBytes(Path.Combine(FixtureDir, "rt_sample.pb"));
        long entities = 0, tu = 0, alerts = 0;
        var (_, _, _, vp, _) = FeedReader.ForEachEntity(body,
            _ => { }, _ => { }, _ => { });
        // ForEachEntity liefert Gesamtwerte:
        (entities, tu, alerts, vp, _) = FeedReader.ForEachEntity(body, _ => { }, _ => { }, _ => { });
        Assert.Equal(m.GetProperty("trip_updates").GetInt64(), tu);
        Assert.Equal(m.GetProperty("alerts").GetInt64(), alerts);
        Assert.Equal(0, vp);   // 0 VehiclePositions — doppelt gemessen (Bestandsaufnahme §1)
    }

    [Fact]
    public void RouteId_im_Feed_leer_wie_gemessen()   // gtfs.de: 100 % leer
    {
        var m = Manifest().RootElement;
        var body = File.ReadAllBytes(Path.Combine(FixtureDir, "rt_sample.pb"));
        int withRoute = 0;
        FeedReader.ForEachEntity(body,
            raw => { if (!string.IsNullOrEmpty(raw.RouteIdFromFeed)) withRoute++; },
            _ => { }, _ => { });
        Assert.Equal(m.GetProperty("tu_with_route_id").GetInt64(), withRoute);
    }

    [Fact]
    public void Alert_Normalizer_klassifiziert_Attributierung_wie_Python()
    {
        var m = Manifest().RootElement;
        var body = File.ReadAllBytes(Path.Combine(FixtureDir, "rt_sample.pb"));
        var normalizer = AlertNormalizer.CreateDefault();
        var counters = new NormalizerCounters();
        var dedupKeys = new HashSet<string>();
        FeedReader.ForEachEntity(body, _ => { },
            raw =>
            {
                var n = normalizer.Normalize(raw.Header, raw.Description, raw.Url, raw.Cause, raw.Effect, raw.Severity,
                    raw.InformedTripIds, raw.InformedStopIds, counters);
                dedupKeys.Add(n.DedupKey);
            },
            _ => { });
        Assert.Equal(m.GetProperty("alerts_attribution").GetInt64(), counters.AlertsNoise);
        Assert.True(dedupKeys.Count < counters.AlertsSeen);   // Duplikate reduzieren (N1-Kern)
    }

    [Fact]
    public void DedupKey_CSharp_gleich_Python()   // Wire-Konsistenz der Dedup-Definition
    {
        foreach (var sample in Manifest().RootElement.GetProperty("dedup_samples").EnumerateArray())
        {
            var key = AlertNormalizer.ComputeDedupKey(sample.GetProperty("header").GetString()!,
                sample.GetProperty("desc").GetString()!);
            Assert.Equal(sample.GetProperty("key").GetString(), key);
        }
    }
}

public sealed class CityExtractorTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly GeoBoundingBox Hamburg = new(53.30, 53.85, 9.55, 10.50);

    [Fact]
    public void Extrakt_liefert_Stops_Trips_Whitelist()
    {
        using var fs = File.OpenRead(Path.Combine(FixtureDir, "static_mini.zip"));
        using var gtfs = new GtfsStaticArchive(fs);
        var x = CityExtractor.Extract(gtfs, Hamburg);

        // Fixture-Kennzahlen — erzeugt von verifikation/build_static_mini.py.
        // Seit 22.08.2026 enthält sie zusätzlich HHA5 (Farmsen) und die Linie U2
        // im 20-Minuten-Takt, damit HHA1→HHA5 einen echten UMSTIEG erzwingt
        // (vorher lag jede Haltestelle auf derselben Linie — Umstiegsverbindungen
        // kamen in keinem Test vor, und genau dort saß Launch-Blocker 1).
        Assert.Equal(5, x.StopsInBox);                 // HHA1-5 in Box, XX1/XX2 draußen
        Assert.Equal(147, x.TripsInBox);               // T_HH_1..3 + 2 × 72 Taktfahrten
        Assert.Equal(147, x.Whitelist.Count);
        Assert.Equal(147, x.Schedules.Count);
        Assert.Equal(5, x.Stops.Count);
        Assert.Contains("T_HH_1", x.Whitelist.Keys);
        Assert.Equal("R_U1", x.Whitelist["T_HH_1"].RouteId);
        Assert.Equal("HHA4", x.Whitelist["T_HH_1"].LastStopId);          // höchste Sequenz
        Assert.Equal(36360, x.Whitelist["T_HH_1"].EndTimeSeconds);       // 36000 + 3*120
        Assert.DoesNotContain("T_XX_1", x.Whitelist.Keys);

        // Umstiegsstrecke: U2 bedient HHA5, U1 nicht — sonst gäbe es eine Direktfahrt.
        Assert.Contains("T_U2_000", x.Whitelist.Keys);
        Assert.Equal("R_U2", x.Whitelist["T_U2_000"].RouteId);
        Assert.Equal("HHA5", x.Whitelist["T_U2_000"].LastStopId);
        Assert.DoesNotContain(x.Schedules.Values,
            sp => sp.Stops.Any(h => h.StopId == "HHA1") && sp.Stops.Any(h => h.StopId == "HHA5"));

        // Fahrplan-Schedules für Fahrtensuche (Auftrag 21.08.): Richtung A→HHA4
        var sched = x.Schedules["T_HH_1"];
        Assert.Equal(4, sched.Stops.Count);
        Assert.Equal("HHA1", sched.Stops[0].StopId);
        Assert.Equal(36360, sched.Stops[^1].DepartureS);
        Assert.Equal("Ohlsdorf", sched.Headsign);            // trips.txt: service_id=S1, trip_headsign=Ohlsdorf
    }

    [Fact]
    public void CSV_Reader_versteht_Quotes_und_Mehrzeiler()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("stops.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("stop_id,stop_name,stop_lat,stop_lon\nS1,\"Halt, mit Komma\",53.55,9.99\nS2,\"Feld\nmit Zeilenumbruch\",53.56,9.98\n");
        }
        ms.Position = 0;
        using var gtfs = new GtfsStaticArchive(ms);
        var stops = gtfs.Stops().ToList();
        Assert.Equal(2, stops.Count);
        Assert.Equal("Halt, mit Komma", stops[0].StopName);
        Assert.Equal("Feld\nmit Zeilenumbruch", stops[1].StopName);
    }
}
