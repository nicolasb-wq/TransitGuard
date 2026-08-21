using Npgsql;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// T-PG-SCHEMA: Schema, Partitionierung und Retention gegen ECHTES Postgres.
/// Vorher standen diese Eigenschaften auf Belegen einer früheren Session.
/// </summary>
[Collection("postgres")]
public sealed class PostgresSchemaTests
{
    private readonly PostgresFixture _pg;
    public PostgresSchemaTests(PostgresFixture pg) => _pg = pg;
    private bool Ueberspringen(out string g) { g = _pg.Grund ?? ""; return !_pg.Verfuegbar; }

    [SkippableFact]
    public void T_PG_SCHEMA_1_AlleMigrationenAngewandt()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne();
        using var cmd = new NpgsqlCommand("SELECT filename FROM schema_migrations ORDER BY filename", con);
        using var r = cmd.ExecuteReader();
        var namen = new List<string>();
        while (r.Read()) namen.Add(r.GetString(0));
        Assert.Equal(
            new[] { "0001_core.sql", "0002_gtfs.sql", "0003_rls.sql",
                    "0004_partitions_retention.sql", "0005_report_last_known_station.sql" },
            namen);
    }

    [SkippableFact]
    public void T_PG_SCHEMA_2_PostGIS_ist_scharf_und_wird_benutzt()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne();
        Assert.NotNull(PostgresFixture.Skalar(con, "SELECT postgis_version()"));
        // Nicht nur installiert: die Stadt-Box ist eine echte Geometrie.
        var typ = (string?)PostgresFixture.Skalar(con,
            "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a "
            + "WHERE a.attrelid = 'cities'::regclass AND a.attname = 'bbox'");
        Assert.Contains("geometry", typ ?? "");
        // Und sie rechnet: Punkt in Hamburg liegt in der Hamburg-Box.
        var drin = PostgresFixture.Skalar(con,
            "SELECT ST_Contains(ST_MakeEnvelope(9.55, 53.30, 10.50, 53.85, 4326), "
            + "ST_SetSRID(ST_MakePoint(9.991, 53.554), 4326))");
        Assert.Equal(true, drin);
    }

    [SkippableFact]
    public void T_PG_SCHEMA_3_reports_ist_tagesweise_partitioniert()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne();
        Assert.Equal("p", PostgresFixture.Skalar(con,
            "SELECT relkind::text FROM pg_class WHERE relname = 'reports'"));
        var strategie = PostgresFixture.Skalar(con,
            "SELECT partstrat::text FROM pg_partitioned_table WHERE partrelid = 'reports'::regclass");
        Assert.Equal("r", strategie);   // r = RANGE
        var n = (long)PostgresFixture.Skalar(con,
            "SELECT count(*) FROM pg_class WHERE relname ~ '^reports_[0-9]{8}$'")!;
        Assert.True(n >= 15, $"Erwartet mindestens 15 Tagespartitionen, gefunden {n}");
    }

    [SkippableFact]
    public void T_PG_SCHEMA_4_Partitionsanlage_ist_wiederholbar_und_legt_neue_Tage_an()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne();
        long Zaehle() => (long)PostgresFixture.Skalar(con,
            "SELECT count(*) FROM pg_class WHERE relname ~ '^reports_[0-9]{8}$'")!;

        // Vorlauf aus dem IST-Zustand ableiten, nicht fest verdrahten: sonst ist
        // der Test beim zweiten Lauf gegen dieselbe Datenbank nicht mehr gültig
        // (am 22.08.2026 genau so hereingefallen).
        var maxTag = (DateTime)PostgresFixture.Skalar(con,
            "SELECT max(to_date(substring(relname from '[0-9]{8}$'), 'YYYYMMDD')) "
            + "FROM pg_class WHERE relname ~ '^reports_[0-9]{8}$' "
            + "AND to_date(substring(relname from '[0-9]{8}$'), 'YYYYMMDD') >= current_date")!;
        var vorlauf = (int)(maxTag - DateTime.UtcNow.Date).TotalDays;

        var vorher = Zaehle();
        PostgresFixture.Ausfuehren(con, $"SELECT tg_ensure_report_partitions({vorlauf})");
        Assert.Equal(vorher, Zaehle());              // idempotent: nichts Neues

        var neueTage = 3;
        PostgresFixture.Ausfuehren(con, $"SELECT tg_ensure_report_partitions({vorlauf + neueTage})");
        var nachher = Zaehle();
        Assert.Equal(vorher + neueTage, nachher);

        // Aufräumen: die Datenbank bleibt, wie sie war.
        for (var i = 1; i <= neueTage; i++)
        {
            var tag = maxTag.AddDays(i);
            PostgresFixture.Ausfuehren(con, $"DROP TABLE IF EXISTS reports_{tag:yyyyMMdd}");
        }
        Assert.Equal(vorher, Zaehle());
    }

    [SkippableFact]
    public void T_PG_SCHEMA_5_Retention_loescht_alte_Partitionen_wirklich()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne();
        // Eine Partition weit in der Vergangenheit anlegen und die Retention
        // darauf loslassen — DSGVO-Löschkonzept reports: 90 Tage.
        var alt = DateTime.UtcNow.Date.AddDays(-200);
        var name = $"reports_{alt:yyyyMMdd}";
        PostgresFixture.Ausfuehren(con,
            $"CREATE TABLE IF NOT EXISTS {name} PARTITION OF reports "
            + $"FOR VALUES FROM ('{alt:yyyy-MM-dd}') TO ('{alt.AddDays(1):yyyy-MM-dd}')");
        Assert.Equal(1L, PostgresFixture.Skalar(con,
            $"SELECT count(*) FROM pg_class WHERE relname = '{name}'"));

        var geloescht = (int)PostgresFixture.Skalar(con, "SELECT tg_drop_old_report_partitions(90)")!;
        Assert.True(geloescht >= 1, "Retention hat keine Partition entfernt.");
        Assert.Equal(0L, PostgresFixture.Skalar(con,
            $"SELECT count(*) FROM pg_class WHERE relname = '{name}'"));

        // Junge Partitionen bleiben unangetastet.
        var heute = $"reports_{DateTime.UtcNow:yyyyMMdd}";
        Assert.Equal(1L, PostgresFixture.Skalar(con,
            $"SELECT count(*) FROM pg_class WHERE relname = '{heute}'"));
    }

    [SkippableFact]
    public void T_PG_SCHEMA_6_Meldung_landet_in_der_Tagespartition()
    {
        Skip.If(Ueberspringen(out var g), g);
        using var con = _pg.Oeffne();
        var stadt = "t_pg_part_stadt";
        PostgresFixture.Ausfuehren(con,
            $"INSERT INTO cities(city_id, slug, display_name, is_active, gtfs_static_source, bbox) "
            + $"VALUES ('{stadt}', '{stadt}', 'Partition', true, 'test', ST_MakeEnvelope(9,53,10,54,4326)) "
            + "ON CONFLICT (city_id) DO NOTHING");
        var id = Guid.NewGuid();
        try
        {
            PostgresFixture.Ausfuehren(con,
                $"INSERT INTO reports(id, created_at, city_id, anchor_type, report_type, vehicle_kind, "
                + $"station_id, inspector_count, reporter_device_id, reporter_trust, status, expires_at, ttl_base_s) "
                + $"VALUES ('{id}', now(), '{stadt}', 1, 2, 1, 'X', 1, gen_random_uuid(), 50, 'active', now() + interval '30 min', 1800)");

            // Die Zeile liegt physisch in der Tagespartition, nicht in der Elterntabelle.
            var partition = (string?)PostgresFixture.Skalar(con,
                $"SELECT tableoid::regclass::text FROM reports WHERE id = '{id}'");
            Assert.Equal($"reports_{DateTime.UtcNow:yyyyMMdd}", partition);
        }
        finally
        {
            PostgresFixture.Ausfuehren(con, $"DELETE FROM reports WHERE id = '{id}'");
            PostgresFixture.Ausfuehren(con, $"DELETE FROM cities WHERE city_id = '{stadt}'");
        }
    }
}
