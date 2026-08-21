namespace TransitGuard.Core.Gate;

/// <summary>
/// Ticket-First-Gate (docs/18-rechtliche-haertung H1). Weiche Durchsetzung nach Age-Gate-Muster:
/// Der Server verlangt die Erklärung (Präsenz), speichert ausschließlich Tagesaggregate.
/// </summary>
public enum TicketGateDecision { Allow, Block }

public sealed record TicketGateResult(TicketGateDecision Decision, string? ErrorCode)
{
    public static readonly TicketGateResult Allowed = new(TicketGateDecision.Allow, null);
    public static TicketGateResult BlockedRead() => new(TicketGateDecision.Block, "ticket_gate_blocked");            // 403
    public static TicketGateResult BlockedWrite() => new(TicketGateDecision.Block, "ticket_confirmation_required"); // 422
}

public sealed class TicketGate
{
    public bool Enabled { get; set; } = true;   // feature_flags-gesteuert (Ops kann Gate testweise abschalten)

    public TicketGateResult CheckWrite(bool? ticketConfirmed) =>
        !Enabled || ticketConfirmed == true ? TicketGateResult.Allowed : TicketGateResult.BlockedWrite();

    public TicketGateResult CheckRead(bool ticketConfirmedHeader) =>
        !Enabled || ticketConfirmedHeader ? TicketGateResult.Allowed : TicketGateResult.BlockedRead();
}
