using Npgsql;
using Xunit;

namespace TransitGuard.Api.Tests;

/// <summary>
/// Verbindung zu einer ECHTEN Postgres-Instanz. Fehlt sie, werden die Tests
/// übersprungen — nicht grün gemeldet. Eine fehlende Datenbank ist kein Beweis
/// für ein korrektes Schema (dieselbe Regel wie beim QA-Loop, 26-build-log §3).
///
/// Instanz setzen: TG_TEST_DB="Host=127.0.0.1;Port=5433;Database=transitguard;Username=postgres"
/// </summary>
public sealed class PostgresFixture
{
    public string? Conninfo { get; }
    public bool Verfuegbar => Conninfo is not null;
    public string? Grund { get; }

    public PostgresFixture()
    {
        var cs = Environment.GetEnvironmentVariable("TG_TEST_DB");
        if (string.IsNullOrWhiteSpace(cs)) { Grund = "TG_TEST_DB nicht gesetzt"; return; }
        try
        {
            using var con = new NpgsqlConnection(cs);
            con.Open();
            using var cmd = new NpgsqlCommand("SELECT 1 FROM schema_migrations LIMIT 1", con);
            cmd.ExecuteScalar();
            Conninfo = cs;
        }
        catch (Exception e) { Grund = $"nicht erreichbar/migriert: {e.Message}"; }
    }

    public NpgsqlConnection Oeffne(string? rolle = null)
    {
        var con = new NpgsqlConnection(Conninfo);
        con.Open();
        if (rolle is not null)
        {
            using var c = new NpgsqlCommand($"SET ROLE {rolle}", con);
            c.ExecuteNonQuery();
        }
        return con;
    }

    public static object? Skalar(NpgsqlConnection con, string sql)
    {
        using var c = new NpgsqlCommand(sql, con);
        return c.ExecuteScalar();
    }

    public static int Ausfuehren(NpgsqlConnection con, string sql)
    {
        using var c = new NpgsqlCommand(sql, con);
        return c.ExecuteNonQuery();
    }
}

/// <summary>Markiert Tests, die eine echte Postgres-Instanz brauchen.</summary>
[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
