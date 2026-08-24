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
    FeedEtagStore etags,
    IAlertStore alertStore,
    ILogger<PollRealtimeJob>? logger = null)
{
    public async Task<PollResult> RunAsync(string feedUrl, string cityId, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // ETag aus dem prozessweiten Speicher — NICHT aus einem Instanzfeld: der Job ist Scoped,
        // jede Hangfire-Ausfuehrung bekaeme sonst eine frische Instanz ohne ETag (T-ETAG-1).
        var result = await fetcher.FetchAsync(feedUrl, etags.Get(feedUrl), ct).ConfigureAwait(false);
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
        var counters = new NormalizerCounters();
        var tuByTrip = new Dictionary<string, NormalizedTripUpdate>(4096);
        var alerts = new List<NormalizedAlert>(256);
        var feedTime = DateTimeOffset.MinValue;

        // Der Parse liegt in einem eigenen Schutz: ein leerer Body (Status 200, 0 Bytes) und
        // ein verstuemmeltes Protobuf sind Betriebsalltag, keine Ausnahmefaelle. Ohne diesen
        // Block flog eine NullReferenceException bzw. InvalidProtocolBufferException aus dem
        // Job heraus — ohne Metrikzeile, ohne Fehlerzaehler, also ohne Circuit-Breaker.
        // Der Ausfall waere nur im Hangfire-Dashboard sichtbar gewesen (T-POLL-ERR-1/-2).
        long entities, tuCount, alertCount, vpCount;
        try
        {
            (entities, tuCount, alertCount, vpCount, _) = ParseFeed(result.Body);
        }
        catch (Exception ex) when (ex is Google.Protobuf.InvalidProtocolBufferException
                                      or NullReferenceException or IndexOutOfRangeException
                                      or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            healthTracker.RecordRun(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), 0, failed: true);
            metrics.Write(new IngestMetricRow
            {
                Ts = clock.UtcNow, Feed = feedUrl, HttpStatus = result.StatusCode, Bytes = result.Body.Length,
                Error = $"parse_fehler: {ex.GetType().Name}: {ex.Message}", ParseMs = (int)sw.ElapsedMilliseconds
            });
            logger?.LogError(ex, "Poll {City}: Feed nicht lesbar ({Bytes} Bytes)", cityId, result.Body.Length);
            // Der gemerkte ETag wird bewusst NICHT gesetzt: sonst wuerde der naechste Lauf
            // denselben kaputten Inhalt per 304 als "unveraendert" abhaken.
            return PollResult.FailedResult;
        }

        // ETag erst jetzt merken — nach erfolgreichem Parse. Sonst wuerde ein einmal
        // verstuemmelter Feed per 304 dauerhaft als "unveraendert" abgehakt.
        etags.Set(feedUrl, result.ETag);

        var now = clock.UtcNow;
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var age = feedTime > DateTimeOffset.MinValue ? (int)(now - feedTime).TotalSeconds : (int?)null;
        healthTracker.RecordRun(day, entities, failed: false);
        var health = new FeedHealthEvaluator().Evaluate(age, entities, healthTracker.WeekdayMean(day), healthTracker.ConsecutiveFailures);

        (long, long, long, long, DateTimeOffset) ParseFeed(byte[] body) => FeedReader.ForEachEntity(
            body,
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

        // Alerts in den Stadt-Store. Bis zum 24.08.2026 wurde diese Liste zwar gefuellt,
        // aber NIRGENDWO hingeschrieben: /v1/cities/{slug}/alerts war dauerhaft leer,
        // egal wieviele Stoerungsmeldungen im Feed standen (T-ALERT-1).
        //
        // Gefiltert wird ueber die Stadt-Whitelist der informierten Fahrten. Messung
        // 24.08.2026: 99,9 % der Alerts tragen eine trip_id, 0,1 % eine stop_id, und
        // KEIN Hamburg-Treffer kam ueber stop_id, den die trip_id nicht auch gefunden
        // haette. Ein trip-basierter Filter ist damit vollstaendig, nicht nur bequem.
        var stadtWhitelist = whitelistProvider.GetWhitelist(cityId);
        int alertsGespeichert = 0;
        foreach (var a in alerts)
        {
            if (a.IsNoise) continue;                                   // Rauschen gar nicht erst ablegen
            bool gehoertZurStadt = false;
            foreach (var t in a.InformedTripIds)
                if (stadtWhitelist.ContainsKey(t)) { gehoertZurStadt = true; break; }
            if (!gehoertZurStadt) continue;
            alertStore.Upsert(cityId, a, feedTime > DateTimeOffset.MinValue ? feedTime : now);
            alertsGespeichert++;
        }

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

        logger?.LogInformation("Poll {City}: {Entities} Entities, {Matched} Stadt-TUs, {Changed} Deltas, {Alerts} Stadt-Alerts, Health {Health}",
            cityId, entities, tuByTrip.Count, changed, alertsGespeichert, health);
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
