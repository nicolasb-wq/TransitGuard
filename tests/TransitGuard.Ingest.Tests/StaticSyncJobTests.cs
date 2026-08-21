using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TransitGuard.Core.Geo;
using TransitGuard.Core.Abstractions;
using TransitGuard.Ingest.Jobs;
using Xunit;

namespace TransitGuard.Ingest.Tests;

public sealed class StaticSyncJobTests
{
    private sealed class FakeHttp : HttpMessageHandler
    {
        private readonly byte[] _body;
        public FakeHttp(byte[] body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_body) });
    }

    private static readonly GeoBoundingBox Hamburg = new(53.30, 53.85, 9.55, 10.50);

    [Fact]
    public async Task Sync_befuellt_Stores_und_aktuiert_Build()
    {
        var whitelist = new MutableWhitelist();
        var schedules = new MutableSchedules();
        var stops = new MutableStops();
        var builds = new InMemoryStaticBuildStore();
        var job = new StaticSyncJob(whitelist, schedules, stops, builds, new LoggingAlarmSink(), NullLogger<StaticSyncJob>.Instance);
        var zip = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "static_mini.zip"));

        var result = await job.RunAsync(new HttpClient(new FakeHttp(zip)), "https://example/static.zip", "hamburg", Hamburg);

        Assert.True(result.Success);
        Assert.Equal(3, whitelist.Count);
        Assert.Equal(3, ((System.Collections.IDictionary)schedules.Inner).Count);
        Assert.Equal(4, stops.Count);
        Assert.NotNull(builds.ActiveBuildId("hamburg"));
    }

    [Fact]
    public async Task Sync_bei_HTTP_Fehler_Alarm_und_kein_Swap()
    {
        var whitelist = new MutableWhitelist();
        var builds = new InMemoryStaticBuildStore();
        var alarms = new RecordingAlarms();
        var job = new StaticSyncJob(whitelist, new MutableSchedules(), new MutableStops(), builds, alarms, NullLogger<StaticSyncJob>.Instance);
        var failing = new HttpClient(new FailingHandler());

        var result = await job.RunAsync(failing, "https://example/kein.zip", "hamburg", Hamburg);

        Assert.False(result.Success);
        Assert.Equal(0, whitelist.Count);
        Assert.Contains(alarms.Criticals, a => a.Contains("static-sync"));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    private sealed class RecordingAlarms : IAlarmSink
    {
        public List<string> Criticals { get; } = new();
        public void Warn(string s, string m) { }
        public void Critical(string s, string m) => Criticals.Add($"{s}: {m}");
    }

    private sealed class MutableWhitelist : ITripWhitelistProvider
    {
        private IReadOnlyDictionary<string, Core.Normalize.TripLookup> _w = new Dictionary<string, Core.Normalize.TripLookup>();
        public int Count => _w.Count;
        public IReadOnlyDictionary<string, Core.Normalize.TripLookup> GetWhitelist(string cityId) => _w;
        public void Replace(string cityId, IReadOnlyDictionary<string, Core.Normalize.TripLookup> w) => _w = w;
    }

    private sealed class MutableSchedules : ITripScheduleStore
    {
        public System.Collections.IDictionary Inner { get; } = new System.Collections.Hashtable();
        public bool TryGet(string c, string t, out Core.Journeys.TripSchedule s) { s = (Core.Journeys.TripSchedule)Inner[t]!; return Inner.Contains(t); }
        public IEnumerable<KeyValuePair<string, Core.Journeys.TripSchedule>> All(string c) { foreach (var k in Inner.Keys) yield return new((string)k, (Core.Journeys.TripSchedule)Inner[k]!); }
        public void ReplaceCity(string c, IReadOnlyDictionary<string, Core.Journeys.TripSchedule> s) { Inner.Clear(); foreach (var kv in s) Inner[kv.Key] = kv.Value; }
    }

    private sealed class MutableStops : IStopStore
    {
        private IReadOnlyList<Core.Journeys.StopInfo> _s = Array.Empty<Core.Journeys.StopInfo>();
        public int Count => _s.Count;
        public IReadOnlyList<Core.Journeys.StopInfo> Stops(string c) => _s;
        public void ReplaceCity(string c, IReadOnlyList<Core.Journeys.StopInfo> s) => _s = s;
    }
}
