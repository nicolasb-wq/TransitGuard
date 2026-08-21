using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransitGuard.Api.Tests;

/// <summary>
/// Fährt die echte API im Lokal-Modus (In-Memory-Stores, Fixture-Fahrplan) über
/// WebApplicationFactory hoch. Kein Netz, kein Port — aber dieselbe Middleware-,
/// Routing- und Serialisierungskette wie in Produktion. Genau die Schicht, in der
/// die vier Launch-Blocker vom 21.08.2026 saßen.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Das Arbeitsverzeichnis der Tests ist bin/…; die API sucht die Fixture
        // relativ zum ContentRoot.
        builder.UseContentRoot(RepoWurzel());
    }

    private static string RepoWurzel() => ApiFixtureHelfer.RepoWurzel();

    /// <summary>Frisches Gerät samt Token — jeder Test startet unbelastet.</summary>
    public async Task<string> NeuesGeraetAsync(HttpClient c)
    {
        var r = await c.PostAsync("/v1/devices", null);
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        return j.GetProperty("device_token").GetString()!;
    }

    public HttpClient Klient() => CreateClient();
}

/// <summary>Gemeinsame Helfer für alle Testfabriken.</summary>
public static class ApiFixtureHelfer
{
    /// <summary>Repo-Wurzel finden — die API sucht ihre Fixture relativ zum ContentRoot.</summary>
    public static string RepoWurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TransitGuard.sln"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }
}
