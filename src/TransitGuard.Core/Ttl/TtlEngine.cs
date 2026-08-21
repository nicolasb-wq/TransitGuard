using TransitGuard.Core.Models;

namespace TransitGuard.Core.Ttl;

public sealed record TtlDecision(DateTimeOffset ExpiresAt, int TtlBaseSeconds, TtlProfile Profile);

public enum ContradictionOutcome { GraceApplied, ReviewFlagOnly }

public enum ExpiryActionKind { None, Expire, ExpireWithDerivedTerminus, DegradeToStation, Freeze }

public sealed record ExpiryAction(ExpiryActionKind Kind, Report? DerivedReport = null, string Reason = "");

/// <summary>
/// TTL-Engine — deterministischer Regelkern (ADR-0007). Reine Funktionen, Serverzeit autoritativ,
/// tabellengetrieben testbar (Testplan T-TTL-1…12). Gleiche Eingaben ⇒ gleiches Ergebnis.
/// Regeln: v1.1 B.5 + Bestandsaufnahme B1 (G11-Gate), B3, §5.2 (F-16), F-15.
/// </summary>
public sealed class TtlEngine(TtlOptions? options = null)
{
    private TtlProfile P(Report r) => options?.DevOverrideSeconds is { } o and > 0
        ? new ConfigurableTtlProfile(TtlProfiles.For(r.AnchorType, r.VehicleKind, r.Kind), o)
        : TtlProfiles.For(r.AnchorType, r.VehicleKind, r.Kind);

    /// <summary>Initial-Expiry. Trip-Anker enden natürlich: trip_end + Buffer (kein 45-min-Cap, F-16).</summary>
    public TtlDecision ComputeInitial(Report r, DateTimeOffset now, DateTimeOffset? tripEndUtc)
    {
        var profile = P(r);
        if (profile.BaseSeconds is { } baseS)
            return new TtlDecision(now.AddSeconds(baseS), baseS, profile);

        if (r.AnchorType == Models.AnchorType.Trip)
        {
            if (tripEndUtc is null)
                throw new ArgumentException("Trip-Anker benötigt tripEndUtc (Interpolation)", nameof(tripEndUtc));
            var exp = tripEndUtc.Value.AddSeconds(profile.TripEndBufferSeconds);
            var ttl = (int)Math.Max(0, (exp - now).TotalSeconds);
            return new TtlDecision(exp, ttl, profile);
        }
        throw new ArgumentOutOfRangeException(nameof(r.AnchorType));
    }

    /// <summary>
    /// Bestätigungen verlängern NUR Stations-/at_stop-Anker (max. +ConfirmMaxExtra ab Base, dann Cap).
    /// Trip-Anker: keine Verlängerung (natürliches Ende). T-TTL-2.
    /// </summary>
    public DateTimeOffset ApplyConfirmation(Report r, DateTimeOffset currentExpiry)
    {
        var profile = P(r);
        if (profile.ConfirmBonusSeconds <= 0 || r.Confirmations <= 0) return currentExpiry;

        var extra = Math.Min(r.Confirmations * profile.ConfirmBonusSeconds, profile.ConfirmMaxExtraSeconds);
        var target = r.CreatedAt.AddSeconds(profile.BaseSeconds!.Value + extra);
        if (profile.CapSeconds is { } cap) target = target <= r.CreatedAt.AddSeconds(cap) ? target : r.CreatedAt.AddSeconds(cap);
        return target > currentExpiry ? target : currentExpiry;   // Bestätigung verkürzt nie
    }

    /// <summary>
    /// Widerspruch mit Parität (Actor-Trust ≥ Reporter-Trust) oder ab 2 Widersprüchen ⇒ Grace (now+120 s);
    /// sonst nur Review-Flag ohne TTL-Wirkung (B6). T-TTL-3/4/5.
    /// </summary>
    public (DateTimeOffset NewExpiry, ContradictionOutcome Outcome) ApplyContradiction(
        Report r, decimal actorTrust, DateTimeOffset now)
    {
        var profile = P(r);
        if (actorTrust >= r.ReporterTrust || r.Contradictions >= 2)
            return (now.AddSeconds(profile.ContradictionGraceSeconds), ContradictionOutcome.GraceApplied);
        return (r.ExpiresAt, ContradictionOutcome.ReviewFlagOnly);
    }

