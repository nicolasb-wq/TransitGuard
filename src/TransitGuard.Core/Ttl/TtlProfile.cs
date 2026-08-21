namespace TransitGuard.Core.Ttl;

/// <summary>
/// TTL-Profil (Server-Config, sql/0004 ttl_config). Alle Werte in Sekunden.
/// CapSeconds = null ⇒ natürliches Ende (Trip-Anker endet trip_end + Buffer; Bestandsaufnahme §5.2/F-16).
/// </summary>
public record TtlProfile(
    string Name,
    int? BaseSeconds,
    int ConfirmBonusSeconds = 300,
    int ConfirmMaxExtraSeconds = 900,
    int ContradictionGraceSeconds = 120,
    int? CapSeconds = null,
    int TripEndBufferSeconds = 120);

/// <summary>Default-Profile exakt nach docs/02-architektur §5 + sql/0004 (F-15/F-16 entschieden).</summary>
public static class TtlProfiles
{
    public const int RailStationBase = 1200, RailStationCap = 2700;
    public const int BusStationBase = 720, BusStationCap = 1800;
    public const int AtStopBase = 480, AtStopCap = 1800;
    public const int DerivedTerminusTtl = 600;

    public static readonly TtlProfile RailStation = new("rail_station", RailStationBase, CapSeconds: RailStationCap);
    public static readonly TtlProfile BusStation = new("bus_station", BusStationBase, CapSeconds: BusStationCap);
    public static readonly TtlProfile AtStop = new("at_stop", AtStopBase, CapSeconds: AtStopCap);
    public static readonly TtlProfile DerivedTerminus = new("derived_terminus", DerivedTerminusTtl,
        ConfirmBonusSeconds: 0, ConfirmMaxExtraSeconds: 0, CapSeconds: DerivedTerminusTtl);
    public static readonly TtlProfile RailTrip = new("rail_trip", BaseSeconds: null, CapSeconds: null);
    public static readonly TtlProfile BusTrip = new("bus_trip", BaseSeconds: null, CapSeconds: null);

    public static TtlProfile For(Models.AnchorType anchor, Models.VehicleKind kind, Models.ReportKind reportKind) =>
        (anchor, kind, reportKind) switch
        {
            (Models.AnchorType.Trip, Models.VehicleKind.Rail, _) => RailTrip,
            (Models.AnchorType.Trip, Models.VehicleKind.Bus, _) => BusTrip,
            (Models.AnchorType.DerivedTerminus, _, _) => DerivedTerminus,
            (_, _, Models.ReportKind.AtStop) => AtStop,
            (Models.AnchorType.Station, Models.VehicleKind.Rail, _) => RailStation,
            (Models.AnchorType.Station, Models.VehicleKind.Bus, _) => BusStation,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor), "unbekannte Kombination")
        };
}
