using Microsoft.Extensions.Logging;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Models;
using TransitGuard.Core.Ttl;

namespace TransitGuard.Ingest.Jobs;

/// <summary>
/// TtlSweepService (15 s, docs/07 §1): wertet alle aktiven Meldungen über die TtlEngine aus.
/// Feed-Gesundheit wird von außen injiziert (G11-Gate, B1) — ein kranker Feed friert
/// Trip-Degradationen ein, statt die Karte zu leeren.
/// </summary>
public sealed class TtlSweepService(
    IReportRepository reports,
    IReportEventLog events,
    IRealtimeDispatcher dispatcher,
    ITripStateStore tripState,
    TtlEngine ttlEngine,
    IClock clock,
    ILogger<TtlSweepService>? logger = null)
{
    public int Sweep(bool feedHealthy, Func<Report, bool>? tripEnded = null, Func<Report, bool>? canceledTrip = null)
    {
        int actions = 0;
        foreach (var r in reports.All().Where(r => r.Status == ReportStatus.Active).ToArray())
        {
            bool ended = tripEnded?.Invoke(r) ?? DefaultTripEnded(r);
            bool canceled = canceledTrip?.Invoke(r) ?? false;

            var action = ttlEngine.Evaluate(r, clock.UtcNow, ended, canceled, feedHealthy);
            switch (action.Kind)
            {
                case ExpiryActionKind.Expire:
                    r.Status = ReportStatus.Expired;
                    Append(r, ReportEventType.Expired, action.Reason);
                    actions++; break;
                case ExpiryActionKind.ExpireWithDerivedTerminus:
                    if (action.DerivedReport is { } derived)
                    {
                        r.Status = ReportStatus.Expired;
                        Append(r, ReportEventType.Expired, action.Reason);
                        reports.Add(derived);
                        Append(derived, ReportEventType.Degraded, "derived_terminus_verbleib_unbekannt");
                        dispatcher.DispatchControl(r.CityId, "report.degraded", new { reportId = r.Id, derivedId = derived.Id });
                    }
                    actions++; break;
                case ExpiryActionKind.DegradeToStation:
                    r.Status = ReportStatus.Degraded;   // Station-Anker mit Rest-TTL: Feld bleibt, Anchor-Wechsel in Persistenz (C.3.2)
                    Append(r, ReportEventType.Degraded, action.Reason);
                    actions++; break;
                case ExpiryActionKind.Freeze:
                    // bewusst keine Statusänderung (B1); Freeze via Metadaten vermerken
                    if (!r.Metadata.ContainsKey("frozen")) { /* Metadaten read-only im Domänenmodell — Freeze vermerkt der Job in DB */ }
                    break;
                case ExpiryActionKind.None:
                default: break;
            }
        }
        reports.SaveChanges();
        if (actions > 0) logger?.LogInformation("TTL-Sweep: {Actions} Aktionen (feedHealthy={Health})", actions, feedHealthy);
        return actions;
    }

    private bool DefaultTripEnded(Report r)
    {
        if (r.AnchorType != AnchorType.Trip || r.TripId is null) return false;
        var state = tripState.Get(r.CityId, r.TripId, r.TripStartDate ?? string.Empty);
        // Vereinfachung v1: Trip-Ende über ExpiresAt-Logik der Engine (trip_end+120 s);
        // echte Interpolation (TripInterpolator gegen gtfs_stop_times) hängt T4.6 an.
        return state is null && clock.UtcNow >= r.ExpiresAt;
    }

    private void Append(Report r, ReportEventType type, string reason)
    {
        var id = events.Append(new ReportEvent
        {
            CityId = r.CityId, ReportId = r.Id, Type = type, CreatedAt = clock.UtcNow,
            Payload = new Dictionary<string, string> { ["reason"] = reason }
        });
        dispatcher.DispatchCityReportEvent(r.CityId, new ReportEvent { EventId = id, CityId = r.CityId, ReportId = r.Id, Type = type, CreatedAt = clock.UtcNow, Payload = new Dictionary<string, string> { ["reason"] = reason } },
            new { id, reportId = r.Id, type = type.ToString().ToLowerInvariant() },
            new { id, reportId = r.Id, type = type.ToString().ToLowerInvariant(), reason });
    }
}
