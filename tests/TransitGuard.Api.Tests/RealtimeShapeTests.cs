using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Models;
using TransitGuard.Core.Normalize;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-RTSHAPE: Feldform der Echtzeit-Anteile einer Abfahrt.
///
/// Diese Felder sind über die Vertragsaufzeichnung NICHT erreichbar: der
/// Lokal-Modus hat keinen laufenden Ingest, also ist <c>realtime</c> immer false
/// und <c>delay_s</c>/<c>estimated_time</c> fehlen (WhenWritingNull), und ohne
/// passende aktive Meldung bleibt <c>warnings</c> leer. Statt einen Ersatzbeweis
/// zu bauen, wird der Zustand hier GESETZT und die Form danach festgenagelt.
/// Damit sind auch diese Felder gegen stille Umbenennung gesichert.
/// </summary>
public sealed class RealtimeShapeTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _f;
    public RealtimeShapeTests(ApiFixture f) => _f = f;

    private const string Stadt = "hamburg";

    /// <summary>Setzt Echtzeit-Zustand und eine Trip-Meldung auf eine reale Fixture-Fahrt.</summary>
    private (string TripId, string StartDate) ZustandSetzen(int verspaetungS)
    {
        var sp = _f.Services;
        var schedules = sp.GetRequiredService<ITripScheduleStore>();
        var rt = sp.GetRequiredService<ITripStateStore>();
        var reports = sp.GetRequiredService<IReportRepository>();
        var uhr = sp.GetRequiredService<TransitGuard.Core.Abstractions.IClock>();

        var stadtId = TransitGuard.Api.Services.CityRegistry.BySlug(Stadt)!.CityId;
        var heute = uhr.UtcNow.ToString("yyyyMMdd");

        // Eine Fahrt wählen, die HHA1 vor HHA3 bedient (dann liegt die Meldung an
        // HHA3 VOR dem Nutzer und erzeugt eine Warnung) UND deren Abfahrt noch
        // bevorsteht — vergangene Fahrten stehen nicht in der Abfahrtstafel.
        var jetztS = (int)uhr.UtcNow.TimeOfDay.TotalSeconds;
        var (tripId, plan) = schedules.All(stadtId)
            .Where(kv => kv.Value.IndexOf("HHA1") is { } a && kv.Value.IndexOf("HHA3") is { } b && b > a)
            .Where(kv => kv.Value.Stops[kv.Value.IndexOf("HHA1")!.Value].DepartureS > jetztS + 60)
            .OrderBy(kv => kv.Value.Stops[kv.Value.IndexOf("HHA1")!.Value].DepartureS)
            .FirstOrDefault();
        Assert.False(tripId is null,
            "Keine künftige Fahrt HHA1→HHA3 in der Fixture — build_static_mini.py fährt im 20-Minuten-Takt, "
            + "das darf nur kurz vor Mitternacht UTC scheitern.");

        rt.Upsert(stadtId, new NormalizedTripUpdate
        {
            TripId = tripId, StartDate = heute, RouteId = plan.RouteId,
            StopIds = plan.Stops.Select(s => s.StopId).ToList(),
            LastStopId = "HHA1", LastDelaySeconds = verspaetungS, HasDelayData = true,
            FeedSeenAt = uhr.UtcNow,
        });

        reports.Add(new Report
        {
            CreatedAt = uhr.UtcNow, ExpiresAt = uhr.UtcNow.AddMinutes(30),
            CityId = stadtId, AnchorType = AnchorType.Trip,
            TripId = tripId, TripStartDate = heute, RouteId = plan.RouteId,
            StationId = "HHA3", StationName = "Kellinghusenstrasse",
            Kind = ReportKind.InVehicle, VehicleKind = VehicleKind.Rail,
            InspectorCount = 1, ReporterDeviceId = Guid.NewGuid(),
            Status = ReportStatus.Active, Visible = true,
        });

        return (tripId, heute);
    }

    private async Task<JsonElement> AbfahrtenAsync(string stopId)
    {
        var c = _f.Klient();
        var token = await _f.NeuesGeraetAsync(c);
        var m = new HttpRequestMessage(HttpMethod.Get, $"/v1/stops/{stopId}/departures?limit=50");
        m.Headers.Add("X-Device-Token", token);
        m.Headers.Add("X-Ticket-Confirmed", "true");
        var r = await c.SendAsync(m);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task T_RTSHAPE_1_EchtzeitFelder_heissen_delay_s_und_estimated_time()
    {
        var (tripId, _) = ZustandSetzen(180);
        var liste = await AbfahrtenAsync("HHA1");

        var d = liste.EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("trip_ref").GetProperty("trip_id").GetString() == tripId);
        Assert.True(d.ValueKind is JsonValueKind.Object,
            $"Fahrt {tripId} nicht unter den Abfahrten — Fixture/Zeitfenster prüfen.");

        Assert.True(d.GetProperty("realtime").GetBoolean());
        Assert.Equal(180, d.GetProperty("delay_s").GetInt32());
        Assert.True(d.TryGetProperty("estimated_time", out var est) && est.ValueKind == JsonValueKind.String);

        // Die Soll-Zeit bleibt daneben stehen — der Client zeigt beides.
        Assert.True(d.TryGetProperty("scheduled_time", out _));
        // Keine PascalCase-Zwillinge im Vertrag.
        Assert.False(d.TryGetProperty("DelayS", out _));
        Assert.False(d.TryGetProperty("EstimatedTime", out _));
    }

    private async Task<JsonElement> FahrtensucheAsync(string von, string nach)
    {
        var c = _f.Klient();
        var token = await _f.NeuesGeraetAsync(c);
        var m = new HttpRequestMessage(HttpMethod.Post, "/v1/journeys/search")
        { Content = JsonContent.Create(new { from_stop_id = von, to_stop_id = nach }) };
        m.Headers.Add("X-Device-Token", token);
        m.Headers.Add("X-Ticket-Confirmed", "true");
        var r = await c.SendAsync(m);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task T_RTSHAPE_0_Abfahrtstafel_liefert_grundsaetzlich_keine_Warnungen()
    {
        // Vertragsfakt, kein Fehler: JourneyService.DeparturesFrom setzt Warnings
        // nicht — Kontroll-Warnungen entstehen ausschliesslich in der Fahrtensuche.
        // Festgehalten, damit niemand eine Oberflaeche auf die Abfahrtstafel baut
        // und sich wundert, dass dort nie gewarnt wird.
        var (tripId, _) = ZustandSetzen(60);
        var liste = await AbfahrtenAsync("HHA1");
        var d = liste.EnumerateArray()
            .First(x => x.GetProperty("trip_ref").GetProperty("trip_id").GetString() == tripId);
        Assert.Empty(d.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task T_RTSHAPE_2_Warnung_traegt_die_vier_vertraglichen_Felder()
    {
        var (tripId, _) = ZustandSetzen(60);
        // Die Taktfahrten bedienen HHA1→HHA2→HHA3; HHA4 liegt nicht auf ihrer Strecke.
        var ergebnis = await FahrtensucheAsync("HHA1", "HHA3");

        var warnungen = ergebnis.GetProperty("direct_connections").EnumerateArray()
            .SelectMany(c => c.GetProperty("next_departures").EnumerateArray())
            .Where(x => x.GetProperty("trip_ref").GetProperty("trip_id").GetString() == tripId)
            .SelectMany(x => x.GetProperty("warnings").EnumerateArray())
            .ToList();
        Assert.NotEmpty(warnungen);

        var w = warnungen[0];
        Assert.Equal("HHA3", w.GetProperty("affected_stop_id").GetString());
        Assert.True(w.GetProperty("affected_stop_sequence").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(w.GetProperty("message").GetString()));
        // user_eta_seconds ist optional (fehlt ohne Prognose) — der Name aber fest.
        if (w.TryGetProperty("user_eta_seconds", out var eta))
            Assert.True(eta.ValueKind is JsonValueKind.Number or JsonValueKind.Null);
        Assert.False(w.TryGetProperty("AffectedStopId", out _));
    }

    [Fact]
    public async Task T_RTSHAPE_3_OhneEchtzeit_fehlen_die_Felder_statt_null_zu_sein()
    {
        // Vertragsdetail mit Client-Wirkung: DefaultIgnoreCondition=WhenWritingNull.
        // Deshalb ist delay_s in TypeScript optional (?) und in Dart nullable.
        var liste = await AbfahrtenAsync("HHA4");
        var ohneRt = liste.EnumerateArray().FirstOrDefault(x => !x.GetProperty("realtime").GetBoolean());
        if (ohneRt.ValueKind is not JsonValueKind.Object) return;   // Fixture-abhängig, kein Fehlschlag
        Assert.False(ohneRt.TryGetProperty("delay_s", out _));
        Assert.False(ohneRt.TryGetProperty("estimated_time", out _));
    }
}
