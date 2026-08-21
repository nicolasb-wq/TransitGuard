using TransitGuard.Core.Journeys;
using Xunit;

namespace TransitGuard.Core.Tests;

/// <summary>
/// T-STOP: Auflösung Haltestellen-ID → Anzeigename.
/// Anlass (26-build-log §): ReportsController schrieb <c>StationName = StopId</c>;
/// die Meldeliste zeigte Nutzenden dadurch „HHA1" statt „Jungfernstieg" — im
/// Browser-Rauchtest der PWA am 21.08.2026 sichtbar geworden.
/// </summary>
public sealed class StopLookupTests
{
    private static readonly IReadOnlyList<StopInfo> Stops =
    [
        new("HHA1", "Jungfernstieg", 53.554, 9.991),
        new("HHA2", "Stephansplatz", 53.559, 9.993),
    ];

    [Fact]
    public void T_STOP_1_BekannteId_liefert_Anzeigenamen() =>
        Assert.Equal("Jungfernstieg", StopLookup.DisplayName(Stops, "HHA1"));

    [Fact]
    public void T_STOP_2_UnbekannteId_faellt_auf_die_Id_zurueck() =>
        // Kein Erfinden von Namen: eine unbekannte ID bleibt sichtbar die ID.
        Assert.Equal("HHX9", StopLookup.DisplayName(Stops, "HHX9"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void T_STOP_3_LeereId_liefert_null(string? id) =>
        Assert.Null(StopLookup.DisplayName(Stops, id));

    [Fact]
    public void T_STOP_4_LeereHaltestellenliste_faellt_auf_die_Id_zurueck() =>
        Assert.Equal("HHA1", StopLookup.DisplayName([], "HHA1"));
}
