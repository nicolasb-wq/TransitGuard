using Npgsql;
using TransitGuard.Core.Abstractions;

namespace TransitGuard.Api.Services;

/// <summary>
/// Metrik-Senke des Echtzeit-Ingests (docs/07 §4, Tabelle ingest_metrics).
///
/// Bis 22.08.2026 gab es zu <see cref="IIngestMetricsSink"/> im ganzen Repo
/// KEINE Implementierung und keine Registrierung — <c>PollRealtimeJob</c>
/// verlangt sie aber im Konstruktor. Mit <c>Ingest:Enabled=true</c> stürzte die
/// API deshalb beim Start ab (T-DI-2). Im Lokal-Modus ist Ingest aus, weshalb es
/// acht Bausessions lang unentdeckt blieb.
/// </summary>
public sealed class LoggingIngestMetricsSink(ILogger<LoggingIngestMetricsSink> log) : IIngestMetricsSink
{
    public void Write(IngestMetricRow r) => log.LogInformation(
        "Ingest {Feed}: http={Status} {Bytes}B alter={Age}s entities={Entities} tu={Tu} alerts={Alerts} "
        + "vp={Vp} stadt={Matched} quote={Rate:P1} routeMisses={Misses} clamped={Clamped} parse={Parse}ms etag={Etag}{Fehler}",
        r.Feed, r.HttpStatus, r.Bytes, r.FeedAgeSeconds, r.Entities, r.TripUpdates, r.Alerts,
        r.VehiclePositions, r.CityTripsMatched, r.MatchRate, r.RouteMisses, r.DelayClamped,
        r.ParseMs, r.EtagHit, r.Error is null ? "" : $" FEHLER={r.Error}");
}

/// <summary>
/// Prod-Senke: schreibt nach <c>ingest_metrics</c> (sql/0001_core.sql §184).
/// Schreibfehler dürfen den Poll-Lauf NIE abbrechen — Metrik ist Beiwerk, der
/// Feed ist die Aufgabe. Deshalb Auffangen und Weiterloggen.
/// </summary>
public sealed class PostgresIngestMetricsSink(string verbindung, ILogger<PostgresIngestMetricsSink> log)
    : IIngestMetricsSink
{
    private const string Sql = """
        INSERT INTO ingest_metrics
          (ts, feed, http_status, bytes, feed_age_s, entities, tu_count, alert_count, vp_count,
           city_trips_matched, match_rate, route_misses, delay_clamped, parse_ms, etag_hit, error)
        VALUES
          (@ts, @feed, @status, @bytes, @age, @entities, @tu, @alerts, @vp,
           @matched, @rate, @misses, @clamped, @parse, @etag, @error)
        """;

    public void Write(IngestMetricRow r)
    {
        try
        {
            using var con = new NpgsqlConnection(verbindung);
            con.Open();
            using var cmd = new NpgsqlCommand(Sql, con);
            cmd.Parameters.AddWithValue("ts", r.Ts);
            cmd.Parameters.AddWithValue("feed", r.Feed);
            cmd.Parameters.AddWithValue("status", r.HttpStatus);
            cmd.Parameters.AddWithValue("bytes", r.Bytes);
            cmd.Parameters.AddWithValue("age", (object?)r.FeedAgeSeconds ?? DBNull.Value);
            cmd.Parameters.AddWithValue("entities", (int)r.Entities);
            cmd.Parameters.AddWithValue("tu", (int)r.TripUpdates);
            cmd.Parameters.AddWithValue("alerts", (int)r.Alerts);
            cmd.Parameters.AddWithValue("vp", (int)r.VehiclePositions);
            cmd.Parameters.AddWithValue("matched", (int)r.CityTripsMatched);
            cmd.Parameters.AddWithValue("rate", (decimal)Math.Round(r.MatchRate, 4));
            cmd.Parameters.AddWithValue("misses", r.RouteMisses);
            cmd.Parameters.AddWithValue("clamped", r.DelayClamped);
            cmd.Parameters.AddWithValue("parse", r.ParseMs);
            cmd.Parameters.AddWithValue("etag", r.EtagHit);
            cmd.Parameters.AddWithValue("error", (object?)r.Error ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Ingest-Metrik konnte nicht geschrieben werden — Poll läuft weiter.");
        }
    }
}
