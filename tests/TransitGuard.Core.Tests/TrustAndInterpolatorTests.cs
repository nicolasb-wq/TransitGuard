using TransitGuard.Core.Interpolation;
using TransitGuard.Core.Models;
using TransitGuard.Core.Trust;
using Xunit;

namespace TransitGuard.Core.Tests;

public sealed class TrustEngineTests   // T-TRUST + Rang-Fix B5
{
    private readonly TrustEngine _t = new();

    [Fact] public void Neues_Geraet_Start50_Neuling()   // fixt v0.1-Bug: 50 ⇒ Neuling (nicht Scout)
    {
        var s = TrustEngine.NewDevice(Guid.NewGuid());
        Assert.Equal(50m, s.Score);
        Assert.Equal(TrustRank.Neuling, s.Rank);
    }

    [Fact] public void Rang_Grenzen_60_100_150()
    {
        Assert.Equal(TrustRank.Neuling, TrustEngine.RankFor(59.9m));
        Assert.Equal(TrustRank.Scout, TrustEngine.RankFor(60m));
        Assert.Equal(TrustRank.Waechter, TrustEngine.RankFor(100m));
        Assert.Equal(TrustRank.Legende, TrustEngine.RankFor(150m));
    }

    [Fact] public void Bestaetigungen_erhoehen_Score()
    {
        var s = TrustEngine.NewDevice(Guid.NewGuid());
        _t.OnReportConfirmed(s); _t.OnReportConfirmed(s);
        Assert.Equal(56m, s.Score);
    }

    [Fact] public void Spam_Burst_minus20()
    {
        var s = TrustEngine.NewDevice(Guid.NewGuid());
        _t.OnSpamBurst(s);
        Assert.Equal(30m, s.Score);
    }

    [Fact] public void Score_gedeckelt_0_200()
    {
        var s = TrustEngine.NewDevice(Guid.NewGuid());
        for (int i = 0; i < 100; i++) _t.OnReportConfirmed(s);
        Assert.Equal(200m, s.Score);
    }

    [Fact] public void Sichtbarkeit_unter_20_nur_nach_Bestaetigung()   // Troll-Gate
    {
        var s = TrustEngine.NewDevice(Guid.NewGuid());
        _t.OnSpamBurst(s); _t.OnSpamBurst(s);   // 50-40=10
        Assert.False(TrustEngine.IsVisibleImmediately(s));
        Assert.True(TrustEngine.IsVisibleImmediately(TrustEngine.NewDevice(Guid.NewGuid())));
    }
}

public sealed class TripInterpolatorTests   // docs/02 §6, Labels (N2/N3)
{
    private static readonly DateTimeOffset DayStart = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    private static readonly StopSchedule[] Stops =
    {
        new(1, "A", 36000, 36000), new(2, "B", 36600, 36600), new(3, "C", 37200, 37200), new(4, "D", 37800, 37800)
    };   // 10:00, 10:10, 10:20, 10:30 UTC

    [Fact] public void Fortschritt_zwischen_Halten_mit_Delay()
    {
        var now = new DateTimeOffset(2026, 8, 21, 10, 6, 0, TimeSpan.Zero);   // 10:06
        var p = TripInterpolator.Compute(Stops, DayStart, now, lastDelaySeconds: 60, lastRealtimeAnchorUtc: now);
        Assert.False(p.Ended);
        Assert.Equal("A", p.CurrentOrLastStopId);            // letzter PASSIERTER Halt (10:01 mit +60 s) — Semantik für movement/B.5
        Assert.Equal(60, p.DelaySeconds);
        Assert.True(p.ProgressFraction is > 0.05 and < 0.5); // zwischen A und B (5/10 min im ersten Viertel)
    }

    [Fact] public void Ende_nach_letztem_Halt_plus_Buffer()
    {
        var end = new DateTimeOffset(2026, 8, 21, 10, 32, 1, TimeSpan.Zero);   // trip_end+120+1
        var p = TripInterpolator.Compute(Stops, DayStart, end, 0, null);
        Assert.True(p.Ended);
        Assert.Equal("D", p.CurrentOrLastStopId);
    }

    [Fact] public void Label_Schedule_ohne_aktuellen_Anker()   // „Live" existiert nicht
    {
        var now = new DateTimeOffset(2026, 8, 21, 10, 5, 0, TimeSpan.Zero);
        Assert.Equal(PositionLabel.ScheduleOnly, TripInterpolator.Compute(Stops, DayStart, now, null, null).Label);
        Assert.Equal(PositionLabel.ScheduleOnly,
            TripInterpolator.Compute(Stops, DayStart, now, 60, now.AddMinutes(-10)).Label);   // Anker älter als 5 min
        Assert.Equal(PositionLabel.RealtimePrognosis, TripInterpolator.Compute(Stops, DayStart, now, 0, now).Label);
    }

    [Fact] public void Eta_am_Halt()
    {
        var eta = TripInterpolator.EtaAt(Stops, DayStart, "C", 120);
        Assert.Equal(new DateTimeOffset(2026, 8, 21, 10, 22, 0, TimeSpan.Zero), eta);
    }
}
