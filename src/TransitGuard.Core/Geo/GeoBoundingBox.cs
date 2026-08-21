namespace TransitGuard.Core.Geo;

/// <summary>Geografische Bounding-Box (Grad). Für Stadt-Filter und Plausibilitätsprüfungen.</summary>
public sealed record GeoBoundingBox(double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    public bool Contains(double lat, double lon) =>
        lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;

    /// <summary>Abstand Punkt↔Box in Grad (grob, für Plausibilität — nicht für Navigation).</summary>
    public double DistanceToEdge(double lat, double lon)
    {
        double dLat = lat < MinLat ? MinLat - lat : lat > MaxLat ? lat - MaxLat : 0;
        double dLon = lon < MinLon ? MinLon - lon : lon > MaxLon ? lon - MaxLon : 0;
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }
}
