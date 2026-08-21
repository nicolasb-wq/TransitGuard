using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-BILL-RESTORE: Wiederherstellung eines Abos auf einem NEUEN Gerät.
///
/// Ohne Konten (ADR-0005) ist der Restore-Code der einzige Weg, ein bezahltes
/// Abo auf ein neues Gerät zu holen. Am 22.08.2026 bei der Vertragsaufzeichnung
/// aufgefallen: <c>/v1/billing/restore</c> stand in der Auth-Middleware auf der
/// „offen"-Liste, dort wurde <c>Items["DeviceId"]</c> nie gesetzt — der
/// Controller verlangt sie aber und fiel deshalb IMMER in den 429-Zweig.
/// Jeder Gerätewechsel eines zahlenden Nutzers endete in „rate_limited".
/// </summary>
public sealed class BillingRestoreTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _f;
    public BillingRestoreTests(ApiFixture f) => _f = f;

    private static HttpRequestMessage Post(string pfad, string token, object body)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, pfad) { Content = JsonContent.Create(body) };
        m.Headers.Add("X-Device-Token", token);
        return m;
    }

    private async Task<(string RestoreCode, string AccessToken)> AboAnlegenAsync(HttpClient c, string token)
    {
        var r = await c.SendAsync(Post("/v1/billing/activate", token,
            new { platform = "web", receipt = $"R-{Guid.NewGuid():N}" }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (j.GetProperty("restore_code").GetString()!, j.GetProperty("access_token").GetString()!);
    }

    [Fact]
    public async Task T_BILL_RESTORE_1_NeuesGeraet_stellt_Abo_wieder_her()
    {
        var c = _f.Klient();
        var altesGeraet = await _f.NeuesGeraetAsync(c);
        var (code, _) = await AboAnlegenAsync(c, altesGeraet);

        // Gerätewechsel: frisches Gerät, erster Restore-Versuch überhaupt.
        var neuesGeraet = await _f.NeuesGeraetAsync(c);
        var r = await c.SendAsync(Post("/v1/billing/restore", neuesGeraet, new { restore_code = code }));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("access_token").GetString()));
        Assert.Equal("pro", j.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task T_BILL_RESTORE_2_FalscherCode_ist_403_und_nicht_429()
    {
        var c = _f.Klient();
        var geraet = await _f.NeuesGeraetAsync(c);
        var r = await c.SendAsync(Post("/v1/billing/restore", geraet, new { restore_code = "XXXX-XXXX-XXXX-XXXX" }));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("rate_limited", j.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task T_BILL_RESTORE_3_OhneGeraeteToken_ist_401_und_nicht_429()
    {
        // Ein fehlendes Gerät ist keine Ratenbegrenzung. Der alte Code hat beides
        // in denselben 429-Zweig geworfen und damit die Ursache verschleiert.
        var c = _f.Klient();
        var r = await c.PostAsync("/v1/billing/restore",
            JsonContent.Create(new { restore_code = "XXXX-XXXX-XXXX-XXXX" }));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task T_BILL_RESTORE_4_Ratenbegrenzung_greift_erst_nach_fuenf_Versuchen()
    {
        var c = _f.Klient();
        var geraet = await _f.NeuesGeraetAsync(c);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var r = await c.SendAsync(Post("/v1/billing/restore", geraet, new { restore_code = $"FALSCH-{i}" }));
            codes.Add(r.StatusCode);
        }
        // Fünf echte Versuche (403), erst der sechste wird gebremst (429).
        Assert.All(codes.Take(5), s => Assert.Equal(HttpStatusCode.Forbidden, s));
        Assert.Equal((HttpStatusCode)429, codes[5]);
    }
}
