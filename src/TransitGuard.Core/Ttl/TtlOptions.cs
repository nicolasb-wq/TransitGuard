namespace TransitGuard.Core.Ttl;

/// <summary>
/// TTL-Konfiguration (prod: ttl_config-Tabelle; hier: Prozess-Config).
/// DevOverrideSeconds ist NUR für lokale Smoke-Tests (beweist Hangfire-Sweep in Sekunden
/// statt Minuten) — in Produktion null lassen (Validierung: Startup warnt laut).
/// </summary>
public sealed class TtlOptions
{
    public int? DevOverrideSeconds { get; set; }
}

public sealed record ConfigurableTtlProfile : TtlProfile
{
    public ConfigurableTtlProfile(TtlProfile baseProfile, int? devOverrideSeconds)
        : base(baseProfile.Name,
              devOverrideSeconds ?? baseProfile.BaseSeconds,
              baseProfile.ConfirmBonusSeconds, baseProfile.ConfirmMaxExtraSeconds,
              baseProfile.ContradictionGraceSeconds,
              baseProfile.CapSeconds, baseProfile.TripEndBufferSeconds) { }
}
