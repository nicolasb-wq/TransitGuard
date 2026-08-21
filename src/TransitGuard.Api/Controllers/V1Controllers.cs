using TransitGuard.Core.Journeys;
using Microsoft.AspNetCore.Mvc;
using TransitGuard.Api.Contracts;
using TransitGuard.Api.Services;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Gate;
using TransitGuard.Core.Models;
using TransitGuard.Core.Trust;
using TransitGuard.Core.Ttl;

namespace TransitGuard.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/health/live")] public IActionResult Live() => Ok(new { status = "live" });
    [HttpGet("/health/ready")]
    public IActionResult Ready([FromServices] IFeatureFlagService flags)
        => flags.IsEnabled("reports_enabled") || flags.IsEnabled("alerts_enabled")
            ? Ok(new { status = "ready" })
            : StatusCode(503, new { status = "all_features_disabled" });
}

[ApiController]
public sealed class DevicesController(
    IDeviceAuthStore devices, ITrustStore trust, IDeviceRegistry registry) : ControllerBase
{
    [HttpPost("/v1/devices")]
    public IActionResult Issue()
    {
        var (id, token) = devices.Issue();
        trust.GetOrCreate(id);
        registry.MarkIssued(id);   // Trial-Start (ADR-0014): 14 Tage ab erster Nutzung
        return StatusCode(201, new { device_id = id, device_token = token });
    }

    [HttpGet("/v1/devices/me")]
    public IActionResult Me([FromServices] AccessGate access, [FromHeader(Name = "Authorization")] string? authorization)
    {
        if (HttpContext.Items["DeviceId"] is not Guid id) return Unauthorized();
        var state = trust.GetOrCreate(id);
        var acc = access.AccessFor(id, Bearer(authorization));
        return Ok(new
        {
            trust_score = state.Score, rank = RankName(state.Rank), total_reports = state.TotalReports,
            access = acc.Level.ToString().ToLowerInvariant(),                       // trial | subscriber | locked
            trial_days_remaining = acc.TrialDaysRemaining,
            subscription_price_eur = TransitGuard.Core.Entitlement.TrialPolicy.MonthlyPriceEur
        });
    }

    private static string? Bearer(string? authorization) =>
        authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? authorization[7..].Trim() : null;

    /// <summary>DSGVO-Selbstlöschung (docs/04): Trust weg, reporter_device_id in Meldungen wird anonymisiert.</summary>
    [HttpDelete("/v1/devices/me")]
    public IActionResult Delete()
    {
        if (HttpContext.Items["DeviceId"] is not Guid id) return Unauthorized();
        trust.Delete(id);   // Meldungen bleiben als anonymes Crowd-Wissen; Device-Referenz-Bereinigung übernimmt die Persistenzschicht (T-API, EF M4)
        return NoContent();
    }

    private static string RankName(TrustRank r) => r switch { TrustRank.Neuling => "neuling", TrustRank.Scout => "scout", TrustRank.Waechter => "waechter", _ => "legende" };
}

