using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransitGuard.Core.Ttl;
using TransitGuard.Ingest.Jobs;

namespace TransitGuard.Api.Jobs;

/// <summary>
/// Hangfire-Registrierung (docs/07 §1): TtlSweep 15 s, PollRealtime 60 s (nur wenn
/// Ingest:Enabled — Local-Modus holt bewusst keinen 40-MB-Feed), PartitionMaint täglich,
/// StaticSync 2×/Woche. Dashboard unter /admin/hangfire (Prod: hinter Caddy-Basic-Auth, 10-deployment §7).
/// </summary>
/// <summary>
/// Zeitpläne als benannte Konstanten — damit sie testbar sind (T-CRON).
/// Am 22.08.2026 stand hier für rt-poll <c>"*/60 * * * * *"</c>; das Sekundenfeld
/// erlaubt nur 1–59, Hangfire warf beim Registrieren eine CronFormatException und
/// die API startete mit eingeschaltetem Ingest überhaupt nicht.
/// „Alle 60 Sekunden" schreibt man als Sekunde 0 jeder Minute.
/// </summary>
public static class JobSchedules
{
    public const string TtlSweep = "*/15 * * * * *";      // alle 15 s (docs/07 §1)
    public const string RtPoll = "0 * * * * *";           // jede Minute zur Sekunde 0 = alle 60 s
    public const string PartitionMaint = "0 4 * * *";     // täglich 04:00 UTC
    public const string StaticSync = "0 3 * * 2,6";       // Di+Sa 03:00 UTC

    public static IReadOnlyDictionary<string, string> Alle { get; } = new Dictionary<string, string>
    {
        ["ttl-sweep"] = TtlSweep,
        ["rt-poll"] = RtPoll,
        ["partition-maint"] = PartitionMaint,
        ["static-sync"] = StaticSync,
    };
}

public static class JobRegistration
{
    public static void RegisterRecurringJobs(this IServiceProvider services, bool ingestEnabled, string feedUrl, string cityId)
    {
        var sweep = services.GetRequiredService<TtlSweepService>();
        var health = services.GetRequiredService<FeedHealthProvider>();

        RecurringJob.AddOrUpdate<TtlSweepInvoker>("ttl-sweep",
            x => x.Run(health.Current.IsHealthy), JobSchedules.TtlSweep, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<PartitionMaintInvoker>("partition-maint", x => x.Run(), JobSchedules.PartitionMaint, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        if (ingestEnabled)
        {
            var poll = services.GetRequiredService<PollRealtimeJob>();
            RecurringJob.AddOrUpdate<PollRealtimeInvoker>("rt-poll",
                x => x.Run(feedUrl, cityId), JobSchedules.RtPoll, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
            RecurringJob.AddOrUpdate<StaticSyncInvoker>("static-sync", x => x.Run(), JobSchedules.StaticSync, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
    }
}

/// <summary>Invoker-Klassen halten die Jobs von DI-Auflösungsproblemen in Hangfile-Serialisierungsdelegaten fern.</summary>
public sealed class TtlSweepInvoker(TtlSweepService sweep, ILogger<TtlSweepInvoker>? logger = null)
{
    public Task Run(bool feedHealthy) => Task.Run(() =>
    {
        var n = sweep.Sweep(feedHealthy);
        if (n > 0) logger?.LogInformation("TTL-Sweep (Hangfire): {N} Aktionen", n);
    });
}

public sealed class PollRealtimeInvoker(PollRealtimeJob poll, FeedHealthProvider health, ILogger<PollRealtimeInvoker>? logger = null)
{
    public Task Run(string feedUrl, string cityId) => poll.RunAsync(feedUrl, cityId).ContinueWith(r =>
    {
        if (r.IsFaulted) logger?.LogError(r.Exception, "RT-Poll fehlgeschlagen");
        else if (r.Result is { Success: true, FeedHealthy: bool h }) health.Update(new Core.Ingest.FeedHealthState(h, Array.Empty<string>()));
    });
}

public sealed class StaticSyncInvoker(TransitGuard.Ingest.Jobs.StaticSyncJob job, IHttpClientFactory httpFactory, Microsoft.Extensions.Configuration.IConfiguration cfg)
{
    public Task Run() => job.RunAsync(httpFactory.CreateClient(),
        cfg["Ingest:StaticZipUrl"] ?? "https://download.gtfs.de/germany/nv_free/latest.zip",
        cfg["Ingest:City"] ?? "hamburg",
        Services.CityRegistry.All[cfg["Ingest:City"] ?? "hamburg"].BoundingBox);
}

public sealed class PartitionMaintInvoker(AppDbContextProxy db, ILogger<PartitionMaintInvoker>? logger = null)
{
    public Task Run() => db.MaintainPartitionsAsync().ContinueWith(r =>
        logger?.LogInformation("PartitionMaint: {Msg}", r.IsFaulted ? r.Exception!.Message : "ok (ensure/drop ausgeführt)"));
}

/// <summary>Dünner Proxy, damit das Job-Projekt nicht von EF abhängt (Local-Modus: No-Op).</summary>
public sealed class AppDbContextProxy
{
    private readonly IServiceProvider _sp;
    public AppDbContextProxy(IServiceProvider sp) => _sp = sp;
    public Task MaintainPartitionsAsync()
    {
        var type = Type.GetType("TransitGuard.Data.AppDbContext, TransitGuard.Data");
        if (type is null) return Task.CompletedTask;   // Local-Modus: keine Partitionen
        var ctx = _sp.GetService(type);
        if (ctx is null) return Task.CompletedTask;
        var method = type.GetMethod("ExecuteSqlRaw", new[] { typeof(string) });
        method?.Invoke(ctx, new object[] { "SELECT tg_ensure_report_partitions(14); SELECT tg_drop_old_report_partitions(90);" });
        return Task.CompletedTask;
    }
}