    /// <summary>
    /// Zentrale Auswertung pro Sweep-Tick (TtlSweepJob 15 s). T-TTL-7/8/10/12.
    /// </summary>
    /// <param name="tripEnded">Interpolation hat trip_end + Buffer erreicht (nur Trip-Anker relevant).</param>
    /// <param name="canceledInFeed">RT meldet schedule_relationship=CANCELED.</param>
    /// <param name="feedHealthy">G11-Gate: bei ungesundem Feed KEINE Degradation, nur Freeze (B1).</param>
    /// <param name="tripDisappearedFromStatic">C.3.2 Swap: trip_id nicht mehr im Static.</param>
    public ExpiryAction Evaluate(
        Report r, DateTimeOffset now,
        bool tripEnded = false, bool canceledInFeed = false,
        bool feedHealthy = true, bool tripDisappearedFromStatic = false)
    {
        // Zukunftsdatierte Ereignisse ignorieren — Serverzeit autoritativ (T-TTL-12)
        if (r.CreatedAt > now) return new ExpiryAction(ExpiryActionKind.None, Reason: "created_in_future_ignored");

        if (canceledInFeed)
            return new ExpiryAction(ExpiryActionKind.Expire, Reason: "canceled");   // sofort, OHNE Derived (T-TTL-8)

        if (tripDisappearedFromStatic)
            return feedHealthy
                ? new ExpiryAction(ExpiryActionKind.DegradeToStation, Reason: "static_swap_trip_gone")
                : new ExpiryAction(ExpiryActionKind.Freeze, Reason: "g11_feed_unhealthy_no_degradation"); // B1/T-TTL-10

        if (r.AnchorType == Models.AnchorType.Trip && tripEnded)
        {
            if (!feedHealthy) return new ExpiryAction(ExpiryActionKind.Freeze, Reason: "g11_feed_unhealthy_frozen");
            return new ExpiryAction(ExpiryActionKind.ExpireWithDerivedTerminus, CreateDerivedTerminus(r, now), "trip_end");
        }

        if (now >= r.ExpiresAt)
        {
            return r.Origin == ReportOrigin.User && r.AnchorType == Models.AnchorType.Trip
                ? new ExpiryAction(ExpiryActionKind.ExpireWithDerivedTerminus, CreateDerivedTerminus(r, now), "expired_trip")
                : new ExpiryAction(ExpiryActionKind.Expire, Reason: "expired");
        }
        return new ExpiryAction(ExpiryActionKind.None);
    }

    /// <summary>
    /// B.5: abgeleitete Endstations-Meldung — TTL 600 s fix, Trust 0,7×, Kennzeichnung „Verbleib unbekannt",
    /// KEINE Verkettung auf Rücktrip (v1.1 B.5 Nr. 3). T-TTL-7.
    /// </summary>
    public Report CreateDerivedTerminus(Report r, DateTimeOffset now)
    {
        var terminusStation = r.LastKnownStationId ?? r.StationId
            ?? throw new InvalidOperationException("Derived-Terminus ohne bekannte Station nicht möglich (Interpolation muss LastKnownStationId setzen)");
        return new Report
        {
            CityId = r.CityId,
            AnchorType = Models.AnchorType.DerivedTerminus,
            StationId = terminusStation,
            StationName = r.StationName,
            RouteId = r.RouteId,
            DirectionId = r.DirectionId,
            Headsign = r.Headsign,
            Kind = r.Kind,
            VehicleKind = r.VehicleKind,
            InspectorCount = r.InspectorCount,
            ReporterDeviceId = r.ReporterDeviceId,
            ReporterTrust = Math.Round(r.ReporterTrust * 0.7m, 2),   // gedämpfte Konfidenz
            ExpiresAt = now.AddSeconds(TtlProfiles.DerivedTerminusTtl),
            TtlBaseSeconds = TtlProfiles.DerivedTerminusTtl,
            ParentReportId = r.Id,
            Origin = ReportOrigin.Derived,
            Visible = true
        };
    }
}
