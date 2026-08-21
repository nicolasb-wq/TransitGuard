using IngestJobs = TransitGuard.Ingest.Jobs;
using Hangfire;
using Hangfire.MemoryStorage;   // NuGet: UseMemoryStorage-Erweiterung
using TransitGuard.Api.Hubs;
using TransitGuard.Api.Services;
using TransitGuard.Core.Abstractions;
using TransitGuard.Core.Gate;
using TransitGuard.Core.Normalize;
using TransitGuard.Core.Ttl;
using TransitGuard.Ingest.GtfsRt;
using TransitGuard.Ingest.Jobs;
using TransitGuard.Api.Jobs;
using TransitGuard.Ingest.Static;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;   // Vertrag: snake_case (docs/04)
    o.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
// --- CORS: standardmaessig AUS -------------------------------------------
// Produktion liefert PWA und API unter EINEM Origin aus (deploy/Caddyfile),
// deshalb braucht es kein CORS — und weniger Angriffsflaeche ist besser.
// Nur fuer lokale Laufzeittests (Flutter-Web unter eigenem Port) kann eine
// enge Allowlist gesetzt werden: Cors__AllowedOrigins="http://localhost:1234".
// Ohne diese Konfiguration wird KEINE CORS-Middleware registriert (T-CORS).
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SystemClock>(SystemClock.Instance);
builder.Services.AddSingleton<IClock>(sp => sp.GetRequiredService<SystemClock>());
builder.Services.AddSingleton<IDeviceAuthStore, InMemoryDeviceAuthStore>();
builder.Services.AddSingleton<ITripStateStore, InMemoryTripStateStore>();
builder.Services.AddSingleton<InMemoryTripWhitelistProvider>();
builder.Services.AddSingleton<ITripWhitelistProvider>(sp => sp.GetRequiredService<InMemoryTripWhitelistProvider>());
builder.Services.AddSingleton<IEntitlementStore, InMemoryEntitlementStore>();
builder.Services.AddSingleton<InMemoryIdempotencyStore>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton<TransitGuard.Core.Journeys.JourneyService>();
builder.Services.AddSingleton<ITripScheduleStore, InMemoryTripScheduleStore>();
builder.Services.AddSingleton<IStopStore, InMemoryStopStore>();
builder.Services.AddSingleton<IAlertStore, InMemoryAlertStore>();
builder.Services.AddSingleton<IDeviceRegistry, InMemoryDeviceRegistry>();
builder.Services.AddSingleton(new TransitGuard.Core.Entitlement.TrialPolicy(trialDays: 14));
builder.Services.AddSingleton<AccessGate>();
builder.Services.AddSingleton<EntitlementContext>();
builder.Services.AddSingleton<TicketGate>();
builder.Services.AddSingleton<TtlEngine>();
builder.Services.AddSingleton<AlertNormalizer>(AlertNormalizer.CreateDefault());
builder.Services.AddSingleton(new TripUpdateNormalizer());
builder.Services.AddSingleton<FeedHealthTracker>();
builder.Services.AddSingleton<FeedHealthProvider>();
builder.Services.Configure<TransitGuard.Core.Ttl.TtlOptions>(builder.Configuration.GetSection("Ttl"));
builder.Services.AddSingleton<TtlEngine>(sp =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TransitGuard.Core.Ttl.TtlOptions>>().Value;
    return new TtlEngine(opt);
});
builder.Services.AddSingleton<TransitGuard.Core.Journeys.TransferRouter>(sp => new(sp.GetRequiredService<ITripScheduleStore>()));
builder.Services.AddSingleton<AppDbContextProxy>();
builder.Services.AddSingleton<IngestJobs.IAlarmSink, IngestJobs.LoggingAlarmSink>();
// Metrik-Senke des Ingests: im Memory-Modus ins Log, im Prod-Modus nach
// ingest_metrics (siehe Postgres-Zweig unten). Ohne diese Registrierung ist
// PollRealtimeJob nicht auflösbar und die API startet mit Ingest gar nicht (T-DI).
builder.Services.AddSingleton<IIngestMetricsSink, TransitGuard.Api.Services.LoggingIngestMetricsSink>();
builder.Services.AddSingleton<IngestJobs.IStaticBuildStore, IngestJobs.InMemoryStaticBuildStore>();
builder.Services.AddScoped<IngestJobs.StaticSyncJob>();
builder.Services.AddScoped<TtlSweepService>();   // Auflösung je Job-Ausführung (Hangfire-Scope)
builder.Services.AddScoped<PollRealtimeJob>();
builder.Services.AddHttpClient<FeedFetcher>(c => c.Timeout = TimeSpan.FromSeconds(30));   // docs/07 §2
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 64 * 1024);
builder.Services.AddSingleton<IRealtimeDispatcher, SignalRRealtimeDispatcher>();

