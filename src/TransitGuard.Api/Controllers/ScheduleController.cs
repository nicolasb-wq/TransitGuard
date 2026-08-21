using Microsoft.AspNetCore.Mvc;
using TransitGuard.Api.Services;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Gate;
using TransitGuard.Core.Journeys;
using TransitGuard.Core.Models;

namespace TransitGuard.Api.Controllers;

/// <summary>Companion-Kern: Haltestellen (Auto-Start per Standort), Abfahrten, Fahrtensuche, Alerts.</summary>
[ApiController]
public sealed class ScheduleController(
    IStopStore stops, ITripStateStore rtState, ITripScheduleStore schedules, IReportRepository reports,
    AccessGate access, TicketGate gate, IClock clock, Core.Journeys.TransferRouter transferRouter) : ControllerBase
{
    [HttpGet("/v1/cities/{slug}/stops/nearby")]
    public IActionResult Nearby(string slug, [FromQuery] double? lat, [FromQuery] double? lon,
        [FromQuery] string? q, [FromQuery] int take = 5)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();
        if (Locked(deviceId, out var locked)) return locked;
        var city = CityRegistry.BySlug(slug);
        if (city is null || !city.IsActive) return NotFound();
        var all = stops.Stops(city.CityId);
        if (!string.IsNullOrWhiteSpace(q))   // Zielsuche nach Name (ohne Standort)
            return Ok(all.Where(s => s.StopName.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Clamp(take, 1, 20))
                .Select(s => new { s.StopId, s.StopName, s.Lat, s.Lon }));
        if (lat is not { } la || lon is not { } lo)
            return UnprocessableEntity(new { error = new { code = "validation", message = "lat+lon oder q erforderlich" } });
        var result = GeoMath.Nearest(all, la, lo, Math.Clamp(take, 1, 20))
            .Select(x => new { x.Stop.StopId, x.Stop.StopName, x.Stop.Lat, x.Stop.Lon, distance_km = Math.Round(x.Km, 3) });
        return Ok(result);
    }

    [HttpGet("/v1/stops/{stopId}/departures")]
    public IActionResult Departures(string stopId, [FromQuery] string? date, [FromQuery] int limit = 20)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();
        if (Locked(deviceId, out var locked)) return locked;
        var city = CityRegistry.All.Values.FirstOrDefault(c => c.IsActive);
        if (city is null) return NotFound();
        var svc = new JourneyService(schedules, clock);
        var today = date ?? clock.UtcNow.ToString("yyyyMMdd");
        var deps = svc.DeparturesFrom(city.CityId, today, stopId, rtState, Math.Clamp(limit, 1, 50));
        return Ok(deps.Select(d => new
        {
            trip_ref = new { trip_id = d.TripId, start_date = d.StartDate },
            route_id = d.RouteId, headsign = d.Headsign,
            scheduled_time = d.ScheduledAt, estimated_time = d.EstimatedAt,
            delay_s = d.DelaySeconds, realtime = d.Realtime,
            warnings = (d.Warnings ?? Array.Empty<Core.Journeys.ControlWarning>())
                .Select(w => new { w.AffectedStopId, w.AffectedStopSequence, w.UserEtaSeconds, w.Message })
        }));
    }

    /// <summary>Fahrtensuche Start→Ziel: Direktlinien + nächste Abfahrten (Start optional per lat/lon automatisch).</summary>
    [HttpPost("/v1/journeys/search")]
    public IActionResult Search([FromBody] JourneySearchRequest req)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();
        if (Locked(deviceId, out var locked)) return locked;
        var city = CityRegistry.All.Values.FirstOrDefault(c => c.IsActive);
        if (city is null) return NotFound();

        var from = req.FromStopId;
        if (string.IsNullOrEmpty(from) && req.FromLat is { } flat && req.FromLon is { } flon)
            from = GeoMath.Nearest(stops.Stops(city.CityId), flat, flon, 1).FirstOrDefault().Stop?.StopId;   // Standort-Autofill
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(req.ToStopId))
            return UnprocessableEntity(new { error = new { code = "validation", message = "from (stop_id oder lat/lon) und to_stop_id erforderlich" } });
        if (from == req.ToStopId)
            return UnprocessableEntity(new { error = new { code = "validation", message = "Start und Ziel identisch" } });

        var svc = new JourneyService(schedules, clock);
        var today = clock.UtcNow.ToString("yyyyMMdd");
        var connections = svc.FindDirectConnections(city.CityId, today, from, req.ToStopId, rtState);

        // Kontroll-Warnungen je Fahrt (Anzeige mindestens 1 Haltestelle vorher — Auftrag 21.08.)
        var active = reports.ListActive(city.CityId).ToList();
        foreach (var c in connections)
            foreach (var dep in c.NextDepartures)
                dep.Warnings = svc.WarningsForJourney(city.CityId, dep.TripId, dep.StartDate, from, active, rtState);

        // Umstiegsverbindungen (v2, TransferRouter) — nur wenn keine/wenige Direkte
        var transfers = connections.Count >= 3
            ? Array.Empty<Core.Journeys.TransferRouter.TransferConnection>()
            : transferRouter.FindWithOneTransfer(city.CityId, today, from, req.ToStopId, clock.UtcNow).ToArray();

        return Ok(new
        {
            from_stop_id = from,
            to_stop_id = req.ToStopId,
            direct_connections = connections.Select(c => new
            {
                route_id = c.RouteId, direction_id = c.DirectionId, headsign = c.Headsign,
                ride_seconds = c.RideSeconds, stops_count = c.StopsCount,
                next_departures = c.NextDepartures.Select(DepartureDto)
            }),
            transfer_connections = transfers.Select(tr => new
            {
                total_seconds = tr.TotalSeconds, transfer_stop_id = tr.TransferStopId, wait_seconds = tr.WaitSeconds,
                leg_a = new { tr.LegA.RouteId, tr.LegA.Headsign, board_stop = tr.LegA.BoardStopId, alight_stop = tr.LegA.AlightStopId, board_at = tr.LegA.BoardAt, alight_at = tr.LegA.AlightAt },
                leg_b = new { tr.LegB.RouteId, tr.LegB.Headsign, board_stop = tr.LegB.BoardStopId, alight_stop = tr.LegB.AlightStopId, board_at = tr.LegB.BoardAt, alight_at = tr.LegB.AlightAt }
            })
        });
    }

    [HttpGet("/v1/journeys/{tripId}/warnings")]
    public IActionResult Warnings(string tripId, [FromQuery] string start_date, [FromQuery] string from_stop_id)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();
        if (gate.CheckRead(HttpContext.Request.Headers.ContainsKey("X-Ticket-Confirmed")) is { Decision: TicketGateDecision.Block })
            return StatusCode(403, new { error = new { code = "ticket_gate_blocked", message = "docs/18 H1" } });
        if (Locked(deviceId, out var locked)) return locked;
        var city = CityRegistry.All.Values.FirstOrDefault(c => c.IsActive);
        if (city is null) return NotFound();
        var svc = new JourneyService(schedules, clock);
        var active = reports.ListActive(city.CityId).ToList();
        var warnings = svc.WarningsForJourney(city.CityId, tripId, start_date ?? clock.UtcNow.ToString("yyyyMMdd"), from_stop_id ?? "", active, rtState);
        return Ok(warnings.Select(w => new { w.AffectedStopId, w.AffectedStopSequence, w.UserEtaSeconds, w.Message, w.ReportId }));
    }

    [HttpGet("/v1/cities/{slug}/alerts")]
    public IActionResult Alerts(string slug)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();
        if (Locked(deviceId, out var locked)) return locked;
        var city = CityRegistry.BySlug(slug);
        if (city is null) return NotFound();
        var store = HttpContext.RequestServices.GetRequiredService<IAlertStore>();
        return Ok(store.Visible(city.CityId, clock.UtcNow).Select(a => new
        {
            header = a.HeaderText, description = a.DescriptionText, url = a.Url,
            severity = a.Severity, route_refs = a.InformedTripIds
        }));
    }

    private static object DepartureDto(Core.Journeys.Departure d) => new
    {
        trip_ref = new { trip_id = d.TripId, start_date = d.StartDate },
        scheduled_time = d.ScheduledAt, estimated_time = d.EstimatedAt,
        delay_s = d.DelaySeconds, realtime = d.Realtime,
        warnings = (d.Warnings ?? Array.Empty<Core.Journeys.ControlWarning>())
            .Select(w => new { w.AffectedStopId, w.AffectedStopSequence, w.UserEtaSeconds, w.Message })
    };

    private bool Locked(Guid deviceId, out IActionResult result)
    {
        var acc = access.AccessFor(deviceId, Bearer());
        result = acc.IsLocked ? StatusCode(402, AccessGate.LockedBody(14 - acc.TrialDaysRemaining)) : null!;
        return acc.IsLocked;
    }

    private string? Bearer() =>
        Request.Headers.TryGetValue("Authorization", out var a) && a.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? a.ToString()[7..].Trim() : null;
}

public sealed class JourneySearchRequest
{
    public string? FromStopId { get; set; }
    public double? FromLat { get; set; }
    public double? FromLon { get; set; }
    public string? ToStopId { get; set; }
}
