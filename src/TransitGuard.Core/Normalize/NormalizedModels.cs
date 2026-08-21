namespace TransitGuard.Core.Normalize;

/// <summary>Zähler des Normalisierungspfads (Metriken für ingest_metrics, sql/0001).</summary>
public sealed class NormalizerCounters
{
    public int TripUpdatesSeen { get; set; }
    public int WhitelistMisses { get; set; }      // Trip nicht in Stadt-Extrakt (erwartbar: anderer Verbund)
    public int RouteMisses { get; set; }          // C.3.1: Whitelist-Hit, aber route_id unlösbar — Alarm-Metrik!
    public int DelayClamped { get; set; }         // B2
    public int VehicleFieldPresentButEmpty { get; set; }   // A2-Messung (8,4 % der TUs)
    public int AlertsSeen { get; set; }
    public int AlertsNoise { get; set; }
}

/// <summary>Normalisiertes TripUpdate (Ergebnis des TU-Normalisierers).</summary>
public sealed record NormalizedTripUpdate
{
    public required string TripId { get; init; }
    public required string StartDate { get; init; }
    public required string RouteId { get; init; }             // aus Feed ODER Lookup (C.3.1)
    public bool RouteIdFromFeed { get; init; }                // VBB-Muster (gtfs.de: immer false)
    public required IReadOnlyList<string> StopIds { get; init; }
    public string? LastStopId { get; init; }
    public int? LastDelaySeconds { get; init; }               // geclampt
    public bool DelayWasClamped { get; init; }
    public bool HasDelayData { get; init; }
    public bool VehicleFieldPresentButEmpty { get; init; }
    public required DateTimeOffset FeedSeenAt { get; init; }
}

/// <summary>Whitelist-Lookup-Ergebnis (In-Mem-Dictionary bzw. Postgres-Fallback, C.3.1).</summary>
public sealed record TripLookup(string RouteId, int? DirectionId, string? LastStopId, int? EndTimeSeconds);

/// <summary>Normalisierter Alert.</summary>
public sealed record NormalizedAlert
{
    public required string DedupKey { get; init; }            // sha256(header|desc[:150]) — N1
    public required string HeaderText { get; init; }
    public required string DescriptionText { get; init; }
    public string? Url { get; init; }
    public int Cause { get; init; }
    public int Effect { get; init; }
    public int Severity { get; init; }
    public IReadOnlyList<string> InformedTripIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InformedStopIds { get; init; } = Array.Empty<string>();
    public bool IsNoise { get; init; }
    public string? NoiseRuleId { get; init; }
}
