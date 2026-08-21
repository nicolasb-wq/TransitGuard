using TransitGuard.Core.Gate;
using TransitGuard.Core.Ingest;
using TransitGuard.Core.Normalize;
using Xunit;

namespace TransitGuard.Core.Tests;

public sealed class TripUpdateNormalizerTests   // Testplan T-NORM-1…5
{
    private static readonly DateTimeOffset Seen = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private static TripUpdateNormalizer.RawTripUpdate Raw(string tripId, string? route = null,
        (int?, int?)[]? delays = null, bool vehiclePresent = false, string? vehicleId = null) =>
        new(tripId, "20260821", route,
            (delays ?? Array.Empty<(int?, int?)>()).Select(d => new TripUpdateNormalizer.RawStopTimeUpdate("S" + Guid.NewGuid().ToString("N")[..6], d.Item1, d.Item2)).ToArray(),
            vehiclePresent, vehicleId, Seen);

    private static Dictionary<string, TripLookup> Whitelist(params (string, string)[] trips) =>
        trips.ToDictionary(t => t.Item1, t => new TripLookup(t.Item2, 1, "LAST", 60000));

    [Fact] public void N1_RouteId_leer_Lookup_liefert()   // T-NORM-1 (gtfs.de: 100 % leer)
    {
        var wl = Whitelist(("110895", "U1"));
        var n = new TripUpdateNormalizer().Normalize(Raw("110895"), wl, new NormalizerCounters());
        Assert.NotNull(n);
        Assert.Equal("U1", n!.RouteId);
        Assert.False(n.RouteIdFromFeed);
    }

    [Fact] public void N2_Whitelist_Miss_zaehlt_nicht_als_RouteMiss()
    {
        var c = new NormalizerCounters();
        var n = new TripUpdateNormalizer().Normalize(Raw("unbekannt"), Whitelist(("110895", "U1")), c);
        Assert.Null(n);
        Assert.Equal(1, c.WhitelistMisses);
        Assert.Equal(0, c.RouteMisses);
    }

    [Fact] public void N3_RouteId_aus_Feed_gewinnt()   // T-NORM-3 (VBB-Muster)
    {
        var n = new TripUpdateNormalizer().Normalize(Raw("18945_700", route: "S1"), Whitelist(("18945_700", "S1")), new NormalizerCounters());
        Assert.Equal("S1", n!.RouteId);
        Assert.True(n.RouteIdFromFeed);
    }

    [Fact] public void N4_Delay_Clamp_gemessene_Ausreisser()   // T-NORM-4 (−30384 s real gemessen; +7148 liegt UNTER Clamp 7200)
    {
        var c = new NormalizerCounters();
        var n = new TripUpdateNormalizer().Normalize(
            Raw("110895", delays: new (int?, int?)[] { (-30384, null), (null, 7148) }),
            Whitelist(("110895", "U1")), c);
        Assert.Equal(1, c.DelayClamped);            // nur −30.384 verletzt [−120,+7200]
        Assert.Equal(7148, n!.LastDelaySeconds);    // +7.148 bleibt unangetastet (Messwert!)
        Assert.True(n.DelayWasClamped);
        Assert.True(n.HasDelayData);
    }

    [Fact] public void N4b_Delay_Clamp_obere_Grenze()
    {
        var c = new NormalizerCounters();
        var n = new TripUpdateNormalizer().Normalize(
            Raw("110895", delays: new (int?, int?)[] { (null, 9000) }),
            Whitelist(("110895", "U1")), c);
        Assert.Equal(1, c.DelayClamped);
        Assert.Equal(7200, n!.LastDelaySeconds);
    }

    [Fact] public void N5_VehicleFeld_präsent_aber_leer()   // T-NORM-5 (A2: 8,4 % der TUs)
    {
        var c = new NormalizerCounters();
        var n = new TripUpdateNormalizer().Normalize(
            Raw("110895", vehiclePresent: true, vehicleId: ""),
            Whitelist(("110895", "U1")), c);
        Assert.True(n!.VehicleFieldPresentButEmpty);
        Assert.Equal(1, c.VehicleFieldPresentButEmpty);
    }
}

public sealed class AlertNormalizerTests   // Testplan T-NORM-6…10 + Dedup-Kreuzprüfung
{
    private readonly AlertNormalizer _a = AlertNormalizer.CreateDefault();
    private readonly NormalizerCounters _c = new();

