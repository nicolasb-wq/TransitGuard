using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Journeys;
using TransitGuard.Core.Models;
using TransitGuard.Core.Normalize;
using TransitGuard.Core.Trust;
using System.Security.Cryptography;

namespace TransitGuard.Data;

// ============================================================================
// M4-EF: Produktions-Repositories über AppDbContext (Rolle tg_app) bzw.
// BillingDbContext (tg_billing). Die In-Memory-Varianten bleiben Lokal-Modus.
// Compile-geprüft; Laufzeit gegen Postgres via DATABASE__APP (docker-compose.dev).
// ============================================================================

public sealed class EfReportRepository(AppDbContext db) : IReportRepository
{
    private static Report ToDomain(ReportRow r) => new()
    {
        Id = r.Id, CreatedAt = r.CreatedAt, CityId = r.CityId,
        AnchorType = (AnchorType)r.AnchorType, StationId = r.StationId, StationName = r.StationName,
        TripId = r.TripId, TripStartDate = r.TripStartDate, RouteId = r.RouteId, DirectionId = r.DirectionId == null ? null : r.DirectionId,
        Headsign = r.Headsign, Kind = (ReportKind)r.ReportType, VehicleKind = (VehicleKind)r.VehicleKind,
        InspectorCount = r.InspectorCount, ReporterDeviceId = r.ReporterDeviceId, ReporterTrust = r.ReporterTrust,
        Status = Enum.Parse<ReportStatus>(r.Status, true), ExpiresAt = r.ExpiresAt, TtlBaseSeconds = r.TtlBaseSeconds,
        Confirmations = r.Confirmations, Contradictions = r.Contradictions, ParentReportId = r.ParentReportId,
        Origin = Enum.Parse<ReportOrigin>(r.Origin, true), Visible = r.Visible,
        GeoLat = r.Geo?.Y, GeoLon = r.Geo?.X, LastKnownStationId = r.LastKnownStationId
    };

    private static ReportRow ToRow(Report r) => new()
    {
        Id = r.Id, CreatedAt = r.CreatedAt, CityId = r.CityId, AnchorType = (short)r.AnchorType,
        StationId = r.StationId, StationName = r.StationName, TripId = r.TripId, TripStartDate = r.TripStartDate,
        RouteId = r.RouteId, DirectionId = (short?)(r.DirectionId ?? null), Headsign = r.Headsign,
        ReportType = (short)r.Kind, VehicleKind = (short)r.VehicleKind, InspectorCount = (short)r.InspectorCount,
        ReporterDeviceId = r.ReporterDeviceId, ReporterTrust = r.ReporterTrust, Status = r.Status.ToString().ToLowerInvariant(),
        ExpiresAt = r.ExpiresAt, TtlBaseSeconds = r.TtlBaseSeconds, Confirmations = r.Confirmations,
        Contradictions = r.Contradictions, ParentReportId = r.ParentReportId, Origin = r.Origin.ToString().ToLowerInvariant(),
        Visible = r.Visible,
        Geo = r.GeoLat is { } la && r.GeoLon is { } lo
            ? new NetTopologySuite.Geometries.Point(lo, la) { SRID = 4326 } : null,
        LastKnownStationId = r.LastKnownStationId
    };

    public void Add(Report r) => db.Reports.Add(ToRow(r));
    public Report? Find(Guid id) => db.Reports.Where(x => x.Id == id).OrderByDescending(x => x.CreatedAt).FirstOrDefault() is { } row ? ToDomain(row) : null;
    public Report? FindActiveTripAnchor(string cityId, string tripId, string startDate) =>
        db.Reports.Where(x => x.CityId == cityId && x.TripId == tripId && x.TripStartDate == startDate && x.Status == "active")
          .OrderByDescending(x => x.CreatedAt).FirstOrDefault() is { } row ? ToDomain(row) : null;
    public IReadOnlyList<Report> ListActive(string cityId) =>
        db.Reports.Where(x => x.CityId == cityId && x.Status == "active" && x.Visible).OrderBy(x => x.CreatedAt).ToList().Select(ToDomain).ToList();
    public IReadOnlyList<Report> All() => db.Reports.ToList().Select(ToDomain).ToList();
    public void SaveChanges() => db.SaveChanges();
}

public sealed class EfReportEventLog(AppDbContext db) : IReportEventLog
{
    public long Append(ReportEvent e)
    {
        var row = new ReportEventRow { CityId = e.CityId, ReportId = e.ReportId, EventType = e.Type.ToString().ToLowerInvariant(), ActorDeviceId = e.ActorDeviceId, CreatedAt = e.CreatedAt, Payload = "{}" };
        db.ReportEvents.Add(row);
        db.SaveChanges();
        e.EventId = row.EventId;
        return row.EventId;
    }
    public IReadOnlyList<ReportEvent> Since(string cityId, long sinceId, int limit) =>
        db.ReportEvents.Where(x => x.CityId == cityId && x.EventId > sinceId).OrderBy(x => x.EventId).Take(limit).ToList()
          .Select(r => new ReportEvent { EventId = r.EventId, CityId = r.CityId, ReportId = r.ReportId, Type = Enum.Parse<ReportEventType>(r.EventType, true), ActorDeviceId = r.ActorDeviceId, CreatedAt = r.CreatedAt })
          .ToList();
}

