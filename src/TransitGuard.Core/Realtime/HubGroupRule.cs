namespace TransitGuard.Core.Realtime;

/// <summary>
/// Zentrale Gruppen-Namensregel des Hubs (docs/06 §1) — als reine Logik, damit Regel und Tests
/// am gleichen Ort leben. Unterstützt BEIDE Formen: city.{slug}.reports.free|.pro (4 Segmente)
/// und city.{slug}.alerts (3 Segmente) — der Alert-Fall fehlte in der ersten Hub-Implementierung
/// (Fehler-Sweep 21.08., 20-build-log).
/// </summary>
public static class HubGroupRule
{
    public sealed record Group(string CitySlug, string Family, string? Tier);   // Tier null bei alerts

    public static bool TryParse(string? group, out Group parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(group)) return false;
        var p = group.Split('.');
        if (p.Length == 4 && p[0] == "city" && !string.IsNullOrEmpty(p[1]) && p[2] == "reports" && p[3] is "free" or "pro")
        { parsed = new Group(p[1], "reports", p[3]); return true; }
        if (p.Length == 3 && p[0] == "city" && !string.IsNullOrEmpty(p[1]) && p[2] == "alerts")
        { parsed = new Group(p[1], "alerts", null); return true; }
        return false;
    }

    public static bool RequiresTicketGate(Group g) => g.Family == "reports";
    public static bool RequiresPro(Group g) => g.Family == "reports" && g.Tier == "pro";
}