[ApiController]
public sealed class ReportsController(
    IReportRepository reports, IReportEventLog events, IRealtimeDispatcher dispatcher,
    IFeatureFlagService flags, ITrustStore trust, TicketGate gate, IClock clock,
    RateLimiter limiter, InMemoryIdempotencyStore idempotency, InMemoryTripWhitelistProvider whitelist,
    EntitlementContext tiers, AccessGate access, TtlEngine ttlEngine, IStopStore stops) : ControllerBase
{
    [HttpPost("/v1/reports")]
    public IActionResult Create([FromBody] ReportCreateRequest req, [FromHeader(Name = "Idempotency-Key")] string? idemKey,
        [FromHeader(Name = "Authorization")] string? authorization)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();

        // Ticket-First-Gate (H1) — VERTRAGLICH vor allen anderen Prüfungen, auch vor Idempotenz (T-GATE-1, Sweep-Fix Session 5)
        if (gate.CheckWrite(req.Client?.TicketConfirmed) is { Decision: TicketGateDecision.Block } blocked)
            return UnprocessableEntity(new ApiError { Body = new() { Code = blocked.ErrorCode!, Message = "Ticket-Bestätigung erforderlich" } });

        if (string.IsNullOrWhiteSpace(idemKey))
            return UnprocessableEntity(new ApiError { Body = new() { Code = "validation", Message = "Idempotency-Key erforderlich" } });

        if (idempotency.TryGet(idemKey) is { } cached)
            return StatusCode(201, Content(cached, "application/json").Content);   // gleiche Antwort inkl. Status (docs/04 §4)

        var trialAccess = access.AccessFor(deviceId, Bearer(authorization));   // ADR-0014
        if (trialAccess.IsLocked)
            return StatusCode(402, AccessGate.LockedBody(14 - trialAccess.TrialDaysRemaining));

        if (!flags.IsEnabled("reports_enabled"))
            return NotFound(new ApiError { Body = new() { Code = "feature_disabled", Message = "Kontroll-Feature deaktiviert" } });

        var city = CityRegistry.BySlug(req.CitySlug ?? "");
        if (city is null || !city.IsActive)
            return NotFound(new ApiError { Body = new() { Code = "city_inactive", Message = "Stadt nicht aktiv" } });

        if (!limiter.Allow("reports:create", deviceId))
            return StatusCode(429, new ApiError { Body = new() { Code = "rate_limited", Message = "Max. 5 Meldungen/10 min" } });

        // Validierung
        if (req.Station is null || string.IsNullOrEmpty(req.Station.StopId))
            return UnprocessableEntity(Err("validation", "station.stop_id erforderlich"));
        if (req.InspectorCount is < 1 or > 8)
            return UnprocessableEntity(Err("validation", "inspector_count 1..8"));

        var anchor = req.AnchorType?.ToLowerInvariant() switch
        {
            "station" => AnchorType.Station, "trip" => AnchorType.Trip, _ => (AnchorType?)null
        };
        if (anchor is null) return UnprocessableEntity(Err("validation", "anchor_type station|trip"));
        var kind = req.ReportType?.ToLowerInvariant() switch
        {
            "in_vehicle" => ReportKind.InVehicle, "on_platform" => ReportKind.OnPlatform, "at_stop" => ReportKind.AtStop, _ => (ReportKind?)null
        };
        if (kind is null) return UnprocessableEntity(Err("validation", "report_type ungültig"));
        var vKind = req.VehicleKind?.ToLowerInvariant() == "bus" ? VehicleKind.Bus : VehicleKind.Rail;

        DateTimeOffset? tripEndUtc = null;
        if (anchor == AnchorType.Trip)
        {
            if (req.Trip is null || string.IsNullOrEmpty(req.Trip.TripId) || string.IsNullOrEmpty(req.Trip.StartDate))
                return UnprocessableEntity(Err("validation", "trip.trip_id+start_date bei Trip-Anker erforderlich"));
            var wl = whitelist.GetWhitelist(city.CityId);
            if (!wl.TryGetValue(req.Trip.TripId, out var lookup) || lookup.EndTimeSeconds is null)
                return UnprocessableEntity(Err("trip_not_resolvable", "Fahrt nicht im aktiven Stadt-Extrakt (±Fenster)"));
            tripEndUtc = DayStart(req.Trip.StartDate).AddSeconds(lookup.EndTimeSeconds.Value);
            if (tripEndUtc.Value.AddSeconds(120) <= clock.UtcNow)
                return UnprocessableEntity(Err("trip_not_resolvable", "Fahrt bereits beendet — kein Trip-Anker möglich"));   // Sweep-Fund: sonst 'tot geborene' Meldungen
        }
        if (req.Station.Lat is { } lat && req.Station.Lon is { } lon && !city.BoundingBox.Contains(lat, lon))
            return UnprocessableEntity(Err("station_not_in_city", "GPS-Position außerhalb der Stadt-Box"));

        var state = trust.GetOrCreate(deviceId);
        var engine = ttlEngine;   // DI-Instanz: trägt TtlOptions (Dev-Override)
        var now = clock.UtcNow;
        var report = new Report
        {
            CreatedAt = now, CityId = city.CityId, AnchorType = anchor.Value,
            StationId = req.Station.StopId,
            // Anzeigename statt technischer ID (T-STOP): stand hier vorher als
            // StopId und erschien so wortwoertlich in der Meldeliste der App.
            StationName = StopLookup.DisplayName(stops.Stops(city.CityId), req.Station.StopId),
            TripId = req.Trip?.TripId, TripStartDate = req.Trip?.StartDate, RouteId = req.Trip?.RouteId,
            DirectionId = req.Trip?.DirectionId, Headsign = req.Trip?.Headsign,
            Kind = kind.Value, VehicleKind = vKind, InspectorCount = req.InspectorCount,
            ReporterDeviceId = deviceId, ReporterTrust = state.Score,
            Visible = TrustEngine.IsVisibleImmediately(state),
            GeoLat = req.Station.Lat, GeoLon = req.Station.Lon
        };
        var decision = engine.ComputeInitial(report, now, tripEndUtc);
        report.ExpiresAt = decision.ExpiresAt;

        reports.Add(report);
        var evtId = events.Append(new ReportEvent { CityId = report.CityId, ReportId = report.Id, Type = ReportEventType.Created, ActorDeviceId = deviceId, CreatedAt = now, Payload = new Dictionary<string, string>() });
        new TrustEngine().OnReportCreated(state);
        trust.Save(state);   // EF-Modus: persistieren (Sweep-Fund: Mutation an gelöstem Objekt ging sonst verloren)
        reports.SaveChanges();
        dispatcher.DispatchCityReportEvent(report.CityId,
            new ReportEvent { EventId = evtId, CityId = report.CityId, ReportId = report.Id, Type = ReportEventType.Created, CreatedAt = now },
            new { id = evtId, reportId = report.Id, type = "created" },
            new { id = evtId, reportId = report.Id, type = "created" });

        var tier = tiers.TierFor(Bearer(authorization));
        var jsonOpts = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var body = System.Text.Json.JsonSerializer.Serialize(tier == EntitlementTier.Pro
            ? ReportDtoFormer.ToPro(report, city.Slug, RankName(trust.GetOrCreate(deviceId).Rank))
            : (object)ReportDtoFormer.ToFree(report, city.Slug), jsonOpts);
        idempotency.Store(idemKey, body);
        return StatusCode(201, Content(body, "application/json").Content);
    }

    [HttpGet("/v1/cities/{slug}/reports")]
    public IActionResult List(string slug, [FromHeader(Name = "X-Ticket-Confirmed")] bool ticketConfirmed = false,
        [FromHeader(Name = "Authorization")] string? authorization = null)
    {
        if (HttpContext.Items["DeviceId"] is not Guid) return Unauthorized();
        var city = CityRegistry.BySlug(slug);
        if (city is null || !city.IsActive) return NotFound(Err("city_inactive", "Stadt nicht aktiv"));
        if (gate.CheckRead(ticketConfirmed) is { Decision: TicketGateDecision.Block } b)   // T-GATE-2
            return StatusCode(403, Err(b.ErrorCode!, "Kontroll-Hinweise nur mit Ticket-Bestätigung (docs/18)"));
        if (!flags.IsEnabled("reports_enabled")) return NotFound(Err("feature_disabled", "deaktiviert"));

        var tier = tiers.TierFor(Bearer(authorization));
        var list = reports.ListActive(city.CityId)
            .Select(r => tier == EntitlementTier.Pro
                ? (object)ReportDtoFormer.ToPro(r, city.Slug, RankName(trust.GetOrCreate(r.ReporterDeviceId).Rank))
                : ReportDtoFormer.ToFree(r, city.Slug))
            .ToList();
        return Ok(list);
    }

    [HttpPost("/v1/reports/{id}/events")]
    public IActionResult AddEvent(Guid id, [FromBody] ReportEventRequest body)
    {
        if (HttpContext.Items["DeviceId"] is not Guid deviceId) return Unauthorized();
        var r = reports.Find(id);
        if (r is null || r.Status != ReportStatus.Active) return NotFound(Err("validation", "Meldung nicht (mehr) aktiv"));
        if (!limiter.Allow("reports:events", deviceId)) return StatusCode(429, Err("rate_limited", "20 Events/10 min"));
        if (r.ReporterDeviceId == deviceId) return UnprocessableEntity(Err("validation", "Eigene Meldung"));

        var engine = ttlEngine;   // DI-Instanz: trägt TtlOptions (Dev-Override)
        var now = clock.UtcNow;
        if (body.Type == "confirm")
        {
            r.Confirmations++;
            r.ExpiresAt = engine.ApplyConfirmation(r, r.ExpiresAt);
            r.Visible = true;   // Bestätigung hebt Troll-Gate auf
            events.Append(new ReportEvent { CityId = r.CityId, ReportId = r.Id, Type = ReportEventType.Confirmed, ActorDeviceId = deviceId, CreatedAt = now, Payload = new Dictionary<string, string>() });
        }
        else if (body.Type == "contradict")
        {
            r.Contradictions++;
            var actorTrust = trust.GetOrCreate(deviceId);
            var (newExpiry, outcome) = engine.ApplyContradiction(r, actorTrust.Score, now);
            r.ExpiresAt = newExpiry;
            events.Append(new ReportEvent { CityId = r.CityId, ReportId = r.Id, Type = ReportEventType.Contradicted, ActorDeviceId = deviceId, CreatedAt = now, Payload = new Dictionary<string, string> { ["outcome"] = outcome.ToString() } });
        }
        else return UnprocessableEntity(Err("validation", "type confirm|contradict"));

        reports.SaveChanges();
        return NoContent();
    }

    private static DateTimeOffset DayStart(string yyyymmdd) =>
        DateTimeOffset.TryParseExact(yyyymmdd, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d : throw new InvalidOperationException("start_date Format YYYYMMDD");

    private static string? Bearer(string? authorization) =>
        authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? authorization[7..].Trim() : null;

    private static ApiError Err(string code, string msg) => new() { Body = new() { Code = code, Message = msg } };

    private static string RankName(TrustRank r) => r switch { TrustRank.Neuling => "neuling", TrustRank.Scout => "scout", TrustRank.Waechter => "waechter", _ => "legende" };
}

