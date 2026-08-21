using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Journeys;
using TransitGuard.Core.Models;
using TransitGuard.Core.Normalize;
using TransitGuard.Core.Realtime;
using Xunit;

namespace TransitGuard.Core.Tests;

public sealed class JourneyServiceTests
{
    // ScheduleStore/Schedule bewusst fuer TransferRouterTests geteilt (interne Test-Helfer)
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 8, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } = T0; }

    public static TripSchedule Schedule(string tripId, string route, int dir, params (string Stop, int Min)[] stops) =>
        new(tripId, route, dir, "Ziel", stops.Select((s, i) => new ScheduledStop(s.Stop, i + 1, s.Min * 60, s.Min * 60)).ToList());

    public sealed class ScheduleStore : ITripScheduleStore
    {
        public Dictionary<string, TripSchedule> Trips { get; } = new();
        public bool TryGet(string cityId, string tripId, out TripSchedule schedule) => Trips.TryGetValue(tripId, out schedule!);
        public IEnumerable<KeyValuePair<string, TripSchedule>> All(string cityId) => Trips;
        public void ReplaceCity(string cityId, IReadOnlyDictionary<string, TripSchedule> schedules) { foreach (var kv in schedules) Trips[kv.Key] = kv.Value; }
    }

    private sealed class RtState : ITripStateStore
    {
        public Dictionary<string, NormalizedTripUpdate> State { get; } = new();
        public NormalizedTripUpdate? Get(string cityId, string tripId, string startDate) => State.GetValueOrDefault(tripId);
        public void Upsert(string cityId, NormalizedTripUpdate tu) => State[tu.TripId] = tu;
        public int ReplaceCity(string cityId, IReadOnlyDictionary<string, NormalizedTripUpdate> newState) => newState.Count;
        public IEnumerable<NormalizedTripUpdate> All(string cityId) => State.Values;
    }

    private static NormalizedTripUpdate Rt(string tripId, int delay) => new()
    { TripId = tripId, StartDate = "20260821", RouteId = "U1", RouteIdFromFeed = false,
      StopIds = Array.Empty<string>(), LastDelaySeconds = delay, HasDelayData = true, FeedSeenAt = T0 };

    private static readonly ScheduleStore Store = new()
    {
        Trips =
        {
            ["T1"] = Schedule("T1", "U1", 1, ("A", 600), ("B", 610), ("C", 620), ("D", 630)),       // 10:00-10:30
            ["T2"] = Schedule("T2", "U1", 1, ("A", 660), ("B", 670), ("C", 680), ("D", 690)),       // 11:00-11:30
            ["T3"] = Schedule("T3", "U1", 0, ("D", 640), ("C", 650), ("B", 660), ("A", 670)),       // Gegenrichtung
            ["T4"] = Schedule("T4", "S1", 1, ("X", 600), ("C", 640)),                               // berührt C, aber nicht A
        }
    };

    [Fact]
    public void Direktverbindung_A_nach_C_findet_U1_Richtung1_nicht_Gegenrichtung()
    {
        var svc = new JourneyService(Store, new FakeClock());
        var conns = svc.FindDirectConnections("hamburg", "20260821", "A", "C", new RtState());
        var u1 = Assert.Single(conns, c => c.RouteId == "U1" && c.DirectionId == 1);
        Assert.Equal(2, u1.StopsCount);                       // A→C = 2 Segmente
        Assert.Equal(2, u1.NextDepartures.Count);             // T1 (10:10) + T2 (11:10) von A
        Assert.DoesNotContain(conns, c => c.RouteId == "U1" && c.DirectionId == 0);   // D→C→B→A: C vor A
    }

    [Fact]
    public void Abfahrten_sortiert_mit_Ist_Und_Soll()
    {
        var rt = new RtState(); rt.Upsert("T1", Rt("T1", 120));
        var svc = new JourneyService(Store, new FakeClock { UtcNow = new(2026, 8, 21, 9, 30, 0, TimeSpan.Zero) });
        var deps = svc.DeparturesFrom("hamburg", "20260821", "A", rt);
        Assert.Equal(3, deps.Count);                       // T1 (10:00, Ist +120 s), T2 (11:00), T3 (endet 11:10 in A)
        Assert.Equal("T1", deps[0].TripId);
        Assert.Equal(120, deps[0].DelaySeconds);
        Assert.True(deps[0].Realtime);
        Assert.False(deps[1].Realtime);
        Assert.Equal(deps[0].ScheduledAt.AddSeconds(120), deps[0].EstimatedAt);
    }

    [Fact]
    public void Vergangene_Abfahrten_werden_ausgeblendet()
    {
        var svc = new JourneyService(Store, new FakeClock { UtcNow = new(2026, 8, 21, 10, 30, 0, TimeSpan.Zero) });
        var deps = svc.DeparturesFrom("hamburg", "20260821", "A", new RtState());
        Assert.Equal(new[] { "T2", "T3" }, deps.Select(d => d.TripId).ToArray());   // T1 (10:00) vorbei; T3 endet 11:10 in A — valide Abfahrt
    }

    [Fact]
    public void Kontroll_Warnung_mindestens_eine_Haltestelle_vorher()
    {
        var rt = new RtState(); rt.Upsert("T1", Rt("T1", 0));
        var clock = new FakeClock { UtcNow = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero) };
        var svc = new JourneyService(Store, clock);
        var report = new Report
        {
            CreatedAt = clock.UtcNow, CityId = "hamburg", AnchorType = AnchorType.Trip,
            TripId = "T1", TripStartDate = "20260821", RouteId = "U1",
            StationId = "A", LastKnownStationId = "B", Kind = ReportKind.InVehicle,
            ReporterDeviceId = Guid.NewGuid(), ReporterTrust = 50m, ExpiresAt = clock.UtcNow.AddMinutes(20)
        };
        var warnings = svc.WarningsForJourney("hamburg", "T1", "20260821", "A", new[] { report }, rt);
        var w = Assert.Single(warnings);
        Assert.Equal("B", w.AffectedStopId);                  // betroffener Halt = Kontrollposition B, liegt NACH dem Einstieg A
        Assert.Equal(2, w.AffectedStopSequence);
        Assert.Equal(10 * 60, w.UserEtaSeconds);              // jetzt 10:00 → Ankunft B 10:10 = 600 s ab jetzt
        Assert.Equal(ControlWarning.MessageText, w.Message);
    }

    [Fact]
    public void Warnung_nur_wenn_vor_dem_Nutzer_liegend()
    {
        var rt = new RtState(); rt.Upsert("T1", Rt("T1", 0));
        var clock = new FakeClock { UtcNow = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero) };
        var svc = new JourneyService(Store, clock);
        var report = new Report
        {
            CreatedAt = clock.UtcNow, CityId = "hamburg", AnchorType = AnchorType.Trip,
            TripId = "T1", TripStartDate = "20260821", RouteId = "U1",
            StationId = "A", LastKnownStationId = "A", Kind = ReportKind.InVehicle,
            ReporterDeviceId = Guid.NewGuid(), ReporterTrust = 50m, ExpiresAt = clock.UtcNow.AddMinutes(20)
        };
        // Nutzer steigt bei B ein, Kontrolle war bei A (hinter dem Nutzer) ⇒ keine Vorwarnung
        Assert.Empty(svc.WarningsForJourney("hamburg", "T1", "20260821", "B", new[] { report }, rt));
    }
}

