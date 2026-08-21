using System.Collections.Concurrent;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Models;

namespace TransitGuard.Api.Services;

public sealed record CityRegistry
{
    public static readonly IReadOnlyDictionary<string, City> All = new Dictionary<string, City>(StringComparer.OrdinalIgnoreCase)
    {
        ["hamburg"] = new City
        {
            CityId = "hamburg", Slug = "hamburg", DisplayName = "Hamburg & Umland", IsActive = true,
            BoundingBox = new Core.Geo.GeoBoundingBox(53.30, 53.85, 9.55, 10.50),
            TicketDeeplink = "https://www.hvv.de/deutschlandticket"
        }
    };
    public static City? BySlug(string slug) => All.GetValueOrDefault(slug.ToLowerInvariant());
}

/// <summary>Rate-Limits (docs/04 §4): Meldungen 5/10 min, Events 20/10 min, Departures 120/min, Restore 5/h.</summary>
public sealed class RateLimiter(IClock clock)
{
    public sealed record Bucket(int Limit, TimeSpan Window);
    public static readonly IReadOnlyDictionary<string, Bucket> Rules = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase)
    {
        ["reports:create"] = new(5, TimeSpan.FromMinutes(10)),
        ["reports:events"] = new(20, TimeSpan.FromMinutes(10)),
        ["schedule:departures"] = new(120, TimeSpan.FromMinutes(1)),
        ["billing:restore"] = new(5, TimeSpan.FromHours(1))
    };

    private readonly ConcurrentDictionary<(string BucketKey, Guid DeviceId), (DateTimeOffset WindowStart, int Count)> _hits = new();

    public bool Allow(string bucketKey, Guid deviceId)
    {
        var rule = Rules.GetValueOrDefault(bucketKey);
        if (rule is null) return true;
        var now = clock.UtcNow;
        var slot = _hits.GetOrAdd((bucketKey, deviceId), _ => (now, 0));
        if (now - slot.WindowStart >= rule.Window)
        {
            slot = (now, 0);
            _hits[(bucketKey, deviceId)] = slot;
        }
        if (slot.Count >= rule.Limit) return false;
        _hits[(bucketKey, deviceId)] = (slot.WindowStart, slot.Count + 1);
        return true;
    }
}

/// <summary>Idempotenz-Store (docs/04 §4): Key→serialisierte Antwort, 24 h TTL, 429-frei.</summary>
public sealed class InMemoryIdempotencyStore(IClock clock)
{
    private readonly ConcurrentDictionary<string, (string Body, DateTimeOffset ExpiresAt)> _store = new(StringComparer.Ordinal);

    public string? TryGet(string key)
    {
        if (_store.TryGetValue(key, out var v) && v.ExpiresAt > clock.UtcNow) return v.Body;
        return null;
    }

    public void Store(string key, string body) => _store[key] = (body, clock.UtcNow.AddHours(24));
}

public sealed class ApiError
{
    [System.Text.Json.Serialization.JsonPropertyName("error")] public ErrorBody Body { get; set; } = new();
    public sealed class ErrorBody
    {
        public string Code { get; set; } = default!;
        public string Message { get; set; } = default!;
    }
}

public sealed class EntitlementContext(IEntitlementStore store, IFeatureFlagService flags)
{
    public EntitlementTier TierFor(string? accessToken)
    {
        if (!flags.IsEnabled("pro_tier_enabled") || string.IsNullOrEmpty(accessToken)) return EntitlementTier.Free;
        return store.Validate(accessToken) is { Tier: EntitlementTier.Pro, Status: "active" } ? EntitlementTier.Pro : EntitlementTier.Free;
    }
}