// --- Provider-Schalter: Prod = Postgres (DATABASE__APP/BILLING), Local = In-Memory ---
var provider = builder.Configuration["Data:Provider"] ?? "memory";
if (provider == "postgres")
{
    // Achtung: Env DATABASE__APP wird zur Config-Keys "DATABASE:APP" (Doppeltunnel nur im Env-Namen!)
    var appCs = builder.Configuration["DATABASE:APP"] ?? builder.Configuration.GetConnectionString("App")
        ?? throw new InvalidOperationException("DATABASE:APP fehlt (Env DATABASE__APP)");
    var billCs = builder.Configuration["DATABASE:BILLING"] ?? builder.Configuration.GetConnectionString("Billing")
        ?? throw new InvalidOperationException("DATABASE:BILLING fehlt (Env DATABASE__BILLING)");
    TransitGuard.Data.EfServiceCollectionExtensions.AddTransitGuardEfStores(builder.Services, appCs, billCs);
    builder.Services.AddScoped<TransitGuard.Api.Services.ITrustStore, TransitGuard.Api.Services.EfTrustStoreAdapter>();
    // Prod-Metriken landen in der Tabelle, nicht nur im Log.
    builder.Services.AddSingleton<IIngestMetricsSink>(sp =>
        new TransitGuard.Api.Services.PostgresIngestMetricsSink(
            appCs, sp.GetRequiredService<ILogger<TransitGuard.Api.Services.PostgresIngestMetricsSink>>()));
    builder.Logging.AddConsole();
}
// EF überschreibt Scoped-Registrierungen nicht: In-Memory-Stores NUR im Memory-Modus registrieren
else
{
    builder.Services.AddSingleton<InMemoryStores>();
    builder.Services.AddSingleton<IReportRepository>(sp => sp.GetRequiredService<InMemoryStores>());
    builder.Services.AddSingleton<IReportEventLog>(sp => sp.GetRequiredService<InMemoryStores>());
    builder.Services.AddSingleton<IFeatureFlagService>(sp => sp.GetRequiredService<InMemoryStores>());
    builder.Services.AddSingleton<TransitGuard.Api.Services.ITrustStore>(sp => sp.GetRequiredService<InMemoryStores>());
}

builder.Services.AddHangfire(h => h.UseMemoryStorage(new Hangfire.MemoryStorage.MemoryStorageOptions()));   // Prod: Postgres-Storage (M6)
builder.Services.AddHangfireServer();

var app = builder.Build();