public sealed class EfFeatureFlagService(AppDbContext db) : IFeatureFlagService
{
    public bool IsEnabled(string key) => db.FeatureFlags.AsNoTracking().FirstOrDefault(f => f.Key == key) is { } f ? f.Enabled : false;
}

public sealed class EfTrustStore(AppDbContext db) : ITrustStoreApi
{
    public TrustState GetOrCreate(Guid deviceId)
    {
        var row = db.TrustScores.Find(deviceId);
        if (row is null) { row = new TrustRow { DeviceId = deviceId, Score = 50m }; db.TrustScores.Add(row); db.SaveChanges(); }
        return ToDomain(row);
    }
    public void Save(TrustState s)
    {
        var row = db.TrustScores.Find(s.DeviceId) ?? new TrustRow { DeviceId = s.DeviceId };
        row.Score = s.Score; row.TotalReports = s.TotalReports; row.ConfirmedCount = s.ConfirmedCount;
        row.FalseCount = s.FalseCount; row.ContradictedCount = s.ContradictedCount; row.Rank = (short)s.Rank;
        if (db.TrustScores.Find(s.DeviceId) is null) db.TrustScores.Add(row);
        db.SaveChanges();
    }
    public void Delete(Guid deviceId)
    {
        if (db.TrustScores.Find(deviceId) is { } row) { db.TrustScores.Remove(row); db.SaveChanges(); }
    }
    private static TrustState ToDomain(TrustRow r) => new() { DeviceId = r.DeviceId, Score = r.Score, TotalReports = r.TotalReports, ConfirmedCount = r.ConfirmedCount, FalseCount = r.FalseCount, ContradictedCount = r.ContradictedCount, Rank = (TrustRank)r.Rank };
}

/// <summary>API-seitige Trust-Schnittstelle — EF-Implementierung inkl. Api-Adapter.</summary>
public interface ITrustStoreApi
{
    TrustState GetOrCreate(Guid deviceId);
    void Save(TrustState state);
    void Delete(Guid deviceId);
}



public sealed class EfDeviceAuthStore(AppDbContext db) : IDeviceAuthStore
{
    public (Guid DeviceId, string Token) Issue()
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid();
        db.Devices.Add(new DeviceRow { DeviceId = id, TokenHash = Convert.FromHexString(TokenUtil.Sha256Hex(token)), CreatedAt = DateTimeOffset.UtcNow });   // Roh-Digest (64 B) — gleiche Darstellung wie TryResolve
        db.SaveChanges();
        return (id, token);
    }
    public bool TryResolve(string token, out Guid deviceId)
    {
        var hashBytes = FromHex(TokenUtil.Sha256Hex(token));   // bytea-Vergleich ist EF-übersetzbar
        var row = db.Devices.AsNoTracking().FirstOrDefault(d => d.TokenHash == hashBytes);
        deviceId = row?.DeviceId ?? Guid.Empty;
        return row is not null;
    }

    private static byte[] FromHex(string hex) =>
        Convert.FromHexString(hex);
}

/// <summary>Trial-Start = devices.created_at (Schema 0001) — keine Zusatztabelle nötig.</summary>
public sealed class EfDeviceRegistry(AppDbContext db) : IDeviceRegistry
{
    public DateTimeOffset MarkIssued(Guid deviceId) =>
        db.Devices.Find(deviceId) is { } row ? row.CreatedAt : DateTimeOffset.UtcNow;
    public DateTimeOffset? IssuedAt(Guid deviceId) => db.Devices.AsNoTracking().FirstOrDefault(d => d.DeviceId == deviceId)?.CreatedAt;
}

public sealed class EfTripStateStore(AppDbContext db) : ITripStateStore
{
    public NormalizedTripUpdate? Get(string cityId, string tripId, string startDate)
    {
        var r = db.Set<RtTripStateRow>().Find(cityId, tripId, startDate);
        return r is null ? null : ToDomain(r);
    }
    public void Upsert(string cityId, NormalizedTripUpdate tu)
    {
        var r = db.Set<RtTripStateRow>().Find(cityId, tu.TripId, tu.StartDate);
        if (r is null) { r = new RtTripStateRow { CityId = cityId, TripId = tu.TripId, TripStartDate = tu.StartDate }; db.Set<RtTripStateRow>().Add(r); }
        r.RouteId = tu.RouteId; r.LastStopId = tu.LastStopId; r.LastDelayS = tu.LastDelaySeconds; r.UpdatedAt = DateTimeOffset.UtcNow; r.FeedSeenAt = tu.FeedSeenAt;
        db.SaveChanges();
    }
    public int ReplaceCity(string cityId, IReadOnlyDictionary<string, NormalizedTripUpdate> newState) { foreach (var v in newState.Values) Upsert(cityId, v); return newState.Count; }
    public IEnumerable<NormalizedTripUpdate> All(string cityId) => db.Set<RtTripStateRow>().Where(x => x.CityId == cityId).ToList().Select(ToDomain);
    private static NormalizedTripUpdate ToDomain(RtTripStateRow r) => new()
    { TripId = r.TripId, StartDate = r.TripStartDate, RouteId = r.RouteId, RouteIdFromFeed = false, StopIds = Array.Empty<string>(), LastStopId = r.LastStopId, LastDelaySeconds = r.LastDelayS, HasDelayData = r.LastDelayS is not null, FeedSeenAt = r.FeedSeenAt };
}

