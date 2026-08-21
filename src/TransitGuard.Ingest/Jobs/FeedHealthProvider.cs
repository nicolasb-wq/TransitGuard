using TransitGuard.Core.Ingest;

namespace TransitGuard.Ingest.Jobs;

/// <summary>
/// Prozessweiter Feed-Gesundheitszustand: PollRealtimeJob schreibt, TtlSweepService liest
/// (G11-Gate, B1). Default gesund bis zum ersten Poll — bewusst optimistisch, damit der
/// Sweep bei Start nicht alle Degradationen friert (docs/07 §5: Gate greift bei Indizien).
/// </summary>
public sealed class FeedHealthProvider
{
    private volatile FeedHealthState _state = new(true, Array.Empty<string>());
    public FeedHealthState Current => _state;
    public void Update(FeedHealthState state) => _state = state;
}
