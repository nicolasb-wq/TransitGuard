namespace TransitGuard.Core.Models;

public enum TrustRank { Neuling = 1, Scout = 2, Waechter = 3, Legende = 4 }

/// <summary>
/// Vertrauenszustand eines (anonymen) Geräts. Rang-Grenzen korrigiert ggü. v0.1
/// (Bestandsaufnahme B5: Start 50 Punkte muss „Neuling" ergeben):
/// Neuling &lt; 60, Scout &lt; 100, Wächter &lt; 150, Legende ≥ 150. Score-Intervall [0,200].
/// </summary>
public sealed class TrustState
{
    public Guid DeviceId { get; init; }
    public decimal Score { get; set; } = 50m;
    public int TotalReports { get; set; }
    public int ConfirmedCount { get; set; }
    public int FalseCount { get; set; }
    public int ContradictedCount { get; set; }
    public TrustRank Rank { get; set; } = TrustRank.Neuling;
}
