using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Normalize;
using TransitGuard.Ingest.GtfsRt;
using TransitGuard.Ingest.Jobs;
using TransitRealtime;
using Xunit;

namespace TransitGuard.Ingest.Tests;

/// <summary>Zeichnet auf, welche Kopfzeilen der Job tatsaechlich sendet.</summary>
internal sealed class AufzeichnenderHandler(Func<int, HttpResponseMessage> antwort) : HttpMessageHandler
{
    public List<string?> GesehenIfNoneMatch { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        GesehenIfNoneMatch.Add(req.Headers.IfNoneMatch.FirstOrDefault()?.Tag);
        return Task.FromResult(antwort(GesehenIfNoneMatch.Count));
    }
}

public class PollRealtimeJobTests
{
    private static byte[] MiniFeed(long ts = 1_700_000_000)
    {
        var m = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = (ulong)ts } };
        m.Entity.Add(new FeedEntity
        {
            Id = "e1",
            TripUpdate = new TripUpdate
            {
                Trip = new TripDescriptor { TripId = "T1", StartDate = "20260824" },
                StopTimeUpdate = { new TripUpdate.Types.StopTimeUpdate { StopId = "S1", Departure = new TripUpdate.Types.StopTimeEvent { Delay = 60 } } }
            }
        });
        return m.ToByteArray();
    }

    private sealed class Senke : IIngestMetricsSink
    {
        public List<IngestMetricRow> Zeilen { get; } = new();
        public void Write(IngestMetricRow r) => Zeilen.Add(r);
    }

    private sealed class Zustand : ITripStateStore
    {
        private readonly Dictionary<string, NormalizedTripUpdate> _d = new();
        public NormalizedTripUpdate? Get(string c, string t, string s) => _d.GetValueOrDefault($"{c}|{t}|{s}");
        public void Upsert(string c, NormalizedTripUpdate t) => _d[$"{c}|{t.TripId}|{t.StartDate}"] = t;
        public int ReplaceCity(string c, IReadOnlyDictionary<string, NormalizedTripUpdate> n) => n.Count;
        public IEnumerable<NormalizedTripUpdate> All(string c) => _d.Values;
    }

    private sealed class AlertSpeicher : TransitGuard.Core.Abstractions.IAlertStore
    {
        public List<(string City, NormalizedAlert A)> Eintraege { get; } = new();
        public void Upsert(string cityId, NormalizedAlert a, DateTimeOffset seenAt) => Eintraege.Add((cityId, a));
        public IReadOnlyList<NormalizedAlert> Visible(string c, DateTimeOffset n, int h = 6) =>
            Eintraege.Where(e => e.City == c && !e.A.IsNoise).Select(e => e.A).ToList();
    }

    private sealed class Wl : ITripWhitelistProvider
    {
        private IReadOnlyDictionary<string, TripLookup> _w =
            new Dictionary<string, TripLookup> { ["T1"] = new TripLookup("R1", 0, "S1", 3600) };
        public IReadOnlyDictionary<string, TripLookup> GetWhitelist(string c) => _w;
        public void Replace(string c, IReadOnlyDictionary<string, TripLookup> w) => _w = w;
    }

    /// <summary>
    /// T-ETAG-1 — Der If-None-Match-Kopf muss den Wechsel der Job-Instanz ueberleben.
    ///
    /// Hintergrund (gemessen 24.08.2026): PollRealtimeJob ist in der API <c>Scoped</c>
    /// registriert, Hangfire oeffnet je Ausfuehrung einen eigenen Scope. Solange der ETag in
    /// einem Instanzfeld lag, war er beim naechsten Lauf immer <c>null</c> — der 304-Zweig
    /// war toter Code. Dieser Test baut deshalb bewusst ZWEI Instanzen, so wie DI es tut.
    /// Ein Test mit nur einer Instanz haette den Fehler nie gesehen.
    /// </summary>
    [Fact]
    public async Task T_ETAG_1_Zweiter_Lauf_sendet_ETag_auch_bei_neuer_Job_Instanz()
    {
        var handler = new AufzeichnenderHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MiniFeed()) };
            r.Headers.ETag = new EntityTagHeaderValue("\"e1\"");
            return r;
        });
        using var http = new HttpClient(handler);
        var etags = new FeedEtagStore();           // Singleton, wie in Program.cs
        var senke = new Senke();

        PollRealtimeJob Neu() => new(new FeedFetcher(http), new Zustand(), senke, new FeedHealthTracker(),
            new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()),
            new Wl(), SystemClock.Instance, etags, new AlertSpeicher());

        await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");
        await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");   // frische Instanz!

        Assert.Equal(2, handler.GesehenIfNoneMatch.Count);
        Assert.Null(handler.GesehenIfNoneMatch[0]);                    // erster Lauf: noch kein ETag bekannt
        Assert.Equal("\"e1\"", handler.GesehenIfNoneMatch[1]);         // zweiter Lauf: ETag MUSS gesendet werden
    }

    /// <summary>T-ETAG-2 — ETags werden je Feed-URL getrennt gehalten (zwei Staedte, zwei Feeds).</summary>
    [Fact]
    public async Task T_ETAG_2_ETags_sind_je_Feed_URL_getrennt()
    {
        var handler = new AufzeichnenderHandler(n =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MiniFeed()) };
            r.Headers.ETag = new EntityTagHeaderValue($"\"e{n}\"");
            return r;
        });
        using var http = new HttpClient(handler);
        var etags = new FeedEtagStore();
        var senke = new Senke();
        PollRealtimeJob Neu() => new(new FeedFetcher(http), new Zustand(), senke, new FeedHealthTracker(),
            new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()),
            new Wl(), SystemClock.Instance, etags, new AlertSpeicher());

        await Neu().RunAsync("https://a.test/rt.pb", "a");     // 1 → ETag "e1"
        await Neu().RunAsync("https://b.test/rt.pb", "b");     // 2 → kein ETag gesendet, bekommt "e2"
        await Neu().RunAsync("https://a.test/rt.pb", "a");     // 3 → muss "e1" senden, nicht "e2"

        Assert.Null(handler.GesehenIfNoneMatch[0]);
        Assert.Null(handler.GesehenIfNoneMatch[1]);
        Assert.Equal("\"e1\"", handler.GesehenIfNoneMatch[2]);
    }

    /// <summary>
    /// T-ETAG-3 — Auf 304 darf der Job den gemerkten ETag NICHT verlieren.
    /// (FeedFetcher gibt bei 304 den ETag der Antwort zurueck; liefert der Server keinen,
    /// wuerde ein naiver Set(null) den Speicher leeren und der naechste Lauf zoege wieder
    /// den vollen Feed.)
    /// </summary>
    [Fact]
    public async Task T_ETAG_3_304_ohne_ETag_loescht_den_gemerkten_ETag_nicht()
    {
        var handler = new AufzeichnenderHandler(n =>
        {
            if (n == 1)
            {
                var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MiniFeed()) };
                ok.Headers.ETag = new EntityTagHeaderValue("\"e1\"");
                return ok;
            }
            return new HttpResponseMessage(HttpStatusCode.NotModified);   // bewusst OHNE ETag-Kopf
        });
        using var http = new HttpClient(handler);
        var etags = new FeedEtagStore();
        var senke = new Senke();
        PollRealtimeJob Neu() => new(new FeedFetcher(http), new Zustand(), senke, new FeedHealthTracker(),
            new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()),
            new Wl(), SystemClock.Instance, etags, new AlertSpeicher());

        await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");
        await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");
        await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");

        Assert.Equal("\"e1\"", handler.GesehenIfNoneMatch[1]);
        Assert.Equal("\"e1\"", handler.GesehenIfNoneMatch[2]);   // dritter Lauf kennt den ETag weiterhin
    }

    /// <summary>
    /// T-POLL-ERR-1 — Ein leerer Body (Status 200, 0 Bytes) darf den Job nicht sprengen.
    ///
    /// Gemessen am 24.08.2026 gegen einen lokalen Testserver: der Job warf eine
    /// <c>NullReferenceException</c>. Google.Protobuf parst 0 Bytes klaglos zu einer
    /// FeedMessage OHNE Header; der Zugriff auf <c>msg.Header.Timestamp</c> lief ins Leere.
    /// Folge in Produktion: keine Metrikzeile, kein Fehlerzaehler, kein Circuit-Breaker —
    /// der Ausfall waere nur im Hangfire-Dashboard sichtbar gewesen.
    /// </summary>
    [Fact]
    public async Task T_POLL_ERR_1_Leerer_Body_bricht_den_Job_nicht_ab()
    {
        var senke = new Senke();
        var tracker = new FeedHealthTracker();
        var r = await JobMitAntwort(Array.Empty<byte>(), senke, tracker).RunAsync("https://feed.test/rt.pb", "hamburg");

        Assert.False(r.Success);
        Assert.Single(senke.Zeilen);                                  // eine Metrikzeile MUSS entstehen
        Assert.False(string.IsNullOrEmpty(senke.Zeilen[0].Error));    // und sie muss den Fehler nennen
        Assert.Equal(1, tracker.ConsecutiveFailures);                 // sonst greift der Circuit-Breaker nie
    }

    /// <summary>
    /// T-POLL-ERR-2 — Kaputtes Protobuf ebenso. Ein halb uebertragener oder von einem
    /// Zwischenspeicher verstuemmelter Feed ist keine Ausnahme, sondern Betriebsalltag.
    /// </summary>
    [Fact]
    public async Task T_POLL_ERR_2_Kaputtes_Protobuf_bricht_den_Job_nicht_ab()
    {
        var muell = new byte[512];
        new Random(42).NextBytes(muell);
        muell[0] = 0xFF; muell[1] = 0xFF;      // kein gueltiger Protobuf-Tag

        var senke = new Senke();
        var tracker = new FeedHealthTracker();
        var r = await JobMitAntwort(muell, senke, tracker).RunAsync("https://feed.test/rt.pb", "hamburg");

        Assert.False(r.Success);
        Assert.Single(senke.Zeilen);
        Assert.Contains("parse", senke.Zeilen[0].Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, tracker.ConsecutiveFailures);
    }

    /// <summary>
    /// T-POLL-ERR-3 — Nach einem kaputten Feed muss der naechste gueltige wieder greifen,
    /// und der Fehlerzaehler muss zurueckgehen. Ein Job, der sich nach einem Fehler nicht
    /// mehr faengt, ist genauso kaputt wie einer, der abstuerzt.
    /// </summary>
    [Fact]
    public async Task T_POLL_ERR_3_Nach_dem_Fehler_faengt_sich_der_Job()
    {
        var senke = new Senke();
        var tracker = new FeedHealthTracker();
        var etags = new FeedEtagStore();
        var reihe = new Queue<byte[]>(new[] { Array.Empty<byte>(), MiniFeed() });
        var handler = new AufzeichnenderHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(reihe.Dequeue()) });
        using var http = new HttpClient(handler);
        PollRealtimeJob Neu() => new(new FeedFetcher(http), new Zustand(), senke, tracker,
            new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()),
            new Wl(), SystemClock.Instance, etags, new AlertSpeicher());

        var kaputt = await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");
        var gut = await Neu().RunAsync("https://feed.test/rt.pb", "hamburg");

        Assert.False(kaputt.Success);
        Assert.True(gut.Success);
        Assert.Equal(1, gut.Entities);
        Assert.Equal(0, tracker.ConsecutiveFailures);
    }

    private static PollRealtimeJob JobMitAntwort(byte[] koerper, Senke senke, FeedHealthTracker tracker)
    {
        var handler = new AufzeichnenderHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(koerper) });
        var http = new HttpClient(handler);
        return new PollRealtimeJob(new FeedFetcher(http), new Zustand(), senke, tracker,
            new TripUpdateNormalizer(), new AlertNormalizer(Array.Empty<AlertNoiseRule>()),
            new Wl(), SystemClock.Instance, new FeedEtagStore(), new AlertSpeicher());
    }

    private static byte[] FeedMitAlerts()
    {
        var m = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = 1_700_000_000 } };
        void Alert(string id, string kopf, string text, string tripId)
        {
            var a = new Alert { HeaderText = new TranslatedString(), DescriptionText = new TranslatedString() };
            a.HeaderText.Translation.Add(new TranslatedString.Types.Translation { Text = kopf, Language = "de" });
            a.DescriptionText.Translation.Add(new TranslatedString.Types.Translation { Text = text, Language = "de" });
            a.InformedEntity.Add(new EntitySelector { Trip = new TripDescriptor { TripId = tripId } });
            m.Entity.Add(new FeedEntity { Id = id, Alert = a });
        }
        Alert("a1", "Bauarbeiten", "Bauarbeiten zwischen A und B", "T1");            // Stadt, echt
        Alert("a2", "Bordrestaurant", "Bordrestaurant", "T1");                        // Stadt, Rauschen
        Alert("a3", "Weichenstoerung", "Weichenstoerung", "T_FREMD");                 // andere Stadt
        return m.ToByteArray();
    }

    /// <summary>
    /// T-ALERT-1 — Alerts aus dem Feed landen wirklich im Stadt-Speicher.
    ///
    /// Bis zum 24.08.2026 baute der Poll-Job die Alert-Liste auf und warf sie am Ende der
    /// Methode weg. <c>/v1/cities/{slug}/alerts</c> lieferte deshalb IMMER eine leere Liste —
    /// bei 96.663 Alerts je Abruf im echten Feed. Der Endpunkt sah funktionsfaehig aus,
    /// weil "leere Liste" ein gueltiges Ergebnis ist; genau deshalb stand er im Vertrag
    /// als „unbeobachtet".
    /// </summary>
    [Fact]
    public async Task T_ALERT_1_Stadt_Alerts_landen_im_Speicher()
    {
        var speicher = new AlertSpeicher();
        var handler = new AufzeichnenderHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FeedMitAlerts()) });
        using var http = new HttpClient(handler);
        var job = new PollRealtimeJob(new FeedFetcher(http), new Zustand(), new Senke(), new FeedHealthTracker(),
            new TripUpdateNormalizer(), AlertNormalizer.CreateDefault(), new Wl(), SystemClock.Instance,
            new FeedEtagStore(), speicher);

        var r = await job.RunAsync("https://feed.test/rt.pb", "hamburg");

        Assert.True(r.Success);
        Assert.NotEmpty(speicher.Eintraege);
        Assert.Contains(speicher.Eintraege, e => e.A.HeaderText == "Bauarbeiten");
        Assert.All(speicher.Eintraege, e => Assert.Equal("hamburg", e.City));
    }

    /// <summary>
    /// T-ALERT-2 — Und zwar NUR die der Stadt, und NUR ohne Rauschen.
    /// Ohne Stadtfilter saehe Hamburg die Meldungen ganz Deutschlands (530 verschiedene je
    /// Abruf gemessen); ohne Rauschfilter bestuenden 10 von 29 aus „Bordrestaurant" und
    /// „Fahrradmitnahme" (Messung docs/messungen/b_alerts.txt).
    /// </summary>
    [Fact]
    public async Task T_ALERT_2_Fremde_Stadt_und_Rauschen_bleiben_draussen()
    {
        var speicher = new AlertSpeicher();
        var handler = new AufzeichnenderHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FeedMitAlerts()) });
        using var http = new HttpClient(handler);
        var job = new PollRealtimeJob(new FeedFetcher(http), new Zustand(), new Senke(), new FeedHealthTracker(),
            new TripUpdateNormalizer(), AlertNormalizer.CreateDefault(), new Wl(), SystemClock.Instance,
            new FeedEtagStore(), speicher);

        await job.RunAsync("https://feed.test/rt.pb", "hamburg");

        Assert.DoesNotContain(speicher.Eintraege, e => e.A.HeaderText == "Weichenstoerung");  // T_FREMD, nicht in der Whitelist
        Assert.DoesNotContain(speicher.Eintraege, e => e.A.HeaderText == "Bordrestaurant");   // Ausstattungs-Rauschen
        Assert.Single(speicher.Eintraege);
    }
}
