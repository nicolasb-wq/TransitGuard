using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-CORS: Ohne ausdrückliche Konfiguration gibt es KEIN CORS.
///
/// Produktion liefert PWA und API unter einem Origin aus (deploy/Caddyfile,
/// handle-Blöcke). Der CORS-Schalter existiert nur für lokale Laufzeittests mit
/// Flutter-Web, das auf einem eigenen Port läuft. Dieser Test hält fest, dass
/// er im Auslieferungszustand aus ist — ein versehentlich offener Origin wäre
/// zusätzliche Angriffsfläche ohne Nutzen.
/// </summary>
public sealed class CorsTests
{
    private sealed class Fabrik(string? origins) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ApiFixtureHelfer.RepoWurzel());
            if (origins is not null) builder.UseSetting("Cors:AllowedOrigins", origins);
        }
    }

    private static HttpRequestMessage Vorabfrage(string origin)
    {
        var m = new HttpRequestMessage(HttpMethod.Options, "/v1/cities");
        m.Headers.Add("Origin", origin);
        m.Headers.Add("Access-Control-Request-Method", "GET");
        return m;
    }

    [Fact]
    public async Task T_CORS_1_StandardmaessigKeinAccessControlHeader()
    {
        using var f = new Fabrik(null);
        var r = await f.CreateClient().SendAsync(Vorabfrage("http://boese.example"));
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"),
            "Ohne Cors:AllowedOrigins darf kein CORS-Header erscheinen.");
    }

    [Fact]
    public async Task T_CORS_2_KonfigurierterOriginWirdErlaubt_andereNicht()
    {
        using var f = new Fabrik("http://localhost:4321");
        var c = f.CreateClient();

        var erlaubt = await c.SendAsync(Vorabfrage("http://localhost:4321"));
        Assert.True(erlaubt.Headers.Contains("Access-Control-Allow-Origin"));

        var fremd = await c.SendAsync(Vorabfrage("http://boese.example"));
        Assert.False(fremd.Headers.Contains("Access-Control-Allow-Origin"),
            "Nur die konfigurierten Origins dürfen durch — keine Wildcard.");
    }
}
