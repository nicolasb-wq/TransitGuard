namespace TransitGuard.Ingest.Jobs;

/// <summary>
/// Gleitendes Wochentags-Mittel der Entity-Anzahl (7 Tage) + konsekutive Fehler —
/// Datenbasis des G11-Gates (docs/07 §5). In-Memory; Persistenz via ingest_metrics.
/// </summary>
public sealed class FeedHealthTracker
{
    private readonly Dictionary<DateOnly, long> _dailyMaxEntities = new();
    private long _consecutiveFailures;

    public void RecordRun(DateOnly day, long entities, bool failed)
    {
        _consecutiveFailures = failed ? _consecutiveFailures + 1 : 0;
        if (!failed && entities > 0)
            _dailyMaxEntities[day] = Math.Max(_dailyMaxEntities.GetValueOrDefault(day), entities);
        // Aufräumen: älter als 14 Tage
        foreach (var d in _dailyMaxEntities.Keys.Where(d => day.DayNumber - d.DayNumber > 14).ToArray()) _dailyMaxEntities.Remove(d);
    }

    public double? WeekdayMean(DateOnly day)
    {
        var vals = new List<long>();
        for (int i = 1; i <= 2; i++)   // bis zu 2 frühere gleiche Wochentage (Feed existiert noch nicht länger)
        {
            var d = day.AddDays(-7 * i);
            if (_dailyMaxEntities.TryGetValue(d, out var v)) vals.Add(v);
        }
        return vals.Count > 0 ? vals.Average() : null;
    }

    public int ConsecutiveFailures => (int)_consecutiveFailures;
}
