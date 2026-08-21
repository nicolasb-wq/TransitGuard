using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TransitGuard.Api.Jobs;
using TransitGuard.Ingest.Jobs;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-DI: Jeder Hangfire-Job muss aus dem DI-Container auflösbar sein — in der
/// PRODUKTIONSKONFIGURATION, also mit eingeschaltetem Ingest.
///
/// Anlass (22.08.2026): <c>IIngestMetricsSink</c> hatte im ganzen Repo keine
/// Implementierung und keine Registrierung, <c>PollRealtimeJob</c> verlangt sie
/// aber im Konstruktor. Mit <c>Ingest:Enabled=true</c> stürzte die API beim
/// START ab (JobRegistration löst PollRealtimeJob dort auf). Im Lokal-Modus ist
/// Ingest aus — deshalb fiel es in acht Bausessions nicht auf. Der Erst-Deploy
/// wäre in Runbook-Phase 5 gestorben.
/// </summary>
public sealed class DiResolutionTests
{
    private sealed class IngestFabrik : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Production");   // wie auf dem Server
            builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Ingest:Enabled"] = "true" }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ApiFixtureHelfer.RepoWurzel());
            builder.UseSetting("Ingest:Enabled", "true");
            // Kein echtes Netz im Test: geprüft wird der START mit Produktions-
            // konfiguration, nicht der Feed. Ports 9 (discard) laufen sofort ins Leere.
            builder.UseSetting("Ingest:FeedUrl", "http://127.0.0.1:9/none.pb");
            builder.UseSetting("Ingest:StaticZipUrl", "http://127.0.0.1:9/none.zip");
        }
    }

    /// <summary>Alle Typen, die Hangfire zur Laufzeit selbst aus dem Container zieht.</summary>
    public static TheoryData<Type> JobTypen()
    {
        var d = new TheoryData<Type>();
        foreach (var t in new[]
        {
            typeof(PollRealtimeJob), typeof(StaticSyncJob), typeof(TtlSweepService),
            typeof(PollRealtimeInvoker), typeof(StaticSyncInvoker),
            typeof(TtlSweepInvoker), typeof(PartitionMaintInvoker),
        }) d.Add(t);
        return d;
    }

    [Theory]
    [MemberData(nameof(JobTypen))]
    public void T_DI_1_JobIstAufloesbar(Type jobTyp)
    {
        // Über den ECHTEN JobActivator aus dem Container — nicht über GetService.
        // Hangfire.AspNetCore erzeugt Job-Typen, die selbst nicht registriert sind
        // (die Invoker), aus ihren DI-aufloesbaren Konstruktorargumenten. Genau
        // dieser Pfad muss halten, nicht ein davon abweichender Testpfad.
        using var f = new IngestFabrik();
        _ = f.Services;                                   // erzwingt den Host-Aufbau
        using var scope = f.Services.CreateScope();
        var aktivator = scope.ServiceProvider.GetRequiredService<Hangfire.JobActivator>();
        using var jobScope = aktivator.BeginScope((Hangfire.JobActivatorContext)null!);
        var ex = Record.Exception(() => jobScope.Resolve(jobTyp));
        Assert.True(ex is null, $"{jobTyp.Name} ist nicht aktivierbar — Hangfire würde bei jedem Lauf scheitern: {ex?.Message}");
    }

    [Fact]
    public void T_DI_0_HangfireNutztDenDiAktivator()
    {
        // Absicherung der Annahme, auf der T_DI_1 beruht.
        using var f = new IngestFabrik();
        var aktivator = f.Services.GetRequiredService<Hangfire.JobActivator>();
        Assert.Contains("AspNetCore", aktivator.GetType().FullName);
    }

    [Fact]
    public void T_DI_2_ApiStartetMitEingeschaltetemIngest()
    {
        // Das ist der Fall, der in Produktion galt und den niemand je gefahren ist.
        using var f = new IngestFabrik();
        var c = f.CreateClient();
        Assert.NotNull(c);
        Assert.NotNull(f.Services.GetService<IRecurringJobManager>());
    }
}

/// <summary>
/// T-CRON: Alle Hangfire-Zeitpläne müssen parsebar sein. Ein ungültiger Ausdruck
/// wirft erst beim Registrieren — also beim Start der API in Produktion.
/// </summary>
public sealed class CronTests
{
    public static TheoryData<string, string> Zeitplaene()
    {
        var d = new TheoryData<string, string>();
        foreach (var (name, ausdruck) in JobSchedules.Alle) d.Add(name, ausdruck);
        return d;
    }

    [Theory]
    [MemberData(nameof(Zeitplaene))]
    public void T_CRON_1_AusdruckIstGueltig(string name, string ausdruck)
    {
        // Sechs Felder = mit Sekunden, fünf = ab Minute. Hangfire entscheidet nach Feldzahl.
        var felder = ausdruck.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var format = felder >= 6 ? Cronos.CronFormat.IncludeSeconds : Cronos.CronFormat.Standard;
        var ex = Record.Exception(() => Cronos.CronExpression.Parse(ausdruck, format));
        Assert.True(ex is null, $"Zeitplan '{name}' = \"{ausdruck}\" ist ungültig: {ex?.Message}");
    }

    [Fact]
    public void T_CRON_2_RtPollFeuertJedeMinute()
    {
        // Nicht nur parsebar — auch der gemeinte Abstand (docs/07 §1: 60 s).
        var e = Cronos.CronExpression.Parse(JobSchedules.RtPoll, Cronos.CronFormat.IncludeSeconds);
        var t0 = new DateTime(2026, 8, 22, 10, 0, 30, DateTimeKind.Utc);
        var a = e.GetNextOccurrence(t0)!.Value;
        var b = e.GetNextOccurrence(a)!.Value;
        Assert.Equal(TimeSpan.FromSeconds(60), b - a);
    }

    [Fact]
    public void T_CRON_3_TtlSweepFeuertAlleFuenfzehnSekunden()
    {
        var e = Cronos.CronExpression.Parse(JobSchedules.TtlSweep, Cronos.CronFormat.IncludeSeconds);
        var t0 = new DateTime(2026, 8, 22, 10, 0, 1, DateTimeKind.Utc);
        var a = e.GetNextOccurrence(t0)!.Value;
        var b = e.GetNextOccurrence(a)!.Value;
        Assert.Equal(TimeSpan.FromSeconds(15), b - a);
    }
}
