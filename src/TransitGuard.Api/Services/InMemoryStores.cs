using System.Collections.Concurrent;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Models;
using TransitGuard.Core.Normalize;
using TransitGuard.Ingest.Jobs;
using TransitGuard.Core.Trust;

namespace TransitGuard.Api.Services;

public interface ITrustStore
{
    TrustState GetOrCreate(Guid deviceId);
    void Save(TrustState state);   // EF-Modus: explizites Persistieren; In-Memory: No-Op (Referenz)
    void Delete(Guid deviceId);
}

/// <summary>Lokaler/Dev-Betrieb ohne Postgres (Prod: EfRepositories gegen tg_app/tg_billing — Ticket M4).</summary>
public sealed class InMemoryStores : IReportRepository, IReportEventLog, IFeatureFlagService, ITrustStore
{
    private readonly ConcurrentDictionary<Guid, Report> _reports = new();
    private readonly ConcurrentDictionary<long, ReportEvent> _events = new();
    private readonly ConcurrentDictionary<Guid, TrustState> _trust = new();
    private readonly ConcurrentDictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reports_enabled"] = true, ["alerts_enabled"] = true, ["pro_tier_enabled"] = false, ["push_enabled"] = false
    };
    private long _eventId;

    public void Add(Report r) => _reports[r.Id] = r;
    public Report? Find(Guid id) => _reports.GetValueOrDefault(id);
    public Report? FindActiveTripAnchor(string cityId, string tripId, string startDate) =>
        _reports.Values.FirstOrDefault(r => r.CityId == cityId && r.TripId == tripId && r.TripStartDate == startDate
            && r.AnchorType == AnchorType.Trip && r.Status == ReportStatus.Active);
    public IReadOnlyList<Report> ListActive(string cityId) =>
        _reports.Values.Where(r => r.CityId == cityId && r.Status == ReportStatus.Active && r.Visible).OrderBy(r => r.CreatedAt).ToList();
    public IReadOnlyList<Report> All() => _reports.Values.ToList();
    public void SaveChanges() { }

    public long Append(ReportEvent evt)
        { var id = System.Threading.Interlocked.Increment(ref _eventId); _events[id] = evt; return id; }
    public IReadOnlyList<ReportEvent> Since(string cityId, long sinceId, int limit) =>
        _events.Where(kv => kv.Key > sinceId && kv.Value.CityId == cityId).OrderBy(kv => kv.Key).Take(limit).Select(kv => kv.Value).ToList();

    public bool IsEnabled(string key) => _flags.GetValueOrDefault(key, false);
    public void SetFlag(string key, bool value) => _flags[key] = value;   // nur Ops-Endpunkt/T7.3

    public TrustState GetOrCreate(Guid deviceId) => _trust.GetOrAdd(deviceId, _ => TrustEngine.NewDevice(deviceId));
    public void Save(TrustState state) { /* Referenz-Semantik: Objekt liegt im Store */ }
    public void Delete(Guid deviceId) => _trust.TryRemove(deviceId, out _);
}

public sealed class InMemoryDeviceAuthStore : IDeviceAuthStore
{
    private readonly ConcurrentDictionary<string, Guid> _byToken = new(StringComparer.Ordinal);
    public (Guid DeviceId, string Token) Issue()
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid();
        _byToken[token] = id;
        return (id, token);
    }
    public bool TryResolve(string token, out Guid deviceId) => _byToken.TryGetValue(token, out deviceId);
}

public sealed class InMemoryTripStateStore : ITripStateStore
{
    private readonly ConcurrentDictionary<string, NormalizedTripUpdate> _state = new(StringComparer.Ordinal);
    public NormalizedTripUpdate? Get(string cityId, string tripId, string startDate) =>
        _state.TryGetValue(Key(cityId, tripId, startDate), out var v) ? v : null;
    public void Upsert(string cityId, NormalizedTripUpdate tu) => _state[Key(cityId, tu.TripId, tu.StartDate)] = tu;
    public int ReplaceCity(string cityId, IReadOnlyDictionary<string, NormalizedTripUpdate> newState)
    {
        foreach (var k in _state.Keys.Where(k => k.StartsWith(cityId + "|", StringComparison.Ordinal)).ToArray()) _state.TryRemove(k, out _);
        foreach (var v in newState.Values) _state[Key(cityId, v.TripId, v.StartDate)] = v;
        return newState.Count;
    }
    public IEnumerable<NormalizedTripUpdate> All(string cityId) =>
        _state.Where(kv => kv.Key.StartsWith(cityId + "|", StringComparison.Ordinal)).Select(kv => kv.Value);
    private static string Key(string c, string t, string d) => $"{c}|{t}|{d}";
}

