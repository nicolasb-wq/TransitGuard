using System.IO.Compression;
using TransitGuard.Core.Geo;
using TransitGuard.Core.Journeys;
using TransitGuard.Core.Normalize;

namespace TransitGuard.Ingest.Static;

public sealed record GtfsTripRow(string TripId, string RouteId, string ServiceId, string? Headsign, int? DirectionId);
public sealed record GtfsStopRow(string StopId, string StopName, double Lat, double Lon);
public sealed record GtfsStopTimeRow(string TripId, int Sequence, string StopId, int? ArrivalS, int? DepartureS);

/// <summary>Minimaler, RFC4180-korrekter GTFS-CSV-Reader (Quotes, escapte Quotes, BOM).</summary>
public static class GtfsCsv
{
    public static IEnumerable<string[]> Rows(TextReader reader)
    {
        string? line;
        var pending = new System.Text.StringBuilder();
        while ((line = reader.ReadLine()) is not null)
        {
            pending.Append(line);
            if (CountQuotes(pending) % 2 == 1) { pending.Append('\n'); continue; }   // mehrzeiliges Quote-Feld
            yield return ParseLine(pending.ToString());
            pending.Clear();
        }
    }

    private static int CountQuotes(System.Text.StringBuilder sb)
    {
        int n = 0; for (int i = 0; i < sb.Length; i++) if (sb[i] == '"') n++; return n;
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>(); var sb = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = false; }
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    public static int SecondsAfterMidnight(string? hhmmss)
    {
        if (string.IsNullOrEmpty(hhmmss)) return 0;
        var p = hhmmss.Split(':');
        return p.Length == 3 && int.TryParse(p[0], out var h) && int.TryParse(p[1], out var m) && int.TryParse(p[2], out var s)
            ? h * 3600 + m * 60 + s : 0;   // GTFS erlaubt > 24 h
    }

    public static TextReader EntryReader(ZipArchive zip, string name)
    {
        var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"GTFS-Eintrag {name} fehlt");
        return new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
    }
}

public sealed class GtfsStaticArchive : IDisposable
{
    private readonly ZipArchive _zip;
    public GtfsStaticArchive(Stream stream) => _zip = new ZipArchive(stream, ZipArchiveMode.Read);

    public IEnumerable<GtfsStopRow> Stops()
    {
        using var r = GtfsCsv.EntryReader(_zip, "stops.txt");
        foreach (var (f, h) in RowsWithHeader(r))
        {
            if (double.TryParse(Get(f, h, "stop_lat"), System.Globalization.CultureInfo.InvariantCulture, out var la) &&
                double.TryParse(Get(f, h, "stop_lon"), System.Globalization.CultureInfo.InvariantCulture, out var lo))
                yield return new GtfsStopRow(Get(f, h, "stop_id")!, Get(f, h, "stop_name") ?? "", la, lo);
        }
    }

    public IEnumerable<GtfsTripRow> Trips()
    {
        using var r = GtfsCsv.EntryReader(_zip, "trips.txt");
        foreach (var (f, h) in RowsWithHeader(r))
            yield return new GtfsTripRow(Get(f, h, "trip_id")!, Get(f, h, "route_id")!, Get(f, h, "service_id") ?? "",
                Get(f, h, "trip_headsign"), int.TryParse(Get(f, h, "direction_id"), out var d) ? d : null);
    }

    public IEnumerable<(string TripId, string StopId, int Seq)> StopTimesIndex()
    {
        using var r = GtfsCsv.EntryReader(_zip, "stop_times.txt");
        foreach (var (f, h) in RowsWithHeader(r))
            yield return (Get(f, h, "trip_id")!, Get(f, h, "stop_id")!, int.TryParse(Get(f, h, "stop_sequence"), out var s) ? s : 0);
    }

    public IEnumerable<GtfsStopTimeRow> StopTimes()
    {
        using var r = GtfsCsv.EntryReader(_zip, "stop_times.txt");
        foreach (var (f, h) in RowsWithHeader(r))
            yield return new GtfsStopTimeRow(Get(f, h, "trip_id")!, int.TryParse(Get(f, h, "stop_sequence"), out var s) ? s : 0,
                Get(f, h, "stop_id")!, GtfsCsv.SecondsAfterMidnight(Get(f, h, "arrival_time")), GtfsCsv.SecondsAfterMidnight(Get(f, h, "departure_time")));
    }

