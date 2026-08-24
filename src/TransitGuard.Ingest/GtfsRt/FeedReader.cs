using Google.Protobuf;
using TransitGuard.Core.Normalize;
using TransitRealtime;

namespace TransitGuard.Ingest.GtfsRt;

/// <summary>Rohe Alert-Daten vor der Normalisierung.</summary>
public sealed record RawAlert(
    string Header, string Description, string? Url,
    int Cause, int Effect, int Severity,
    IReadOnlyList<string> InformedTripIds, IReadOnlyList<string> InformedStopIds);

/// <summary>
/// Entity-Streaming-Reader (docs/07 §2 Nr. 3): parst die FeedMessage und iteriert Entities
/// einzeln (kein Zurückhalten des Objektbaums; ~180 k Entities / 33–48 MB Feed mittags).
/// </summary>
public static class FeedReader
{
    public sealed record FeedMeta(long Timestamp, string Version);

    public static FeedMeta ReadMeta(byte[] body)
    {
        var msg = FeedMessage.Parser.ParseFrom(body);
        if (msg.Header is null) throw new InvalidDataException("Feed-Antwort ohne FeedHeader.");
        return new FeedMeta((long)msg.Header.Timestamp, msg.Header.GtfsRealtimeVersion);
    }

    /// <summary>Iteration: Callback je Entity; TU/Alert werden zu Roh-Datensätzen konvertiert und sofort freigegeben.</summary>
    public static (long Entities, long TripUpdates, long Alerts, long VehiclePositions, DateTimeOffset FeedTimeUtc)
        ForEachEntity(byte[] body, Action<TripUpdateNormalizer.RawTripUpdate> onTrip, Action<RawAlert> onAlert, Action<DateTimeOffset> onFeedTimeUtc)
    {
        if (body.Length == 0)
            throw new InvalidDataException("Feed-Antwort war leer (0 Bytes) — das ist kein GTFS-RT.");
        var msg = FeedMessage.Parser.ParseFrom(body);
        // Google.Protobuf parst 0 Bytes und auch manche Truemmer klaglos zu einer FeedMessage
        // OHNE Header. Der Zugriff auf msg.Header lief dann ins Leere (T-POLL-ERR-1).
        if (msg.Header is null)
            throw new InvalidDataException($"Feed-Antwort ohne FeedHeader ({body.Length} Bytes) — kein GTFS-RT.");
        long entities = 0, tu = 0, al = 0, vp = 0;
        var feedTime = msg.Header.Timestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)msg.Header.Timestamp)
            : DateTimeOffset.UtcNow;
        onFeedTimeUtc(feedTime);

        foreach (var e in msg.Entity)
        {
            entities++;
            if (e.TripUpdate != null)
            {
                tu++;
                var t = e.TripUpdate;
                var stops = new List<TripUpdateNormalizer.RawStopTimeUpdate>(t.StopTimeUpdate.Count);
                foreach (var s in t.StopTimeUpdate)
                {
                    var sid = s.HasStopId ? s.StopId : null;
                    int? arr = s.Arrival != null && s.Arrival.HasDelay ? s.Arrival.Delay : null;
                    int? dep = s.Departure != null && s.Departure.HasDelay ? s.Departure.Delay : null;
                    stops.Add(new TripUpdateNormalizer.RawStopTimeUpdate(sid, arr, dep));
                }
                onTrip(new TripUpdateNormalizer.RawTripUpdate(
                    TripId: t.Trip.HasTripId ? t.Trip.TripId : string.Empty,
                    StartDate: t.Trip.HasStartDate ? t.Trip.StartDate : null,
                    RouteIdFromFeed: t.Trip.HasRouteId ? t.Trip.RouteId : null,
                    Stops: stops,
                    VehicleFieldPresent: t.Vehicle != null,
                    VehicleId: t.Vehicle != null && t.Vehicle.HasId ? t.Vehicle.Id : null,
                    FeedSeenAt: feedTime));
            }
            else if (e.Vehicle != null) vp++;
            else if (e.Alert != null)
            {
                al++;
                var a = e.Alert;
                string header = PickText(a.HeaderText);
                string desc = PickText(a.DescriptionText);
                string? url = a.Url != null ? PickText(a.Url) : null;
                var trips = new List<string>(); var stopsIds = new List<string>();
                foreach (var ie in a.InformedEntity)
                {
                    if (ie.Trip != null && ie.Trip.HasTripId) trips.Add(ie.Trip.TripId);
                    if (ie.HasStopId && ie.StopId.Length > 0) stopsIds.Add(ie.StopId);
                }
                onAlert(new RawAlert(header, desc, url, (int)a.Cause, (int)a.Effect, (int)a.SeverityLevel, trips, stopsIds));
            }
        }
        return (entities, tu, al, vp, feedTime);
    }

    private static string PickText(TranslatedString t)
    {
        foreach (var tr in t.Translation) if (tr.Text.Length > 0) return tr.Text;   // de-Präferenz folgt in Normalizer-Config
        return string.Empty;
    }
}