    [Fact] public void N6_Attributierungs_Rauschen()   // T-NORM-6 (64.246× gemessen)
    {
        var n = _a.Normalize("", "Echtzeitdaten aufbereitet von GTFS.de, bereitgestellt von DELFI", null, 1, 8, 2, Array.Empty<string>(), Array.Empty<string>(), _c);
        Assert.True(n.IsNoise);
        Assert.Equal("attr_gtfsde", n.NoiseRuleId);
        Assert.True(_c.AlertsNoise >= 1);
    }

    [Fact] public void N7_Ausstattungsnotizen_Rauschen()   // T-NORM-7
    {
        foreach (var h in new[] { "Niederflur", "Bordrestaurant", "WLAN verfügbar" })
        {
            var n = _a.Normalize(h, h, null, 1, 8, 2, Array.Empty<string>(), Array.Empty<string>(), _c);
            Assert.True(n.IsNoise, h);
        }
    }

    [Fact] public void N8_DedupKey_identisch_fuer_duplikate()   // T-NORM-8
    {
        var k1 = AlertNormalizer.ComputeDedupKey("Bordrestaurant", "");
        var k2 = AlertNormalizer.ComputeDedupKey("Bordrestaurant", "");
        Assert.Equal(k1, k2);
        Assert.Equal(64, k1.Length);
    }

    [Fact] public void N9_Echte_Stoerung_sichtbar()   // T-NORM-9 (Stuttgart-Muster aus der Messung)
    {
        var n = _a.Normalize("Zuffenhausen Kelterplatz - Pragsattel: Streckensperrung und Ersatzverkehr",
            "Wegen Weichen-Erneuerung kommt es zu Umleitungen.", null, 10, 1, 3, new[] { "1293928" }, Array.Empty<string>(), _c);
        Assert.False(n.IsNoise);
        Assert.Equal(3, n.Severity);
    }

    [Fact] public void N10_Kreuzprüfung_DedupKey_gegen_Python()   // Wire-Konsistenz mit verifikation/verify_alerts.py
    {
        // python: hashlib.sha256("Kirmes in Moers|".encode()).hexdigest()
        Assert.Equal("019932980d52230801f49c3544a95c0bda5700f0a2b92e12d5bf8c357ea61c1b",
            AlertNormalizer.ComputeDedupKey("Kirmes in Moers", ""));
    }
}

public sealed class FeedHealthEvaluatorTests   // G11-Gate (docs/07 §5)
{
    private readonly FeedHealthEvaluator _e = new();

    [Fact] public void Alter_ueber_300s_krank() => Assert.False(_e.Evaluate(900, 100000, null, 0).IsHealthy);
    [Fact] public void Entities_unter_50_Prozent_Mittel_krank()
    { Assert.False(_e.Evaluate(30, 40000, 100000, 0).IsHealthy); Assert.True(_e.Evaluate(30, 60000, 100000, 0).IsHealthy); }
    [Fact] public void Drei_Fehler_krank() => Assert.False(_e.Evaluate(30, 100000, null, 3).IsHealthy);
    [Fact] public void Alles_gesund() => Assert.True(_e.Evaluate(30, 100000, 90000, 0).IsHealthy);
    [Fact] public void Ohne_Mittelweg_nur_Alter_entscheidet() => Assert.True(_e.Evaluate(200, 100, null, 0).IsHealthy);
}

public sealed class TicketGateTests   // T-GATE (Kernlogik; API-Integration in T-GATE-1..6 via Api-Tests)
{
    [Fact] public void Write_ohne_Bestaetigung_422()
        => Assert.Equal("ticket_confirmation_required", new TicketGate().CheckWrite(null).ErrorCode);
    [Fact] public void Write_mit_false_422()
        => Assert.Equal("ticket_confirmation_required", new TicketGate().CheckWrite(false).ErrorCode);
    [Fact] public void Write_mit_true_ok()
        => Assert.Equal(TicketGateDecision.Allow, new TicketGate().CheckWrite(true).Decision);
    [Fact] public void Read_ohne_Header_403()
        => Assert.Equal("ticket_gate_blocked", new TicketGate().CheckRead(false).ErrorCode);
    [Fact] public void Deaktiviert_laesst_alles_durch()
    { var g = new TicketGate { Enabled = false }; Assert.Null(g.CheckWrite(null).ErrorCode); }
}