    private static IEnumerable<(string[] Fields, Dictionary<string, int> Header)> RowsWithHeader(TextReader r)
    {
        var rows = GtfsCsv.Rows(r).GetEnumerator();
        try
        {
            if (!rows.MoveNext()) yield break;
            var header = new Dictionary<string, int>(rows.Current.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Current.Length; i++) header[rows.Current[i].Trim().TrimStart('\uFEFF')] = i;
            while (rows.MoveNext()) yield return (rows.Current, header);
        }
        finally { rows.Dispose(); }
    }

    private static string? Get(string[] f, Dictionary<string, int> h, string col) =>
        h.TryGetValue(col, out var i) && i < f.Length && f[i].Length > 0 ? f[i] : null;

    public void Dispose() => _zip.Dispose();
}

/// <summary>
/// Stadt-Extrakt (ADR-0008): bbox-Stops → Fahrten, die sie berühren (Closure über stop_times,
/// 2 Durchgänge) → Whitelist trip_id → (route_id, direction_id, last_stop_id, end_time_s).
/// Nur diese Teilmenge verlässt den Static-Sync (J3-Methode: 4.319 gleichzeitige HH-Umland-TUs).
/// </summary>
public static class CityExtractor
{
    public sealed record CityExtract(
        IReadOnlyDictionary<string, TripLookup> Whitelist,
        IReadOnlyDictionary<string, TripSchedule> Schedules,
        IReadOnlyList<StopInfo> Stops,
        int StopsInBox, int TripsInBox);

    public static CityExtract Extract(GtfsStaticArchive gtfs, GeoBoundingBox bbox)
    {
        var stopsInBox = new List<StopInfo>();
        var cityStops = new HashSet<string>();
        foreach (var s in gtfs.Stops())
            if (bbox.Contains(s.Lat, s.Lon)) { cityStops.Add(s.StopId); stopsInBox.Add(new StopInfo(s.StopId, s.StopName, s.Lat, s.Lon)); }

        var cityTrips = new HashSet<string>();
        foreach (var (tripId, stopId, _) in gtfs.StopTimesIndex())
            if (cityStops.Contains(stopId)) cityTrips.Add(tripId);

        // Haltefolgen je Stadt-Fahrt (ein Durchlauf; höchste Sequenz gewinnt für Whitelist-Endzeit)
        var stopsByTrip = new Dictionary<string, List<ScheduledStop>>(cityTrips.Count);
        foreach (var st in gtfs.StopTimes())
        {
            if (!cityTrips.Contains(st.TripId)) continue;
            if (!stopsByTrip.TryGetValue(st.TripId, out var list))
                stopsByTrip[st.TripId] = list = new List<ScheduledStop>();
            list.Add(new ScheduledStop(st.StopId, st.Sequence, st.DepartureS ?? st.ArrivalS ?? 0, st.ArrivalS ?? st.DepartureS ?? 0));
        }

        var whitelist = new Dictionary<string, TripLookup>(cityTrips.Count);
        var schedules = new Dictionary<string, TripSchedule>(cityTrips.Count);
        foreach (var t in gtfs.Trips())
        {
            if (!cityTrips.Contains(t.TripId) || !stopsByTrip.TryGetValue(t.TripId, out var list)) continue;
            var ordered = list.OrderBy(s => s.Sequence).ToList();
            var last = ordered[^1];
            whitelist[t.TripId] = new TripLookup(t.RouteId, t.DirectionId,
                last.StopId.Length > 0 ? last.StopId : null, last.DepartureS > 0 ? last.DepartureS : null);
            schedules[t.TripId] = new TripSchedule(t.TripId, t.RouteId, t.DirectionId, t.Headsign, ordered);
        }
        return new CityExtract(whitelist, schedules, stopsInBox, cityStops.Count, cityTrips.Count);
    }
}