public sealed class ReportEventRequest { public string Type { get; set; } = default!; }

[ApiController]
public sealed class MiscController(IReportEventLog events) : ControllerBase
{
    [HttpGet("/v1/cities")] public IActionResult Cities() => Ok(CityRegistry.All.Values.Select(c => new { c.Slug, c.DisplayName, c.IsActive }));

    [HttpGet("/v1/cities/{slug}/events")]
    public IActionResult Events(string slug, [FromQuery] long since_id = 0, [FromQuery] int limit = 200,
        [FromHeader(Name = "X-Ticket-Confirmed")] bool ticketConfirmed = false)
    {
        if (HttpContext.Items["DeviceId"] is not Guid) return Unauthorized();
        if (HttpContext.RequestServices.GetRequiredService<TicketGate>()
                .CheckRead(ticketConfirmed) is { Decision: Core.Gate.TicketGateDecision.Block })
            return StatusCode(403, new ApiError { Body = new() { Code = "ticket_gate_blocked", Message = "docs/18 H1 — gilt auch für den Event-Feed" } });
        var city = CityRegistry.BySlug(slug);
        if (city is null) return NotFound();
        var list = events.Since(city.CityId, since_id, Math.Clamp(limit, 1, 1000))
            .Select(e => new { event_id = e.EventId, report_id = e.ReportId, type = e.Type.ToString().ToLowerInvariant(), created_at = e.CreatedAt })
            .ToList();
        return Ok(list);
    }
}

