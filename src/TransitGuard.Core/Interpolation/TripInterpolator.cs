namespace TransitGuard.Core.Interpolation;

public enum PositionLabel { RealtimePrognosis, ScheduleOnly }   // „Live" existiert nicht (0 VP, v1.1 §1)

/// <summary>Soll-Fahrplan-Eintrag einer Fahrt (aus gtfs_stop_times; Sekunden ab Betriebstag-Mitternacht, &gt;86400 zulässig).</summary>
public sealed record StopSchedule(int Sequence, string StopId, int? ArrivalSeconds, int? DepartureSeconds);

public sealed record TripProgress(
    bool Ended,
    DateTimeOffset EndsAtUtc,
    string? CurrentOrLastStopId,
    double ProgressFraction,
    PositionLabel Label,
    int? DelaySeconds)
{
    public const int RealtimeAnchorMaxAgeSeconds = 300;   // „Echtzeit-Prognose" nur mit Ist-Anker < 5 min
}

/// <summary>
/// Positions-/Fortschrittsmodell einer Fahrt (docs/02 §6): Soll-Fahrplan + letzter bekannter Delay
/// (Median 0 s — N2) ⇒ linear zwischen Halten. TU-STUs (gtfs.de Median 3 — N3) sind Anker, nie alleinige Quelle.
/// </summary>
public static class TripInterpolator
{
    public static TripProgress Compute(
        IReadOnlyList<StopSchedule> stops,
        DateTimeOffset dayStartUtc,
        DateTimeOffset now,
        int? lastDelaySeconds,
        DateTimeOffset? lastRealtimeAnchorUtc,
        int tripEndBufferSeconds = 120)
    {
        if (stops.Count == 0) throw new ArgumentException("Fahrplan ohne Halte", nameof(stops));
        var ordered = stops.OrderBy(s => s.Sequence).ToArray();
        var delay = lastDelaySeconds ?? 0;

        DateTimeOffset TimeAt(int i) => dayStartUtc.AddSeconds((ordered[i].DepartureSeconds ?? ordered[i].ArrivalSeconds ?? 0) + delay);
        var endsAt = TimeAt(ordered.Length - 1);

        if (now >= endsAt.AddSeconds(tripEndBufferSeconds))
            return new TripProgress(true, endsAt, ordered[^1].StopId, 1.0, LabelFor(now, lastRealtimeAnchorUtc), delay);

        int idx = 0;
        for (int i = 0; i < ordered.Length; i++) { if (TimeAt(i) <= now) idx = i; else break; }

        double fraction;
        if (idx == ordered.Length - 1) fraction = 1.0;
        else
        {
            var t0 = TimeAt(idx).UtcDateTime; var t1 = TimeAt(idx + 1).UtcDateTime; var tN = now.UtcDateTime;
            var span = (t1 - t0).TotalSeconds; fraction = span <= 0 ? 1.0 : Math.Clamp((tN - t0).TotalSeconds / span, 0, 1);
            fraction = (idx + fraction) / ordered.Length;
        }

        var label = LabelFor(now, lastRealtimeAnchorUtc);
        return new TripProgress(false, endsAt, ordered[idx].StopId, fraction, label, delay);
    }

    /// <summary>ETA an einem Halt (Soll + Delay), null wenn Halt nicht im Fahrplan.</summary>
    public static DateTimeOffset? EtaAt(IReadOnlyList<StopSchedule> stops, DateTimeOffset dayStartUtc, string stopId, int? lastDelaySeconds)
    {
        var s = stops.FirstOrDefault(x => x.StopId == stopId) ?? null;
        if (s is null) return null;
        return dayStartUtc.AddSeconds((s.DepartureSeconds ?? s.ArrivalSeconds ?? 0) + (lastDelaySeconds ?? 0));
    }

    private static PositionLabel LabelFor(DateTimeOffset now, DateTimeOffset? anchor) =>
        anchor is { } a && (now - a).TotalSeconds <= TripProgress.RealtimeAnchorMaxAgeSeconds
            ? PositionLabel.RealtimePrognosis
            : PositionLabel.ScheduleOnly;
}