public static class EfServiceCollectionExtensions
{
    /// <summary>Prod-Modus: registriert alle Stores gegen Postgres (DATABASE__APP / DATABASE__BILLING).</summary>
    public static IServiceCollection AddTransitGuardEfStores(this IServiceCollection services, string appConnectionString, string billingConnectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(appConnectionString, npg => npg.UseNetTopologySuite()));
        services.AddDbContext<BillingDbContext>(o => o.UseNpgsql(billingConnectionString));
        services.AddScoped<IReportRepository, EfReportRepository>();
        services.AddScoped<IReportEventLog, EfReportEventLog>();
        services.AddScoped<IFeatureFlagService, EfFeatureFlagService>();
        services.AddScoped<ITrustStoreApi, EfTrustStore>();
        services.AddScoped<EfTrustStore>();
        services.AddScoped<TransitGuard.Core.Abstractions.IEntitlementStore, EfEntitlementStore>();
        services.AddScoped<IDeviceAuthStore, EfDeviceAuthStore>();
        services.AddScoped<IDeviceRegistry, EfDeviceRegistry>();
        services.AddScoped<ITripStateStore, EfTripStateStore>();
        return services;
    }
}

/// <summary>EF-EntitlementStore (tg_billing): hashed Tokens/Receipts wie ADR-0005.</summary>
public sealed class EfEntitlementStore(BillingDbContext db) : TransitGuard.Core.Abstractions.IEntitlementStore
{
    public int ConnectionsCap => 3;

    public EntitlementRecord? Activate(string platform, string receipt, out string? accessToken, out string? restoreCode, out string? error)
    {
        accessToken = restoreCode = error = null;
        if (string.IsNullOrWhiteSpace(receipt)) { error = "receipt_invalid"; return null; }
        var receiptHash = Convert.FromHexString(TokenUtil.Sha256Hex(platform + ":" + receipt));
        if (db.Entitlements.Any(e => e.StoreRefHash == receiptHash)) { error = "receipt_already_used"; return null; }

        var (tok, tokHash) = TokenUtil.NewAccessToken();
        var (code, codeHash) = TokenUtil.NewRestoreCode();
        var rec = new EntitlementRow
        {
            Tier = "pro", Status = "active", Store = platform, StoreRefHash = receiptHash,
            AccessTokenHash = Convert.FromHexString(tokHash),
            RestoreCodeHash = Convert.FromHexString(codeHash),
            ValidUntil = DateTimeOffset.UtcNow.AddYears(1), PurchasedAt = DateTimeOffset.UtcNow
        };
        db.Entitlements.Add(rec); db.SaveChanges();
        accessToken = tok; restoreCode = code;
        return ToRecord(rec);
    }

    public EntitlementRecord? Restore(string restoreCode, out string? accessToken, out string? error)
    {
        accessToken = error = null;
        var hash = Convert.FromHexString(TokenUtil.Sha256Hex(restoreCode.Trim().ToUpperInvariant()));
        var rec = db.Entitlements.FirstOrDefault(e => e.RestoreCodeHash == hash);
        if (rec is null) { error = "restore_invalid"; return null; }
        var (tok, tokHash) = TokenUtil.NewAccessToken();
        rec.AccessTokenHash = Convert.FromHexString(tokHash);
        db.SaveChanges();
        accessToken = tok;
        return ToRecord(rec);
    }

    public EntitlementRecord? Validate(string accessToken)
    {
        var hash = Convert.FromHexString(TokenUtil.Sha256Hex(accessToken));
        var rec = db.Entitlements.FirstOrDefault(e => e.AccessTokenHash == hash);
        return rec is null ? null : ToRecord(rec);
    }

    private static EntitlementRecord ToRecord(EntitlementRow r) => new(r.Id,
        Enum.Parse<TransitGuard.Core.Entitlement.EntitlementTier>(r.Tier, true), r.Status,
        Convert.ToHexString(r.AccessTokenHash).ToLowerInvariant(),
        Convert.ToHexString(r.RestoreCodeHash).ToLowerInvariant(),
        r.Store, r.StoreRefHash is null ? null : Convert.ToHexString(r.StoreRefHash).ToLowerInvariant(), r.ValidUntil);
}