// Dev-Konfiguration: Start-Whitelist aus mitgelieferter Mini-GTFS (falls vorhanden) — Prod: StaticSyncJob/T2.3.
var whitelistProvider = app.Services.GetRequiredService<InMemoryTripWhitelistProvider>();
var miniGtfs = Path.Combine(AppContext.BaseDirectory, "fixtures", "static_mini.zip");
if (File.Exists(miniGtfs))   // Dev: Mini-Extrakt; Prod: StaticSyncJob (T2.4) befüllt dieselben Stores
{
    using var fs = File.OpenRead(miniGtfs);
    using var gtfs = new GtfsStaticArchive(fs);
    var extract = CityExtractor.Extract(gtfs, CityRegistry.All["hamburg"].BoundingBox);
    whitelistProvider.Replace("hamburg", extract.Whitelist);
    app.Services.GetRequiredService<ITripScheduleStore>().ReplaceCity("hamburg", new Dictionary<string, TransitGuard.Core.Journeys.TripSchedule>(extract.Schedules));
    app.Services.GetRequiredService<IStopStore>().ReplaceCity("hamburg", (IReadOnlyList<TransitGuard.Core.Journeys.StopInfo>)extract.Stops);
    app.Logger.LogInformation("Stadt-Extrakt geladen: {Stops} Stops, {Trips} Trips (Whitelist {N}, Fahrplane {S})",
        extract.StopsInBox, extract.TripsInBox, extract.Whitelist.Count, extract.Schedules.Count);
}

// CORS zuerst: eine OPTIONS-Vorabfrage traegt keinen Geraete-Token und wuerde
// von der Auth-Middleware sonst mit 401 abgewiesen, bevor CORS greift.
if (corsOrigins.Length > 0) app.UseCors();

// Device-Auth-Middleware (X-Device-Token; /v1/devices + /health + billing/activate/restore offen)
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.ToString();
    var open = path.StartsWith("/health") || path == "/v1/devices" || path.Contains("/v1/billing/activate") || path.Contains("/v1/billing/restore");
    // Ein mitgeschickter Token wird IMMER aufgeloest — auch auf offenen Pfaden.
    // Frueher geschah das nur fuer !open, wodurch /v1/billing/restore nie eine
    // DeviceId sah und der Controller jeden Versuch als "rate_limited" abwies:
    // ein zahlender Nutzer kam nach einem Geraetewechsel nie wieder an sein Abo
    // (T-BILL-RESTORE). "offen" heisst: kein Token NOETIG — nicht: Token ignorieren.
    if (ctx.Request.Headers.TryGetValue("X-Device-Token", out var tok))
    {
        var store = ctx.RequestServices.GetRequiredService<IDeviceAuthStore>();
        if (store.TryResolve(tok.ToString(), out var deviceId)) ctx.Items["DeviceId"] = deviceId;
    }
    if (!open && ctx.Items.ContainsKey("DeviceId") == false && path.StartsWith("/v1/"))
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { error = new { code = "unauthorized", message = "X-Device-Token erforderlich (POST /v1/devices)" } });
        return;
    }
    await next();
});

// Meldungs-Ressourcen: kein Client-Cache (Kill-Switch-Konsistenz, B8)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.ToString().Contains("/reports") || ctx.Request.Path.ToString().Contains("/events"))
        ctx.Response.Headers.CacheControl = "no-store";
    await next();
});

app.UseHangfireDashboard("/admin/hangfire");
using (var scope = app.Services.CreateScope())
{
    var ingestEnabled = app.Configuration.GetValue<bool>("Ingest:Enabled");
    scope.ServiceProvider.RegisterRecurringJobs(ingestEnabled,
        app.Configuration["Ingest:FeedUrl"] ?? "https://realtime.gtfs.de/realtime-free.pb",
        app.Configuration["Ingest:City"] ?? "hamburg");
    var ttlOpt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TransitGuard.Core.Ttl.TtlOptions>>().Value;
    if (ttlOpt.DevOverrideSeconds is > 0)
        app.Logger.LogWarning("TTL:DevOverrideSeconds={S} aktiv — NUR für lokale Smoke-Tests, niemals Produktion!", ttlOpt.DevOverrideSeconds);
}

app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/v1/realtime");

app.Logger.LogInformation("TransitGuard API gestartet (Lokal-Modus, In-Memory-Stores; Prod-Modus via DATABASE__APP — Ticket M4).");
app.Run();

/// <summary>
/// Sichtbar für die Integrationstests (WebApplicationFactory&lt;Program&gt;).
/// Top-Level-Statements erzeugen sonst eine interne Program-Klasse.
/// </summary>
public partial class Program { }
