namespace TransitGuard.Core.Normalize;

/// <summary>
/// TU-Normalisierung nach C.3.1 + B2 + A2:
/// route_id := Feed-Wert falls gesetzt (VBB), sonst Whitelist-Lookup (gtfs.de, 100 % leer gemessen);
/// Lookup-Miss ⇒ verwerfen + Miss-Counter. Delay-Clamp [−120,+7200] s (gemessene Ausreißer −30.384/+7.148).
/// vehicle-Feld „präsent aber leer" wird als Metrik geführt (kein Fahrzeug-Anker!).
/// </summary>
public sealed class TripUpdateNormalizer(
    int delayClampMinSeconds = -120,
    int delayClampMaxSeconds = 7200)
{
    public sealed record RawTripUpdate(
        string TripId,
        string? StartDate,
        string? RouteIdFromFeed,
        IReadOnlyList<RawStopTimeUpdate> Stops,
        bool VehicleFieldPresent,
        string? VehicleId,
        DateTimeOffset FeedSeenAt);

    public sealed record RawStopTimeUpdate(string? StopId, int? ArrivalDelay, int? DepartureDelay);

    public NormalizedTripUpdate? Normalize(
        RawTripUpdate raw,
        IReadOnlyDictionary<string, TripLookup> cityWhitelist,
        NormalizerCounters counters)
    {
        counters.TripUpdatesSeen++;

        if (!cityWhitelist.TryGetValue(raw.TripId, out var lookup))
        {
            counters.WhitelistMisses++;
            return null;   // andere Stadt/Verbund — erwartbarer Fall (5,2 % HH-Anteil, Messung 14-j3-j6)
        }

        var routeFromFeed = !string.IsNullOrWhiteSpace(raw.RouteIdFromFeed);
        string routeId;
        if (routeFromFeed) routeId = raw.RouteIdFromFeed!;
        else if (!string.IsNullOrEmpty(lookup.RouteId)) routeId = lookup.RouteId;
        else
        {
            counters.RouteMisses++;   // Whitelist-Hit ohne Route ⇒ Datenfehler — Alarm-Metrik (T-NORM-2)
            return null;
        }

        var vehicleEmpty = raw.VehicleFieldPresent && string.IsNullOrEmpty(raw.VehicleId);
        if (vehicleEmpty) counters.VehicleFieldPresentButEmpty++;

        string? lastStopId = null; int? lastDelay = null; bool clamped = false, hasDelay = false;
        foreach (var s in raw.Stops)
        {
            if (!string.IsNullOrEmpty(s.StopId)) lastStopId = s.StopId;
            var d = s.DepartureDelay ?? s.ArrivalDelay;
            if (d is { } delay)
            {
                hasDelay = true;
                var c = Math.Clamp(delay, delayClampMinSeconds, delayClampMaxSeconds);
                if (c != delay) { clamped = true; counters.DelayClamped++; }
                lastDelay = c;
            }
        }
        if (lookup?.LastStopId is { } wl && lastStopId is null) lastStopId = wl;

        return new NormalizedTripUpdate
        {
            TripId = raw.TripId,
            StartDate = raw.StartDate ?? string.Empty,
            RouteId = routeId,
            RouteIdFromFeed = routeFromFeed,
            StopIds = raw.Stops.Where(s => !string.IsNullOrEmpty(s.StopId)).Select(s => s.StopId!).ToArray(),
            LastStopId = lastStopId,
            LastDelaySeconds = lastDelay,
            DelayWasClamped = clamped,
            HasDelayData = hasDelay,
            VehicleFieldPresentButEmpty = vehicleEmpty,
            FeedSeenAt = raw.FeedSeenAt
        };
    }
}
