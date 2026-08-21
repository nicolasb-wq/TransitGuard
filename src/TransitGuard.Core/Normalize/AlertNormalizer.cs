using System.Security.Cryptography;
using System.Text;

namespace TransitGuard.Core.Normalize;

public enum AlertNoiseKind { None = 0, Attribution = 1, Amenity = 2, Other = 9 }

public sealed record AlertNoiseRule(string RuleId, AlertNoiseKind Kind, string MatchType, string Pattern)
{
    public bool Matches(string header, string description) => MatchType switch
    {
        "desc_prefix" => description.StartsWith(Pattern, StringComparison.Ordinal),
        "header_equals" => string.Equals(header, Pattern, StringComparison.Ordinal),
        "header_contains" => header.Contains(Pattern, StringComparison.Ordinal),
        _ => false
    };
}

/// <summary>
/// Alert-Normalisierung (ADR-0011): Dedup-Key sha256(header|desc[:150]) — identisch zum
/// Python-Messskript (N1, Kreuz-Test T-NORM-Dedup) — plus Noise-Regeln aus der Messung
/// 2026-08-21 (sql/0002 alert_noise_rules).
/// </summary>
public sealed class AlertNormalizer(IReadOnlyList<AlertNoiseRule> noiseRules)
{
    public static string ComputeDedupKey(string header, string description)
    {
        var desc = description.Length > 150 ? description[..150] : description;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(header + "|" + desc));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public NormalizedAlert Normalize(
        string header, string description, string? url,
        int cause, int effect, int severity,
        IReadOnlyList<string> informedTripIds, IReadOnlyList<string> informedStopIds,
        NormalizerCounters counters)
    {
        counters.AlertsSeen++;
        string? ruleId = null; AlertNoiseKind kind = AlertNoiseKind.None;
        foreach (var rule in noiseRules)
        {
            if (rule.Matches(header, description))
            {
                ruleId = rule.RuleId; kind = rule.Kind;
                break;
            }
        }
        if (kind != AlertNoiseKind.None) counters.AlertsNoise++;

        return new NormalizedAlert
        {
            DedupKey = ComputeDedupKey(header, description),
            HeaderText = header,
            DescriptionText = description,
            Url = url,
            Cause = cause, Effect = effect, Severity = severity,
            InformedTripIds = informedTripIds,
            InformedStopIds = informedStopIds,
            IsNoise = kind != AlertNoiseKind.None,
            NoiseRuleId = ruleId
        };
    }

    /// <summary>Standard-Regelwerk (gemessen 21.08.2026, verifikation/verify_alerts_output.txt).</summary>
    public static AlertNormalizer CreateDefault() => new(DefaultNoiseRules);

    public static readonly IReadOnlyList<AlertNoiseRule> DefaultNoiseRules = new AlertNoiseRule[]
    {
        new("attr_gtfsde", AlertNoiseKind.Attribution, "desc_prefix", "Echtzeitdaten aufbereitet von GTFS.de"),
        new("amen_niederflur", AlertNoiseKind.Amenity, "header_equals", "Niederflur"),
        new("amen_niederflur2", AlertNoiseKind.Amenity, "header_equals", "NIEDERFLUR"),
        new("amen_rollstuhl", AlertNoiseKind.Amenity, "header_equals", "Rollstuhlgeeignet"),
        new("amen_bordrest", AlertNoiseKind.Amenity, "header_equals", "Bordrestaurant"),
        new("amen_wlan", AlertNoiseKind.Amenity, "header_equals", "WLAN verfügbar"),
        new("amen_klima", AlertNoiseKind.Amenity, "header_equals", "Klimaanlage"),
        new("amen_2kl", AlertNoiseKind.Amenity, "header_equals", "nur 2. Kl."),
        new("amen_behind", AlertNoiseKind.Amenity, "header_equals", "Behindertengerecht"),
        new("amen_behind2", AlertNoiseKind.Amenity, "header_equals", "Behindertengerechtes Fahrzeug"),
        new("amen_fahrrad", AlertNoiseKind.Amenity, "header_equals", "Fahrradmitnahme begrenzt möglich"),
        new("amen_fahrrad2", AlertNoiseKind.Amenity, "header_equals", "Fahrradmitnahme reservierungspflichtig"),
        new("amen_checkin", AlertNoiseKind.Amenity, "header_equals", "Komfort Check-in verfügbar - wenn möglich bitte einchecken"),
        new("amen_einstieg", AlertNoiseKind.Amenity, "header_equals", "Fahrzeuggebundene Einstiegshilfe vorhanden"),
    };
}
