using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-TRANSFER: Umstiegsverbindungen über die echte HTTP-Kette.
///
/// Bis 22.08.2026 lagen alle Fixture-Haltestellen auf EINER Linie — jedes Paar
/// war direkt verbunden, <c>transfer_connections</c> war in jedem Testlauf leer.
/// Genau dort saß Launch-Blocker 1: die PWA las <c>leg_a.RouteId</c>, der Server
/// sendet <c>leg_a.route_id</c>. Kein Test konnte das sehen.
/// Die Fixture erzwingt jetzt HHA1 → HHA5 über einen Umstieg in HHA3.
/// </summary>
public sealed class TransferConnectionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _f;
    public TransferConnectionTests(ApiFixture f) => _f = f;

    private async Task<JsonElement> SucheAsync(string von, string nach)
    {
        var c = _f.Klient();
        var token = await _f.NeuesGeraetAsync(c);
        var m = new HttpRequestMessage(HttpMethod.Post, "/v1/journeys/search")
        { Content = JsonContent.Create(new { from_stop_id = von, to_stop_id = nach }) };
        m.Headers.Add("X-Device-Token", token);
        var r = await c.SendAsync(m);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task T_TRANSFER_1_HHA1_nach_HHA5_liefert_genau_einen_Umstieg()
    {
        var j = await SucheAsync("HHA1", "HHA5");
        Assert.Empty(j.GetProperty("direct_connections").EnumerateArray());
        var umstiege = j.GetProperty("transfer_connections").EnumerateArray().ToList();
        Assert.NotEmpty(umstiege);
    }

    [Fact]
    public async Task T_TRANSFER_2_BeideTeilstrecken_tragen_ihre_Liniennummer()
    {
        var j = await SucheAsync("HHA1", "HHA5");
        var u = j.GetProperty("transfer_connections").EnumerateArray().First();

        // Der Kern des alten Fehlers: die Schlüssel heißen snake_case.
        var a = u.GetProperty("leg_a");
        var b = u.GetProperty("leg_b");
        Assert.Equal("R_U1", a.GetProperty("route_id").GetString());
        Assert.Equal("R_U2", b.GetProperty("route_id").GetString());
        Assert.False(a.TryGetProperty("RouteId", out _), "PascalCase darf im Vertrag nicht vorkommen.");
        Assert.False(b.TryGetProperty("RouteId", out _), "PascalCase darf im Vertrag nicht vorkommen.");

        // Fruehere Fassung verglich feste Zeichenketten ("Kellinghusenstrasse"/"Farmsen").
        // Das war ZEITABHAENGIG und nur durch Glueck gruen: die Fixture enthaelt neben den
        // 72 Taktfahrten (Ziel "Kellinghusenstrasse") auch die Altfahrten T_HH_1/T_HH_3 auf
        // derselben Linie mit Ziel "Ohlsdorf". Welche davon die naechste ist, haengt an der
        // Uhrzeit des Testlaufs — am 24.08.2026 um 10:0x fiel der Test deshalb um, ohne dass
        // sich am Code etwas geaendert hatte. Fuenf QA-Durchlaeufe hintereinander liefen
        // innerhalb weniger Minuten und konnten das nicht finden.
        // Geprueft wird jetzt die EIGENSCHAFT statt des Zufallswerts: jede Teilstrecke traegt
        // ein Ziel, und es ist eines der Ziele IHRER Linie.
        var zielA = a.GetProperty("headsign").GetString();
        var zielB = b.GetProperty("headsign").GetString();
        Assert.False(string.IsNullOrWhiteSpace(zielA), "leg_a ohne headsign");
        Assert.False(string.IsNullOrWhiteSpace(zielB), "leg_b ohne headsign");
        Assert.Contains(zielA, new[] { "Kellinghusenstrasse", "Ohlsdorf", "Ohlsdorf (Abend)" });   // R_U1
        Assert.Equal("Farmsen", zielB);                                                            // R_U2 hat nur dieses Ziel
        Assert.NotEqual(zielA, zielB);
    }

    [Fact]
    public async Task T_TRANSFER_3_Verkettung_ist_zeitlich_plausibel()
    {
        var j = await SucheAsync("HHA1", "HHA5");
        var u = j.GetProperty("transfer_connections").EnumerateArray().First();
        var a = u.GetProperty("leg_a");
        var b = u.GetProperty("leg_b");

        Assert.Equal("HHA1", a.GetProperty("board_stop").GetString());
        Assert.Equal("HHA3", a.GetProperty("alight_stop").GetString());
        Assert.Equal("HHA3", b.GetProperty("board_stop").GetString());
        Assert.Equal("HHA5", b.GetProperty("alight_stop").GetString());
        Assert.Equal("HHA3", u.GetProperty("transfer_stop_id").GetString());

        var ankunftA = a.GetProperty("alight_at").GetDateTimeOffset();
        var abfahrtB = b.GetProperty("board_at").GetDateTimeOffset();
        var wartezeit = u.GetProperty("wait_seconds").GetInt32();

        Assert.True(abfahrtB > ankunftA, "Bein B darf nicht vor der Ankunft von Bein A abfahren.");
        Assert.Equal((int)(abfahrtB - ankunftA).TotalSeconds, wartezeit);
        Assert.True(wartezeit >= 120, $"Umstiegspuffer unterschritten: {wartezeit} s");

        var gesamt = u.GetProperty("total_seconds").GetInt32();
        var abfahrtA = a.GetProperty("board_at").GetDateTimeOffset();
        var ankunftB = b.GetProperty("alight_at").GetDateTimeOffset();
        Assert.Equal((int)(ankunftB - abfahrtA).TotalSeconds, gesamt);
    }

    [Fact]
    public async Task T_TRANSFER_4_Direktstrecke_braucht_keinen_Umstieg()
    {
        // Gegenprobe: wo eine Linie durchfährt, darf der Router nicht einspringen.
        var j = await SucheAsync("HHA1", "HHA3");
        Assert.NotEmpty(j.GetProperty("direct_connections").EnumerateArray());
    }
}