public sealed class InMemoryTripWhitelistProvider : ITripWhitelistHolder, ITripWhitelistProvider
{
    private IReadOnlyDictionary<string, TripLookup> _whitelist = new Dictionary<string, TripLookup>();
    public IReadOnlyDictionary<string, TripLookup> GetWhitelist(string cityId) => _whitelist;   // v1: eine aktive Stadt (MVP)
    public void Replace(string cityId, IReadOnlyDictionary<string, TripLookup> whitelist) => _whitelist = whitelist;
    public int Count => _whitelist.Count;
}

public interface ITripWhitelistHolder { int Count { get; } }

/// <summary>Billing-Sandbox (Prod: Store-Server-Validierung Apple/Google — Ticket T6.2, F-12).</summary>
public sealed class InMemoryEntitlementStore : IEntitlementStore
{
    private readonly ConcurrentDictionary<string, EntitlementRecord> _byTokenHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _byReceiptHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _byRestoreHash = new(StringComparer.Ordinal);
    public int ConnectionsCap => 3;

    public EntitlementRecord? Activate(string platform, string receipt, out string? accessToken, out string? restoreCode, out string? error)
    {
        accessToken = restoreCode = error = null;
        if (string.IsNullOrWhiteSpace(receipt)) { error = "receipt_invalid"; return null; }
        var receiptHash = TokenUtil.Sha256Hex(platform + ":" + receipt);
        if (_byReceiptHash.TryGetValue(receiptHash, out var existingId))
        { error = "receipt_already_used"; return _byTokenHash.Values.FirstOrDefault(e => e.Id == existingId); }

        var (tok, tokHash) = TokenUtil.NewAccessToken();
        var (code, codeHash) = TokenUtil.NewRestoreCode();
        var rec = new EntitlementRecord(Guid.NewGuid(), EntitlementTier.Pro, "active", tokHash, codeHash,
            platform, receiptHash, DateTimeOffset.UtcNow.AddYears(1));
        _byTokenHash[tokHash] = rec; _byReceiptHash[receiptHash] = rec.Id; _byRestoreHash[codeHash] = rec.Id;
        accessToken = tok; restoreCode = code;
        return rec;
    }

    public EntitlementRecord? Restore(string restoreCode, out string? accessToken, out string? error)
    {
        accessToken = error = null;
        var hash = TokenUtil.Sha256Hex(restoreCode.Trim().ToUpperInvariant());
        if (!_byRestoreHash.TryGetValue(hash, out var id)) { error = "restore_invalid"; return null; }
        var rec = _byTokenHash.Values.FirstOrDefault(e => e.Id == id);
        if (rec is null) { error = "restore_invalid"; return null; }
        var (tok, tokHash) = TokenUtil.NewAccessToken();
        var updated = rec with { AccessTokenHash = tokHash };
        _byTokenHash.TryRemove(rec.AccessTokenHash, out _);
        _byTokenHash[tokHash] = updated;
        accessToken = tok;
        return updated;
    }

    public EntitlementRecord? Validate(string accessToken)
    {
        var rec = _byTokenHash.GetValueOrDefault(TokenUtil.Sha256Hex(accessToken));
        if (rec is null) return null;
        if (rec.ValidUntil is { } until && until < DateTimeOffset.UtcNow)
            return rec with { Status = "expired", Tier = EntitlementTier.Free };
        return rec;
    }
}

public sealed class NoopRealtimeDispatcher : IRealtimeDispatcher
{
    public int EntitlementJoinViolations { get; set; }
    public void DispatchCityReportEvent(string cityId, ReportEvent evt, object freePayload, object proPayload) { }
    public void DispatchControl(string cityId, string controlType, object payload) { }
}
