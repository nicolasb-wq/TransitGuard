using TransitGuard.Core.Normalize;

namespace TransitGuard.Core.Journeys;

public sealed record StopInfo(string StopId, string StopName, double Lat, double Lon);

/// <summary>Fahrplan einer Fahrt (Stadt-Extrakt): geordnete Halte mit Soll-Abfahrten (Sekunden ab Betriebstag-Mitternacht).</summary>
public sealed record TripSchedule(
    string TripId, string RouteId, int? DirectionId, string? Headsign,
    IReadOnlyList<ScheduledStop> Stops)
{
    public int? IndexOf(string stopId)
    {
        for (int i = 0; i < Stops.Count; i++) if (Stops[i].StopId == stopId) return i;
        return null;
    }
}

public sealed record ScheduledStop(string StopId, int Sequence, int DepartureS, int ArrivalS);

public sealed record Departure(
    string TripId, string StartDate, string RouteId, string? Headsign,
    DateTimeOffset ScheduledAt, DateTimeOffset? EstimatedAt, int? DelaySeconds, bool Realtime)
{
    /// <summary>Kontroll-Warnungen dieser Abfahrt (Anzeige ≥1 Haltestelle vorher); null = keine.</summary>
    public IReadOnlyList<ControlWarning>? Warnings { get; set; }
}

/// <summary>Direkte Verbindung Start→Ziel (ohne Umstieg — Umstiegssuche bewusst v2, siehe 20-build-log).</summary>
public sealed record DirectConnection(
    string RouteId, int? DirectionId, string? Headsign,
    string BoardStopId, string AlightStopId,
    int RideSeconds, int StopsCount,
    IReadOnlyList<Departure> NextDepartures);

/// <summary>Kontroll-Warnung auf einer Fahrt — Anzeige MINDESTENS eine Haltestelle vorher (Auftrag 21.08.).</summary>
public sealed record ControlWarning(
    Guid ReportId, string AffectedStopId, int AffectedStopSequence,
    int? UserEtaSeconds, string Message)
{
    public const string MessageText = "Achtung Kontrolle – halte deine Fahrkarte bereit";
}

public sealed record JourneyPreview(
    string TripId, string StartDate, string RouteId, string? Headsign,
    IReadOnlyList<Departure> Departures,
    IReadOnlyList<ControlWarning> Warnings);
