using Microsoft.Extensions.Logging;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Ingest;
using TransitGuard.Core.Normalize;
using TransitGuard.Ingest.GtfsRt;

namespace TransitGuard.Ingest.Jobs;

/// <summary>
/// PollRealtimeJob (docs/07 §2): 60 s, ein Feed (gtfs.de), Parse → sofort Stadt-Filter →
/// Delta gegen TripState → Events + Metrikzeile. Idempotent (Zustandsmaschine), Circuit-Breaker
/// über FeedHealthTracker (≥3 Fehler ⇒ Feed ungesund ⇒ G11-Gate friert Degradationen ein).
/// </summary>
public sealed class PollRealtimeJob(
    FeedFetcher fetcher,
    ITripStateStore tripState,
    IIngestMetricsSink metrics,
    FeedHealthTracker healthTracker,
    TripUpdateNormalizer tuNormalizer,
    AlertNormalizer alertNormalizer,
    ITripWhitelistProvider whitelistProvider,
    IClock clock,
    ILogger<PollRealtimeJob>? logger = null)
{
    private string? _etag;

    public async Task<PollResult> RunAsync(string feedUrl, string cityId, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await fetcher.FetchAsync(feedUrl, _etag, ct).ConfigureAwait(false);
        if (result.NotModified)
        {
            metrics.Write(new IngestMetricRow { Ts = clock.UtcNow, Feed = feedUrl, HttpStatus = 304, EtagHit = true, ParseMs = (int)result.ElapsedMs });
            return PollResult.NotModifiedResult;
        }
        if (result.StatusCode != 200 || result.Body is null)
        {
            healthTracker.RecordRun(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), 0, failed: true);
            metrics.Write(new IngestMetricRow { Ts = clock.UtcNow, Feed = feedUrl, HttpStatus = result.StatusCode, Error = result.Error ?? "no_body", ParseMs = (int)result.ElapsedMs });
            return PollResult.FailedResult;
        }
        _etag = result.ETag;

        var counters = new NormalizerCounters();
        var tuByTrip = new Dictionary<string, NormalizedTripUpdate>(4096);
        var alerts = new List<NormalizedAlert>(256);
        var feedTime = DateTimeOffset.MinValue;

        var (entities, tuCount, alertCount, vpCount, feedTimeUtc) = FeedReader.ForEachEntity(
            result.Body,
            raw =>
            {
                if (string.IsNullOrEmpty(raw.TripId)) return;
                var n = tuNormalizer.Normalize(raw, whitelistProvider.GetWhitelist(cityId), counters);
                if (n is not null) tuByTrip[n.TripId + "|" + n.StartDate] = n;
            },
            raw =>
            {
                var n = alertNormalizer.Normalize(raw.Header, raw.Description, raw.Url, raw.Cause, raw.Effect, raw.Severity,
                    raw.InformedTripIds, raw.InformedStopIds, counters);
                alerts.Add(n);
            },
            t => feedTime = t);

        // Delta: nur Änderungen weiterreichen (docs/07 §2 Nr. 4)
        int changed = 0;
        foreach (var kv in tuByTrip)
        {
            var key = kv.Key.Split('|');
            var existing = tripState.Get(cityId, key[0], key[1]);
            if (existing is null || existing.LastDelaySeconds != kv.Value.LastDelaySeconds || existing.LastStopId != kv.Value.LastStopId)
            {
                tripState.Upsert(cityId, kv.Value);
                changed++;
            }
        }

        var now = clock.UtcNow;
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var age = feedTime > DateTimeOffset.MinValue ? (int)(now - feedTime).TotalSeconds : (int?)null;
        healthTracker.RecordRun(day, entities, failed: false);
        var health = new FeedHealthEvaluator().Evaluate(age, entities, healthTracker.WeekdayMean(day), healthTracker.ConsecutiveFailures);

        double matchRate = counters.TripUpdatesSeen > 0
            ? (counters.TripUpdatesSeen - counters.RouteMisses) / (double)counters.TripUpdatesSeen : 0;
        metrics.Write(new IngestMetricRow
        {
            Ts = now, Feed = feedUrl, HttpStatus = 200, Bytes = result.Body.Length, FeedAgeSeconds = age,
            Entities = entities, TripUpdates = tuCount, Alerts = alertCount, VehiclePositions = vpCount,
            CityTripsMatched = tuByTrip.Count, MatchRate = matchRate, RouteMisses = counters.RouteMisses,
            DelayClamped = counters.DelayClamped, ParseMs = (int)sw.ElapsedMilliseconds, EtagHit = false,
            Error = health.IsHealthy ? null : health.ToString()
        });

        logger?.LogInformation("Poll {City}: {Entities} Entities, {Matched} Stadt-TUs, {Changed} Deltas, Health {Health}",
            cityId, entities, tuByTrip.Count, changed, health);
        return PollResult.Ok(entities, tuByTrip.Count, changed, health.IsHealthy);
    }

    public sealed record PollResult(bool Success, bool NotModified, long Entities, long CityTrips, int Changed, bool FeedHealthy)
    {
        public static readonly PollResult NotModifiedResult = new(true, true, 0, 0, 0, true);
        public static readonly PollResult FailedResult = new(false, false, 0, 0, 0, false);
        public static PollResult Ok(long e, long c, int ch, bool h) => new(true, false, e, c, ch, h);
    }
}

/// <summary>Liefert die In-Memory-Whitelist (Stadt-Extrakt, C.3.1 Ebene 1; Fallback Postgres = T2.5).</summary>
public interface ITripWhitelistProvider
{
    IReadOnlyDictionary<string, TripLookup> GetWhitelist(string cityId);
    void Replace(string cityId, IReadOnlyDictionary<string, TripLookup> whitelist);
}
