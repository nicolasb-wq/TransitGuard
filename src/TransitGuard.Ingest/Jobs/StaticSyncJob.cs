using Microsoft.Extensions.Logging;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Ingest;
using TransitGuard.Ingest.Static;

namespace TransitGuard.Ingest.Jobs;

public interface IAlarmSink
{
    void Warn(string source, string message);
    void Critical(string source, string message);   // Kanal: Ops-E-Mail (SMTP in Prod; lokal Log) — docs/07 §8
}

/// <summary>Alarm-Implementierung: lokales Log; Prod hängt SMTP-Decorator an (Ticket Ops, F-19).</summary>
public sealed class LoggingAlarmSink(ILogger<LoggingAlarmSink>? logger = null) : IAlarmSink
{
    public void Warn(string source, string message) => logger?.LogWarning("[ALARM:{Source}] {Message}", source, message);
    public void Critical(string source, string message) => logger?.LogCritical("[ALARM:{Source}] {Message}", source, message);
}

public interface IStaticBuildStore
{
    void Activate(long buildId, string cityId);
    long? ActiveBuildId(string cityId);
}

/// <summary>
/// StaticSyncJob T2.4 (vollständig): Download → CityExtractor (bbox→Trips→Whitelist/Schedules/Stops)
/// → befüllt die Prozess-Stores (ADR-0008) → Build-Bookkeeping. Atomarität: Stores werden erst nach
/// vollständiger Extraktion getauscht („Swap im Halt"): ein Abbruch mid-sync lässt den alten Zustand stehen.
/// Prod-Erweiterung (offen, M6): gtfs_builds/gtfs_*-Tabellen füllen (T2.4-DB-Teil).
/// </summary>
public sealed class StaticSyncJob(
    ITripWhitelistProvider whitelist,
    ITripScheduleStore schedules,
    IStopStore stops,
    IStaticBuildStore builds,
    IAlarmSink alarms,
    ILogger<StaticSyncJob>? logger = null)
{
    /// <summary>Zielverzeichnis der Zwischendatei; ueberschreibbar fuer Tests.</summary>
    public string ZwischenspeicherVerzeichnis { get; init; } = Path.GetTempPath();

    public async Task<SyncResult> RunAsync(HttpClient http, string zipUrl, string cityId, Core.Geo.GeoBoundingBox bbox, CancellationToken ct = default)
    {
        long bytes;
        // Das ZIP geht auf die PLATTE, nicht in den Arbeitsspeicher.
        // Gemessen am echten nv_free-Archiv (251 MB) am 24.08.2026:
        //   ueber MemoryStream  687 MB Spitzen-RSS
        //   ueber Zwischendatei 418 MB Spitzen-RSS   ⇒ 269 MB gespart, praktisch die ZIP-Groesse.
        // Auf einer kleinen VM ist das der Unterschied zwischen "laeuft" und OOM-Kill.
        var temp = Path.Combine(ZwischenspeicherVerzeichnis,
            $"tg-static-{cityId}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var resp = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode) { alarms.Critical("static-sync", $"Download {zipUrl} → {(int)resp.StatusCode}"); return new SyncResult(false, 0, 0, 0); }
                await using var quelle = await resp.Content.ReadAsStreamAsync(ct);
                await using var ziel = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 1 << 20, useAsync: true);
                await quelle.CopyToAsync(ziel, 1 << 20, ct);
                bytes = ziel.Length;
            }

            await using var datei = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: false);
            var extract = CityExtractor.Extract(new GtfsStaticArchive(datei), bbox);
            if (extract.Whitelist.Count == 0) { alarms.Critical("static-sync", "Extrakt leer — Abbruch, alter Stand bleibt aktiv"); return new SyncResult(false, bytes, 0, 0); }

            // „Atomarer" Swap der Prozess-Stores (alte Daten bleiben bei Exception bis hierher stehen)
            whitelist.Replace(cityId, extract.Whitelist);
            schedules.ReplaceCity(cityId, new Dictionary<string, Core.Journeys.TripSchedule>(extract.Schedules));
            stops.ReplaceCity(cityId, (IReadOnlyList<Core.Journeys.StopInfo>)extract.Stops);
            builds.Activate(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cityId);
            logger?.LogInformation("StaticSync {City}: {MB:F1} MB ZIP → {Stops} Stops, {Trips} Trips, Whitelist {N}",
                cityId, bytes / 1048576.0, extract.StopsInBox, extract.TripsInBox, extract.Whitelist.Count);
            return new SyncResult(true, bytes, extract.StopsInBox, extract.TripsInBox);
        }
        finally
        {
            // Die Zwischendatei MUSS auch bei Abbruch verschwinden — sonst fuellt ein
            // wiederholt fehlschlagender Sync zweimal die Woche die Platte mit 251-MB-Leichen.
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException ex) { logger?.LogWarning(ex, "Zwischendatei {Datei} nicht loeschbar", temp); }
            catch (UnauthorizedAccessException ex) { logger?.LogWarning(ex, "Zwischendatei {Datei} nicht loeschbar", temp); }
        }
    }

    public sealed record SyncResult(bool Success, long Bytes, int Stops, int Trips);
}

public sealed class InMemoryStaticBuildStore : IStaticBuildStore
{
    private readonly Dictionary<string, long> _active = new();
    public void Activate(long buildId, string cityId) => _active[cityId] = buildId;
    public long? ActiveBuildId(string cityId) => _active.GetValueOrDefault(cityId);
}
