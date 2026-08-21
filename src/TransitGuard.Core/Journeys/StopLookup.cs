namespace TransitGuard.Core.Journeys;

/// <summary>
/// Auflösung Haltestellen-ID → Anzeigename (T-STOP).
/// Bewusst linear: Meldungen sind rate-limitiert (5/10 min je Gerät), und die
/// Nachbarschaftssuche scannt die Stadt-Haltestellen ohnehin schon linear
/// (GeoMath.Nearest). Wird das je heiß, gehört ein Index in den IStopStore.
/// </summary>
public static class StopLookup
{
    /// <summary>
    /// Anzeigename zur ID; unbekannte ID bleibt sichtbar die ID (nichts erfinden),
    /// leere/fehlende ID ergibt null.
    /// </summary>
    public static string? DisplayName(IReadOnlyList<StopInfo> stops, string? stopId)
    {
        if (string.IsNullOrWhiteSpace(stopId)) return null;
        for (var i = 0; i < stops.Count; i++)
            if (stops[i].StopId == stopId) return stops[i].StopName;
        return stopId;
    }
}