[ApiController]
public sealed class BillingController(IEntitlementStore store, RateLimiter limiter) : ControllerBase
{
    [HttpPost("/v1/billing/activate")]
    public IActionResult Activate([FromBody] ActivateRequest req)
    {
        if (req.Platform is not ("apple" or "google" or "web")) return UnprocessableEntity(new ApiError { Body = new() { Code = "validation", Message = "platform apple|google|web" } });   // web = PWA-Direktabo (Zahlungsdienstleister M8, F-12)
        var rec = store.Activate(req.Platform, req.Receipt ?? "", out var token, out var code, out var error);
        if (rec is null || error is not null)
            return error == "receipt_already_used" ? Conflict(new ApiError { Body = new() { Code = error, Message = "Receipt bereits benutzt" } })
                : UnprocessableEntity(new ApiError { Body = new() { Code = error ?? "receipt_invalid", Message = "Receipt ungültig" } });
        return StatusCode(201, new { access_token = token, restore_code = code, tier = rec.Tier.ToString().ToLowerInvariant(), valid_until = rec.ValidUntil });
    }

    [HttpPost("/v1/billing/restore")]
    public IActionResult Restore([FromBody] RestoreRequest req)
    {
        // Fehlendes Geraet und Ratenbegrenzung sind zwei verschiedene Dinge und
        // brauchen zwei verschiedene Antworten — zusammengeworfen verschleiern sie
        // die Ursache (T-BILL-RESTORE-3).
        if (HttpContext.Items["DeviceId"] is not Guid id)
            return Unauthorized(new ApiError { Body = new() { Code = "unauthorized", Message = "X-Device-Token erforderlich (POST /v1/devices)" } }.Body);
        if (!limiter.Allow("billing:restore", id))
            return StatusCode(429, new ApiError { Body = new() { Code = "rate_limited", Message = "5 Versuche/h" } });
        var rec = store.Restore(req.RestoreCode ?? "", out var token, out var error);
        if (rec is null) return StatusCode(403, new ApiError { Body = new() { Code = error!, Message = "Code ungültig" } });
        return StatusCode(201, new { access_token = token, tier = rec.Tier.ToString().ToLowerInvariant() });
    }

    [HttpPost("/v1/billing/validate")]
    public IActionResult Validate([FromBody] ValidateRequest req)
    {
        var rec = store.Validate(req.Token ?? "");
        if (rec is null) return StatusCode(403, new ApiError { Body = new() { Code = "invalid_token", Message = "Token unbekannt" } });
        return Ok(new { tier = rec.Tier.ToString().ToLowerInvariant(), status = rec.Status, valid_until = rec.ValidUntil });
    }
}

public sealed class ActivateRequest { public string? Platform { get; set; } public string? Receipt { get; set; } }
public sealed class RestoreRequest { public string? RestoreCode { get; set; } }
public sealed class ValidateRequest { public string? Token { get; set; } }