public sealed class TrialPolicyTests   // ADR-0014: 14 Tage Test, dann 2,99 €/Monat
{
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero); }

    [Fact]
    public void Volle_Nutzung_in_den_ersten_14_Tagen()
    {
        var clock = new FakeClock();
        var p = new TrialPolicy(14);
        Assert.Equal(AccessLevel.Trial, p.AccessFor(clock.UtcNow.AddDays(-13), hasActiveSubscription: false, clock));
        Assert.Equal(AccessLevel.Locked, p.AccessFor(clock.UtcNow.AddDays(-15), hasActiveSubscription: false, clock));
        Assert.Equal(AccessLevel.Subscriber, p.AccessFor(clock.UtcNow.AddDays(-30), hasActiveSubscription: true, clock));
    }

    [Fact]
    public void Preis_und_Resttage()
    {
        Assert.Equal(2.99m, TrialPolicy.MonthlyPriceEur);
        var clock = new FakeClock();
        Assert.Equal(14, new TrialPolicy(14).TrialDaysRemaining(clock.UtcNow, clock));
        Assert.Equal(1, new TrialPolicy(14).TrialDaysRemaining(clock.UtcNow.AddDays(-13.5), clock));
        Assert.Equal(0, new TrialPolicy(14).TrialDaysRemaining(clock.UtcNow.AddDays(-20), clock));
    }
}

public sealed class HubGroupRuleTests   // Sweep-Fund: alerts-Gruppen wurden fälschlich abgelehnt
{
    [Theory]
    [InlineData("city.hamburg.reports.free", "hamburg", "reports", "free")]
    [InlineData("city.hamburg.reports.pro", "hamburg", "reports", "pro")]
    [InlineData("city.hamburg.alerts", "hamburg", "alerts", null)]
    public void Gueltige_Gruppen(string group, string slug, string family, string? tier)
    {
        Assert.True(HubGroupRule.TryParse(group, out var g));
        Assert.Equal(slug, g.CitySlug); Assert.Equal(family, g.Family); Assert.Equal(tier, g.Tier);
    }

    [Theory]
    [InlineData("city.hamburg.reports")]
    [InlineData("city.hamburg.reports.pro.extra")]
    [InlineData("stop.hamburg")]
    [InlineData("")]
    [InlineData("city..alerts")]
    public void Ungueltige_Gruppen(string group) => Assert.False(HubGroupRule.TryParse(group, out _));

    [Fact]
    public void Gate_gilt_nur_fuer_reports_Pro_nur_fuer_pro()
    {
        Assert.True(HubGroupRule.TryParse("city.hamburg.reports.free", out var rf) && HubGroupRule.RequiresTicketGate(rf) && !HubGroupRule.RequiresPro(rf));
        Assert.True(HubGroupRule.TryParse("city.hamburg.reports.pro", out var rp) && HubGroupRule.RequiresTicketGate(rp) && HubGroupRule.RequiresPro(rp));
        Assert.True(HubGroupRule.TryParse("city.hamburg.alerts", out var al) && !HubGroupRule.RequiresTicketGate(al) && !HubGroupRule.RequiresPro(al));
    }
}

public sealed class GeoMathTests
{
    [Fact]
    public void Naechste_Haltestelle_geordnet()
    {
        var stops = new[]
        {
            new StopInfo("S1", "Weit", 53.90, 10.60),
            new StopInfo("S2", "Jungfernstieg", 53.554, 9.991),
            new StopInfo("S3", "Naehe", 53.556, 9.995)
        };
        var near = GeoMath.Nearest(stops, 53.555, 9.993, 2);
        Assert.Equal("S3", near[0].Stop.StopId);
        Assert.Equal("S2", near[1].Stop.StopId);
        Assert.True(near[0].Km < 0.5);
    }
}
