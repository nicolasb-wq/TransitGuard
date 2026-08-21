using System.Text.Json.Serialization;
using TransitGuard.Core.Interpolation;
using TransitGuard.Core.Models;

namespace TransitGuard.Api.Contracts;

public sealed class ReportCreateRequest
{
    public string CitySlug { get; set; } = default!;
    public string AnchorType { get; set; } = "station";     // station | trip
    public string ReportType { get; set; } = "in_vehicle";  // in_vehicle | on_platform | at_stop
    public string VehicleKind { get; set; } = "rail";
    public StationRef? Station { get; set; }
    public TripRef? Trip { get; set; }
    public int InspectorCount { get; set; } = 1;
    public ClientInfo? Client { get; set; }
}

public sealed class StationRef { public string StopId { get; set; } = default!; public double? Lat { get; set; } public double? Lon { get; set; } }
public sealed class TripRef
{
    public string TripId { get; set; } = default!;
    public string StartDate { get; set; } = default!;
    public string? RouteId { get; set; }
    public int? DirectionId { get; set; }
    public string? Headsign { get; set; }
}
public sealed class ClientInfo
{
    public bool? TicketConfirmed { get; set; }   // Pflicht (docs/18 H1) — null/false ⇒ 422
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
}

/// <summary>Free-DTO: Pro-Felder fehlen PHYSISCH (ADR: serverseitige Durchsetzung, T-EN-1).</summary>
public class ReportFreeDto
{
    public Guid Id { get; set; }
    public string CitySlug { get; set; } = default!;
    public string AnchorType { get; set; } = default!;
    public string? StationName { get; set; }
    public string? RouteId { get; set; }
    public string? Headsign { get; set; }
    public string ReportType { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = default!;
    public string Origin { get; set; } = default!;
    public string? TripId { get; set; }
    public string? TripStartDate { get; set; }
}

/// <summary>Pro-DTO: alles aus Free + Analysetiefe (Matrix #4/#5; nie Kern-Info exklusiv).</summary>
public sealed class ReportProDto : ReportFreeDto
{
    public int InspectorCount { get; set; }
    public decimal ReporterTrust { get; set; }
    public string ReporterRank { get; set; } = default!;
    public MovementDto? Movement { get; set; }
}

public sealed class MovementDto
{
    public string? LastStopId { get; set; }
    public int? EtaNextStopSeconds { get; set; }
    public string Label { get; set; } = default!;   // realtime_prognosis | schedule_only
}

public static class ReportDtoFormer
{
    private static string Snake(string s) =>
        string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public static ReportFreeDto ToFree(Report r, string slug) => new()
    {
        Id = r.Id, CitySlug = slug, AnchorType = Snake(r.AnchorType.ToString()),
        StationName = r.StationName, RouteId = r.RouteId, Headsign = r.Headsign,
        ReportType = Snake(r.Kind.ToString()), CreatedAt = r.CreatedAt, ExpiresAt = r.ExpiresAt,
        Status = r.Status.ToString().ToLowerInvariant(), Origin = r.Origin.ToString().ToLowerInvariant(),
        TripId = r.TripId, TripStartDate = r.TripStartDate
    };

    public static ReportProDto ToPro(Report r, string slug, string rank, MovementDto? movement = null)
    {
        var free = ToFree(r, slug);
        return new ReportProDto
        {
            Id = free.Id, CitySlug = free.CitySlug, AnchorType = free.AnchorType, StationName = free.StationName,
            RouteId = free.RouteId, Headsign = free.Headsign, ReportType = free.ReportType,
            CreatedAt = free.CreatedAt, ExpiresAt = free.ExpiresAt, Status = free.Status, Origin = free.Origin,
            TripId = free.TripId, TripStartDate = free.TripStartDate,
            InspectorCount = r.InspectorCount, ReporterTrust = r.ReporterTrust, ReporterRank = rank, Movement = movement
        };
    }

    public static MovementDto? Movement(TripProgress? p) => p is null ? null : new MovementDto
    {
        LastStopId = p.CurrentOrLastStopId,
        Label = p.Label.ToString().ToLowerInvariant()
    };
}
