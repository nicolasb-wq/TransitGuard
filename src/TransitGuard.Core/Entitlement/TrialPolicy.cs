using TransitGuard.Core.Abstractions;

namespace TransitGuard.Core.Entitlement;

public enum AccessLevel { Trial, Subscriber, Locked }

/// <summary>
/// 14-Tage-Testzeitraum ab erster Geräte-Nutzung, danach Abo 2,99 €/Monat (Produktentscheidung
/// Auftrag 21.08. — ersetzt die alte Free-forever-Matrix, Details/Beflagung: ADR-0014, 08-entitlement v1.3).
/// Geräte-gebunden, kontolos: Trial-Start = Device-Issue-Datum (Besitz-Modell bleibt: Abo via Token).
/// </summary>
public sealed class TrialPolicy(int trialDays = 14)
{
    public int TrialDays { get; } = trialDays;
    public const decimal MonthlyPriceEur = 2.99m;

    public bool TrialActive(DateTimeOffset deviceIssuedAtUtc, IClock clock) =>
        clock.UtcNow < deviceIssuedAtUtc.AddDays(TrialDays);

    /// <summary>Zugriffsstufe: Trial (volle Nutzung) → Subscriber (valides Abo) → Locked (nach Test).</summary>
    public AccessLevel AccessFor(DateTimeOffset? deviceIssuedAtUtc, bool hasActiveSubscription, IClock clock)
    {
        if (deviceIssuedAtUtc is { } issued && TrialActive(issued, clock)) return AccessLevel.Trial;
        return hasActiveSubscription ? AccessLevel.Subscriber : AccessLevel.Locked;
    }

    public int TrialDaysRemaining(DateTimeOffset deviceIssuedAtUtc, IClock clock) =>
        Math.Max(0, (int)Math.Ceiling((deviceIssuedAtUtc.AddDays(TrialDays) - clock.UtcNow).TotalDays));
}
