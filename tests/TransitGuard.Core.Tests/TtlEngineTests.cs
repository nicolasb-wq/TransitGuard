using TransitGuard.Core.Models;
using TransitGuard.Core.Ttl;
using Xunit;

namespace TransitGuard.Core.Tests;

/// <summary>Testplan T-TTL-1…12 (docs/09) — tabellengetrieben gegen die reine TtlEngine.</summary>
public sealed class TtlEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly TtlEngine _e = new();

    private static Report Station(ReportKind kind = ReportKind.InVehicle, VehicleKind vk = VehicleKind.Rail,
        decimal trust = 50m, DateTimeOffset? created = null) => new()
    {
        CreatedAt = created ?? T0, CityId = "hamburg", AnchorType = AnchorType.Station,
        StationId = "S1", Kind = kind, VehicleKind = vk, ReporterDeviceId = Guid.NewGuid(), ReporterTrust = trust
    };

    private static Report Trip(DateTimeOffset tripEnd, decimal trust = 50m) => new()
    {
        CreatedAt = T0, CityId = "hamburg", AnchorType = AnchorType.Trip, TripId = "110895", TripStartDate = "20260821",
        StationId = "S1", Kind = ReportKind.InVehicle, VehicleKind = VehicleKind.Rail,
        ReporterDeviceId = Guid.NewGuid(), ReporterTrust = trust, LastKnownStationId = "S_END"
    };

    [Fact] public void T1_Station_Basis_1200s()   // T-TTL-1
    {
        var d = _e.ComputeInitial(Station(), T0, null);
        Assert.Equal(T0.AddSeconds(1200), d.ExpiresAt);
        Assert.Equal(1200, d.TtlBaseSeconds);
    }

    [Fact] public void T2_Vier_Bestaetigungen_plus900_cap2700()   // T-TTL-2
    {
        var r = Station(); r.Confirmations = 4;
        var d = _e.ComputeInitial(r, T0, null); r.ExpiresAt = d.ExpiresAt;
        Assert.Equal(T0.AddSeconds(1200 + 900), _e.ApplyConfirmation(r, r.ExpiresAt));   // min(4*300,900)
    }

    [Fact] public void T3_Widerspruch_mit_Paritaet_Grace120()   // T-TTL-3
    {
        var r = Station(trust: 50m); r.ExpiresAt = T0.AddSeconds(1200); r.Contradictions = 1;
        var (exp, outcome) = _e.ApplyContradiction(r, actorTrust: 50m, T0.AddMinutes(5));
        Assert.Equal(ContradictionOutcome.GraceApplied, outcome);
        Assert.Equal(T0.AddMinutes(5).AddSeconds(120), exp);
    }

    [Fact] public void T4_Widerspruch_ohne_Paritaet_nur_Review()   // T-TTL-4
    {
        var r = Station(trust: 100m); r.ExpiresAt = T0.AddSeconds(1200); r.Contradictions = 1;
        var (exp, outcome) = _e.ApplyContradiction(r, actorTrust: 20m, T0.AddMinutes(5));
        Assert.Equal(ContradictionOutcome.ReviewFlagOnly, outcome);
        Assert.Equal(r.ExpiresAt, exp);
    }

    [Fact] public void T5_Zweiter_Widerspruch_Grace_trotz_fehlender_Paritaet()   // T-TTL-5 (B6)
    {
        var r = Station(trust: 100m); r.ExpiresAt = T0.AddSeconds(1200); r.Contradictions = 2;
        var (_, outcome) = _e.ApplyContradiction(r, actorTrust: 10m, T0);
        Assert.Equal(ContradictionOutcome.GraceApplied, outcome);
    }

    [Fact] public void T6_BusStation_6_Bestaetigungen_cap1800()   // T-TTL-6
    {
        var r = Station(vk: VehicleKind.Bus); r.Confirmations = 6;
        var d = _e.ComputeInitial(r, T0, null); r.ExpiresAt = d.ExpiresAt;
        Assert.Equal(T0.AddSeconds(720 + 900), _e.ApplyConfirmation(r, r.ExpiresAt));   // min(1800,900) Bonus, unter Cap 1800
    }

    [Fact] public void T7_TripEnde_beurteilt_Original_und_DerivedTerminus()   // T-TTL-7 (B.5)
    {
        var tripEnd = T0.AddMinutes(20);
        var r = Trip(tripEnd);
        var d = _e.ComputeInitial(r, T0, tripEnd);
        r.ExpiresAt = d.ExpiresAt;
        var a = _e.Evaluate(r, T0.AddMinutes(21), tripEnded: true, feedHealthy: true);
        Assert.Equal(ExpiryActionKind.ExpireWithDerivedTerminus, a.Kind);
        var der = Assert.IsType<Report>(a.DerivedReport);
        Assert.Equal(AnchorType.DerivedTerminus, der.AnchorType);
        Assert.Equal(600, der.TtlBaseSeconds);
        Assert.Equal(35.0m, der.ReporterTrust);                       // 0,7 × 50
        Assert.Equal(r.Id, der.ParentReportId);
        Assert.Equal(ReportOrigin.Derived, der.Origin);
        Assert.Equal("S_END", der.StationId);
    }

    [Fact] public void T8_CANCELED_sofort_ohne_Derived()   // T-TTL-8
    {
        var r = Trip(T0.AddMinutes(30));
        var a = _e.Evaluate(r, T0.AddMinutes(1), canceledInFeed: true);
        Assert.Equal(ExpiryActionKind.Expire, a.Kind);
        Assert.Null(a.DerivedReport);
        Assert.Equal("canceled", a.Reason);
    }

    [Fact] public void T9_StaticSwap_trip_weg_DegradeToStation()   // T-TTL-9 (C.3.2)
    {
        var r = Trip(T0.AddMinutes(30));
        var a = _e.Evaluate(r, T0.AddMinutes(1), tripDisappearedFromStatic: true, feedHealthy: true);
        Assert.Equal(ExpiryActionKind.DegradeToStation, a.Kind);
    }

    [Fact] public void T10_Feed_ungesund_keine_Degradation_Freeze()   // T-TTL-10 (G11-Gate, B1)
    {
        var r = Trip(T0.AddMinutes(30));
        var a = _e.Evaluate(r, T0.AddMinutes(31), tripEnded: true, feedHealthy: false);
        Assert.Equal(ExpiryActionKind.Freeze, a.Kind);
        var b = _e.Evaluate(r, T0.AddMinutes(1), tripDisappearedFromStatic: true, feedHealthy: false);
        Assert.Equal(ExpiryActionKind.Freeze, b.Kind);
    }

    [Fact] public void T11_Profile_atStop480_derived600_railTrip_natuerlich()   // T-TTL-11
    {
        var atStop = _e.ComputeInitial(Station(ReportKind.AtStop), T0, null);
        Assert.Equal(480, atStop.TtlBaseSeconds);
        var derived = new Report { CreatedAt = T0, CityId = "x", AnchorType = AnchorType.DerivedTerminus, Kind = ReportKind.InVehicle, ReporterDeviceId = Guid.NewGuid() };
        Assert.Equal(600, _e.ComputeInitial(derived, T0, null).TtlBaseSeconds);
        var trip = Trip(T0.AddMinutes(15));
        var d = _e.ComputeInitial(trip, T0, T0.AddMinutes(15));
        Assert.Equal(T0.AddMinutes(15).AddSeconds(120), d.ExpiresAt);   // trip_end + 120 s Buffer, KEIN Cap
    }

    [Fact] public void T12_Zukunftsdatierte_Ereignisse_ignoriert()   // T-TTL-12
    {
        var r = Station(created: T0.AddMinutes(5));   // „aus der Zukunft"
        var a = _e.Evaluate(r, T0);
        Assert.Equal(ExpiryActionKind.None, a.Kind);
        Assert.Equal("created_in_future_ignored", a.Reason);
    }

    [Fact] public void Trip_ueber_45min_ueberlebt_Cap()   // F-16: Cap gilt NICHT für Trip-Anker
    {
        var r = Trip(T0.AddMinutes(70));
        var d = _e.ComputeInitial(r, T0, T0.AddMinutes(70));
        Assert.Equal(T0.AddMinutes(70).AddSeconds(120), d.ExpiresAt);
    }
}
