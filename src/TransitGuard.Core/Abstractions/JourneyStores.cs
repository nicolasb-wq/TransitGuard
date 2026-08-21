using TransitGuard.Core.Journeys;
using TransitGuard.Core.Normalize;

namespace TransitGuard.Core.Abstractions;

public interface ITripScheduleStore
{
    bool TryGet(string cityId, string tripId, out TripSchedule schedule);
    IEnumerable<KeyValuePair<string, TripSchedule>> All(string cityId);
    void ReplaceCity(string cityId, IReadOnlyDictionary<string, TripSchedule> schedules);
}

public interface IStopStore
{
    IReadOnlyList<StopInfo> Stops(string cityId);
    void ReplaceCity(string cityId, IReadOnlyList<StopInfo> stops);
}

public interface IAlertStore
{
    void Upsert(NormalizedAlert alert, DateTimeOffset seenAt);
    IReadOnlyList<NormalizedAlert> Visible(string cityId, DateTimeOffset now, int maxAgeHours = 6);
}

/// <summary>Nächste Haltestelle per Haversine (km) — Standort-Autofill „Start" (Auftrag 21.08.).</summary>
public static class GeoMath
{
    public static double KmBetween(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 6371.0, ToRad = Math.PI / 180;
        var dLat = (lat2 - lat1) * ToRad; var dLon = (lon2 - lon1) * ToRad;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * ToRad) * Math.Cos(lat2 * ToRad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static List<(StopInfo Stop, double Km)> Nearest(IEnumerable<StopInfo> stops, double lat, double lon, int take) =>
        stops.Select(s => (s, KmBetween(lat, lon, s.Lat, s.Lon)))
             .OrderBy(x => x.Item2)
             .Take(take)
             .ToList();
}

/// <summary>Device-Metadaten inkl. Trial-Start (14 Tage ab erster Nutzung — ADR-0014; EF: devices.created_at).</summary>
public interface IDeviceRegistry
{
    DateTimeOffset MarkIssued(Guid deviceId);
    DateTimeOffset? IssuedAt(Guid deviceId);
}
