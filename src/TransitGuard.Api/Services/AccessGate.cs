using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;

namespace TransitGuard.Api.Services;

/// <summary>
/// Zugangssteuerung nach ADR-0014: 14 Tage volle Nutzung (Trial) ab Device-Ausstellung,
/// danach Abo 2,99 €/Monat (Entitlement-Token) — sonst Locked (402 trial_expired).
/// Ticket-First-Gate (H1) bleibt davon UNBERÜHRTT und gilt in jeder Stufe.
/// </summary>
public sealed class AccessGate(TrialPolicy trial, IDeviceRegistry devices, IEntitlementStore entitlements, IClock clock)
{
    public sealed record Access(AccessLevel Level, int TrialDaysRemaining)
    {
        public bool IsLocked => Level == AccessLevel.Locked;
    }

    public Access AccessFor(Guid deviceId, string? accessToken)
    {
        var issued = devices.IssuedAt(deviceId);
        var hasSub = entitlements.Validate(accessToken ?? "") is { Tier: EntitlementTier.Pro, Status: "active" };
        var level = trial.AccessFor(issued, hasSub, clock);
        var remaining = issued is { } i ? trial.TrialDaysRemaining(i, clock) : trial.TrialDays;
        return new Access(level, remaining);
    }

    public static object LockedBody(int daysUsed) => new
    {
        error = new { code = "trial_expired", message = $"Testphase abgelaufen — Abo {TrialPolicy.MonthlyPriceEur:0.00} €/Monat" },
        price_eur = TrialPolicy.MonthlyPriceEur,
        days_used = daysUsed
    };
}
