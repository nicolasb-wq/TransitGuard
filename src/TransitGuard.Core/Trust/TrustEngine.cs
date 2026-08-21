using TransitGuard.Core.Models;

namespace TransitGuard.Core.Trust;

/// <summary>
/// Trust-Engine (v0.1 F6, korrigierte Rang-Grenzen B5). Reine Zustandsübergänge;
/// Anomalie-Dämpfung (P5) und Sichtbarkeits-Schwelle (Troll-Gate) inklusive.
/// </summary>
public sealed class TrustEngine
{
    public const decimal InitialScore = 50m;
    public const decimal MaxScore = 200m;
    public const decimal VisibilityThreshold = 20m;   // darunter: Meldung unsichtbar bis 1. Bestätigung

    public static TrustState NewDevice(Guid deviceId) => new() { DeviceId = deviceId, Score = InitialScore, Rank = TrustRank.Neuling };

    public static TrustRank RankFor(decimal score) =>
        score < 60 ? TrustRank.Neuling : score < 100 ? TrustRank.Scout : score < 150 ? TrustRank.Waechter : TrustRank.Legende;

    private static TrustState Apply(TrustState s, decimal delta)
    {
        s.Score = Math.Clamp(s.Score + delta, 0, MaxScore);
        s.Rank = RankFor(s.Score);
        return s;
    }

    public void OnReportCreated(TrustState s) { s.TotalReports++; }

    /// <summary>Meldung von anderen bestätigt: +3.</summary>
    public void OnReportConfirmed(TrustState s) { s.ConfirmedCount++; Apply(s, +3m); }

    /// <summary>Meldung lief unbewidersprochen ab (TTL normal): +1.</summary>
    public void OnReportExpiredUncontradicted(TrustState s) => Apply(s, +1m);

    /// <summary>Meldung als falsch gemeldet (multipl. bestätigt): −10 je Vorkommnis.</summary>
    public void OnFalseReportConfirmed(TrustState s) { s.FalseCount++; Apply(s, -10m); }

    /// <summary>Häufige Meldungen aus unplatiblem Standort: −5.</summary>
    public void OnImplausibleLocationReport(TrustState s) => Apply(s, -5m);

    /// <summary>Spam-Burst (&gt;5 Meldungen in 10 min, v0.1-F6): −20.</summary>
    public void OnSpamBurst(TrustState s) => Apply(s, -20m);

    /// <summary>Meldung erscheint sofort öffentlich? (Troll-Gate; andernfalls erst nach 1. Bestätigung.)</summary>
    public static bool IsVisibleImmediately(TrustState s) => s.Score >= VisibilityThreshold;
}
