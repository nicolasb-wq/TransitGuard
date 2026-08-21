namespace TransitGuard.Core.Models;

public enum AnchorType { Station = 1, Trip = 2, DerivedTerminus = 3 }
public enum ReportKind { InVehicle = 1, OnPlatform = 2, AtStop = 3 }
public enum VehicleKind { Rail = 1, Bus = 2 }
public enum ReportStatus { Active, Expired, Cancelled, Degraded }
public enum ReportOrigin { User, Derived }

/// <summary>
/// Domänen-Entität „Meldung" (mappt 1:1 auf Tabelle reports, sql/0001).
/// Trip-Anker-Semantik nach v1.1 B.4/B.5; TTL-Regeln siehe TtlEngine.
/// </summary>
public sealed class Report
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; }
    public string CityId { get; init; } = default!;

    public AnchorType AnchorType { get; init; }
    public string? StationId { get; init; }
    public string? StationName { get; init; }

    public string? TripId { get; init; }
    public string? TripStartDate { get; init; }           // GTFS start_date YYYYMMDD
    public string? RouteId { get; init; }
    public int? DirectionId { get; init; }
    public string? Headsign { get; init; }

    public ReportKind Kind { get; init; }
    public VehicleKind VehicleKind { get; init; } = VehicleKind.Rail;
    public int InspectorCount { get; init; } = 1;

    public Guid ReporterDeviceId { get; init; }
    public decimal ReporterTrust { get; init; }
    public ReportStatus Status { get; set; } = ReportStatus.Active;
    public DateTimeOffset ExpiresAt { get; set; }
    public int TtlBaseSeconds { get; init; }
    public int Confirmations { get; set; }
    public int Contradictions { get; set; }
    public Guid? ParentReportId { get; init; }
    public ReportOrigin Origin { get; init; } = ReportOrigin.User;
    public bool Visible { get; set; } = true;
    public double? GeoLat { get; init; }
    public double? GeoLon { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>Zuletzt interpolierte Station (für B.5-Derivation und Swap-Degradation, C.3.2).</summary>
    public string? LastKnownStationId { get; set; }
}
