using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using TransitGuard.Api.Jobs;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// Hangfire mit Postgres-Auftragsspeicher (docs/sql/0006_hangfire.sql).
///
/// Warum ueberhaupt: MemoryStorage verliert bei jedem Neustart Zeitplan, Auftraege und
/// Sperren. Ein Deploy (Symlink-Swap + Restart, docs/10 §5) mitten im StaticSync haette
/// den Auftrag stumm verschluckt — und niemand haette es gemerkt, weil „kein Fehler" wie
/// „alles gut" aussieht.
///
/// Diese Tests laufen NUR gegen eine echte Postgres-Instanz. Fehlt sie, werden sie
/// uebersprungen — nicht grün gemeldet.
/// </summary>
[Collection("postgres")]
public sealed class PostgresHangfireTests(PostgresFixture pg)
{
    /// <summary>API mit ALLEN Produktionsschaltern: Postgres-Daten, Postgres-Aufträge, Ingest an.</summary>
    private sealed class ProdFabrik(string cs, bool ingest, string jobStorage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseContentRoot(ApiFixtureHelfer.RepoWurzel());
            // Lebensdauern ausdruecklich pruefen lassen — nicht darauf verlassen, dass
            // die Testumgebung zufaellig "Development" heisst. Genau diese Pruefung hat
            // am 24.08.2026 zwei gefangene Abhaengigkeiten gefunden (T-DI-SCOPE).
            b.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });
            b.UseSetting("Data:Provider", "postgres");
            b.UseSetting("DATABASE:APP", cs);
            b.UseSetting("DATABASE:BILLING", cs);
            b.UseSetting("DATABASE:INGEST", cs);
            b.UseSetting("Jobs:Storage", jobStorage);
            b.UseSetting("Ingest:Enabled", ingest ? "true" : "false");
            // Feed-URL bewusst ins Leere: der Zeitplan soll registriert werden,
            // aber kein 40-MB-Download in einem Unit-Test laufen.
            b.UseSetting("Ingest:FeedUrl", "http://127.0.0.1:1/kein-feed.pb");
        }
    }

    // Hangfire.PostgreSql legt wiederkehrende Auftraege als hangfire.hash-Eintraege mit
    // Schluessel "recurring-job:<id>" ab — NICHT in hangfire.set. Das war zuerst anders
    // vermutet; die Pruefung gegen die echte Datenbank hat es korrigiert (24.08.2026).
    private const string ZaehlSql =
        "SELECT count(DISTINCT key) FROM hangfire.hash WHERE key LIKE 'recurring-job:%'";
    private const string LoeschSql =
        "DELETE FROM hangfire.hash WHERE key LIKE 'recurring-job:%'";

    private static long WiederkehrendeAuftraege(NpgsqlConnection con) =>
        Convert.ToInt64(PostgresFixture.Skalar(con, ZaehlSql) ?? 0L);

    /// <summary>
    /// T-HANGFIRE-1 — Zeitplan, Sperre und Neustart in EINEM Test.
    ///
    /// Bewusst ein Test und nicht vier: Hangfire haelt <c>JobStorage.Current</c> und den
    /// Log-Anbieter PROZESSWEIT. Mehrere Tests, die je einen eigenen Host hoch- und
    /// wieder herunterfahren, greifen sich gegenseitig den globalen Zustand weg — der
    /// erste Anlauf scheiterte genau daran (ObjectDisposedException auf einer
    /// LoggerFactory eines laengst entsorgten Hosts). Ein Test mit klarer Reihenfolge
    /// ist ehrlicher als vier, deren Ergebnis von der Ausfuehrungsreihenfolge abhaengt.
    ///
    /// Der Speicher wird ausdruecklich aus dem LAUFENDEN Host geholt, nicht aus
    /// <c>JobStorage.Current</c> — sonst prueft der Test womoeglich einen toten Speicher.
    /// </summary>
    [SkippableFact]
    public void T_HANGFIRE_1_Zeitplan_und_Sperre_ueberleben_den_Neustart()
    {
        Skip.IfNot(pg.Verfuegbar, $"Postgres nicht verfügbar: {pg.Grund}");

        // (0) T-DI-SCOPE — Start mit ALLEN Produktionsschaltern und BESTANDENER
        // Lebensdauer-Pruefung. Anlass (24.08.2026): AccessGate und EntitlementContext
        // waren Singleton, ziehen aber IDeviceRegistry bzw. IEntitlementStore — im
        // Postgres-Modus EF-Dienste mit Lebensdauer Scoped. Ein Singleton haelt einen
        // solchen Dienst dauerhaft fest: EIN DbContext fuer den ganzen Prozess, geteilt
        // ueber alle gleichzeitigen Anfragen. DbContext ist nicht threadsicher — das
        // faellt nicht beim Start auf, sondern unter Last, sporadisch. Im Speicher-Modus
        // sind beide Dienste Singleton, deshalb war es dort unsichtbar.
        // Steht hier und nicht in einem eigenen Test, weil jeder zusaetzliche Host in
        // diesem Prozess Hangfires globalen JobStorage.Current umbiegt (siehe oben).
        using (var erststart = new ProdFabrik(pg.Conninfo!, ingest: true, jobStorage: "postgres"))
        {
            var antwort = erststart.CreateClient().GetAsync("/health/ready").GetAwaiter().GetResult();
            Assert.True(antwort.IsSuccessStatusCode, $"/health/ready antwortete {(int)antwort.StatusCode}");
        }

        // (b) Ausgangslage wirklich leer — sonst koennte (d) von Altlasten leben.
        using (var con = pg.Oeffne()) PostgresFixture.Ausfuehren(con, LoeschSql);
        using (var con = pg.Oeffne()) Assert.Equal(0L, WiederkehrendeAuftraege(con));

        // (c) Produktionsstart: Daten, Auftraege und Ingest gleichzeitig an.
        using (var api = new ProdFabrik(pg.Conninfo!, ingest: true, jobStorage: "postgres"))
        {
            api.CreateClient().GetAsync("/health/ready").GetAwaiter().GetResult();

            var speicher = api.Services.GetRequiredService<JobStorage>();
            Assert.Contains("PostgreSql", speicher.GetType().FullName!);   // sonst prueft alles Weitere den falschen Speicher

            // Verteilte Sperre: darauf beruht DisableConcurrentExecution.
            using var con1 = speicher.GetConnection();
            using var sperre = con1.AcquireDistributedLock("tg-test-sperre", TimeSpan.FromSeconds(2));
            using var con2 = speicher.GetConnection();
            Exception? gefangen = null;
            try { using var zweite = con2.AcquireDistributedLock("tg-test-sperre", TimeSpan.FromSeconds(2)); }
            catch (Exception e) { gefangen = e; }
            Assert.NotNull(gefangen);   // zweite Anforderung MUSS scheitern, solange die erste haelt
        }   // <- Prozessende simuliert

        // (d) Hier laeuft KEINE API. Was jetzt in der Datenbank steht, hat ueberlebt.
        using (var con = pg.Oeffne())
        {
            Assert.Equal((long)JobSchedules.Alle.Count, WiederkehrendeAuftraege(con));

            var namen = new List<string>();
            using (var cmd = new NpgsqlCommand(
                "SELECT DISTINCT replace(key, 'recurring-job:', '') FROM hangfire.hash WHERE key LIKE 'recurring-job:%'", con))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) namen.Add(rd.GetString(0));
            foreach (var erwartet in JobSchedules.Alle.Keys) Assert.Contains(erwartet, namen);

            // Nicht nur der Name — auch der Zeitplan. Sonst wuesste der naechste Start
            // zwar, DASS es rt-poll gibt, aber nicht, wann er laufen soll.
            Assert.Equal(JobSchedules.RtPoll, PostgresFixture.Skalar(con,
                "SELECT value FROM hangfire.hash WHERE key = 'recurring-job:rt-poll' AND field = 'Cron'"));
        }

        // (e) Zweiter Start darf nicht verdoppeln (AddOrUpdate, nicht Add).
        using (var api2 = new ProdFabrik(pg.Conninfo!, ingest: true, jobStorage: "postgres"))
            api2.CreateClient().GetAsync("/health/ready").GetAwaiter().GetResult();
        using (var con = pg.Oeffne())
            Assert.Equal((long)JobSchedules.Alle.Count, WiederkehrendeAuftraege(con));
    }

    // Die Gegenprobe zur Persistenz — „mit MemoryStorage bleibt NICHTS in der Datenbank" —
    // steht bewusst NICHT hier, sondern in scripts/verify-ingest-e2e.sh. Grund: Hangfire
    // setzt JobStorage.Current prozessweit; ein Wechsel Postgres → Memory INNERHALB eines
    // Testprozesses schreibt weiterhin in den zuerst gesetzten Speicher. Ein solcher Test
    // waere gruen geworden, ohne den Defekt je zu beruehren. Getrennte Prozesse sind der
    // einzige ehrliche Weg.

    /// <summary>
    /// T-HANGFIRE-4 — Die Nicht-Überlappung ist auch wirklich AN DEN Aufträgen angebracht.
    /// T-HANGFIRE-3 zeigt, dass der Mechanismus auf diesem Speicher trägt; dieser Test zeigt,
    /// dass er auf den richtigen Methoden sitzt. Beides zusammen ist der Beleg — eines allein
    /// waere es nicht.
    /// </summary>
    [Fact]
    public void T_HANGFIRE_4_Poll_und_StaticSync_sind_als_nicht_ueberlappend_markiert()
    {
        // Attribute werden ueber CustomAttributeData GELESEN, nicht instanziiert:
        // DisableConcurrentExecutionAttribute holt sich im Konstruktor Hangfires globalen
        // Log-Anbieter, und der zeigt in einem Testprozess mit mehreren Hosts auf eine
        // bereits entsorgte LoggerFactory. Der Test waere an einem Fremdfehler gescheitert
        // und haette nichts ueber die Auftraege ausgesagt (gemessen 24.08.2026).
        static IList<CustomAttributeData> AttributeVon(Type t, string m) =>
            t.GetMethod(m, BindingFlags.Public | BindingFlags.Instance)!.GetCustomAttributesData();

        foreach (var (typ, methode) in new (Type, string)[]
                 {
                     (typeof(PollRealtimeInvoker), nameof(PollRealtimeInvoker.Run)),
                     (typeof(StaticSyncInvoker), nameof(StaticSyncInvoker.Run)),
                     (typeof(TtlSweepInvoker), nameof(TtlSweepInvoker.Run)),
                 })
        {
            Assert.True(
                AttributeVon(typ, methode).Any(a => a.AttributeType == typeof(DisableConcurrentExecutionAttribute)),
                $"{typ.Name}.{methode} braucht [DisableConcurrentExecution] — sonst koennen sich zwei Laeufe " +
                "ueberlappen und den Speicherbedarf verdoppeln (gemessen ~640 MB je Poll-Zyklus).");
        }

        var retry = AttributeVon(typeof(PollRealtimeInvoker), nameof(PollRealtimeInvoker.Run))
            .FirstOrDefault(a => a.AttributeType == typeof(AutomaticRetryAttribute));
        Assert.NotNull(retry);
        var versuche = retry!.NamedArguments.FirstOrDefault(n => n.MemberName == "Attempts");
        Assert.Equal(0, versuche.TypedValue.Value);   // ein verpasster Poll wird von der naechsten Minute erledigt
    }

    /// <summary>
    /// T-HANGFIRE-5 — tg_app darf den Auftragsspeicher NICHT lesen.
    /// Rollentrennung aus 0003_rls.sql: die API-Rolle hat mit Ingest-Aufträgen nichts zu tun.
    /// </summary>
    [SkippableFact]
    public void T_HANGFIRE_5_tg_app_sieht_das_hangfire_Schema_nicht()
    {
        Skip.IfNot(pg.Verfuegbar, $"Postgres nicht verfügbar: {pg.Grund}");
        using var con = pg.Oeffne("tg_app");
        var ex = Record.Exception(() => PostgresFixture.Skalar(con, "SELECT count(*) FROM hangfire.hash"));
        Assert.NotNull(ex);
        Assert.Contains("permission denied", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
