using TransitGuard.Core.Journeys;
using Xunit;
using static TransitGuard.Core.Tests.JourneyServiceTests;

namespace TransitGuard.Core.Tests;

public sealed class TransferRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly ScheduleStore Store = new()
    {
        Trips =
        {
            // Linie A: S1 10:00 → T (Transfer) 10:20
            ["A1"] = Schedule("A1", "LinieA", 1, ("S1", 600), ("M", 610), ("T", 620)),
            // Linie B: T 10:30 → Ziel 10:40
            ["B1"] = Schedule("B1", "LinieB", 1, ("T", 630), ("ZIEL", 640)),
            // Direktverbindung existiert NICHT (Ziel nur via B erreichbar)
            ["A2"] = Schedule("A2", "LinieA", 1, ("S1", 660), ("M", 670), ("T", 680)),
            ["B2"] = Schedule("B2", "LinieB", 1, ("T", 690), ("ZIEL", 700)),
        }
    };

    [Fact]
    public void Ein_Umstieg_wird_gefunden_und_zeitlich_korrekt_verkettet()
    {
        var router = new TransferRouter(Store);
        var conns = router.FindWithOneTransfer("hamburg", "20260821", "S1", "ZIEL", Now);
        var best = Assert.Single(conns);
        Assert.Equal("T", best.TransferStopId);
        Assert.Equal("A1", best.LegA.TripId);
        Assert.Equal("B1", best.LegB.TripId);
        Assert.Equal(10 * 60, best.WaitSeconds);        // Ankunft T 10:20, Abfahrt B 10:30
        Assert.Equal(40 * 60, best.TotalSeconds);       // S1 10:00 → ZIEL 10:40
    }

    [Fact]
    public void Zu_knapper_Umstiegpuffer_faellt_weg()
    {
        var store = new ScheduleStore
        {
            Trips =
            {
                ["A1"] = Schedule("A1", "A", 1, ("S1", 600), ("T", 610)),          // T 10:10
                ["Bx"] = Schedule("Bx", "B", 1, ("T", 611), ("ZIEL", 615)),        // ab T 10:11 — Puffer 60 s < 120 s
                ["Bok"] = Schedule("Bok", "B", 1, ("T", 620), ("ZIEL", 625)),      // ab T 10:20 — passt
            }
        };
        var conns = new TransferRouter(store).FindWithOneTransfer("hamburg", "20260821", "S1", "ZIEL", Now);
        var best = Assert.Single(conns);
        Assert.Equal("Bok", best.LegB.TripId);
    }

    [Fact]
    public void Fensterfilter_Abfahrten_ausserhalb_90min_ignoriert()
    {
        var store = new ScheduleStore
        {
            Trips =
            {
                ["A1"] = Schedule("A1", "A", 1, ("S1", 600), ("T", 620)),
                ["B1"] = Schedule("B1", "B", 1, ("T", 630), ("ZIEL", 640)),
                ["A_spät"] = Schedule("A_spät", "A", 1, ("S1", 720), ("T", 740)),  // 12:00 — außerhalb 90-min-Fenster ab 10:00
                ["B_spät"] = Schedule("B_spät", "B", 1, ("T", 750), ("ZIEL", 760)),
            }
        };
        var conns = new TransferRouter(store).FindWithOneTransfer("hamburg", "20260821", "S1", "ZIEL", Now);
        Assert.Single(conns);
        Assert.Equal("A1", conns[0].LegA.TripId);
    }

    [Fact]
    public void Ohne_kompatible_Beine_leer()
    {
        var store = new ScheduleStore { Trips = { ["A1"] = Schedule("A1", "A", 1, ("S1", 600), ("T", 620)) } };
        Assert.Empty(new TransferRouter(store).FindWithOneTransfer("hamburg", "20260821", "S1", "ZIEL", Now));
    }
}
