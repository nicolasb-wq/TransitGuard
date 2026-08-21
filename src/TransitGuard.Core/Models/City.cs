namespace TransitGuard.Core.Models;

public sealed class City
{
    public string CityId { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public string DisplayName { get; init; } = default!;
    public bool IsActive { get; set; }
    public Geo.GeoBoundingBox BoundingBox { get; init; } = default!;
    public string? TicketDeeplink { get; init; }
}

public enum ReportEventType { Created, Confirmed, Contradicted, TtlExtended, Degraded, Expired, Cancelled }

public sealed class ReportEvent
{
    public long EventId { get; set; }
    public string CityId { get; init; } = default!;
    public Guid ReportId { get; init; }
    public ReportEventType Type { get; init; }
    public Guid? ActorDeviceId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Payload { get; init; } = new Dictionary<string, string>();
}
