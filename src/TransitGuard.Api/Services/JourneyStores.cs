using System.Collections.Concurrent;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Journeys;
using TransitGuard.Core.Normalize;

namespace TransitGuard.Api.Services;

public sealed class InMemoryTripScheduleStore : ITripScheduleStore
{
    private IReadOnlyDictionary<string, TripSchedule> _schedules = new Dictionary<string, TripSchedule>();
    public bool TryGet(string cityId, string tripId, out TripSchedule schedule)
    { schedule = _schedules.GetValueOrDefault(tripId)!; return _schedules.ContainsKey(tripId); }
    public IEnumerable<KeyValuePair<string, TripSchedule>> All(string cityId) => _schedules;
    public void ReplaceCity(string cityId, IReadOnlyDictionary<string, TripSchedule> schedules) => _schedules = schedules;
}

public sealed class InMemoryStopStore : IStopStore
{
    private IReadOnlyList<StopInfo> _stops = Array.Empty<StopInfo>();
    public IReadOnlyList<StopInfo> Stops(string cityId) => _stops;
    public void ReplaceCity(string cityId, IReadOnlyList<StopInfo> stops) => _stops = stops;
}

public sealed class InMemoryAlertStore : IAlertStore
{
    // Schluessel ist stadt|dedupKey. Vorher war es nur der dedupKey und Visible() ignorierte
    // die Stadt vollstaendig — jede Stadt haette die Meldungen jeder anderen gesehen (T-ALERT-2).
    private readonly ConcurrentDictionary<string, (NormalizedAlert Alert, DateTimeOffset SeenAt)> _byKey = new(StringComparer.Ordinal);
    public void Upsert(string cityId, NormalizedAlert alert, DateTimeOffset seenAt) =>
        _byKey[cityId + "|" + alert.DedupKey] = (alert, seenAt);
    public IReadOnlyList<NormalizedAlert> Visible(string cityId, DateTimeOffset now, int maxAgeHours = 6)
    {
        var praefix = cityId + "|";
        return _byKey.Where(kv => kv.Key.StartsWith(praefix, StringComparison.Ordinal)
                               && !kv.Value.Alert.IsNoise
                               && now - kv.Value.SeenAt <= TimeSpan.FromHours(maxAgeHours))
            .Select(kv => kv.Value.Alert).ToList();
    }
}

public sealed class InMemoryDeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _issued = new();
    public DateTimeOffset MarkIssued(Guid deviceId) => _issued.GetOrAdd(deviceId, _ => DateTimeOffset.UtcNow);
    public DateTimeOffset? IssuedAt(Guid deviceId) => _issued.TryGetValue(deviceId, out var at) ? at : null;
}
