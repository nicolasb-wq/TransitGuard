using Microsoft.AspNetCore.SignalR;
using TransitGuard.Api.Services;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Entitlement;
using TransitGuard.Core.Gate;
using TransitGuard.Core.Models;

namespace TransitGuard.Api.Hubs;

/// <summary>
/// Realtime-Hub (docs/06): Gruppen city.{slug}.reports.free|.pro / city.{slug}.alerts.
/// Join-Regeln serverseitig erzwungen: Stadt aktiv, Kill-Switch, Ticket-Gate (H1),
/// Pro-Gruppen nur mit gültigem Entitlement (Verstöße zählen ⇒ Metrik entitlement_join_violations).
/// </summary>
public sealed class RealtimeHub(
    IFeatureFlagService flags,
    TicketGate gate,
    IEntitlementStore entitlements,
    IRealtimeDispatcher dispatcher,
    Api.Services.AccessGate access,
    IDeviceAuthStore devices) : Hub
{
    /// <summary>Trial-Lock auch im Hub (QA-Pass-1-Fund A): abgelaufene Tester empfangen KEINE Live-Kontroll-Events mehr.</summary>
    private async Task<bool> TrialLockedForConnection(string? entitlementToken)
    {
        var ctx = Context.GetHttpContext();
        var deviceToken = ctx?.Request.Query["access_token"].ToString();
        Guid deviceId = Guid.Empty;
        if (!string.IsNullOrEmpty(deviceToken)) devices.TryResolve(deviceToken, out deviceId);
        if (deviceId == Guid.Empty) return false;   // Gaeste ohne Device: nur Flight-Companion-Gruppen, Gate regelt reports
        var acc = access.AccessFor(deviceId, entitlementToken);
        if (!acc.IsLocked) return false;
        await Clients.Caller.SendAsync("control.trial_expired", new { price_eur = Core.Entitlement.TrialPolicy.MonthlyPriceEur });
        return true;
    }
    public async Task Join(string group, bool ticketConfirmed, string? entitlementToken)
    {
        if (!Core.Realtime.HubGroupRule.TryParse(group, out var parsed))
            throw new HubException("unknown_group");
        if (parsed.Family == "reports" && await TrialLockedForConnection(entitlementToken))
            throw new HubException("trial_expired");
        if (parsed.Family == "reports" && !flags.IsEnabled("reports_enabled"))
            throw new HubException("feature_disabled");

        var city = CityRegistry.BySlug(parsed.CitySlug) ?? throw new HubException("unknown_city");
        if (!city.IsActive) throw new HubException("city_inactive");

        if (Core.Realtime.HubGroupRule.RequiresTicketGate(parsed) &&
            gate.CheckRead(ticketConfirmed) is { Decision: TicketGateDecision.Block })
        {
            await Clients.Caller.SendAsync("control.ticket_gate", new { reason = "ticket_not_confirmed" });
            throw new HubException("ticket_gate_blocked");
        }

        if (Core.Realtime.HubGroupRule.RequiresPro(parsed))
        {
            if (!flags.IsEnabled("pro_tier_enabled") ||
                entitlements.Validate(entitlementToken ?? "") is not { Tier: EntitlementTier.Pro, Status: "active" })
            {
                dispatcher.EntitlementJoinViolations++;
                throw new HubException("entitlement_required");
            }
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public Task Leave(string group) => Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

    private static HubException HubException() => new("entitlement_required");
}

/// <summary>Dispatcher-Implementierung: leitet Domänen-Events an die Hub-Gruppen (Free/Pro-Split).</summary>
public sealed class SignalRRealtimeDispatcher(IHubContext<RealtimeHub> hub) : IRealtimeDispatcher
{
    private int _violations;
    public int EntitlementJoinViolations { get => _violations; set => _violations = value; }

    public async void DispatchCityReportEvent(string cityId, ReportEvent evt, object freePayload, object proPayload)
    {
        await hub.Clients.Group($"city.{cityId}.reports.free").SendAsync("report." + evt.Type.ToString().ToLowerInvariant(), freePayload);
        await hub.Clients.Group($"city.{cityId}.reports.pro").SendAsync("report." + evt.Type.ToString().ToLowerInvariant(), proPayload);
    }

    public async void DispatchControl(string cityId, string controlType, object payload) =>
        await hub.Clients.Groups($"city.{cityId}.reports.free", $"city.{cityId}.reports.pro").SendAsync(controlType, payload);
}
