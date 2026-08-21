using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Models;

namespace TransitGuard.Core.Journeys;

/// <summary>
/// Fahrtensuche/Abfahrten/Kontroll-Warnungen — reine Logik auf Stadt-Extrakt (TripSchedule) + RT-State.
/// „Basierend auf Start und Ziel die Linie finden, dann nächste Abfahrt sehen" (Auftrag 21.08.).
/// v1: Direktverbindungen (gleiche Fahrt berührt Start UND Ziel in richtiger Reihenfolge);
/// Umstiegsverbindungen ausdrücklich v2 (Aufwand eines echten Routers, siehe 20-build-log).
/// </summary>
public sealed class JourneyService(ITripScheduleStore schedules, IClock clock)
{
    private static DateTimeOffset DayStartFor(string startDate) =>
        DateTimeOffset.TryParseExact(startDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d : throw new InvalidOperationException("start_date YYYYMMDD");

    /// <summary>Alle Direktverbindungen Start→Ziel, gruppiert nach Linie/Richtung, mit den nächsten Abfahrten.</summary>
    public IReadOnlyList<DirectConnection> FindDirectConnections(string cityId, string startDate,
        string fromStopId, string toStopId, ITripStateStore rtState, int departuresPerLine = 3)
    {
        var dayStart = DayStartFor(startDate);
        var now = clock.UtcNow;
        var result = new List<DirectConnection>();

        foreach (var (tripId, schedule) in schedules.All(cityId))
        {
            var iFrom = schedule.IndexOf(fromStopId);
            if (iFrom is null) continue;
            var iTo = schedule.IndexOf(toStopId);
            if (iTo is null || iTo <= iFrom) continue;   // Ziel muss nach Start liegen (Richtung)

            var deps = NextDeparturesForTrip(cityId, startDate, schedule, iFrom.Value, rtState, dayStart, now);
            var grouped = result.FirstOrDefault(r => r.RouteId == schedule.RouteId && r.DirectionId == schedule.DirectionId && r.Headsign == schedule.Headsign);
            if (grouped is null)
            {
                result.Add(new DirectConnection(schedule.RouteId, schedule.DirectionId, schedule.Headsign,
                    fromStopId, toStopId,
                    schedule.Stops[iTo.Value].ArrivalS - schedule.Stops[iFrom.Value].DepartureS,
                    iTo.Value - iFrom.Value,
                    deps.Take(departuresPerLine).ToList()));
            }
            else if (deps.Count > 0)
            {
                var merged = grouped.NextDepartures.Concat(deps).OrderBy(d => d.ScheduledAt).Take(departuresPerLine).ToList();
                result[result.IndexOf(grouped)] = grouped with { NextDepartures = merged };
            }
        }
        return result.OrderByDescending(c => c.NextDepartures.Count).ThenBy(c => c.RouteId).ToList();
    }

    /// <summary>Abfahrtsliste einer Haltestelle (Soll + Ist aus rt_trip_state-Delta).</summary>
    public IReadOnlyList<Departure> DeparturesFrom(string cityId, string startDate, string stopId,
        ITripStateStore rtState, int limit = 20)
    {
        var dayStart = DayStartFor(startDate);
        var now = clock.UtcNow;
        var list = new List<Departure>();
        foreach (var (tripId, schedule) in schedules.All(cityId))
        {
            var idx = schedule.IndexOf(stopId);
            if (idx is null) continue;
            list.AddRange(NextDeparturesForTrip(cityId, startDate, schedule, idx.Value, rtState, dayStart, now, maxPerTrip: 1));
        }
        return list.OrderBy(d => d.EstimatedAt ?? d.ScheduledAt).Take(limit).ToList();
    }

    /// <summary>
    /// Kontroll-Warnungen für eine konkrete Fahrt ab Einstiegshaltestelle: aktive Meldungen auf
    /// (a) derselben Fahrt (trip_id+start_date) oder (b) derselben Linie+Richtung, deren Position
    /// (LastKnownStationId) noch vor dem Nutzer liegt. Regel: Anzeige, sobald die betroffene
    /// Haltestelle DIE NÄCHSTE ist (also mindestens eine Haltestelle vorher).
    /// </summary>
    public IReadOnlyList<ControlWarning> WarningsForJourney(
        string cityId, string tripId, string startDate, string fromStopId,
        IReadOnlyList<Report> activeReports, ITripStateStore rtState)
    {
        if (!schedules.TryGet(cityId, tripId, out var schedule)) return Array.Empty<ControlWarning>();
        var iFrom = schedule.IndexOf(fromStopId) ?? 0;

        var warnings = new List<ControlWarning>();
        foreach (var r in activeReports)
        {
            if (r.Status != ReportStatus.Active || r.ExpiresAt <= clock.UtcNow) continue;

            bool sameTrip = r.AnchorType == AnchorType.Trip && r.TripId == tripId && r.TripStartDate == startDate;
            bool sameRoute = r.RouteId is not null && r.RouteId == schedule.RouteId
                && (r.DirectionId ?? schedule.DirectionId) == schedule.DirectionId
                && r.AnchorType != AnchorType.Trip;   // Linien-/Stations-Anker

            if (!sameTrip && !sameRoute) continue;
            var affectedStop = sameTrip
                ? (r.LastKnownStationId ?? r.StationId)
                : r.StationId;
            if (string.IsNullOrEmpty(affectedStop)) continue;

            var idx = schedule.IndexOf(affectedStop);
            if (idx is null || idx <= iFrom) continue;   // hinter dem Nutzer oder an/vor Einstieg: kein Vorwarn-Fall

            // ETA des Nutzers an der betroffenen Haltestelle: Sekunden AB JETZT (Soll+Delay)
            var rt = rtState.Get(cityId, tripId, startDate);
            var arrivalUtc = DayStartFor(startDate)
                .AddSeconds(schedule.Stops[idx.Value].ArrivalS + (rt?.LastDelaySeconds ?? 0));
            var etaFromNow = (int)Math.Max(0, (arrivalUtc - clock.UtcNow).TotalSeconds);

            warnings.Add(new ControlWarning(r.Id, affectedStop, schedule.Stops[idx.Value].Sequence,
                etaFromNow, ControlWarning.MessageText));
        }
        return warnings.OrderBy(w => w.AffectedStopSequence).ToList();
    }

    private List<Departure> NextDeparturesForTrip(string cityId, string startDate, TripSchedule schedule, int stopIdx,
        ITripStateStore rtState, DateTimeOffset dayStart, DateTimeOffset now, int? maxPerTrip = null)
    {
        var rt = rtState.Get(cityId, schedule.TripId, startDate);
        int? delay = rt?.LastDelaySeconds;
        var scheduled = dayStart.AddSeconds(schedule.Stops[stopIdx].DepartureS);
        DateTimeOffset? estimated = delay is null ? null : scheduled.AddSeconds(delay.Value);
        // Fahrten der Vergangenheit ausblenden (Toleranz 60 s), außer RT sagt: fährt noch
        if ((estimated ?? scheduled) < now.AddSeconds(-60)) return new();
        var dep = new Departure(schedule.TripId, startDate, schedule.RouteId, schedule.Headsign,
            scheduled, estimated, delay, rt is { HasDelayData: true });
        return new List<Departure> { dep };
    }
}
