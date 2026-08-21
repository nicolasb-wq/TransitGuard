namespace TransitGuard.Core.Ingest;

public sealed record FeedHealthState(bool IsHealthy, IReadOnlyList<string> Reasons)
{
    public override string ToString() => IsHealthy ? "healthy" : "unhealthy: " + string.Join("; ", Reasons);
}

/// <summary>
/// G11-Gate (Bestandsaufnahme B1, docs/07 §5): Ein Feed gilt als krank, wenn
/// Alter &gt; 300 s ODER Entities &lt; 50 % des gleitenden Wochentags-Mittels ODER ≥3 konsekutive Fehler.
/// Nur bei gesundem Feed dürfen Trip-Degradationen ausgeführt werden — sonst Freeze
/// (VBB-Szenario: Massen-Degradation verhindert; T-TTL-10).
/// </summary>
public sealed class FeedHealthEvaluator(
    int maxAgeSeconds = 300,
    double minEntityRatio = 0.5,
    int maxConsecutiveFailures = 3)
{
    public FeedHealthState Evaluate(int? feedAgeSeconds, long? entities, double? rollingWeekdayMean, int consecutiveFailures)
    {
        var reasons = new List<string>();
        if (feedAgeSeconds is > 300 && feedAgeSeconds.Value > maxAgeSeconds) reasons.Add($"feed_age={feedAgeSeconds}s>{maxAgeSeconds}s");
        if (rollingWeekdayMean is > 0 && entities is > 0 && entities.Value < rollingWeekdayMean.Value * minEntityRatio)
            reasons.Add($"entities={entities}<{minEntityRatio:P0}*mean({rollingWeekdayMean:F0})");
        if (consecutiveFailures >= maxConsecutiveFailures) reasons.Add($"consecutive_failures={consecutiveFailures}");
        return new FeedHealthState(reasons.Count == 0, reasons);
    }
}
