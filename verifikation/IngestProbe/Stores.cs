using System.Collections.Concurrent;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Normalize;
using TransitGuard.Ingest.Jobs;

namespace TransitGuard.Verifikation.IngestProbe;

/// <summary>
/// Minimalspeicher fuer die Messung. Bewusst KEINE Kopie der Produktionslogik ausser der
/// Schluesselbildung — Persistenz und SignalR werden nicht hier, sondern im Ende-zu-Ende-Lauf
/// gegen die echte API belegt (docs/28 Abschnitt C).
/// </summary>
public sealed class MessTripStateStore : ITripStateStore
{
    private readonly ConcurrentDictionary<string, NormalizedTripUpdate> _state = new(StringComparer.Ordinal);
    public int Count => _state.Count;
    public NormalizedTripUpdate? Get(string cityId, string tripId, string startDate) =>
        _state.TryGetValue(Key(cityId, tripId, startDate), out var v) ? v : null;
    public void Upsert(string cityId, NormalizedTripUpdate tu) => _state[Key(cityId, tu.TripId, tu.StartDate)] = tu;
    public int ReplaceCity(string cityId, IReadOnlyDictionary<string, NormalizedTripUpdate> neu)
    {
        foreach (var k in _state.Keys.Where(k => k.StartsWith(cityId + "|", StringComparison.Ordinal)).ToArray()) _state.TryRemove(k, out _);
        foreach (var v in neu.Values) _state[Key(cityId, v.TripId, v.StartDate)] = v;
        return neu.Count;
    }
    public IEnumerable<NormalizedTripUpdate> All(string cityId) =>
        _state.Where(kv => kv.Key.StartsWith(cityId + "|", StringComparison.Ordinal)).Select(kv => kv.Value);
    private static string Key(string c, string t, string d) => $"{c}|{t}|{d}";
}

public sealed class MessMetrikSenke : IIngestMetricsSink
{
    public List<IngestMetricRow> Zeilen { get; } = new();
    public void Write(IngestMetricRow row) => Zeilen.Add(row);
}

public sealed class MessWhitelist : ITripWhitelistProvider
{
    private IReadOnlyDictionary<string, TripLookup> _wl = new Dictionary<string, TripLookup>();
    public int Count => _wl.Count;
    public IReadOnlyDictionary<string, TripLookup> GetWhitelist(string cityId) => _wl;
    public void Replace(string cityId, IReadOnlyDictionary<string, TripLookup> wl) => _wl = wl;
}

public sealed class MessFahrplaene : TransitGuard.Core.Abstractions.ITripScheduleStore
{
    private readonly Dictionary<string, TransitGuard.Core.Journeys.TripSchedule> _d = new(StringComparer.Ordinal);
    public int Count => _d.Count;
    public bool TryGet(string cityId, string tripId, out TransitGuard.Core.Journeys.TripSchedule schedule)
        => _d.TryGetValue(tripId, out schedule!);
    public void ReplaceCity(string cityId, IReadOnlyDictionary<string, TransitGuard.Core.Journeys.TripSchedule> neu)
    { _d.Clear(); foreach (var kv in neu) _d[kv.Key] = kv.Value; }
    public IEnumerable<KeyValuePair<string, TransitGuard.Core.Journeys.TripSchedule>> All(string cityId) => _d;
}

public sealed class MessHalte : TransitGuard.Core.Abstractions.IStopStore
{
    private IReadOnlyList<TransitGuard.Core.Journeys.StopInfo> _l = Array.Empty<TransitGuard.Core.Journeys.StopInfo>();
    public int Count => _l.Count;
    public IReadOnlyList<TransitGuard.Core.Journeys.StopInfo> Stops(string cityId) => _l;
    public void ReplaceCity(string cityId, IReadOnlyList<TransitGuard.Core.Journeys.StopInfo> neu) => _l = neu;
}

public sealed class MessAlarme : IAlarmSink
{
    public List<string> Meldungen { get; } = new();
    public void Warn(string q, string m) => Meldungen.Add($"WARN {q}: {m}");
    public void Critical(string q, string m) { Meldungen.Add($"CRIT {q}: {m}"); Console.WriteLine($"  ALARM {q}: {m}"); }
}

public sealed class MessAlertSpeicher : TransitGuard.Core.Abstractions.IAlertStore
{
    private readonly Dictionary<string, (NormalizedAlert A, DateTimeOffset T)> _d = new(StringComparer.Ordinal);
    public int Count => _d.Count;
    public void Upsert(string cityId, NormalizedAlert a, DateTimeOffset seenAt) => _d[cityId + "|" + a.DedupKey] = (a, seenAt);
    public IReadOnlyList<NormalizedAlert> Visible(string cityId, DateTimeOffset now, int maxAgeHours = 6) =>
        _d.Where(kv => kv.Key.StartsWith(cityId + "|", StringComparison.Ordinal) && !kv.Value.A.IsNoise)
          .Select(kv => kv.Value.A).ToList();
}
