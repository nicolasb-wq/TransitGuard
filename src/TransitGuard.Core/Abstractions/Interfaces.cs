using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Models;
using TransitGuard.Core.Normalize;

namespace TransitGuard.Core.Abstractions;

public interface IClock { DateTimeOffset UtcNow { get; } }

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedClock(DateTimeOffset at) : IClock
{
    private DateTimeOffset _at = at;
    public DateTimeOffset UtcNow => _at;
    public void Advance(TimeSpan span) => _at += span;
}

public sealed class IngestMetricRow
{
    public DateTimeOffset Ts { get; init; }
    public string Feed { get; init; } = default!;
    public int HttpStatus { get; init; }
    public long Bytes { get; init; }
    public int? FeedAgeSeconds { get; init; }
    public long Entities { get; init; }
    public long TripUpdates { get; init; }
    public long Alerts { get; init; }
    public long VehiclePositions { get; init; }
    public long CityTripsMatched { get; init; }
    public double MatchRate { get; init; }
    public int RouteMisses { get; init; }
    public int DelayClamped { get; init; }
    public int ParseMs { get; init; }
    public bool EtagHit { get; init; }
    public string? Error { get; init; }
}

public interface IIngestMetricsSink { void Write(IngestMetricRow row); }

/// <summary>Rollender Ist-Zustand der aktiven Fahrten (In-Memory-Spiegel von rt_trip_state).</summary>
public interface ITripStateStore
{
    NormalizedTripUpdate? Get(string cityId, string tripId, string startDate);
    void Upsert(string cityId, NormalizedTripUpdate tu);
    int ReplaceCity(string cityId, IReadOnlyDictionary<string, NormalizedTripUpdate> newState);
    IEnumerable<NormalizedTripUpdate> All(string cityId);
}

public interface IReportRepository
{
    void Add(Report r);
    Report? Find(Guid id);
    Report? FindActiveTripAnchor(string cityId, string tripId, string startDate);
    IReadOnlyList<Report> ListActive(string cityId);
    IReadOnlyList<Report> All();
    void SaveChanges();
}

public interface IReportEventLog
{
    long Append(ReportEvent evt);
    IReadOnlyList<ReportEvent> Since(string cityId, long sinceId, int limit);
}

public interface IRealtimeDispatcher
{
    void DispatchCityReportEvent(string cityId, ReportEvent evt, object freePayload, object proPayload);
    void DispatchControl(string cityId, string controlType, object payload);
    int EntitlementJoinViolations { get; set; }
}

public interface IFeatureFlagService { bool IsEnabled(string key); }

public interface IDeviceAuthStore
{
    (Guid DeviceId, string Token) Issue();
    bool TryResolve(string token, out Guid deviceId);
}

public sealed record EntitlementRecord(Guid Id, EntitlementTier Tier, string Status, string AccessTokenHash,
    string RestoreCodeHash, string Store, string? StoreRefHash, DateTimeOffset? ValidUntil);

public interface IEntitlementStore
{
    EntitlementRecord? Activate(string platform, string receipt, out string? accessToken, out string? restoreCode, out string? error);
    EntitlementRecord? Restore(string restoreCode, out string? accessToken, out string? error);
    EntitlementRecord? Validate(string accessToken);
    int ConnectionsCap { get; }
}
